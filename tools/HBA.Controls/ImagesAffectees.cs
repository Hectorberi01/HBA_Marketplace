using System.Diagnostics;
using System.Text.Json;

namespace HBA.Controls;

/// <summary>
/// Quels services un changement affecte-t-il vraiment ?
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE CALCUL EXISTE POUR NE PAS RECONSTRUIRE VINGT ET UNE IMAGES À CHAQUE COMMIT.
///
/// C'est le seul gain immédiat de la découpe, et un pipeline qui reconstruit
/// tout l'annule : corriger une faute de frappe dans restaurant-service ne doit
/// pas republier l'image des paiements.
///
/// IL EST DÉRIVÉ DU GRAPHE RÉEL, PAS D'UNE LISTE DE CHEMINS.
///
/// La tentation est d'écrire, par service, la liste des dossiers qui le
/// concernent. Cette liste devient fausse au premier `ProjectReference` ajouté —
/// et le défaut est SILENCIEUX DANS LE MAUVAIS SENS : le service n'est pas
/// reconstruit, l'image publiée reste l'ancienne, et la correction qu'on croit
/// déployée ne l'est pas. On lit donc les `.csproj`, transitivement.
///
/// TROIS FICHIERS AFFECTENT TOUT LE MONDE. `Directory.Build.props`,
/// `Directory.Packages.props` et `HBA.sln` changent le cadre cible ou les
/// versions de paquets de CHAQUE projet. Les traiter comme un changement
/// ordinaire laisserait passer une montée de version d'EF Core sans reconstruire
/// quoi que ce soit.
///
/// UNE BASE INJOIGNABLE RECONSTRUIT TOUT. Premier commit, historique tronqué par
/// un clone peu profond : cela ne doit pas se traduire par « aucun service
/// affecté », qui serait un pipeline vert qui ne publie rien.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class ImagesAffectees
{
    /// <summary>Le verbe qui déclenche ce calcul en ligne de commande.</summary>
    public const string Verbe = "images-affectees";

    // Un changement dans l'un de ces fichiers reconstruit tout.
    private static readonly string[] Globaux =
        ["Directory.Build.props", "Directory.Packages.props", "HBA.sln"];

    // Dossiers dont un changement ne peut affecter aucune image.
    private static readonly string[] SansEffet =
        ["docs/", "k8s/", "infra/", "tests/", ".github/", "scripts/", "tools/", "_to_delete/"];

    private sealed record Image(string Nom, string Dockerfile, IReadOnlyList<string> Dossiers);

    /// <summary>Point d'entrée. Rend le code de sortie du processus.</summary>
    public static int Executer(string[] args)
    {
        var catalogue = Catalogue();

        if (args.Contains("--liste"))
        {
            foreach (var image in catalogue.OrderBy(i => i.Nom, StringComparer.Ordinal))
            {
                Console.WriteLine($"{image.Nom,-26} {image.Dossiers.Count,2} projet(s)  "
                                  + image.Dockerfile);
            }

            return 0;
        }

        List<Image> cibles;
        if (args.Contains("--tous"))
        {
            cibles = [.. catalogue];
        }
        else
        {
            var basePoint = args.FirstOrDefault(a => !a.StartsWith('-') && a != Verbe)
                            ?? "origin/main";
            var modifies = Modifies(basePoint);
            cibles = [.. Affectes(modifies, catalogue)];
        }

        // Format attendu par `strategy.matrix` de GitHub Actions. On sérialise
        // un dictionnaire et non un enregistrement : les noms de clés doivent
        // être `service` et `dockerfile` en minuscules, et un enregistrement les
        // rendrait en PascalCase sans que rien ne le signale.
        var sortie = cibles
            .OrderBy(i => i.Nom, StringComparer.Ordinal)
            .Select(i => new Dictionary<string, string>
            {
                ["service"] = i.Nom,
                ["dockerfile"] = i.Dockerfile,
            })
            .ToList();

        Console.WriteLine(JsonSerializer.Serialize(sortie));
        return 0;
    }

    /// <summary>
    /// Les images construites par le dépôt, avec les dossiers de projets
    /// réellement compilés dedans.
    /// </summary>
    private static List<Image> Catalogue()
    {
        var trouvees = new List<Image>();

        foreach (var racineNom in new[] { "services", "apps" })
        {
            var racine = Depot.Dossier(racineNom);
            foreach (var dossier in DossiersPortantUnDockerfile(racine))
            {
                var hotes = Depot.Fichiers(dossier, ".Api.csproj").ToList();
                if (hotes.Count == 0)
                {
                    continue;
                }

                var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pile = new Stack<string>(hotes);
                while (pile.Count > 0)
                {
                    var courant = pile.Pop();

                    // `vus.Add` seul ne suffit pas : un projet deja vu doit etre
                    // ignore SANS etre retire de l'ensemble, et un chemin qui
                    // n'existe pas ne doit jamais y entrer. Les melanger
                    // retirerait un projet legitime au second passage.
                    if (vus.Contains(courant) || !File.Exists(courant))
                    {
                        continue;
                    }

                    vus.Add(courant);

                    foreach (var (_, absolu) in Projets.References(courant))
                    {
                        pile.Push(absolu);
                    }
                }

                trouvees.Add(new Image(
                    Path.GetFileName(dossier),
                    Depot.Relatif(Path.Combine(dossier, "Dockerfile")),
                    vus.Select(p => Depot.Relatif(Path.GetDirectoryName(p)!) + "/")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.Ordinal)
                        .ToList()));
            }
        }

        return trouvees;
    }

    /// <summary>
    /// Les dossiers portant un `Dockerfile`, sans descendre plus bas.
    /// </summary>
    /// <remarks>
    /// UN DOCKERFILE PAR SERVICE : une fois trouvé, on ne descend pas dans les
    /// sous-dossiers. Descendre ferait apparaître un même service deux fois s'il
    /// portait un Dockerfile secondaire, et la matrice construirait deux images
    /// sous des noms différents pour le même service.
    /// </remarks>
    private static IEnumerable<string> DossiersPortantUnDockerfile(string racine)
    {
        if (File.Exists(Path.Combine(racine, "Dockerfile")))
        {
            yield return racine;
            yield break;
        }

        foreach (var sous in Directory.EnumerateDirectories(racine))
        {
            var nom = Path.GetFileName(sous);
            if (Depot.Ignores.Contains(nom) || nom.StartsWith('.'))
            {
                continue;
            }

            foreach (var trouve in DossiersPortantUnDockerfile(sous))
            {
                yield return trouve;
            }
        }
    }

    /// <summary>Les fichiers modifiés depuis `base`, ou `*` si base injoignable.</summary>
    private static IReadOnlyList<string> Modifies(string basePoint)
    {
        var depart = new ProcessStartInfo("git")
        {
            WorkingDirectory = Depot.Racine,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        depart.ArgumentList.Add("diff");
        depart.ArgumentList.Add("--name-only");
        depart.ArgumentList.Add($"{basePoint}...HEAD");

        using var processus = Process.Start(depart)
            ?? throw new InvalidOperationException("git n'a pas pu être lancé");
        var sortie = processus.StandardOutput.ReadToEnd();
        processus.WaitForExit();

        if (processus.ExitCode != 0)
        {
            Console.Error.WriteLine($"# base « {basePoint} » injoignable, on reconstruit tout");
            return ["*"];
        }

        return sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    private static IEnumerable<Image> Affectes(IReadOnlyList<string> fichiers,
                                               List<Image> catalogue)
    {
        if (fichiers.Contains("*") || fichiers.Any(f => Globaux.Contains(f)))
        {
            return catalogue;
        }

        var pertinents = fichiers
            .Where(f => !SansEffet.Any(s => f.StartsWith(s, StringComparison.Ordinal)))
            .ToList();

        return catalogue.Where(image =>
            pertinents.Any(f =>
                image.Dossiers.Any(d => f.StartsWith(d, StringComparison.Ordinal))));
    }
}
