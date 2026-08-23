using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HBA.Admin.Desktop.Services;

/// <summary>Un rôle et ses permissions.</summary>
public sealed record RoleAdmin(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("isSystem")] bool IsSystem,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string>? Permissions);

/// <summary>Le format d'une permission, tel que le domaine l'exige.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// IL N'EXISTE AUCUNE LISTE FERMÉE DE PERMISSIONS, ET C'EST LE POINT.
///
/// `Permission.Create` n'accepte pas une valeur parmi N : elle valide une FORME,
/// « ressource.action », en minuscules, cent caractères au plus. Le domaine
/// n'énumère rien — chaque service nomme ses propres droits.
///
/// L'écran ne peut donc pas proposer de liste déroulante. Il accepte du texte, et
/// vérifie la forme AVANT d'envoyer : sinon le service répond 400 sur la ligne
/// fautive, sans dire laquelle des vingt.
///
/// LE MOTIF EST RECOPIÉ DU DOMAINE, PAS APPROXIMÉ.
///
/// `^[a-z0-9_]+(\.[a-z0-9_]+)+$` — le souligné est accepté, le tiret non, et il
/// faut au moins un point. Écrire « à peu près la même chose » ferait refuser
/// côté client des permissions que le serveur accepte, ou l'inverse.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public static partial class FormatPermission
{
    /// <summary>Longueur maximale, alignée sur `Permission.Create`.</summary>
    public const int LongueurMaximale = 100;

    [GeneratedRegex(@"^[a-z0-9_]+(\.[a-z0-9_]+)+$")]
    private static partial Regex Motif();

    /// <summary>Valide une permission isolée.</summary>
    public static bool EstValide(string valeur)
        => !string.IsNullOrWhiteSpace(valeur)
           && valeur.Length <= LongueurMaximale
           && Motif().IsMatch(valeur);

    /// <summary>
    /// Découpe une saisie multi-ligne et rend la première ligne fautive, ou `null`.
    /// </summary>
    /// <remarks>
    /// ON REND LA LIGNE FAUTIVE, PAS UN BOOLÉEN.
    ///
    /// « Permissions invalides » sur une liste de vingt oblige à les relire une à
    /// une. Nommer la ligne refusée fait corriger en un geste.
    ///
    /// La normalisation — minuscules, espaces retirés — est faite ICI comme dans
    /// `Permission.Create`, pour qu'une saisie en majuscules soit acceptée des
    /// deux côtés de la même façon.
    /// </remarks>
    public static string? PremiereInvalide(IEnumerable<string> permissions)
        => permissions.FirstOrDefault(p => !EstValide(p));

    /// <summary>Normalise une saisie multi-ligne en liste de permissions.</summary>
    public static IReadOnlyList<string> Decouper(string? saisie)
        => string.IsNullOrWhiteSpace(saisie)
            ? []
            : saisie
                .Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries
                                               | StringSplitOptions.TrimEntries)
                .Select(p => p.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}
