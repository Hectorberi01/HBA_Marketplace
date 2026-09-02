using System.Text.RegularExpressions;

namespace HBA.Controls;

/// <summary>Lecture des `.csproj` — une seule definition, partagée.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// POURQUOI CE N'EST PAS UN ANALYSEUR XML, ET C'EST UN RECUL ASSUMÉ.
///
/// Le contrôle Python passait par `xml.etree.ElementTree`, qui a besoin de
/// `pyexpat`. Sur le Python 3.14 de Homebrew ce module est absent : le contrôle
/// mourait sur vingt lignes de trace, au milieu de la barrière, alors qu'il n'a
/// besoin que d'une chose — la valeur de l'attribut `Include`. Un contrôle qui
/// ne tourne pas ne contrôle rien.
///
/// En C# le risque n'est plus le même (XLinq est dans la bibliothèque
/// standard), mais la raison de rester sur une lecture textuelle tient : elle
/// accepte un csproj mal formé, là où un analyseur XML lèverait. Un fichier
/// cassé doit produire un CONSTAT, pas une exception.
///
/// CE QUE CE RECUL COÛTE : une expression régulière ne comprend pas le XML.
/// Elle lirait un `ProjectReference` placé dans un commentaire, ou dans un
/// `ItemGroup` conditionné par un `Condition` faux. Les deux existent en
/// MSBuild. En pratique aucun csproj de ce dépôt n'en contient — et le prix
/// d'un faux positif ici est de désigner une référence qui EXISTE, pas d'en
/// manquer une.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
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
    /// Les cibles des `ProjectReference` d'un projet : le chemin tel qu'écrit,
    /// et le chemin absolu qu'il désigne.
    /// </summary>
    /// <remarks>
    /// MSBuild écrit ses chemins avec des antislashs, y compris sous Unix : la
    /// conversion n'est pas cosmétique, sans elle aucune cible n'est trouvée et
    /// le contrôle déclare tout le dépôt cassé.
    /// </remarks>
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
