using System.Collections.Concurrent;
using HBA.Media.Contracts;

namespace HBA.Merchants.IntegrationTests;

/// <summary>UN SERVICE MÉDIA EN MÉMOIRE, PILOTABLE DEPUIS LES TESTS.</summary>
internal sealed class MediaDeTest : IMediaModuleApi
{
    private readonly ConcurrentDictionary<Guid, MediaView> _medias = new();

    /// <summary>Dépose un média et rend son identifiant.</summary>
    /// <param name="ownerId">
    /// Le propriétaire — un identifiant de VENDEUR pour une pièce KYB.
    /// </param>
    /// <param name="mediaType">
    /// `SellerDocument` pour une pièce légale ; `StoreMedia` pour une photo de
    /// boutique.
    /// </param>
    /// <param name="status">
    /// `Ready`, ou `Processing` pour éprouver le refus d'un fichier pas encore
    /// traité.
    /// </param>
    /// <param name="ownerType">
    /// `Seller`, ou autre chose pour éprouver le contrôle de propriété.
    /// </param>
    /// <param name="deposeParUserId">
    /// L'utilisateur qui a téléversé le fichier, tel que media-service le lit dans
    /// le JETON — la seule donnée du contrat que l'appelant ne choisit pas.
    /// </param>
    public Guid Deposer(
        Guid ownerId,
        string mediaType = "SellerDocument",
        string status = "Ready",
        string ownerType = "Seller",
        Guid? deposeParUserId = null)
    {
        var id = Guid.NewGuid();

        _medias[id] = new MediaView(
            Id: id,
            OwnerType: ownerType,
            OwnerId: ownerId,
            MediaType: mediaType,
            OriginalFileName: "cni.pdf",
            ContentType: "application/pdf",
            SizeBytes: 120_000,
            // Une pièce KYB est PRIVÉE par politique (§12) : pas d'URL permanente.
            Visibility: "Private",
            Status: status,
            Width: null,
            Height: null,
            Url: null,
            Variants: [],
            CreatedOnUtc: DateTime.UtcNow,
            CreatedByUserId: deposeParUserId ?? Guid.Empty);

        return id;
    }

    public Task<MediaView?> GetAsync(Guid mediaId, CancellationToken cancellationToken = default)
        => Task.FromResult(_medias.TryGetValue(mediaId, out var media) ? media : null);

    public Task<IReadOnlyList<MediaView>> GetManyAsync(
        IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MediaView>>(
            mediaIds.Select(id => _medias.TryGetValue(id, out var m) ? m : null)
                .Where(m => m is not null)
                .Select(m => m!)
                .ToList());

    public Task<IReadOnlyList<MediaView>> ListByOwnerAsync(
        string ownerType, Guid ownerId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MediaView>>(
            _medias.Values
                .Where(m => string.Equals(m.OwnerType, ownerType, StringComparison.OrdinalIgnoreCase)
                            && m.OwnerId == ownerId)
                .ToList());

    /// <summary>LÈVE PLUTÔT QUE DE RENDRE UNE URL FACTICE.</summary>
    public Task<SignedMediaUrl?> CreateSignedUrlAsync(
        Guid mediaId, int expiresSeconds = 300, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "seller-service ne signe pas d'URL. Si c'est devenu le cas, ce test doit décider "
            + "ce qu'il rend — pas hériter d'un défaut silencieux sur un chemin qui sert des "
            + "pièces d'identité.");
}
