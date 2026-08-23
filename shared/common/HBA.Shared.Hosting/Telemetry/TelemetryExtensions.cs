using HBA.Shared.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HBA.Shared.Hosting.Telemetry;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// TRACES, MÉTRIQUES ET JOURNAUX POUR LES QUATORZE SERVICES, EN UN SEUL ENDROIT.
///
/// AVANT CE FICHIER, SEULE LA PASSERELLE ÉTAIT INSTRUMENTÉE.
///
/// Les quatorze services n'émettaient RIEN : ni trace, ni métrique, ni journal
/// structuré. Concrètement, une trace commençait à la passerelle, montrait le saut
/// YARP, et s'arrêtait net à la frontière du service. Toute latence au-delà — la
/// requête SQL lente, l'appel gRPC qui expire, le handler qui boucle — se
/// diagnostiquait en lisant des journaux texte, service par service, en recoupant
/// des horodatages à la main.
///
/// CE QUE CE FICHIER N'EST PAS : UNE COPIE DE `OpenTelemetryExtensions` DE LA
///    PASSERELLE.
///
/// Il en reprend la forme, pas le contenu. La passerelle instrumente YARP et ses
/// agrégateurs BFF ; un service instrumente sa BASE, son client gRPC et sa
/// MESSAGERIE. Les trois sources ajoutées ici — Npgsql, Grpc.Net.Client, Hba.Kafka
/// — n'ont aucun sens côté passerelle, et les siennes n'en ont aucun ici.
///
/// AUCUN PAQUET NOUVEAU N'A ÉTÉ ÉPINGLÉ POUR CE LOT.
///
/// Les cinq paquets OpenTelemetry étaient déjà dans `Directory.Packages.props`
/// pour la passerelle. Pour la base et gRPC, on s'abonne aux sources que Npgsql et
/// `Grpc.Net.Client` émettent NATIVEMENT plutôt que d'ajouter
/// `OpenTelemetry.Instrumentation.EntityFrameworkCore`, qui est encore en bêta.
/// La contrepartie est décrite plus bas, à l'endroit où elle se paie.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Branche l'instrumentation du service. Appelé par <c>AddHbaService</c> : aucun
    /// `Program.cs` n'a à s'en préoccuper.
    /// </summary>
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
                        //
                        // Docker les appelle toutes les 30 s, Kubernetes plus souvent
                        // encore. Conservées, elles représentent la majorité des traces
                        // d'un service peu sollicité et saturent l'échantillonnage : les
                        // vraies requêtes se retrouvent écartées au profit de sondes qui
                        // ne renseignent sur rien.
                        instrumentation.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health");

                        instrumentation.RecordException = true;
                    })

                    // Appels sortants : c'est ce qui rattache la latence d'un service à
                    // celle du service qu'il attend, au lieu de la lui imputer.
                    .AddHttpClientInstrumentation()

                    // ═════════════════════════════════════════════════════════════
                    // LA BASE, PAR LA SOURCE NATIVE DE NPGSQL.
                    //
                    // Npgsql émet ses propres activités sous ce nom depuis la
                    // version 8 : une requête SQL lente apparaît donc comme un span
                    // enfant de la requête HTTP, avec son texte et sa durée.
                    //
                    // Ce que l'on N'A PAS, faute d'ajouter le paquet EF Core (encore
                    // en bêta) : le découpage par `DbContext` et le nom de la méthode
                    // EF appelante. On voit le SQL et sa durée, pas la ligne C# qui
                    // l'a produit. C'est le compromis assumé — et il se renverse en
                    // une ligne le jour où le paquet sera stable.
                    // ═════════════════════════════════════════════════════════════
                    .AddSource("Npgsql")

                    // Les appels inter-services. Le VOLET SERVEUR est déjà couvert par
                    // l'instrumentation ASP.NET Core — un appel gRPC entrant est une
                    // requête HTTP/2 — mais le volet CLIENT ne l'est pas, et c'est lui
                    // qui manque quand on cherche pourquoi catalog attend merchant.
                    .AddSource("Grpc.Net.Client")

                    // ═════════════════════════════════════════════════════════════
                    // SANS CETTE LIGNE, TOUT L'ASYNCHRONE RESTE INVISIBLE.
                    //
                    // Le publieur pose déjà un en-tête `traceparent` sur chaque
                    // message. Il ne servait à rien : le consommateur ne le lisait pas,
                    // et aucune source ne déclarait `Hba.Kafka`. Une commande passée
                    // produisait donc une trace qui s'arrêtait au `SaveChanges`, et les
                    // huit effets qui suivent — stock réservé, paiement, notification,
                    // course créée — n'apparaissaient nulle part.
                    //
                    // C'est la moitié de la plateforme, et c'est la moitié où les
                    // pannes sont les plus dures à reproduire.
                    // ═════════════════════════════════════════════════════════════
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

                    // Compteurs du limiteur de débit : c'est ce qui permet de constater
                    // qu'une politique est trop stricte AVANT que les utilisateurs ne le
                    // signalent.
                    .AddMeter("Microsoft.AspNetCore.RateLimiting");

                if (hasEndpoint)
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = endpoint!);
                }
            });

        ConfigurerJournaux(builder, options, hasEndpoint, endpoint);

        return builder;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES JOURNAUX — SANS SERILOG, ET C'EST UNE DÉCISION (D18).
    ///
    /// Le plan de lot disait « Serilog ». Le dépôt n'en a aucun paquet : il utilise
    /// `Microsoft.Extensions.Logging` partout, avec un `LoggingBehavior` MediatR.
    /// L'ajouter aurait posé une SECONDE pile de journalisation à côté de celle
    /// d'ASP.NET Core, sur quatorze services, pour un gain que l'exportateur OTLP
    /// couvre déjà.
    ///
    /// CE QUE CE BRANCHEMENT APPORTE, ET QUE LE TEXTE NE DONNAIT PAS.
    ///
    /// `AddOpenTelemetry` sur le journaliseur attache à chaque ligne le `traceId` et
    /// le `spanId` de l'activité courante. On passe donc d'une trace lente à SES
    /// journaux d'un clic, au lieu de chercher par horodatage dans quatorze flux.
    /// C'est exactement ce qui manquait, et cela ne demande de réécrire aucun des
    /// milliers d'appels `_logger.LogInformation` existants.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static void ConfigurerJournaux(
        WebApplicationBuilder builder, TelemetryOptions options, bool hasEndpoint, Uri? endpoint)
    {
        if (options.JsonConsole)
        {
            // ON REMPLACE LE FORMATEUR, ON N'AJOUTE PAS UNE SECONDE CONSOLE.
            //
            // `AddJsonConsole` sur un journaliseur qui a déjà sa console par défaut
            // écrit CHAQUE ligne deux fois — une fois en texte, une fois en JSON.
            // Le volume double, les agents de collecte comptent chaque erreur deux
            // fois, et l'on cherche d'où vient le doublon dans le code applicatif.
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
            // introuvable » — et l'on perd le gabarit et ses paramètres. Or c'est le
            // GABARIT qui permet de compter les occurrences d'une même erreur, et le
            // paramètre qui permet de filtrer sur un identifiant. Un journal
            // structuré qui n'est structuré qu'à moitié coûte le stockage d'un
            // journal structuré et rend le service d'un journal texte.
            logging.IncludeFormattedMessage = true;
            logging.ParseStateValues = true;

            logging.AddOtlpExporter(exporter => exporter.Endpoint = endpoint!);
        });
    }
}
