using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Shared.Infrastructure.Inbox;

namespace HBA.Catalog.UnitTests;

/// <summary>
/// Doublures des dépendances des gestionnaires de suspension.
/// </summary>
/// <remarks>
/// TOUT CE QUI N'EST PAS SOLLICITÉ LÈVE, PLUTÔT QUE DE RENDRE UNE VALEUR NEUTRE.
///
/// Rendre une liste vide ou `null` ferait passer en silence un test qui
/// emprunterait un chemin imprévu : on croirait avoir éprouvé le retrait des
/// offres alors qu'on aurait éprouvé une lecture qui ne rend rien. L'exception dit
/// exactement quelle méthode a été appelée sans qu'on l'ait voulu.
/// </remarks>
internal sealed class DepotDOffres : IProductOfferRepository
{
    private readonly List<ProductOffer> _offres;

    public DepotDOffres(params ProductOffer[] offres) => _offres = [.. offres];

    /// <summary>
    /// REND LA MÊME LISTE POUR LE VENDEUR ET POUR LA BOUTIQUE, ET C'EST FIDÈLE.
    ///
    /// `StoreId` est aujourd'hui peuplé avec l'identifiant du vendeur — c'est écrit
    /// dans l'encadré de `ListAllBySellerForUpdateAsync`. Filtrer ici sur l'un ou
    /// l'autre ferait diverger la doublure du dépôt réel, et un test passerait sur
    /// une distinction qui n'existe pas encore en base.
    /// </summary>
    public Task<IReadOnlyList<ProductOffer>> ListAllBySellerForUpdateAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ProductOffer>>(_offres);

    public Task<IReadOnlyList<ProductOffer>> ListAllByStoreForUpdateAsync(
        Guid storeId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ProductOffer>>(_offres);

    public Task<ProductOffer?> GetByIdAsync(OfferId id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<IReadOnlyList<ProductOffer>> ListActiveByProductAsync(
        Guid productId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<IReadOnlyList<ProductOffer>> ListByStoreAsync(
        Guid storeId, int take = 200, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<IReadOnlyList<ProductOffer>> ListByVariantAsync(
        Guid variantId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<bool> ExistsForStoreAndVariantAsync(
        Guid storeId, Guid variantId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task AddAsync(ProductOffer offer, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<IReadOnlyList<ProductOffer>> ListByIdsAsync(
        IReadOnlyCollection<OfferId> ids, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<IReadOnlyList<ProductOffer>> ListBySkuAsync(
        string sku, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");
}

/// <summary>
/// Le dépôt de fiches. Une seule méthode est appelée par la réouverture d'une
/// boutique : la résolution des SKU, qui n'entre dans aucune décision testée ici.
/// </summary>
internal sealed class DepotDeProduits : IProductRepository
{
    public Task<IReadOnlyDictionary<Guid, string>> GetSkusByVariantIdsAsync(
        IReadOnlyCollection<Guid> variantIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

    public Task AddAsync(Product product, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<IReadOnlyList<Product>> ListBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<IReadOnlyList<Product>> ListBySellerForUpdateAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<IReadOnlyList<Product>> ListAllAsync(
        int take = 500, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<(IReadOnlyList<Product> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, string? search, ProductStatus? status, string? sort, bool desc,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<IReadOnlyCollection<Slug>> ListTakenSlugsAsync(
        IReadOnlyCollection<Slug> candidats, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<Product?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<(IReadOnlyList<Product> Items, int Total)> SearchPublishedAsync(
        RecherchePublique criteres, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<(IReadOnlyList<Product> Items, int Total)> ListPendingReviewAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    public Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");

    /// <summary>
    /// LE SEUL MEMBRE SYNCHRONE DE L'INTERFACE, ET C'EST CE QUI L'A FAIT OUBLIER.
    ///
    /// Recenser les membres d'un dépôt en cherchant `Task` en rate exactement
    /// celui-ci. Le compilateur l'a dit tout de suite (`CS0535`) ; sans lui, la
    /// doublure aurait eu l'air complète.
    /// </summary>
    public void Remove(Product product)
        => throw new NotSupportedException("Non sollicité par les tests de suspension.");
}

/// <summary>
/// L'unité de travail. Elle COMPTE ses appels : c'est la seule façon, sans base,
/// de distinguer « rien à faire » de « rien n'a été committé ».
/// </summary>
internal sealed class UniteDeTravail : ICatalogUnitOfWork
{
    public int Sauvegardes { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Sauvegardes++;

        return Task.FromResult(0);
    }
}

/// <summary>
/// La boîte de réception. `DejaTraite` simule un rejeu.
/// </summary>
/// <remarks>
/// `MarkProcessedAsync` NE COMMITTE RIEN, exactement comme la vraie.
///
/// C'est le `SaveChangesAsync` du gestionnaire qui valide la trace ET l'effet dans
/// la même transaction. Une doublure qui persisterait de son côté masquerait une
/// régression où la marque serait committée séparément — précisément la fenêtre
/// que l'inbox existe pour fermer.
/// </remarks>
internal sealed class BoiteDeReception : IConsumerInbox
{
    public bool DejaTraite { get; init; }

    public int Marques { get; private set; }

    public Task<bool> HasProcessedAsync(
        Guid eventId, string consumerName, CancellationToken cancellationToken = default)
        => Task.FromResult(DejaTraite);

    public Task MarkProcessedAsync(
        Guid eventId,
        string consumerName,
        string eventType,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        Marques++;

        return Task.CompletedTask;
    }
}
