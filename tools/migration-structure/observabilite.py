#!/usr/bin/env python3
"""
L'OBSERVABILITE DESCEND DANS CHAQUE SERVICE — ET ELLE COMBLE UN VRAI TROU.

CE QUI EXISTAIT.

`AddHbaService` posait l'instrumentation OpenTelemetry pour les vingt-six services
d'un coup, et UNE SEULE sonde de sante :

    services.AddHealthChecks().AddDbContextCheck<TDbContext>("database", tags: ["ready"]);

La base est verifiee. Redis, Kafka et gRPC ne le sont pas. Un service dont le
consommateur Kafka est mort repond donc « ready » — et le deploiement individuel
qu'on vient d'ajouter (`up -d --no-deps <service>`) le croit sain. C'est la panne
de ce mois-ci qui redevient invisible, a l'endroit ou l'on venait de gagner en
visibilite.

CE QUI DESCEND : les sondes, les metriques du service, sa source d'activite, et
le point d'entree `AjouterObservabilite<Service>()`.

CE QUI RESTE PARTAGE : le PIPELINE OpenTelemetry (`AddHbaTelemetry`) — exportateur,
protocole, ressource. C'est du deploiement, pas de la politique de service : deux
configurations d'exportateur, ce sont deux formats de trace pour un meme
collecteur. Et les INTERFACES de metriques (`IPaymentMetrics`, `IOutboxMetrics`…),
qui sont les ports dont depend la couche Application.

TROIS DECISIONS QUI MERITENT D'ETRE LUES.

1. LA SONDE KAFKA INTERROGE VRAIMENT LE COURTIER. Une sonde qui rendrait
   « Healthy » sans rien verifier serait pire que pas de sonde : elle donnerait
   une garantie fausse. Elle demande les metadonnees au courtier, avec un delai
   court, sur un `IAdminClient` construit UNE FOIS — pas a chaque appel de la
   sonde, qui est appelee toutes les quelques secondes par l'orchestrateur.

2. LA SONDE gRPC N'EST PAS DANS `ready`, ET C'EST DELIBERE. Une sonde de
   disponibilite qui echoue parce qu'un AUTRE service est tombe transforme une
   panne en N pannes : l'orchestrateur retire du service un conteneur
   parfaitement sain. Elle est donc taguee `dependencies` et rend `Degraded`,
   jamais `Unhealthy`. Elle sert un tableau de bord, pas une decision de
   redemarrage.

3. LA SONDE REDIS PASSE PAR `IDistributedCache`, pas par un client Redis. C'est
   ce que le service utilise reellement ; verifier autre chose que ce que le code
   emprunte, c'est verifier autre chose.
"""
import os, re, io, sys, shutil
import re

RACINE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SKIP = {"obj", "bin", ".git", "build", "node_modules"}
SEC = "--ecrire" in sys.argv
SOURCE_METRIQUES = os.path.join(RACINE, "shared", "common", "HBA.Shared.Infrastructure",
                                "Observability", "NoOpMetrics.cs")


def fichiers_cs(base):
    for d, dirs, fs in os.walk(base):
        dirs[:] = [x for x in dirs if x not in SKIP]
        for f in fs:
            if f.endswith(".cs"):
                yield os.path.join(d, f)


def racine_de_namespace(dossier):
    declares = []
    for f in fichiers_cs(dossier):
        m = re.search(r'^namespace\s+([\w\.]+)', io.open(f, encoding="utf-8", errors="replace").read(), re.M)
        if m: declares.append(m.group(1).split("."))
    if not declares: return None
    commun = declares[0]
    for d in declares[1:]:
        n = 0
        while n < min(len(commun), len(d)) and commun[n] == d[n]: n += 1
        commun = commun[:n]
    while len(commun) > 1 and os.path.isdir(os.path.join(dossier, commun[-1])):
        commun = commun[:-1]
    return ".".join(commun) if commun else None


