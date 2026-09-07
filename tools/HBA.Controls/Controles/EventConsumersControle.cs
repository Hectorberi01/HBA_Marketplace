using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>Ce que l'extraction a laissé derrière : les consommateurs d'événements.</summary>
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

    // MODULES DONT ON SAIT QU'ILS N'ONT PAS ENCORE ÉTÉ EXTRAITS.
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

    /// <summary>Le monolithe de référence, VOLONTAIREMENT HORS DÉPÔT.</summary>
    private static string CheminMonolithe()
        => Path.GetFullPath(Path.Combine(Depot.Racine, "..", "src"));

    /// <summary>Événements consommés sous `racine`, et les fichiers qui les consomment.</summary>
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

    /// <summary>Un même événement déclaré dans deux espaces est une bombe à retardement.</summary>
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
            // L'ABSENCE DU MONOLITHE EST UNE INFORMATION, PAS UNE PANNE — mais elle
            // DOIT être dite : sans cette ligne, un contrôle amputé de la moitié de
            // son travail rendrait le même vert qu'un contrôle complet.
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
