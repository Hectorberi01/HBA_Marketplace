using HBA.Shared.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HBA.Shared.Hosting.Telemetry;

/// <summary>TRACES, MÉTRIQUES ET JOURNAUX POUR LES QUATORZE SERVICES, EN UN SEUL ENDROIT.</summary>
public static class TelemetryExtensions
{
    /// <summary>Branche l'instrumentation du service.</summary>
    public static WebApplicationBuilder AddHbaTelemetry(
        this WebApplicationBuilder builder, string serviceName)
    {
        var options = builder.Configuration
            .GetSection(TelemetryOptions.SectionName)
            .Get<TelemetryOptions>() ?? new TelemetryOptions();

        var resolvedName = string.IsNullOrWhiteSpace(options.ServiceName)
            ? serviceName
            : options.ServiceName!;

        var hasEndpoint = Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint);

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(resolvedName)
                .AddAttributes([
                    new KeyValuePair<string, object>(
                        "deployment.environment", builder.Environment.EnvironmentName)
                ]))

            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(instrumentation =>
                    {
                        // LES SONDES DE SANTÉ SONT EXCLUES DES TRACES.
                        instrumentation.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health");

                        instrumentation.RecordException = true;
                    })

                    // Appels sortants : c'est ce qui rattache la latence d'un
                    // service à celle du service qu'il attend, au lieu de la lui
                    // imputer.
                    .AddHttpClientInstrumentation()

                    // LA BASE, PAR LA SOURCE NATIVE DE NPGSQL.
                    .AddSource("Npgsql")

                    // Les appels inter-services.
                    .AddSource("Grpc.Net.Client")

                    // SANS CETTE LIGNE, TOUT L'ASYNCHRONE RESTE INVISIBLE.
                    .AddSource(HbaTelemetry.KafkaSourceName);

                if (hasEndpoint)
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = endpoint!);
                }
            })

            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()

                    // Compteurs du limiteur de débit : c'est ce qui permet de
                    // constater qu'une politique est trop stricte AVANT que les
                    // utilisateurs ne le signalent.
                    .AddMeter("Microsoft.AspNetCore.RateLimiting");

                if (hasEndpoint)
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = endpoint!);
                }
            });

        ConfigurerJournaux(builder, options, hasEndpoint, endpoint);

        return builder;
    }

    /// <summary>LES JOURNAUX — SANS SERILOG, ET C'EST UNE DÉCISION (D18).</summary>
    private static void ConfigurerJournaux(
        WebApplicationBuilder builder, TelemetryOptions options, bool hasEndpoint, Uri? endpoint)
    {
        if (options.JsonConsole)
        {
            // ON REMPLACE LE FORMATEUR, ON N'AJOUTE PAS UNE SECONDE CONSOLE.
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(console =>
            {
                console.IncludeScopes = true;
                console.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
            });
        }

        if (!hasEndpoint || !options.ExportLogs)
        {
            return;
        }

        builder.Logging.AddOpenTelemetry(logging =>
        {
            // Sans ces deux drapeaux, le message arrive rendu — « Produit 42
            // introuvable » — et l'on perd le gabarit et ses paramètres.
            logging.IncludeFormattedMessage = true;
            logging.ParseStateValues = true;

            logging.AddOtlpExporter(exporter => exporter.Endpoint = endpoint!);
        });
    }
}
