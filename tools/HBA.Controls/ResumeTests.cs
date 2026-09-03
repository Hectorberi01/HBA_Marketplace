using System.Xml.Linq;

namespace HBA.Controls;

/// <summary>
/// Lit les fichiers <c>.trx</c> produits par <c>dotnet test</c> et rend, en
/// clair, LE NOM ET LE MESSAGE DE CHAQUE TEST EN ÉCHEC.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// POURQUOI CE VERBE EXISTE.
///
/// La boucle de tests de la CI nomme désormais le PROJET en échec. Ce n'est
/// pas assez : le 2 septembre, « HBA.Merchants.IntegrationTests » était nommé,
/// et il fallait encore dérouler un journal de seize mille lignes pour trouver
/// LEQUEL de ses trente-huit cas était tombé, et pourquoi. Le journal du job
/// n'est lisible que par un administrateur du dépôt ; l'artefact `.trx` aussi.
/// Autant dire que la cause d'un échec n'était accessible qu'à une personne.
///
/// Ce verbe met le nom du cas, son message d'assertion et les premières lignes
/// de sa pile dans la SORTIE DU PAS et dans le résumé de l'exécution — deux
/// endroits que tout le monde peut lire, y compris depuis un téléphone.
///
/// CE QUE ÇA NE COUVRE PAS. Un projet qui ne produit AUCUN `.trx` — parce que
/// l'hôte de test s'est effondré avant d'écrire quoi que ce soit, un code 137
/// par exemple — ne laisse rien à lire ici. C'est pourquoi l'absence de tout
/// fichier est DITE au lieu d'être traitée comme « aucun échec ».
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class ResumeTests
{
    /// <summary>Le verbe qui déclenche la lecture.</summary>
    public const string Verbe = "resume-tests";

    private const int LignesDePileGardees = 6;

    /// Au-delà, GitHub cesse d'afficher les annotations d'un pas.
    private const int AnnotationsMaximum = 8;

    /// De quoi porter la première ligne d'exception, pas la pile entière.
    private const int LignesDAnnotation = 6;

    private static readonly XNamespace Trx =
        "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>
    /// Parcourt les <c>.trx</c> du dépôt et écrit le détail des échecs.
    /// </summary>
    /// <returns>
    /// Toujours 0. CE VERBE RAPPORTE, IL NE JUGE PAS : c'est
    /// <c>dotnet test</c> qui décide du rouge. Rendre 1 ici ferait échouer une
    /// seconde fois le même travail, et masquerait le code de sortie réel.
    /// </returns>
    public static int Executer(string[] args)
    {
        var racine = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Depot.Racine;

        if (!Directory.Exists(racine))
        {
            Console.Error.WriteLine($"resume-tests : dossier introuvable — {racine}");
            return 0;
        }

        // Les `.trx` vivent dans des dossiers `TestResults`, que `Depot.Ignores`
        // écarte pour les CONTRÔLES. Ici, c'est exactement ce qu'on cherche : on
        // balaie donc sans passer par les aides de `Depot`.
        var fichiers = Directory
            .EnumerateFiles(racine, "*.trx", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        if (fichiers.Length == 0)
        {
            Console.WriteLine("resume-tests : aucun fichier .trx trouvé.");
            Console.WriteLine("     Un projet dont l'hôte de test s'effondre avant d'écrire son");
            Console.WriteLine("     rapport ne laisse rien à lire — regarder le journal du pas.");
            return 0;
        }

        var echecs = new List<Echec>();
        var lus = 0;

        foreach (var fichier in fichiers)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(fichier);
            }
            catch (Exception erreur)
            {
                // UN RAPPORT ILLISIBLE SE DIT. Le sauter en silence ferait
                // conclure « aucun échec » sur un fichier tronqué.
                Console.WriteLine($"resume-tests : {Nom(fichier)} illisible — "
                                  + $"{erreur.GetType().Name} : {erreur.Message}");
                continue;
            }

            lus++;

            foreach (var resultat in document.Descendants(Trx + "UnitTestResult"))
            {
                var issue = (string?)resultat.Attribute("outcome");
                if (!string.Equals(issue, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var info = resultat.Descendants(Trx + "ErrorInfo").FirstOrDefault();

                echecs.Add(new Echec(
                    Projet: Path.GetFileNameWithoutExtension(fichier),
                    Test: (string?)resultat.Attribute("testName") ?? "(sans nom)",
                    Message: Texte(info, "Message"),
                    Pile: Texte(info, "StackTrace")));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"── Détail des échecs ({lus} rapport(s) .trx lu(s))");

        if (echecs.Count == 0)
        {
            Console.WriteLine("Aucun test en échec dans les rapports lus.");
            Console.WriteLine("     Si le pas est rouge malgré cela, l'échec est ANTÉRIEUR aux tests :");
            Console.WriteLine("     démarrage de l'hôte, conteneur qui ne monte pas, ou processus tué.");
            return 0;
        }

        foreach (var groupe in echecs.GroupBy(e => e.Projet).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine();
            Console.WriteLine($"  {groupe.Key} — {groupe.Count()} cas en échec");

            foreach (var echec in groupe)
            {
                Console.WriteLine();
                Console.WriteLine($"    ✗ {echec.Test}");

                foreach (var ligne in Lignes(echec.Message, int.MaxValue))
                {
                    Console.WriteLine($"        {ligne}");
                }

                foreach (var ligne in Lignes(echec.Pile, LignesDePileGardees))
                {
                    Console.WriteLine($"        · {ligne}");
                }
            }
        }

        EcrireLeResume(echecs);
        PoserLesAnnotations(echecs);
        return 0;
    }

    /// <summary>
    /// Pose quelques cas en échec en annotations `::error::` sur le travail.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// POURQUOI CE CANAL EN PLUS DU RÉSUMÉ.
    ///
    /// Le journal du job et l'artefact `.trx` demandent tous deux les droits
    /// d'ADMINISTRATEUR du dépôt. Les annotations, elles, sont rendues par l'API
    /// publique des `check-runs` : elles sont donc lisibles par quiconque voit le
    /// dépôt, et par un outil qui n'a aucun jeton. C'est le seul canal par lequel
    /// la cause d'un échec sort de la machine sans passer par une personne.
    ///
    /// PLAFONNÉ, ET DÉLIBÉRÉMENT BAS. GitHub n'affiche qu'une dizaine
    /// d'annotations par pas ; en poser vingt-trois les rendrait toutes
    /// invisibles. Le compte total est dit dans la dernière, pour qu'un lecteur
    /// sache qu'il n'a pas tout vu — un extrait qui se fait passer pour un
    /// inventaire est pire qu'un extrait.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static void PoserLesAnnotations(IReadOnlyList<Echec> echecs)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not "true")
        {
            return;
        }

        foreach (var echec in echecs.Take(AnnotationsMaximum))
        {
            var corps = string.Join(
                "%0A", Lignes(echec.Message, LignesDAnnotation).Select(Echapper));

            Console.WriteLine(
                $"::error title={Echapper(echec.Test)}::{corps}");
        }

        if (echecs.Count > AnnotationsMaximum)
        {
            Console.WriteLine(
                $"::error title=Cas en échec::{echecs.Count} au total ; "
                + $"les {AnnotationsMaximum} premiers sont annotés, le reste est dans le résumé.");
        }
    }

    /// <summary>
    /// Échappe ce qu'une commande de flux de travail ne supporte pas.
    /// </summary>
    /// <remarks>
    /// Une nouvelle ligne brute couperait la commande en deux et la seconde
    /// moitié s'afficherait comme une ligne de journal ordinaire ; `::` non
    /// échappé ouvrirait une commande imbriquée. On perdrait l'annotation sans
    /// aucun message d'erreur.
    /// </remarks>
    private static string Echapper(string texte)
        => texte
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal)
            .Replace(":", "%3A", StringComparison.Ordinal)
            .Replace(",", "%2C", StringComparison.Ordinal);

    /// <summary>
    /// Recopie le détail dans le résumé de l'exécution GitHub, quand il existe.
    /// </summary>
    /// <remarks>
    /// La page du run est le seul endroit lisible sans dérouler le journal.
    /// Hors CI, <c>GITHUB_STEP_SUMMARY</c> est absent et l'on n'écrit rien —
    /// une exécution locale ne doit pas créer de fichier au hasard.
    /// </remarks>
    private static void EcrireLeResume(IReadOnlyList<Echec> echecs)
    {
        var chemin = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrWhiteSpace(chemin))
        {
            return;
        }

        var texte = new System.Text.StringBuilder();
        texte.AppendLine("## Cas en échec");
        texte.AppendLine();

        foreach (var groupe in echecs.GroupBy(e => e.Projet).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            texte.AppendLine($"### {groupe.Key}");
            texte.AppendLine();

            foreach (var echec in groupe)
            {
                texte.AppendLine($"- **{echec.Test}**");

                var message = Lignes(echec.Message, 12).ToArray();
                if (message.Length > 0)
                {
                    texte.AppendLine();
                    texte.AppendLine("  ```");
                    foreach (var ligne in message)
                    {
                        texte.AppendLine($"  {ligne}");
                    }

                    foreach (var ligne in Lignes(echec.Pile, LignesDePileGardees))
                    {
                        texte.AppendLine($"  {ligne}");
                    }

                    texte.AppendLine("  ```");
                }

                texte.AppendLine();
            }
        }

        try
        {
            File.AppendAllText(chemin, texte.ToString());
        }
        catch (Exception erreur)
        {
            // Le résumé est un CONFORT. S'il ne s'écrit pas, la sortie du pas
            // porte déjà tout le détail : on le signale et l'on continue.
            Console.WriteLine($"resume-tests : résumé non écrit — "
                              + $"{erreur.GetType().Name} : {erreur.Message}");
        }
    }

    private static string Texte(XElement? info, string nom)
        => info?.Element(Trx + nom)?.Value ?? string.Empty;

    private static IEnumerable<string> Lignes(string brut, int maximum)
        => brut
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .Take(maximum);

    private static string Nom(string chemin)
        => Path.GetFileName(chemin);

    private sealed record Echec(string Projet, string Test, string Message, string Pile);
}
