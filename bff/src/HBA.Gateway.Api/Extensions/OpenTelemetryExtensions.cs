using HBA.Gateway.Api.Options;
using Microsoft.AspNetCore.Http;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Api.Extensions;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddGatewayOpenTelemetry(
        this IServiceCollection services, IConfiguration configuration, string environmentName)
    {
        var options = configuration
            .GetSection(TelemetryOptions.SectionName)
            .Get<TelemetryOptions>() ?? new TelemetryOptions();

        var hasEndpoint = Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint);

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(options.ServiceName)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment", environmentName)]))

            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(instrumentation =>
                    {
                        // LES SONDES DE SANTÉ SONT EXCLUES DES TRACES.
                        //
                        // Docker les appelle toutes les 30 s, Kubernetes plus
                        // souvent encore. Conservées, elles représentent la
                        // majorité des traces d'un service peu sollicité et
                        // saturent l'échantillonnage : les vraies requêtes se
                        // retrouvent écartées au profit de sondes qui ne
                        // renseignent sur rien.
                        instrumentation.Filter = ShouldTraceRequest;

                        instrumentation.RecordException = true;
                    })
                    .AddHttpClientInstrumentation()

                    // YARP publie ses activités sous cette source : sans elle, la
                    // trace s'arrête à la passerelle et le saut vers le
                    // microservice n'apparaît nulle part.
                    .AddSource("Yarp.ReverseProxy")

                    // SANS CETTE LIGNE, LES SPANS D'AGRÉGATION SONT ÉMIS ET JETÉS.
                    //
                    // `AggregationContext` ouvre déjà un span par écran et un par
                    // dépendance. OpenTelemetry n'écoute que les sources
                    // explicitement abonnées : non déclarée, la source produit des
                    // `Activity` nulles, le code continue de fonctionner, et la
                    // latence par dépendance du §34 reste invisible. Un défaut qui
                    // ne se manifeste que par une absence.
                    .AddSource(BffTelemetry.Name);

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
                    .AddMeter("Microsoft.AspNetCore.RateLimiting")

                    // Les quatre compteurs du §43 : durée d'agrégation, durée par
                    // dépendance, réponses partielles, échecs de dépendance.
                    .AddMeter(BffTelemetry.Name);

                if (hasEndpoint)
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = endpoint!);
                }
            });

        return services;
    }

    private static bool ShouldTraceRequest(HttpContext context)
        => !context.Request.Path.StartsWithSegments("/health");
}
