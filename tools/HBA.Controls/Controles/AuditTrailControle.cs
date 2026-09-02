using System.Text.RegularExpressions;

namespace HBA.Controls.Controles;

/// <summary>
/// Un contexte qui promet un journal d'audit a-t-il la table qui va avec ?
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// UN JOURNAL D'AUDIT PROMIS PAR LE MODÈLE, ABSENT DE LA BASE.
///
/// CE DÉFAUT NE SE VOIT NI À LA COMPILATION, NI AU DÉMARRAGE.
///
/// `ModuleDbContext.KeepsAuditTrail` est une propriété du MODÈLE. La passer à
/// `true` mappe l'entité `AuditEntry` et fait écrire une ligne par entité mutée
/// — dans la même transaction que la mutation. Si la TABLE n'existe pas, l'échec
/// arrive au premier `SaveChanges` d'un geste métier : ce n'est pas le journal
/// qui casse, c'est la commande de l'utilisateur.
///
/// C'est arrivé : `ReturnRefundDbContext` a porté `KeepsAuditTrail =&gt; true` sans
/// table, et sa migration de rattrapage le dit en toutes lettres — « la table
/// était déclarée, promise à l'exploitant, et absente de la base ».
///
/// ET LA RÉCIPROQUE COMPTE AUTANT.
///
/// Deux commentaires du dépôt — `AuditQueries.cs` et `SellersDbContext.cs` —
/// affirmaient qu'un journal existait dans catalog, inventory et order. Aucun
/// des trois n'avait ni surcharge, ni table. Un lecteur planifiait un lot en
/// croyant la moitié du travail déjà faite. Ce contrôle refuse aussi ce sens-là :
/// une table `audit_entries` sans surcharge est une table que personne
/// n'alimente.
///
/// CE QU'IL VÉRIFIE, POUR CHAQUE CONTEXTE QUI JOURNALISE
///   1. une migration de son service crée `&lt;schema&gt;.audit_entries` ;
///   2. son `*ModelSnapshot.cs` porte le bloc `AuditEntry` — sans quoi la
///      prochaine migration générée voudra recréer la table ;
///   3. son `OnModelCreating` appelle `base.OnModelCreating` — c'est LÀ que
///      `AuditConfiguration` est appliquée, et un override qui l'oublie mappe
///      tout sauf le journal, en silence ;
///   4. réciproquement, aucun service ne porte une table `audit_entries` sans
///      `KeepsAuditTrail =&gt; true`.
///
/// CE QU'IL NE VÉRIFIE PAS : que la table existe dans une base RÉELLE. Il lit le
/// dépôt. `check-migrations.py` rejoue les migrations à froid contre le
/// snapshot ; les deux ensemble couvrent le chemin, pas une base éditée à la
/// main.
///
/// ET IL NE RETIRE PAS LES COMMENTAIRES, VOLONTAIREMENT. Le script Python
/// d'origine ne le faisait pas non plus, et ses chiffres sont la référence de ce
/// portage. Conséquence à connaître : un `KeepsAuditTrail =&gt; true` cité dans un
/// encadré compterait pour une surcharge réelle, et le mot `audit_entries` cité
/// dans le commentaire d'une migration compterait pour une création de table.
/// Passer par <see cref="SourceCsharp.SansCommentaires"/> changerait les
/// comptes ; ce sera un choix à faire une fois le portage comparé, pas au
/// milieu.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class AuditTrailControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "audit-trail";

    /// <inheritdoc/>
    public string Resume => "tout contexte qui journalise a sa table, et toute table son contexte";

    private static readonly Regex Contexte = new(
        @"class\s+(\w*DbContext)\s*:\s*ModuleDbContext", RegexOptions.Compiled);

    private static readonly Regex Schema = new(
        @"SchemaName\s*=\s*""([^""]+)""", RegexOptions.Compiled);

    private static readonly Regex Actif = new(
        @"KeepsAuditTrail\s*=>\s*true", RegexOptions.Compiled);

    private static readonly Regex BaseAppelee = new(
        @"base\.OnModelCreating", RegexOptions.Compiled);

    /// <summary>Un contexte de module et ce que son fichier promet.</summary>
    private sealed record Module(
        string Nom,
        string? Schema,
        bool Journalise,
        bool BaseAppelee,
        string? Service,
        string Fichier);

    /// <inheritdoc/>
    public Verdict Executer()
    {
        // L'absence du dossier LÈVE : un contrôle qui ne peut rien regarder ne
        // doit pas rendre « 0 anomalie ».
        var racineServices = Depot.Dossier("services");

        var modules = new List<Module>();

        // Les services dont une migration crée la table, et ceux dont un
        // snapshot porte l'entité — relevés dans la même lecture.
        var avecTable = new HashSet<string>(StringComparer.Ordinal);
        var avecSnapshot = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chemin in Depot.Fichiers(racineServices, ".cs"))
        {
            var texte = File.ReadAllText(chemin);
            var service = ServiceDe(chemin);
            var fichier = Path.GetFileName(chemin);
            var estSnapshot = fichier.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal);
            var relatif = Depot.Relatif(chemin);

            if (service is not null)
            {
                if (estSnapshot && texte.Contains("Audit.AuditEntry", StringComparison.Ordinal))
                {
                    avecSnapshot.Add(service);
                }
                else if (!estSnapshot
                         && relatif.Contains("Migrations", StringComparison.Ordinal)
                         && texte.Contains("audit_entries", StringComparison.Ordinal))
                {
                    avecTable.Add(service);
                }
            }

            var trouve = Contexte.Match(texte);
            if (!trouve.Success)
            {
                continue;
            }

            var schema = Schema.Match(texte);
            modules.Add(new Module(
                trouve.Groups[1].Value,
                schema.Success ? schema.Groups[1].Value : null,
                Actif.IsMatch(texte),
                BaseAppelee.IsMatch(texte),
                service,
                chemin));
        }

        var fautes = new List<string>();
        var actifs = modules.Where(m => m.Journalise).ToList();

        foreach (var module in actifs)
        {
            var relatif = Depot.Relatif(module.Fichier);

            if (module.Schema is null)
            {
                fautes.Add($"{module.Nom} ({relatif}) : journalise mais ne déclare "
                           + "aucun SchemaName.");
                continue;
            }

            if (!module.BaseAppelee)
            {
                fautes.Add(
                    $"{module.Nom} ({relatif}) : `OnModelCreating` n'appelle pas "
                    + "`base.OnModelCreating` — `AuditConfiguration` n'est donc jamais "
                    + "appliquée et `audit_entries` n'est pas mappée.");
            }

            // Un contexte hors d'un dossier de service ne peut pas se voir
            // attribuer de migrations : le dire plutôt que de le taire.
            if (module.Service is null)
            {
                fautes.Add($"{module.Nom} ({relatif}) : journalise, mais son chemin ne "
                           + "désigne aucun dossier de service — ni migration ni "
                           + "snapshot n'ont PU être cherchés.");
                continue;
            }

            var service = Depot.Relatif(module.Service);

            if (!avecTable.Contains(module.Service))
            {
                fautes.Add(
                    $"{module.Nom} : `KeepsAuditTrail => true`, mais AUCUNE migration de "
                    + $"{service} ne crée `{module.Schema}.audit_entries`. Le premier "
                    + "geste métier lèvera.");
            }

            if (!avecSnapshot.Contains(module.Service))
            {
                fautes.Add(
                    $"{module.Nom} : `AuditEntry` absent du ModelSnapshot de {service}. "
                    + "La prochaine migration générée voudra recréer la table.");
            }
        }

        // Le sens inverse : une table sans surcharge.
        var servicesActifs = actifs
            .Where(m => m.Service is not null)
            .Select(m => m.Service!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var service in avecTable.Except(servicesActifs)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            fautes.Add(
                $"{Depot.Relatif(service)} : une migration crée `audit_entries`, mais "
                + "aucun contexte de ce service ne porte `KeepsAuditTrail => true`. "
                + "Table que personne n'alimente.");
        }

        var constats = new List<string>
        {
            $"{modules.Count} contexte(s) de module, dont {actifs.Count} journalisent.",
        };

        foreach (var module in actifs.OrderBy(m => m.Schema ?? "", StringComparer.Ordinal))
        {
            var schema = module.Schema ?? "(aucun schéma)";
            constats.Add($"journal : {schema,-22} {module.Nom}");
        }

        return new Verdict(
            fautes,
            constats,
            [
                "que la table existe dans une base RÉELLE — ce contrôle lit le dépôt ; "
                + "`check-migrations.py` rejoue les migrations à froid contre le snapshot",
                "les commentaires ne sont PAS retirés : un `KeepsAuditTrail => true` ou "
                + "un `audit_entries` cité dans un encadré compte comme réel",
                "le contenu de la migration : le mot `audit_entries` suffit, la colonne "
                + "et le schéma effectivement créés ne sont pas lus",
            ]);
    }

    /// <summary>
    /// Le dossier de service qui contient ce fichier —
    /// <c>services/&lt;famille&gt;/&lt;service&gt;</c>, ou <c>null</c> s'il est
    /// posé plus haut.
    /// </summary>
    private static string? ServiceDe(string chemin)
    {
        var segments = Depot.Relatif(chemin).Split('/');
        return segments.Length >= 3
            ? Depot.Chemin(segments[0], segments[1], segments[2])
            : null;
    }
}
