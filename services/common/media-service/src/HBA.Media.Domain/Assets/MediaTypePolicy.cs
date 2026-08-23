using HBA.Shared.Domain.Results;

namespace HBA.Media.Domain.Assets;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES RÈGLES DE CHAQUE NATURE DE MÉDIA (cahier des charges §8, §13, §19).
///
/// UNE SEULE TABLE, ET C'EST L'ESSENTIEL DE CE FICHIER.
///
/// Formats admis, taille maximale, visibilité, rétention, variantes : cinq
/// décisions qui doivent rester COHÉRENTES pour une même nature de fichier.
/// Éparpillées — la taille dans un validateur, la visibilité dans un handler, la
/// rétention dans une tâche de fond — elles auraient divergé au premier ajout, et
/// une pièce d'identité se serait retrouvée dans un bucket public sans que
/// personne ne l'ait décidé.
///
/// Le préfixe de clé d'objet en fait partie : c'est lui qui range physiquement
/// les fichiers, et un document privé rangé sous « products/ » finirait servi par
/// le CDN.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="AllowedContentTypes">
/// Liste BLANCHE explicite (§8). Jamais une liste noire : une liste noire laisse
/// passer tout ce qu'on n'a pas pensé à interdire, et c'est toujours le format
/// exotique qui sert de vecteur.
/// </param>
/// <param name="MaxSizeBytes">Plafond par nature. Une facture PDF n'a pas le poids d'une photo.</param>
/// <param name="DefaultVisibility">
/// IMPOSÉE, PAS SUGGÉRÉE. L'appelant ne choisit pas : une pièce d'identité est
/// privée parce qu'elle est une pièce d'identité, pas parce qu'un développeur y a
/// pensé ce jour-là.
/// </param>
/// <param name="KeyPrefix">Le dossier logique dans le stockage (§6).</param>
/// <param name="GeneratesVariants">
/// Les images publiques sont déclinées (§11). Un PDF ne l'est pas, et une pièce
/// privée non plus — dériver une CNI en miniature multiplierait par cinq les
/// copies d'un document sensible.
/// </param>
/// <param name="RetentionDaysAfterDelete">
/// Délai avant l'effacement PHYSIQUE (§19). Les documents légaux et les preuves
/// de livraison survivent plus longtemps : un litige se déclare des semaines après
/// la livraison, et une preuve effacée le lendemain ne prouve plus rien.
/// </param>
public sealed record MediaTypePolicy(
    IReadOnlySet<string> AllowedContentTypes,
    long MaxSizeBytes,
    MediaVisibility DefaultVisibility,
    string KeyPrefix,
    bool GeneratesVariants,
    int RetentionDaysAfterDelete)
{
    private const long Ko = 1024;
    private const long Mo = 1024 * Ko;

    private static readonly HashSet<string> Images =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    private static readonly HashSet<string> ImagesEtPdf =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp", "application/pdf" };

    private static readonly HashSet<string> PdfSeul =
        new(StringComparer.OrdinalIgnoreCase) { "application/pdf" };

    public static MediaTypePolicy For(MediaType type) => type switch
    {
        MediaType.ProductImage => new(Images, 10 * Mo, MediaVisibility.Public, "products", true, 30),
        MediaType.StoreMedia => new(Images, 10 * Mo, MediaVisibility.Public, "stores", true, 30),
        MediaType.RestaurantMedia => new(Images, 10 * Mo, MediaVisibility.Public, "restaurants", true, 30),

        // Un avatar est petit par nature ; accepter dix mégaoctets pour une photo
        // de profil, c'est accepter de les servir à chaque affichage de liste.
        MediaType.UserAvatar => new(Images, 5 * Mo, MediaVisibility.Public, "users", true, 30),

        // PRIVÉS, ET SANS VARIANTES. Les décliner multiplierait les copies d'une
        // pièce d'identité — chaque copie étant une fuite de plus le jour où le
        // bucket est mal configuré.
        MediaType.SellerDocument => new(ImagesEtPdf, 15 * Mo, MediaVisibility.Private, "sellers/documents", false, 365),
        MediaType.DriverDocument => new(ImagesEtPdf, 15 * Mo, MediaVisibility.Private, "drivers/documents", false, 365),

        // Restreint et non privé : le client, le livreur et le support ont chacun
        // une raison légitime de la voir. C'est au service Delivery de trancher.
        MediaType.DeliveryProof => new(ImagesEtPdf, 10 * Mo, MediaVisibility.Restricted, "deliveries/proofs", false, 180),

        MediaType.Invoice => new(PdfSeul, 10 * Mo, MediaVisibility.Private, "invoices", false, 3650),
        MediaType.Attachment => new(ImagesEtPdf, 20 * Mo, MediaVisibility.Restricted, "attachments", false, 90),

        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>
    /// Le fichier proposé respecte-t-il la règle ?
    ///
    /// LE NOM DU FICHIER N'EST PAS UNE PREUVE (§8). On valide le TYPE MIME
    /// déclaré, puis l'on vérifie que l'extension lui correspond — un
    /// « facture.pdf » annoncé en <c>image/jpeg</c> ment forcément sur l'un des
    /// deux, et on ne sait pas lequel.
    /// </summary>
    public Result Validate(string contentType, string fileName, long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return Result.Failure(Error.Validation("media.empty", "Fichier vide."));
        }

        if (sizeBytes > MaxSizeBytes)
        {
            return Result.Failure(Error.Validation(
                "media.too_large",
                $"Fichier trop volumineux : {sizeBytes / Mo} Mo pour un maximum de {MaxSizeBytes / Mo} Mo."));
        }

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType.Trim()))
        {
            return Result.Failure(Error.Validation(
                "media.content_type_not_allowed",
                $"Format « {contentType} » non autorisé. Attendu : {string.Join(", ", AllowedContentTypes)}."));
        }

        var extension = ExtensionFor(contentType);
        var extensionDuNom = Path.GetExtension(fileName ?? string.Empty).TrimStart('.').ToLowerInvariant();

        // « jpg » et « jpeg » désignent la même chose, et refuser l'un des deux
        // ferait échouer un upload sur trois sans raison compréhensible.
        var equivalentes = extensionDuNom is "jpg" or "jpeg" && extension is "jpg";

        if (extensionDuNom.Length > 0 && !equivalentes && extensionDuNom != extension)
        {
            return Result.Failure(Error.Validation(
                "media.extension_mismatch",
                $"L'extension « .{extensionDuNom} » ne correspond pas au format déclaré « {contentType} »."));
        }

        return Result.Success();
    }

    /// <summary>
    /// L'extension canonique d'un type MIME.
    ///
    /// DÉRIVÉE DU TYPE MIME, JAMAIS REPRISE DU NOM UTILISATEUR (§6, §8). Un nom
    /// de fichier peut contenir des séparateurs, des points, des caractères de
    /// contrôle — et servir de clé de stockage à ce qu'on n'a pas prévu.
    /// </summary>
    public static string ExtensionFor(string contentType) => contentType?.Trim().ToLowerInvariant() switch
    {
        "image/jpeg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        "application/pdf" => "pdf",
        _ => "bin"
    };

    /// <summary>Le fichier est-il une image ? Décide de la génération de variantes.</summary>
    public static bool IsImage(string contentType)
        => contentType?.Trim().StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
}