def projets():
    for base in ("services", "bff"):
        for d, dirs, fs in os.walk(os.path.join(RACINE, base)):
            dirs[:] = [x for x in dirs if x not in SKIP]
            if os.path.basename(d) == "src":
                for x in sorted(dirs):
                    if x.endswith(".Infrastructure"):
                        yield os.path.join(d, x)


def ecrire(chemin, contenu):
    os.makedirs(os.path.dirname(chemin), exist_ok=True)
    l = os.path.join(os.path.dirname(chemin), "LISEZMOI.md")
    if os.path.exists(l):
        os.remove(l)
    io.open(chemin, "w", encoding="utf-8").write(contenu)


def main():
    cibles = list(projets())
    print(f"{len(cibles)} projets d'infrastructure")
    if not SEC:
        print("SIMULATION.")
        return

    metriques = io.open(SOURCE_METRIQUES, encoding="utf-8").read()

    for infra in cibles:
        racine_ns = racine_de_namespace(infra)
        court = os.path.basename(infra).replace("HBA.", "").replace(".Infrastructure", "").replace(".", "")
        a_du_grpc = os.path.isdir(os.path.join(infra, "Grpc", "Clients")) and any(
            f.endswith(".cs") for f in os.listdir(os.path.join(infra, "Grpc", "Clients")))

        # ── Metrics : les implementations neutres appartiennent au service
        m = metriques.replace("namespace HBA.Shared.Infrastructure.Observability;",
                              f"""// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Shared.Infrastructure.Observability`.
//
// Les INTERFACES restent partagees — ce sont les ports dont depend la couche
// Application. Les implementations neutres, elles, appartiennent au service :
// c'est lui qui decide s'il compte quelque chose, et par quoi il remplace le
// neutre le jour ou il compte vraiment.
// ═════════════════════════════════════════════════════════════════════════════

namespace {racine_ns}.Observability.Metrics;""")
        ecrire(os.path.join(infra, "Observability", "Metrics", "MetriquesNeutres.cs"), m)

        # ── Tracing : la source d'activite du service
        ecrire(os.path.join(infra, "Observability", "Tracing", "ActivitySources.cs"), f"""using System.Diagnostics;

namespace {racine_ns}.Observability.Tracing;

/// <summary>
/// La source d'activite de ce service.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// STATIQUE, ET C'EST LA REGLE D'OpenTelemetry, PAS UN RACCOURCI.
///
/// Une `ActivitySource` est faite pour vivre aussi longtemps que le processus :
/// en creer une par requete produirait des sources orphelines que le collecteur
/// ne rattache a rien.
///
/// LE NOM EST CELUI DU MODULE, PAS DU SERVICE HTTP. Trois hotes montent plusieurs
/// modules dans un processus ; un nom par hote melangerait leurs traces sans
/// qu'on puisse les separer apres coup.
///
/// CE QUE ÇA NE FAIT PAS : declarer la source ne l'exporte pas. Le pipeline
/// OpenTelemetry — exportateur, protocole, ressource — reste pose par
/// `AddHbaTelemetry`, dans le socle : deux configurations d'exportateur seraient
/// deux formats de trace pour un meme collecteur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class SourcesDActivite
{{
    public const string Nom = "{racine_ns}";

    public static readonly ActivitySource Source = new(Nom);
}}
""")

        # ── HealthChecks
        ecrire(os.path.join(infra, "Observability", "HealthChecks", "RedisHealthCheck.cs"), f"""using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace {racine_ns}.Observability.HealthChecks;

/// <summary>
/// Le cache repond-il ?
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ELLE PASSE PAR `IDistributedCache`, PAS PAR UN CLIENT REDIS.
///
/// C'est ce que le service emprunte reellement. Verifier autre chose que le
/// chemin du code, c'est verifier autre chose : une sonde qui ouvrirait sa propre
/// connexion Redis pourrait etre verte pendant que le cache du service, mal
/// configure, retombe en memoire.
///
/// CE QU'ELLE NE DIT PAS : si le cache est PARTAGE. Un service retombe sur le
/// cache memoire repond « Healthy » — il a bien un cache, il n'a simplement pas
/// celui qu'on croit. C'est le message de demarrage d'`AjouterCache{court}` qui
/// porte cette nuance, pas cette sonde.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class SondeDuCache : IHealthCheck
{{
    private const string Cle = "hba:sonde";

    private readonly IDistributedCache _cache;

    public SondeDuCache(IDistributedCache cache) => _cache = cache;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {{
        try
        {{
            await _cache.GetAsync(Cle, cancellationToken);
            return HealthCheckResult.Healthy();
        }}
        catch (Exception exception)
        {{
            return HealthCheckResult.Unhealthy("Le cache ne repond pas.", exception);
        }}
    }}
}}
""")

        ecrire(os.path.join(infra, "Observability", "HealthChecks", "KafkaHealthCheck.cs"), f"""using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace {racine_ns}.Observability.HealthChecks;

/// <summary>
/// Le courtier Kafka repond-il ?
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE TROU QUE CETTE SONDE FERME.
///
/// Jusqu'ici, `ready` ne verifiait QUE la base. Un service dont le consommateur
/// Kafka etait mort repondait donc « pret », et le deploiement individuel
/// (`up -d --no-deps`) le croyait sain. C'est exactement la panne de ce mois-ci,
/// a l'endroit ou l'on venait de gagner en visibilite.
///
/// ELLE INTERROGE VRAIMENT LE COURTIER. Une sonde qui rendrait « Healthy » sans
/// rien verifier serait pire que pas de sonde : elle donnerait une garantie
/// fausse — le defaut meme que ce depot refuse partout ailleurs.
///
/// L'`IAdminClient` EST CONSTRUIT UNE FOIS. La sonde est appelee toutes les
/// quelques secondes par l'orchestrateur ; ouvrir une connexion a chaque appel
/// couterait plus que ce qu'elle mesure.
///
/// CE QU'ELLE NE DIT PAS : que CE service consomme. Elle dit que le courtier est
/// joignable et que les metadonnees arrivent. Un groupe de consommateurs bloque
/// en rebalancement perpetuel la laisserait verte — pour cela il faudrait lire le
/// decalage du groupe, ce que cette sonde ne fait pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class SondeDeKafka : IHealthCheck, IDisposable
{{
    private static readonly TimeSpan Delai = TimeSpan.FromSeconds(3);

    private readonly IAdminClient? _admin;

    public SondeDeKafka(IConfiguration configuration)
    {{
        var serveurs = configuration["Kafka:BootstrapServers"];

        // KAFKA ETEINT N'EST PAS UNE PANNE. Les harnais de tests posent
        // « Kafka:Enabled=false » ; une sonde rouge y ferait echouer des tests qui
        // n'ont rien a voir avec le bus.
        var actif = !string.Equals(configuration["Kafka:Enabled"], "false", StringComparison.OrdinalIgnoreCase);

        if (actif && !string.IsNullOrWhiteSpace(serveurs))
        {{
            _admin = new AdminClientBuilder(new AdminClientConfig {{ BootstrapServers = serveurs }}).Build();
        }}
    }}

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {{
        if (_admin is null)
        {{
            return Task.FromResult(HealthCheckResult.Healthy("Kafka desactive pour cet hote."));
        }}

        try
        {{
            var metadonnees = _admin.GetMetadata(Delai);

            return Task.FromResult(metadonnees.Brokers.Count > 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Aucun courtier dans les metadonnees."));
        }}
        catch (Exception exception)
        {{
            return Task.FromResult(HealthCheckResult.Unhealthy("Le courtier ne repond pas.", exception));
        }}
    }}

    public void Dispose() => _admin?.Dispose();
}}
""")

        if a_du_grpc:
            ecrire(os.path.join(infra, "Observability", "HealthChecks", "GrpcHealthCheck.cs"), f"""using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace {racine_ns}.Observability.HealthChecks;

/// <summary>
/// Les destinations gRPC de ce service sont-elles configurees ?
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ELLE N'EST PAS DANS `ready`, ET C'EST LE POINT LE PLUS IMPORTANT DE CE FICHIER.
///
/// Une sonde de disponibilite qui echoue parce qu'un AUTRE service est tombe
/// transforme une panne en N pannes : l'orchestrateur retire du service un
/// conteneur parfaitement sain, dont la seule faute est d'avoir un voisin malade.
/// C'est le mecanisme par lequel une panne d'un service devient une panne de
/// plateforme.
///
/// Elle est donc taguee `dependencies`, rend `Degraded` et JAMAIS `Unhealthy`, et
/// ne sert qu'a un tableau de bord — pas a une decision de redemarrage.
///
/// CE QU'ELLE VERIFIE REELLEMENT : que les adresses declarees sont presentes et
/// analysables. Pas qu'elles repondent — le disjoncteur et l'echeance de cinq
/// secondes s'en chargent au moment de l'appel, la ou l'information sert.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class SondeDesDestinationsGrpc : IHealthCheck
{{
    private readonly IConfiguration _configuration;

    public SondeDesDestinationsGrpc(IConfiguration configuration) => _configuration = configuration;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {{
        var absentes = _configuration.GetSection("Services").GetChildren()
            .Where(entree => string.IsNullOrWhiteSpace(entree.Value)
                             || !Uri.TryCreate(entree.Value, UriKind.Absolute, out _))
            .Select(entree => entree.Key)
            .ToArray();

        return Task.FromResult(absentes.Length == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded(
                "Destinations gRPC absentes ou illisibles : " + string.Join(", ", absentes)));
    }}
}}
""")

        # ── le module
        # LE NOM DE SONDE PORTE LE MODULE. Trois hotes montent plusieurs
        # modules dans un seul processus ; `DefaultHealthCheckService` refuse
        # deux sondes de meme nom, et il le fait a la RESOLUTION, donc au
        # premier `MapHealthChecks`. Voir observabilite_noms.py.
        slug = re.sub(r"(?<!^)(?=[A-Z])", "-", court).lower()

        grpc_enr = ("""
        // LA SONDE gRPC N'EST PAS DANS `ready` — voir son encadre. Une sonde de
        // disponibilite qui tombe avec un voisin transforme une panne en N pannes.
        services.AddHealthChecks().AddCheck<SondeDesDestinationsGrpc>(
            "grpc-{slug}", tags: ["dependencies"]);
""".replace("{slug}", slug) if a_du_grpc else "")

        ecrire(os.path.join(infra, "Observability", "DependencyInjection.cs"), f"""using {racine_ns}.Observability.HealthChecks;
using {racine_ns}.Observability.Metrics;
using HBA.Shared.Application.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace {racine_ns}.Observability;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'OBSERVABILITE DE CE SERVICE — UN SEUL POINT D'ENTREE.
///
/// CE QUI EST ICI : ce que ce service expose de lui-meme — ses sondes, ses
/// metriques, sa source d'activite.
///
/// CE QUI RESTE AU SOCLE : le pipeline OpenTelemetry (`AddHbaTelemetry`) et la
/// sonde de base de donnees, posee par `AddHbaService` avec le `TDbContext` de
/// l'hote. Deux configurations d'exportateur seraient deux formats de trace pour
/// un meme collecteur.
///
/// LES TAGS DECIDENT DE CE QUE FAIT L'ORCHESTRATEUR :
///
///   ready         — la base, le cache, le courtier. Rouge = ce conteneur ne peut
///                   pas travailler, on le retire du service.
///   dependencies  — les voisins. Jamais rouge : voir `SondeDesDestinationsGrpc`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DependencyInjection
{{
    public static IServiceCollection AjouterObservabilite{court}(
        this IServiceCollection services, IConfiguration configuration)
    {{
        // LES METRIQUES NEUTRES DE CE SERVICE.
        //
        // `TryAdd` : un service qui compte vraiment quelque chose enregistre SON
        // implementation avant d'appeler ce module, et elle gagne.
        services.TryAddSingleton<IPaymentMetrics, NoOpPaymentMetrics>();
        services.TryAddSingleton<IHbaBusinessMetrics, NoOpBusinessMetrics>();
        services.TryAddSingleton<ISecurityMetrics, NoOpSecurityMetrics>();
        services.TryAddSingleton<IOutboxMetrics, NoOpOutboxMetrics>();

        // SINGLETON : `AddCheck<T>` resout par `GetServiceOrCreateInstance`, donc
        // sans lui une NOUVELLE sonde — et une connexion au courtier — serait
        // construite a chaque appel de l'orchestrateur.
        services.AddSingleton<SondeDeKafka>();

        // LE NOM PORTE LE MODULE : dans un hote compose, chaque module a SON cache
        // et SON courtier. Le verdict de l'orchestrateur ne change pas, il agrege
        // sur le TAG `ready`, jamais sur le nom.
        services.AddHealthChecks()
            .AddCheck<SondeDuCache>("cache-{slug}", tags: ["ready"])
            .AddCheck<SondeDeKafka>("kafka-{slug}", tags: ["ready"]);
{grpc_enr}
        return services;
    }}
}}
""")

        # ── paquets
        csproj = [os.path.join(infra, f) for f in os.listdir(infra) if f.endswith(".csproj")][0]
        s = io.open(csproj, encoding="utf-8").read()
        # PAS DE PAQUET POUR LES SONDES : `Microsoft.Extensions.Diagnostics.HealthChecks`
        # est fourni par Microsoft.AspNetCore.App, et le SDK en pose une reference
        # implicite AVEC version. En ajouter une explicite sous gestion centralisee
        # des versions donne NU1008 sur les vingt-six projets d'un coup.
        manquants = [p for p in ("Confluent.Kafka",) if f'"{p}"' not in s]
        if manquants:
            bloc = ("\n  <!-- L'OBSERVABILITE DE CE SERVICE (Observability/). Les sondes\n"
                    "       interrogent vraiment le courtier et le cache ; ces paquets venaient\n"
                    "       transitivement du socle. -->\n  <ItemGroup>\n"
                    + "".join(f'    <PackageReference Include="{p}" />\n' for p in manquants)
                    + "  </ItemGroup>\n")
            s = s.replace("</Project>", bloc + "\n</Project>")
            io.open(csproj, "w", encoding="utf-8").write(s)

        # ── l'appel, la ou le cache est deja branche
        for d, dirs, fs in os.walk(infra):
            dirs[:] = [x for x in dirs if x not in SKIP]
            for f in fs:
                if not f.endswith(".cs"):
                    continue
                p = os.path.join(d, f)
                if os.sep + "Caching" + os.sep in p:
                    continue
                t = io.open(p, encoding="utf-8").read()
                if f"services.AjouterCache{court}(configuration);" not in t:
                    continue
                if f"AjouterObservabilite{court}" in t:
                    continue
                t = t.replace(
                    f"        services.AjouterCache{court}(configuration);",
                    f"        services.AjouterCache{court}(configuration);\n\n"
                    "        // LES SONDES DE CE SERVICE (Observability/). Jusqu'ici seule la base\n"
                    "        // etait verifiee : un service dont le consommateur Kafka etait mort\n"
                    "        // repondait « ready », et le deploiement individuel le croyait sain.\n"
                    f"        services.AjouterObservabilite{court}(configuration);")
                besoin = f"using {racine_ns}.Observability;"
                if not re.search(r'^' + re.escape(besoin), t, re.M):
                    u = list(re.finditer(r'^using\s+[^\n]+;\s*$', t, re.M))
                    t = t[:u[-1].end()] + "\n" + besoin + t[u[-1].end():]
                io.open(p, "w", encoding="utf-8").write(t)

    # le socle ne porte plus les implementations neutres
    os.remove(SOURCE_METRIQUES)
    print("modules d'observabilite ecrits ; NoOpMetrics retire du socle.")


if __name__ == "__main__":
    main()
