using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Les trois endroits qui nomment les sujets Kafka doivent dire la même chose.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE DÉFAUT NE CASSE RIEN — IL REND DES ÉVÉNEMENTS INERTES.
///
/// C'est ISSUE-001. Le producteur dérivait son sujet de `SERVICE_NAME`, le
/// consommateur s'abonnait à une liste écrite en dur, et les manifestes
/// Kubernetes provisionnaient un troisième schéma. Un message part, le courtier
/// l'acquitte, et il n'arrive nulle part. Aucune exception, aucun avertissement,
/// aucune métrique en rouge : le seul symptôme est « ce service ne reçoit rien »,
/// des jours plus tard, et il ne désigne jamais le fichier fautif.
///
/// `HbaTopics` a fermé les deux premières sources en n'en laissant qu'une. Ce
/// contrôle empêche la troisième de re-diverger, et surveille ce que le catalogue
/// ne peut pas voir tout seul :
///
///   1. `HbaTopics.DomaineParService` — la table qui fait foi.
///   2. `docker-compose.dev.yml` — un service qui PUBLIE et qui manque à la table
///      publie sur un sujet auquel personne n'est abonné. Le repli de
///      `HbaTopics.Domaine` fabrique un nom plausible, ce qui rend le défaut plus
///      discret encore : le sujet existe, il est juste seul.
///
/// CE QUI N'EST PLUS VERIFIE, ET IL FAUT LE SAVOIR.
///
/// Ce controle comparait aussi les sujets aux manifestes `k8s/overlays/*` : les
/// sujets provisionnes devaient etre EXACTEMENT ceux que la table engendre. Un
/// sujet en trop coute du stockage et ment ; un sujet en moins est auto-cree par
/// le courtier avec UNE partition et la retention par defaut, donc sans les
/// garanties du §9.
///
/// Le chemin Kubernetes ayant ete retire du depot, cette comparaison n'a plus
/// d'objet ICI — mais le risque, lui, demeure : le provisionnement passe
/// desormais par `scripts/kafka-topics.sh`, que ce controle NE LIT PAS. Un sujet
/// oublie la-bas ne sera signale par personne.
///
/// UN `SERVICE_NAME` N'EST PAS UNE PREUVE DE PUBLICATION.
///
/// Les trois BFF et la passerelle en ont un et ne publient rien. Les quatre
/// squelettes food retirés par D30 (`menu`, `availability`, `kitchen-prep`,
/// `food-review`) ont même un `KAFKA__PRODUCER` dans le compose, et pas une ligne
/// de code qui publie. Les exiger dans la table ferait provisionner des sujets
/// pour du code qui va disparaître — et un contrôle qui crie à tort finit ignoré.
///
/// On cherche donc une TRACE de publication dans le dossier du service :
/// `IIntegrationEventPublisher`, l'outbox, un événement d'intégration. Sans
/// trace, le service est listé À PART, en constat, jamais en faute. Ce qu'on ne
/// sait pas trancher, on le montre.
///
/// CE CONTRÔLE NE REGARDE PAS LES DÉPLOIEMENTS KUBERNETES DE LA MÊME FAÇON.
///
/// Là-bas les `SERVICE_NAME` portent des noms de DOMAINE (`merchant-service`,
/// `commerce-service`…) et la découpe est plus grossière qu'en développement.
/// Aucun n'est une clé de la table : tous passent par le repli, qui tombe juste
/// par construction — `merchant-service` devient `merchant`. Le sujet est donc
/// correct, mais chaque pod journalise « producteur non inscrit » au premier
/// événement. C'est listé en constat, pas en faute. S'il retombe AILLEURS que sur
/// un domaine du catalogue, c'est un vrai orphelin — et là on échoue.
///
/// `_service/` EST UN GABARIT, PAS UN SERVICE. Son `SERVICE_NAME` vaut
/// littéralement « service » — un remplaçant que chaque kustomization corrige par
/// un patch. Le lire ferait un orphelin permanent, et un contrôle qui crie
/// toujours finit ignoré.
///
/// CE QUE LE PORTAGE A COÛTÉ, ET IL FAUT LE SAVOIR.
///
/// Le contrôle Python chargeait le compose ET les manifestes de sujets avec
/// PyYAML, et s'ignorait tout entier — « PyYAML absent, contrôle ignoré », code
/// de sortie 0 — quand le paquet manquait. Cet outil n'a aucune dépendance : la
/// lecture est textuelle. Voir <see cref="ComposeDev"/> pour le compose, et
/// <see cref="SujetsOverlay"/> pour les manifestes.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class KafkaTopicsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "kafka-topics";

    /// <inheritdoc/>
    public string Resume =>
        "les trois endroits qui nomment les sujets Kafka disent la même chose";

    // `["seller-service"] = "merchant",` — la forme exacte des entrées de la table.
    private static readonly Regex Entree = new(
        @"\[""([A-Za-z0-9._\-]+)""\]\s*=\s*""([A-Za-z0-9._\-]+)""", RegexOptions.Compiled);

    private static readonly Regex NomDeService = new(
        @"name:\s*SERVICE_NAME\s*\n\s*value:\s*([A-Za-z0-9._\-]+)", RegexOptions.Compiled);

    private static readonly Regex ClePremierNiveau = new(@"^([A-Za-z0-9_.\-]+):\s*(.*?)\s*$");

    private static readonly Regex CleImbriquee = new(@"^  ([A-Za-z0-9_.\-]+):\s*(.*?)\s*$");

    // CES MARQUEURS DISENT « CE SERVICE PUBLIE », PAS « CE SERVICE EST COMPLET ».
    //
    // Ils sont volontairement larges : un faux positif ici ne coûte qu'une entrée
    // de plus dans la table — donc un sujet provisionné pour rien. Un faux
    // négatif, lui, rendrait un vrai producteur invisible au contrôle, ce qui est
    // exactement le défaut qu'on traque.
    private static readonly string[] Marqueurs =
        ["IIntegrationEventPublisher", "IntegrationEvent", "OutboxMessage", "AddOutbox"];

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fautes = new List<string>();
        var constats = new List<string>();
        var nonCouvert = new List<string>
        {
            "la VALIDITÉ YAML du compose et des manifestes de sujets : les deux sont lus "
            + "ligne à ligne, sans analyseur. Un manifeste cassé serait lu comme un "
            + "document de moins, donc comme un sujet MANQUANT — la faute serait criée au "
            + "bon endroit, mais avec le mauvais motif",
            "les manifestes de sujets ne sont reconnus que sous leur forme écrite ici : "
            + "documents séparés par une ligne de trois tirets, `kind:` en première "
            + "colonne, `name:` et `topicName:` à deux espaces. Un ancrage YAML, un "
            + "document en style de flux ou une clé plus profonde passeraient inaperçus",
            "que le sujet provisionné soit celui que le code écrit VRAIMENT : la "
            + "dérivation est relue depuis les valeurs par défaut littérales de "
            + "`KafkaEventBusOptions`. Une surcharge par configuration, en production, "
            + "produirait d'autres noms sans que rien ici ne bouge",
            "les `SERVICE_NAME` de Kubernetes sont trouvés par expression régulière sur "
            + "deux lignes consécutives : une variable posée ailleurs dans le compose, "
            + "ou écrite autrement, ne serait pas vue",
        };

        // ── La table qui fait foi ────────────────────────────────────────────
        var dossierKafka = Depot.Dossier(
            "shared", "common", "HBA.Shared.Infrastructure", "Kafka");
        var topicsCs = Path.Combine(dossierKafka, "HbaTopics.cs");
        var optionsCs = Path.Combine(dossierKafka, "KafkaEventBusOptions.cs");

        if (!File.Exists(topicsCs) || !File.Exists(optionsCs))
        {
            fautes.Add(
                "HbaTopics.cs ou KafkaEventBusOptions.cs est absent de "
                + "shared/common/HBA.Shared.Infrastructure/Kafka : la table qui fait foi "
                + "est introuvable, et ce contrôle ne peut RIEN comparer.");
            return new Verdict(fautes, constats, nonCouvert);
        }

        var catalogue = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match entree in Entree.Matches(File.ReadAllText(topicsCs)))
        {
            catalogue[entree.Groups[1].Value] = entree.Groups[2].Value;
        }

        if (catalogue.Count == 0)
        {
            fautes.Add(
                "shared/common/HBA.Shared.Infrastructure/Kafka/HbaTopics.cs : aucune entrée "
                + "trouvée — le format de la table a-t-il changé ? Attendu : "
                + "[« nom-service »] = « domaine »,");
            return new Verdict(fautes, constats, nonCouvert);
        }

        var options = File.ReadAllText(optionsCs);
        var prefixe = ValeurParDefaut(options, "TopicPrefix");
        var version = ValeurParDefaut(options, "TopicVersion");

        if (prefixe is null || version is null)
        {
            fautes.Add(
                "KafkaEventBusOptions : TopicPrefix ou TopicVersion n'a plus de valeur par "
                + "défaut littérale — le contrôle ne peut plus dériver les noms de sujets, "
                + "et se tairait sur les trois environnements à la fois.");
            return new Verdict(fautes, constats, nonCouvert);
        }

        var domaines = catalogue.Values.ToHashSet(StringComparer.Ordinal);
        var attendus = domaines
            .Select(d => $"{prefixe}.{d}.{version}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        constats.Add(
            $"{catalogue.Count} service(s) au catalogue → {attendus.Count} sujet(s) "
            + $"« {prefixe}.<domaine>.{version} ».");

        // ── 1. Les producteurs du compose ────────────────────────────────────
        var sansTrace = new List<string>();

        foreach (var service in ComposeDev.Services()
                     .OrderBy(s => s.Nom, StringComparer.Ordinal))
        {
            // `Kafka:Producer` prime sur `SERVICE_NAME` — c'est l'ordre de
            // `KafkaEventNaming.Producer`, appelé par le publieur.
            var producteur = service.Environnement.GetValueOrDefault("KAFKA__PRODUCER");
            if (string.IsNullOrEmpty(producteur))
            {
                producteur = service.Environnement.GetValueOrDefault("SERVICE_NAME");
            }

            if (string.IsNullOrEmpty(producteur) || catalogue.ContainsKey(producteur))
            {
                continue;
            }

            var dossier = ComposeDev.DossierDeBuild(service);
            var trace = dossier is not null && Directory.Exists(dossier) && Publie(dossier);

            if (!trace)
            {
                // CONSTAT, ET C'EST DÉLIBÉRÉ. Un `SERVICE_NAME` sans une ligne qui
                // publie décrit un BFF, la passerelle ou un squelette. Les compter
                // en faute obligerait à inscrire au catalogue des services qui ne
                // publieront peut-être jamais — et à provisionner leurs sujets en
                // production.
                sansTrace.Add($"{service.Nom} — SERVICE_NAME/KAFKA__PRODUCER « {producteur} »");
                continue;
            }

            var derive = producteur.Replace("-service", string.Empty);
            fautes.Add(
                $"{service.Nom} : « {producteur} » publie et n'est pas dans "
                + "HbaTopics.DomaineParService ; ses événements partiraient sur "
                + $"« {prefixe}.{derive}.{version} », auquel personne ne s'abonne");
        }

        if (sansTrace.Count > 0)
        {
            constats.Add(
                "déclarés producteurs, aucune trace de publication dans le code "
                + "(BFF, passerelle, squelettes : à trancher à la main, pas un échec) :");
            constats.AddRange(sansTrace.Select(s => "  " + s));
        }

        return new Verdict(fautes, constats, nonCouvert);
    }

    /// <summary>La valeur par défaut littérale d'une propriété de configuration.</summary>
    private static string? ValeurParDefaut(string source, string propriete)
    {
        var motif = new Regex(
            @"public\s+string\s+" + Regex.Escape(propriete) + @"\s*\{[^}]*\}\s*=\s*""([^""]+)""");
        var trouve = motif.Match(source);
        return trouve.Success ? trouve.Groups[1].Value : null;
    }

    /// <summary>Le dossier d'un service contient-il une trace de publication ?</summary>
    private static bool Publie(string dossier)
    {
        foreach (var fichier in Depot.Fichiers(dossier, ".cs"))
        {
            // Les migrations citent les événements sans les publier : les lire
            // ferait passer pour producteur un service qui ne l'est pas.
            var relatif = "/" + Depot.Relatif(fichier).Replace('\\', '/') + "/";
            if (relatif.Contains("/Migrations/", StringComparison.Ordinal))
            {
                continue;
            }

            var texte = File.ReadAllText(fichier);
            if (Marqueurs.Any(m => texte.Contains(m, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

}
