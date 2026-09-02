using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Ce que l'extraction a laissé derrière : les consommateurs d'événements.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// ON A DÉMÉNAGÉ LES MODULES, PAS CE QUI LES RELIAIT.
///
/// Dans le monolithe, les ponts entre modules vivaient dans la composition root
/// (`Marketplace.Api/Integration`) — le seul endroit ayant le droit de connaître
/// deux mondes à la fois. En extrayant un module vers son service, on emporte
/// son domaine, son application, sa persistance et ses routes. Le fichier qui le
/// RELIAIT aux autres reste dans le monolithe.
///
/// Le premier cas trouvé : identity-service publiait consciencieusement
/// `UserRegisteredIntegrationEvent`, et personne ne l'écoutait. Un compte se
/// créait dans `identity.users`, aucune ligne n'apparaissait dans
/// `users.profiles`.
///
/// CETTE PANNE-LÀ EST TOTALEMENT MUETTE.
///
/// Un événement sans destinataire ne se plaint pas. Le producteur réussit, le
/// courtier stocke, et rien ne signale que le fait publié n'a produit aucun
/// effet. Ni le compilateur, ni les tests unitaires, ni les journaux. On le
/// découvre en regardant une table vide et en se demandant pourquoi.
///
/// CE QUE CE CONTRÔLE COMPARE, ET SA LIMITE.
///
/// Il recense les `IIntegrationEventHandler&lt;X&gt;` de chaque côté et signale
/// les X consommés dans le monolithe et plus dans HBA. Il ne dit PAS si c'est
/// grave : beaucoup de ces événements appartiennent à des modules non encore
/// extraits (Search, Disputes, Shipping, Products), et leur consommateur
/// reviendra avec eux.
///
/// C'est une LISTE À TRIER, pas une liste d'erreurs — d'où son passage en
/// CONSTAT et non en FAUTE. Sa valeur est de la rendre visible plutôt que de la
/// laisser se découvrir une table vide à la fois.
///
/// ET PENDANT TOUT CE TEMPS, IL BALAYAIT UN DOSSIER QUI N'EXISTE PAS.
///
/// Le côté HBA était lu depuis `&lt;dépôt&gt;/src` — le chemin du monolithe,
/// supprimé par la réorganisation en monorepo. Le parcours d'un dossier absent
/// ne lève pas en Python, il n'itère pas : le script comptait « 0 événement
/// consommé dans HBA ». Il en compte plus de soixante depuis la réparation.
///
/// Deux conséquences se cachaient derrière ce zéro :
///   • si le monolithe avait été présent, TOUS ses événements auraient été
///     déclarés « perdus » — cent faux positifs d'un coup ;
///   • la détection des noms d'événements AMBIGUS, qui ne dépend que de HBA,
///     balayait le même chemin fantôme et ne pouvait rien trouver. Elle était de
///     surcroît sautée d'office quand le monolithe manquait, c'est-à-dire
///     toujours.
///
/// Le côté HBA passe donc par <see cref="SourceCsharp.Fichiers"/>, qui s'appuie
/// sur <see cref="Depot.Dossier"/> et LÈVE quand une racine déclarée manque.
/// Le MONOLITHE, lui, reste volontairement hors dépôt : son absence est une
/// information, pas une panne, et se teste à la main.
///
/// LA COMPARAISON EST OPTIONNELLE ; LE RESTE DU CONTRÔLE NE L'EST PAS.
///
/// Le script sortait sur-le-champ quand le monolithe était absent —
/// c'est-à-dire presque toujours. Il emportait avec lui la détection des noms
/// d'événements AMBIGUS, qui ne dépend que du dépôt HBA et n'avait aucune raison
/// d'être conditionnée à la présence d'un dossier externe.
///
/// CE QUI DIFFÈRE DU SCRIPT D'ORIGINE, ET POURQUOI.
///
/// Le script n'échouait qu'avec `--strict`, et seulement sur les consommateurs
/// perdus. Ici, la FAUTE est le nom d'événement AMBIGU : il ne dépend que de ce
/// dépôt, il est déterministe, et c'est une dette réelle. Les consommateurs
/// perdus restent un constat, conformément au texte ci-dessus qui dit
/// explicitement qu'ils forment une liste à trier.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class EventConsumersControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "event-consumers";

    /// <inheritdoc/>
    public string Resume => "aucun événement d'intégration n'est déclaré dans deux espaces de noms";

    private static readonly Regex Gestionnaire = new(
        @"IIntegrationEventHandler<\s*([A-Za-z]+IntegrationEvent)\s*>",
        RegexOptions.Compiled);

    private static readonly Regex Declaration = new(
        @"record ([A-Za-z]+IntegrationEvent)\b",
        RegexOptions.Compiled);

    private static readonly Regex EspaceDeNoms = new(
        @"^namespace ([\w.]+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // ═══════════════════════════════════════════════════════════════════════
    // MODULES DONT ON SAIT QU'ILS N'ONT PAS ENCORE ÉTÉ EXTRAITS.
    //
    // Leurs consommateurs reviendront avec eux ; les lister comme des pertes
    // serait du bruit, et un contrôle bruyant finit par n'être plus lu.
    // ═══════════════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, string[]> NonExtraits = new(StringComparer.Ordinal)
    {
        ["Search"] =
        [
            "ProductCreatedIntegrationEvent", "ProductOfferCreatedIntegrationEvent",
            "ProductOfferPriceChangedIntegrationEvent",
            "ProductOfferStatusChangedIntegrationEvent", "ReviewRejectedIntegrationEvent",
        ],
        ["Disputes"] = ["DisputeOpenedIntegrationEvent", "DisputeResolvedIntegrationEvent"],
        ["Shipping"] = ["ShipmentReadyForPickupIntegrationEvent"],
        ["Products/Offers"] =
        [
            "ProductStatusChangedIntegrationEvent", "ProductVariantDeactivatedIntegrationEvent",
            "StockReplenishedIntegrationEvent", "StoreOpenedIntegrationEvent",
            "StoreClosedIntegrationEvent", "ProductMediaRemovedIntegrationEvent",
        ],
    };

    /// <summary>
    /// Le monolithe de référence, VOLONTAIREMENT HORS DÉPÔT.
    /// </summary>
    /// <remarks>
    /// C'est l'ancien monolithe gardé à côté du monorepo. Il n'est pas versionné
    /// ici et manque la plupart du temps. NE PAS CONFONDRE avec les racines du
    /// dépôt lui-même qui, elles, DOIVENT exister — voir
    /// <see cref="Depot.Dossier"/>. Son absence se teste à la main et se dit ;
    /// elle ne lève pas.
    /// </remarks>
    private static string CheminMonolithe()
        => Path.GetFullPath(Path.Combine(Depot.Racine, "..", "src"));

    /// <summary>
    /// Événements consommés sous `racine`, et les fichiers qui les consomment.
    /// </summary>
    /// <remarks>
    /// RÉSERVÉ AU MONOLITHE. Pour le dépôt HBA, passer par
    /// <see cref="ConsommateursHba"/> : cette fonction-ci rend silencieusement
    /// un dictionnaire vide sur un chemin inexistant, ce qui est acceptable pour
    /// une référence externe optionnelle et ne l'est PAS pour le code qu'on
    /// prétend contrôler.
    /// </remarks>
    private static Dictionary<string, SortedSet<string>> ConsommateursDuMonolithe(string racine)
    {
        var trouves = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        if (!Directory.Exists(racine))
        {
            return trouves;
        }

        foreach (var fichier in Directory.EnumerateFiles(racine, "*.cs", SearchOption.AllDirectories))
        {
            var dossier = (Path.GetDirectoryName(fichier) ?? string.Empty)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (dossier.Contains("/obj/", StringComparison.Ordinal)
                || dossier.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match m in Gestionnaire.Matches(File.ReadAllText(fichier)))
            {
                Ajouter(trouves, m.Groups[1].Value, Path.GetFileName(fichier));
            }
        }

        return trouves;
    }

    /// <summary>Événements consommés DANS CE DÉPÔT.</summary>
    /// <remarks>
    /// C'EST ICI QUE LE CONTRÔLE NE REGARDAIT RIEN. Le script lisait
    /// `&lt;dépôt&gt;/src`, un dossier qui vient du monolithe et n'existe pas
    /// dans le monorepo. Il concluait donc que HBA ne consommait AUCUN
    /// événement, et présentait par conséquent TOUS les événements du monolithe
    /// comme des consommateurs perdus… sauf que le monolithe étant lui aussi
    /// absent, il s'arrêtait avant, sur « comparaison impossible ». Deux silences
    /// superposés, et un verdict qui n'avait jamais rien vérifié.
    /// </remarks>
    private static Dictionary<string, SortedSet<string>> ConsommateursHba(
        IReadOnlyList<(string Chemin, string Texte)> sources)
    {
        var trouves = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var (chemin, texte) in sources)
        {
            foreach (Match m in Gestionnaire.Matches(texte))
            {
                Ajouter(trouves, m.Groups[1].Value, Path.GetFileName(chemin));
            }
        }

        return trouves;
    }

    /// <summary>
    /// Un même événement déclaré dans deux espaces est une bombe à retardement.
    /// </summary>
    /// <remarks>
    /// L'enveloppe Kafka ne transporte que le NOM court — « order.confirmed ».
    /// Le consommateur retrouve le type en balayant les assemblies chargées. Si
    /// deux y répondent, le vainqueur dépendait de l'ordre de chargement.
    ///
    /// Et si un gestionnaire est enregistré pour L'AUTRE type,
    /// `IIntegrationEventHandler&lt;T&gt;` ne correspond pas, aucun gestionnaire
    /// n'est trouvé, et l'événement passe SANS EFFET ni erreur.
    ///
    /// La résolution est désormais déterministe et signalée à l'exécution, mais
    /// le duplicata reste une dette : deux contrats pour un même fait finissent
    /// par diverger.
    /// </remarks>
    private static SortedDictionary<string, SortedSet<string>> Doublons(
        IReadOnlyList<(string Chemin, string Texte)> sources)
    {
        var parNom = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var (_, texte) in sources)
        {
            var espace = EspaceDeNoms.Match(texte);
            var nomEspace = espace.Success ? espace.Groups[1].Value : "?";
            foreach (Match m in Declaration.Matches(texte))
            {
                Ajouter(parNom, m.Groups[1].Value, nomEspace);
            }
        }

        var ambigus = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var paire in parNom)
        {
            if (paire.Value.Count > 1)
            {
                ambigus[paire.Key] = paire.Value;
            }
        }

        return ambigus;
    }

    private static void Ajouter(
        Dictionary<string, SortedSet<string>> table, string cle, string valeur)
    {
        if (!table.TryGetValue(cle, out var ensemble))
        {
            ensemble = new SortedSet<string>(StringComparer.Ordinal);
            table[cle] = ensemble;
        }

        ensemble.Add(valeur);
    }

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var sources = SourceCsharp.Fichiers()
            .Select(c => (Chemin: c, Texte: File.ReadAllText(c)))
            .ToList();

        var hba = ConsommateursHba(sources);
        var constats = new List<string>();
        var nonCouvert = new List<string>();

        var connus = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in NonExtraits)
        {
            foreach (var evenement in module.Value)
            {
                connus[evenement] = module.Key;
            }
        }

        var monolithe = CheminMonolithe();
        var aTraiter = new List<string>();
        var attendus = new List<string>();

        if (Directory.Exists(monolithe))
        {
            var mono = ConsommateursDuMonolithe(monolithe);
            var perdus = mono.Keys
                .Where(e => !hba.ContainsKey(e))
                .OrderBy(e => e, StringComparer.Ordinal)
                .ToList();

            aTraiter = perdus.Where(e => !connus.ContainsKey(e)).ToList();
            attendus = perdus.Where(e => connus.ContainsKey(e)).ToList();

            foreach (var evenement in aTraiter)
            {
                constats.Add(
                    $"consommateur perdu, LES DEUX CÔTÉS ÉTANT EXTRAITS : {evenement} "
                    + $"(monolithe : {string.Join(", ", mono[evenement])}). À TRIER — le "
                    + "contrôle ne tranche pas s'il s'agit d'un oubli d'extraction.");
            }

            foreach (var evenement in attendus)
            {
                constats.Add(
                    $"attendu — le module consommateur « {connus[evenement]} » n'est pas "
                    + $"encore extrait : {evenement}");
            }
        }
        else
        {
            // L'ABSENCE DU MONOLITHE EST UNE INFORMATION, PAS UNE PANNE — mais
            // elle DOIT être dite : sans cette ligne, un contrôle amputé de la
            // moitié de son travail rendrait le même vert qu'un contrôle complet.
            constats.Add(
                $"monolithe introuvable ({monolithe}) — comparaison des consommateurs "
                + "perdus sautée ; le reste du contrôle porte sur le dépôt HBA seul et "
                + "s'exécute normalement.");
            nonCouvert.Add(
                "les consommateurs d'événements perdus à l'extraction : le monolithe de "
                + $"référence ({monolithe}) est absent, la comparaison n'a PAS eu lieu");
        }

        var ambigus = Doublons(sources);
        var fautes = new List<string>();
        foreach (var paire in ambigus)
        {
            fautes.Add(
                $"« {paire.Key} » est déclaré dans PLUSIEURS espaces de noms : "
                + $"{string.Join(", ", paire.Value)}. L'enveloppe Kafka ne porte que le nom "
                + "court : un gestionnaire enregistré pour l'un de ces types n'est jamais "
                + "appelé si le consommateur résout l'autre — sans erreur.");
        }

        constats.Add(
            $"{hba.Count} événement(s) consommé(s) dans HBA, {aTraiter.Count} perte(s) à "
            + $"traiter, {attendus.Count} attendue(s), {ambigus.Count} nom(s) ambigu(s).");

        nonCouvert.Add(
            "les gestionnaires qui n'écrivent pas `IIntegrationEventHandler<X>` "
            + "littéralement — un alias, un type générique ouvert ou une inscription par "
            + "réflexion sont invisibles");
        nonCouvert.Add(
            "qu'un événement PUBLIÉ ait un consommateur : ce contrôle recense les "
            + "consommateurs, il ne rapproche pas les producteurs des consommateurs");
        nonCouvert.Add(
            "les événements déclarés autrement que par `record NomIntegrationEvent` — une "
            + "`class` ou un `record struct` renommé passerait");

        return new Verdict(fautes, constats, nonCouvert);
    }
}
