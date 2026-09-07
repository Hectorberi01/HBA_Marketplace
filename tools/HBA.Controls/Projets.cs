using System.Text.RegularExpressions;

namespace HBA.Controls;

/// <summary>Lecture des `.csproj` — une seule definition, partagée.</summary>
public static class Projets
{
    private static readonly Regex Include = new(
        @"<ProjectReference\b[^>]*?\bInclude\s*=\s*[""']([^""']+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Tous les `.csproj` du dépôt, chemins absolus, triés.</summary>
    public static IReadOnlyList<string> Tous()
        => Depot.Fichiers(Depot.Racine, ".csproj")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Les cibles des `ProjectReference` d'un projet : le chemin tel qu'écrit, et
    /// le chemin absolu qu'il désigne.
    /// </summary>
    public static IEnumerable<(string Brut, string Absolu)> References(string csproj)
    {
        var dossier = Path.GetDirectoryName(csproj)!;
        foreach (Match m in Include.Matches(File.ReadAllText(csproj)))
        {
            var brut = m.Groups[1].Value;
            var relatif = brut.Replace('\\', Path.DirectorySeparatorChar);
            yield return (brut, Path.GetFullPath(Path.Combine(dossier, relatif)));
        }
    }
}
