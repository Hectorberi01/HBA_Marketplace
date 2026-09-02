using System.Text.Json;
using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// La règle ADDITIVE des contrats d'événements (décision D32).
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE QUE CE CONTRÔLE EMPÊCHE, ET POURQUOI RIEN D'AUTRE NE LE VOIT.
///
/// Un événement d'intégration n'est pas un objet interne : il est sérialisé,
/// écrit en base dans l'outbox, publié sur Kafka, et relu par d'autres services —
/// parfois plusieurs minutes plus tard, parfois par une version déployée la
/// semaine passée.
///
/// Renommer un de ses champs compile. Le supprimer compile. Ajouter un champ
/// `required` compile. Et rien ne casse à l'exécution non plus : le sérialiseur
/// JSON lit ce qu'il reconnaît, ignore le reste, et rend un objet aux champs
/// manquants à `null`. Le gestionnaire s'exécute sur une charge amputée, écrit un
/// effet faux, et la seule trace est un span vert.
///
/// D'où la convention : ON N'AJOUTE QUE DES CHAMPS OPTIONNELS. Une rupture crée
/// un NOUVEAU type d'événement — un `V2` portant son propre nom — jamais une
/// version 2 du même.
///
/// POURQUOI UN INSTANTANÉ VERSIONNÉ PLUTÔT QU'UNE ANALYSE.
///
/// La question n'est pas « à quoi ressemble ce contrat aujourd'hui » — un
/// compilateur le sait. Elle est « en quoi a-t-il changé depuis la dernière
/// fois ». Cela demande une mémoire, et cette mémoire doit être relue en revue :
/// c'est le fichier `docs/contrats-evenements.json`, versionné avec le code qu'il
/// décrit.
///
/// Une modification légitime met à jour l'instantané dans le MÊME commit, et le
/// relecteur voit exactement ce qui a bougé. C'est le point : rendre la rupture
/// visible, pas l'interdire.
///
/// CE CONTRÔLE NE RÉÉCRIT PAS L'INSTANTANÉ, ET C'EST UNE DIFFÉRENCE ASSUMÉE AVEC
/// LE SCRIPT D'ORIGINE. Celui-ci créait le fichier quand il manquait, et le
/// mettait à jour sur `--accepter`. Un contrôle rend un verdict ; il ne modifie
/// pas le dépôt qu'il examine, et <see cref="IControle"/> n'a pas d'options. Un
/// instantané absent est donc une FAUTE qui nomme le geste à faire, et
/// l'acceptation d'un changement voulu reste
/// `dotnet run --project tools/HBA.Controls -- event-contracts
///
/// CE QU'IL NE VOIT PAS : le SENS d'un contrat. Un champ conservé, de même nom et
/// de même type, mais dont la signification change — une énumération à laquelle on
/// ajoute une valeur, une unité qui passe du centime à l'euro — traverse ce
/// contrôle sans un mot.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class EventContractsControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "event-contracts";

    /// <inheritdoc/>
    public string Resume => "les contrats d'événements ne changent qu'en ajoutant de l'optionnel";

    // `public required Guid SellerId { get; init; }` — le `required` est ce qui
    // compte : c'est lui qui casse la désérialisation d'une charge en vol.
    private static readonly Regex Propriete = new(
        @"^\s*public\s+(?<required>required\s+)?(?<type>[\w<>?\[\],. ]+?)\s+(?<nom>\w+)\s*\{\s*get;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex Declaration = new(
        @"public sealed record (\w+IntegrationEvent)\b", RegexOptions.Compiled);

    private static readonly Regex CommentaireDoc = new(@"///[^\n]*", RegexOptions.Compiled);

    private static readonly Regex CommentaireLigne = new(@"//[^\n]*", RegexOptions.Compiled);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var nonCouvert = NonCouvert();
        var (actuels, requis) = Evenements();

        var instantane = Depot.Chemin("docs", "contrats-evenements.json");
        if (!File.Exists(instantane))
        {
            return new Verdict(
                [$"{Depot.Relatif(instantane)} est absent : il n'y a RIEN à quoi "
                 + $"comparer les {actuels.Count} événement(s) lus. Créer l'instantané "
                 + "avec `dotnet run --project tools/HBA.Controls -- event-contracts le "
                 + "relire, et le committer avec le code qu'il décrit."],
                [],
                nonCouvert);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(instantane));
        var racine = document.RootElement;

        if (racine.ValueKind != JsonValueKind.Object
            || !racine.TryGetProperty("evenements", out var connusJson)
            || connusJson.ValueKind != JsonValueKind.Object)
        {
            return new Verdict(
                [$"{Depot.Relatif(instantane)} ne porte pas d'objet `evenements` : "
                 + "l'instantané est illisible, et le comparer rendrait « aucune "
                 + "rupture » sans avoir rien comparé."],
                [],
                nonCouvert);
        }

        var ruptures = new List<string>();
        var ajouts = new List<string>();
        var connus = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entree in connusJson.EnumerateObject()
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var nom = entree.Name;
            connus.Add(nom);

            if (!actuels.TryGetValue(nom, out var champs))
            {
                ruptures.Add($"{nom} : événement SUPPRIMÉ — ses consommateurs ne "
                             + "recevront plus rien");
                continue;
            }

            var attendus = ChampsAttendus(entree.Value);

            foreach (var attendu in attendus.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!champs.TryGetValue(attendu.Key, out var typeActuel))
                {
                    ruptures.Add($"{nom} : champ « {attendu.Key} » RETIRÉ ou RENOMMÉ — "
                                 + "un consommateur déployé le lit encore");
                }
                else if (!string.Equals(typeActuel, attendu.Value, StringComparison.Ordinal))
                {
                    ruptures.Add($"{nom} : champ « {attendu.Key} » : type passé de "
                                 + $"« {attendu.Value} » à « {typeActuel} » — les charges "
                                 + "en vol ne se désérialiseront pas comme prévu");
                }
            }

            List<string> obligatoires = requis.TryGetValue(nom, out var liste) ? liste : [];

            foreach (var champ in champs.Keys
                         .Where(c => !attendus.ContainsKey(c))
                         .OrderBy(x => x, StringComparer.Ordinal))
            {
                if (obligatoires.Contains(champ))
                {
                    ruptures.Add($"{nom} : champ « {champ} » ajouté en REQUIRED — un "
                                 + "producteur déjà déployé ne le remplira pas, et la "
                                 + "désérialisation échouera");
                }
                else
                {
                    ajouts.Add($"{nom}.{champ}");
                }
            }
        }

        var nouveaux = actuels.Keys
            .Where(n => !connus.Contains(n))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var constats = new List<string>
        {
            $"{actuels.Count} événement(s) suivi(s), {ruptures.Count} rupture(s) de contrat",
        };

        foreach (var ajout in ajouts)
        {
            constats.Add($"champ optionnel ajouté, conforme à la convention : {ajout}");
        }

        foreach (var nom in nouveaux)
        {
            constats.Add($"événement nouveau : {nom}");
        }

        if (ruptures.Count > 0)
        {
            constats.Add("si le changement est VOULU, il ne se glisse pas : "
                         + "`dotnet run --project tools/HBA.Controls -- event-contracts met "
                         + "l'instantané à jour, et la revue voit exactement ce qui a bougé");
        }

        return new Verdict(ruptures, constats, nonCouvert);
    }

    /// <summary>Les champs d'un événement tels que l'instantané les décrit.</summary>
    private static Dictionary<string, string> ChampsAttendus(JsonElement entree)
    {
        var attendus = new Dictionary<string, string>(StringComparer.Ordinal);

        if (entree.ValueKind != JsonValueKind.Object
            || !entree.TryGetProperty("champs", out var champs)
            || champs.ValueKind != JsonValueKind.Object)
        {
            return attendus;
        }

        foreach (var champ in champs.EnumerateObject())
        {
            attendus[champ.Name] = champ.Value.GetString() ?? string.Empty;
        }

        return attendus;
    }

    /// <summary>
    /// Les contrats tels que le code les écrit AUJOURD'HUI : par événement, ses
    /// champs et leurs types, plus la liste de ceux qui sont <c>required</c>.
    /// </summary>
    /// <remarks>
    /// LE CORPS EST DÉLIMITÉ EN COMPTANT LES ACCOLADES, à partir de la première
    /// rencontrée après le nom du type. Un événement déclaré avec un constructeur
    /// primaire et refermé par un point-virgule n'a PAS de corps : la première
    /// accolade trouvée est alors celle du type SUIVANT, et ses champs sont
    /// attribués au mauvais événement. Aucun événement de ce dépôt ne s'écrit
    /// ainsi, et la convention est justement d'exposer des propriétés
    /// `{ get; init; }`.
    ///
    /// DEUX ÉVÉNEMENTS DE MÊME NOM dans deux fichiers : le dernier lu écrase le
    /// premier, en silence. C'est un défaut connu, hérité du script d'origine.
    /// </remarks>
    private sealed record Contrats(
        Dictionary<string, Dictionary<string, string>> Champs,
        Dictionary<string, List<string>> Requis);

    private static Contrats Evenements()
    {
        var trouves = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var requis = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var fichier in Depot.Fichiers(Depot.Racine, ".cs"))
        {
            // Les tests déclarent des événements de fixture : les suivre ferait
            // du bruit dans un instantané qui décrit des contrats publiés.
            if (Depot.Relatif(fichier).Split('/').Contains("tests"))
            {
                continue;
            }

            string source;
            try
            {
                source = File.ReadAllText(fichier);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (Match debut in Declaration.Matches(source))
            {
                var nom = debut.Groups[1].Value;
                var reste = source[(debut.Index + debut.Length)..];

                var ouvrante = reste.IndexOf('{');
                if (ouvrante < 0)
                {
                    trouves[nom] = new Dictionary<string, string>(StringComparer.Ordinal);
                    requis[nom] = [];
                    continue;
                }

                var profondeur = 0;
                var fin = reste.Length;
                for (var i = ouvrante; i < reste.Length; i++)
                {
                    if (reste[i] == '{')
                    {
                        profondeur++;
                    }
                    else if (reste[i] == '}')
                    {
                        profondeur--;
                        if (profondeur == 0)
                        {
                            fin = i;
                            break;
                        }
                    }
                }

                var corps = reste[ouvrante..fin];
                corps = CommentaireDoc.Replace(corps, string.Empty);
                corps = CommentaireLigne.Replace(corps, string.Empty);

                var champs = new Dictionary<string, string>(StringComparer.Ordinal);
                var obligatoires = new List<string>();

                foreach (Match m in Propriete.Matches(corps))
                {
                    champs[m.Groups["nom"].Value] = m.Groups["type"].Value.Trim();
                    if (m.Groups["required"].Success)
                    {
                        obligatoires.Add(m.Groups["nom"].Value);
                    }
                }

                obligatoires.Sort(StringComparer.Ordinal);
                trouves[nom] = champs;
                requis[nom] = obligatoires;
            }
        }

        return new Contrats(trouves, requis);
    }

    private static List<string> NonCouvert()
        =>
        [
            "ce contrôle ne met PAS l'instantané à jour : un contrôle rend un "
            + "verdict, il ne réécrit pas le dépôt. L'acceptation d'un changement "
            + "voulu reste `dotnet run --project tools/HBA.Controls -- event-contracts
            "le SENS d'un champ : même nom, même type, signification changée — une "
            + "valeur ajoutée à une énumération, une unité qui change — passe sans "
            + "un mot",
            "les événements déclarés avec un constructeur primaire et sans corps : "
            + "la délimitation par accolades leur attribuerait les champs du type "
            + "suivant",
            "les fichiers sous un dossier `tests/`, et tout événement dont le nom "
            + "ne se termine pas par `IntegrationEvent`",
            "deux événements de même nom dans deux fichiers : le dernier lu écrase "
            + "le premier, en silence",
            "que le producteur remplisse effectivement les champs, et que le "
            + "consommateur les lise : ce contrôle compare des formes, pas des "
            + "comportements",
        ];
}
