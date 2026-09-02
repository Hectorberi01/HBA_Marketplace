namespace HBA.Controls;

/// <summary>
/// Où vit le code de ce dépôt, et comment on refuse de regarder à côté.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CETTE CLASSE EXISTE À CAUSE D'UN DÉFAUT QUI S'EST RÉPÉTÉ QUATRE FOIS.
///
/// Quatre contrôles Python balayaient <c>&lt;dépôt&gt;/src</c>, un dossier qui
/// n'a jamais existé dans ce monorepo — le code vit sous <c>services/</c>,
/// <c>shared/</c> et <c>apps/</c>. Parcourir un dossier absent ne lève pas : il
/// ne produit simplement aucune itération. Les contrôles affichaient donc
/// « 0 anomalie », et ce zéro se lisait « tout va bien » alors qu'il voulait
/// dire « je n'ai rien regardé ». Derrière, deux clients gRPC entièrement
/// bouchonnés en production.
///
/// D'où la règle tenue ici : <see cref="Dossier"/> LÈVE si le dossier manque.
/// Un contrôle qui ne peut pas regarder doit s'arrêter, pas rendre zéro.
///
/// CE QUE CELA NE GARANTIT PAS : qu'un balayage présent regarde la bonne chose.
/// Un critère trop étroit rendra toujours zéro, et ce zéro-là reste
/// indiscernable d'un dépôt sain. On ferme le silence du chemin, pas celui du
/// critère.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class Depot
{
    /// <summary>Dossiers qu'aucun contrôle ne doit parcourir.</summary>
    public static readonly string[] Ignores =
        ["bin", "obj", "node_modules", ".git", "_to_delete", ".vs", "TestResults"];

    private static string? _racine;

    /// <summary>
    /// La racine du dépôt : le premier dossier ascendant qui porte
    /// <c>HBA.sln</c>.
    /// </summary>
    /// <remarks>
    /// PAS UN CHEMIN RELATIF DEPUIS L'ASSEMBLY. Sous <c>dotnet run</c>, le
    /// binaire vit dans <c>bin/Debug/net9.0/</c> ; publié, ailleurs ; en CI,
    /// ailleurs encore. Un <c>../../..</c> écrit en dur marche sur le poste de
    /// celui qui l'a écrit et nulle part ailleurs.
    /// </remarks>
    public static string Racine
    {
        get
        {
            if (_racine is not null)
            {
                return _racine;
            }

            var dossier = new DirectoryInfo(AppContext.BaseDirectory);
            while (dossier is not null && !File.Exists(Path.Combine(dossier.FullName, "HBA.sln")))
            {
                dossier = dossier.Parent;
            }

            _racine = dossier?.FullName
                ?? throw new InvalidOperationException(
                    "racine du dépôt introuvable : aucun dossier ascendant ne porte HBA.sln");
            return _racine;
        }
    }

    /// <summary>Chemin absolu d'un élément du dépôt.</summary>
    public static string Chemin(params string[] segments)
        => segments.Length == 0 ? Racine : Path.Combine(Racine, Path.Combine(segments));

    /// <summary>Chemin relatif à la racine, avec des séparateurs `/`.</summary>
    public static string Relatif(string absolu)
        => Path.GetRelativePath(Racine, absolu).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Un dossier du dépôt, dont l'absence est une ERREUR et non un vide.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">
    /// Si le dossier n'existe pas — voir le commentaire de la classe.
    /// </exception>
    public static string Dossier(params string[] segments)
    {
        var chemin = Chemin(segments);
        if (!Directory.Exists(chemin))
        {
            throw new DirectoryNotFoundException(
                $"{Relatif(chemin)} n'existe pas : ce contrôle ne peut RIEN regarder. "
                + "Corriger le chemin plutôt que de laisser rendre zéro.");
        }

        return chemin;
    }

    /// <summary>
    /// Tous les fichiers d'un dossier portant l'une des extensions données,
    /// dossiers ignorés exclus.
    /// </summary>
    public static IEnumerable<string> Fichiers(string racine, params string[] extensions)
    {
        foreach (var fichier in Directory.EnumerateFiles(racine, "*", SearchOption.AllDirectories))
        {
            var relatif = Path.GetRelativePath(racine, fichier);
            var segments = relatif.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(s => Ignores.Contains(s) || s.StartsWith('.')))
            {
                continue;
            }

            if (extensions.Length == 0
                || extensions.Any(e => fichier.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                yield return fichier;
            }
        }
    }
}
