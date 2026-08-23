namespace HBA.Media.Contracts;

/// <summary>Une représentation dérivée, telle qu'exposée (§12).</summary>
public sealed record MediaVariantView(
    string VariantType, string Url, int Width, int Height, long SizeBytes);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN MÉDIA, VU DE L'EXTÉRIEUR DU MODULE.
///
/// <paramref name="Url"/> EST NULLE POUR UN FICHIER PRIVÉ, ET CE N'EST PAS UN
/// OUBLI.
///
/// Le §10 est explicite : les documents sensibles ne doivent jamais avoir d'URL
/// publique permanente. Une pièce d'identité ne se lit que par une URL signée de
/// courte durée, demandée nommément. Remplir ce champ « pour la commodité »
/// suffirait à faire fuiter une CNI dans un journal applicatif.
///
/// L'appelant qui a besoin de lire un fichier privé demande une URL signée —
/// c'est un geste séparé, tracé, et à durée limitée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record MediaView(
    Guid Id,
    string OwnerType,
    Guid OwnerId,
    string MediaType,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Visibility,
    string Status,
    int? Width,
    int? Height,

    /// <summary>URL permanente. NULLE si le média n'est pas public.</summary>
    string? Url,

    IReadOnlyList<MediaVariantView> Variants,
    DateTime CreatedOnUtc);

/// <summary>Une URL signée et sa durée de validité (§10).</summary>
public sealed record SignedMediaUrl(string Url, int ExpiresInSeconds);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'API DU SERVICE MÉDIA, POUR LES AUTRES MODULES.
///
/// ELLE NE PREND ET NE REND JAMAIS D'OCTETS.
///
/// Les modules métier gardent un <c>MediaId</c> et rien d'autre — c'est le
/// principe fondateur du §1 : « les autres services gardent seulement un MediaId,
/// mais ne stockent jamais les octets du fichier dans leur base métier ».
///
/// ELLE NE VÉRIFIE AUCUN DROIT MÉTIER.
///
/// Le §20 partage la responsabilité : Media connaît la VISIBILITÉ du fichier, le
/// service propriétaire connaît les DROITS. Media sait qu'une pièce de livreur est
/// privée ; il ne sait pas si ce demandeur-ci a le droit de la voir. C'est à
/// l'appelant de trancher AVANT de demander une URL signée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IMediaModuleApi
{
    Task<MediaView?> GetAsync(Guid mediaId, CancellationToken cancellationToken = default);

    /// <summary>Plusieurs à la fois — une galerie produit en demande dix d'un coup.</summary>
    Task<IReadOnlyList<MediaView>> GetManyAsync(
        IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaView>> ListByOwnerAsync(
        string ownerType, Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// URL de lecture temporaire pour un fichier privé (§10).
    ///
    /// L'APPELANT A DÉJÀ VÉRIFIÉ LE DROIT MÉTIER. Cette méthode ne le fait pas
    /// et ne peut pas le faire — elle ignore ce qu'est un vendeur vérifié ou un
    /// livreur affecté à une course.
    /// </summary>
    Task<SignedMediaUrl?> CreateSignedUrlAsync(
        Guid mediaId, int expiresSeconds = 300, CancellationToken cancellationToken = default);
}
