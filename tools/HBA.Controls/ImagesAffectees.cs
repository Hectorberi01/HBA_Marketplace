using System.Diagnostics;
using System.Text.Json;

namespace HBA.Controls;

/// <summary>Quels services un changement affecte-t-il vraiment ?</summary>
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

    // LE NOM PUBLIE N'EST PAS TOUJOURS LE NOM DU DOSSIER, ET C'EST ICI QUE CA SE
    // DECIDE — parce que c'est ce fichier qui alimente `strategy.matrix`, donc ce
    // que la CI POUSSE reellement.
    //
    // La passerelle vit dans `bff/`. Sans cette table, la CI poussait
    // `ghcr.io/…/bff` pendant que `docker-compose.prod.yml` demandait
    // `ghcr.io/…/api-gateway` : le `pull` et la verification de signature
    // echouaient sur MANIFEST_UNKNOWN, pour une image que personne n'avait
    // jamais poussee sous ce nom.
    //
    // ELLE DOIT DIRE LA MEME CHOSE QUE `ComposeProd.NomsImages`, qui renomme le
    // meme composant cote compose. Les deux gardes de `compose-prod` refusent le
    // rendu des qu'elles divergent : une image du compose qui n'est pas dans
    // `NomsPubliables()` fait echouer la generation.
    private static readonly Dictionary<string, string> NomsPublies = new(StringComparer.Ordinal)
    {
        ["bff"] = "api-gateway",
    };

    /// <summary>Les noms des images que la CI sait publier.</summary>
    public static HashSet<string> NomsPubliables()
        => Catalogue().Select(i => i.Nom).ToHashSet(StringComparer.Ordinal);

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

        // Format attendu par `strategy.matrix` de GitHub Actions.
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
    /// Les images construites par le dépôt, avec les dossiers de projets réellement
    /// compilés dedans.
    /// </summary>
    private static List<Image> Catalogue()
    {
        var trouvees = new List<Image>();

        foreach (var racineNom in new[] { "services", "bff" })
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
                    // n'existe pas ne doit jamais y entrer.
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

                var dossierNom = Path.GetFileName(dossier);
                trouvees.Add(new Image(
                    NomsPublies.GetValueOrDefault(dossierNom, dossierNom),
                    Depot.Relatif(Path.Combine(dossier, "Dockerfile")),
                    vus.Select(p => Depot.Relatif(Path.GetDirectoryName(p)!) + "/")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.Ordinal)
                        .ToList()));
            }
        }

        return trouvees;
    }

    /// <summary>Les dossiers portant un `Dockerfile`, sans descendre plus bas.</summary>
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
