using Microsoft.EntityFrameworkCore;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Infrastructure.Persistence;

/// <summary>Accès aux offres.</summary>
/// <remarks>
/// DEUX FAMILLES DE LECTURES, ET LA DIFFÉRENCE N'EST PAS COSMÉTIQUE.
///
///   • celles qui servent un ÉCRAN sont `AsNoTracking` et filtrent les archivées ;
///   • celles qui servent une DÉCISION (`*ForUpdateAsync`, `ListByVariantAsync`)
///     sont SUIVIES par EF et ne filtrent rien.
///
/// Détacher les secondes obligerait à recharger chaque offre une à une pour la
/// modifier ; y ajouter un filtre ferait échapper des offres à une sanction.
/// </remarks>
internal sealed class ProductOfferRepository : IProductOfferRepository
{
    private readonly CatalogDbContext _dbContext;

    public ProductOfferRepository(CatalogDbContext dbContext) => _dbContext = dbContext;

    public async Task<ProductOffer?> GetByIdAsync(OfferId id, CancellationToken cancellationToken = default)
        => await _dbContext.Offers.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProductOffer>> ListActiveByProductAsync(
        Guid productId, CancellationToken cancellationToken = default)
        // Triées par prix acheteur croissant : c'est l'ordre de la Buy Box, et le
        // laisser au consommateur reviendrait à ce que la vitrine et
        // l'application le trient différemment.
        => await _dbContext.Offers
            .AsNoTracking()
            .Where(o => o.ProductId == productId && o.Status == OfferStatus.Active)
            .OrderBy(o => o.BuyerPrice.Amount)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductOffer>> ListByStoreAsync(
        Guid storeId, int take = 200, CancellationToken cancellationToken = default)
        => await _dbContext.Offers
            .AsNoTracking()
            .Where(o => o.StoreId == storeId && o.Status != OfferStatus.Archived)
            .OrderByDescending(o => o.CreatedOnUtc)
            .Take(take <= 0 ? 200 : take)
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// SUR `SellerId`, PAS `StoreId`. Les deux valaient la même chose dans le
    /// monolithe — la reprise y avait peuplé `StoreId` avec l'identifiant du
    /// vendeur. Côté HBA, `Store` existe réellement (merchant-service, tâche S6) :
    /// filtrer sur la boutique ne suspendrait qu'une des boutiques d'un vendeur
    /// sanctionné. Ce filtre-ci reste juste dans les deux mondes.
    /// </remarks>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CELLE-CI N'EST PAS BORNÉE, ET ELLE NE DOIT PAS L'ÊTRE (§12).
    ///
    /// Le relevé du lot 8.4 la classe parmi les lectures non bornées, et c'est
    /// exact. Mais elle sert la SUSPENSION D'UN VENDEUR : elle doit rendre TOUTES
    /// ses offres, parce que l'appelant les retire de la vente.
    ///
    /// Y poser un `Take` laisserait, après suspension d'un vendeur au gros
    /// catalogue, une partie de ses offres EN VENTE — silencieusement, et
    /// précisément celles que la borne aurait coupées. Une sanction appliquée à
    /// moitié est pire qu'une requête lente : la première se voit sur la vitrine,
    /// la seconde dans les journaux.
    ///
    /// LA VRAIE RÉPONSE, LE JOUR OÙ CE VOLUME POSERA PROBLÈME, EST UN TRAITEMENT
    /// PAR LOTS — lire mille offres, les retirer, recommencer jusqu'à épuisement.
    /// C'est un changement du HANDLER, pas de cette signature, et il demande de
    /// rendre l'opération reprenable. Ce lot ne le fait pas ; il refuse seulement
    /// la correction qui aurait l'air d'en être une.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<IReadOnlyList<ProductOffer>> ListAllBySellerForUpdateAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Offers
            .Where(o => o.SellerId == sellerId)
            .ToListAsync(cancellationToken);

    /// <remarks>
    /// NON BORNÉE POUR LA MÊME RAISON QUE sa voisine : elle sert la fermeture
    /// d'une boutique, et doit rendre toutes ses offres.
    /// </remarks>
    public async Task<IReadOnlyList<ProductOffer>> ListAllByStoreForUpdateAsync(
        Guid storeId, CancellationToken cancellationToken = default)
        => await _dbContext.Offers
            .Where(o => o.StoreId == storeId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductOffer>> ListByVariantAsync(
        Guid variantId, CancellationToken cancellationToken = default)
        // SUIVIES (pas de `AsNoTracking`) : l'appelant archive ces offres quand la
        // variante est désactivée.
        => await _dbContext.Offers
            .Where(o => o.VariantId == variantId && o.Status != OfferStatus.Archived)
            .ToListAsync(cancellationToken);

    public async Task<bool> ExistsForStoreAndVariantAsync(
        Guid storeId, Guid variantId, CancellationToken cancellationToken = default)
        => await _dbContext.Offers
            .AsNoTracking()
            .AnyAsync(
                o => o.StoreId == storeId && o.VariantId == variantId && o.Status != OfferStatus.Archived,
                cancellationToken);

    public async Task AddAsync(ProductOffer offer, CancellationToken cancellationToken = default)
        => await _dbContext.Offers.AddAsync(offer, cancellationToken);
    public async Task<IReadOnlyList<ProductOffer>> ListByIdsAsync(
        IReadOnlyCollection<OfferId> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            // Sans ce court-circuit, EF traduirait `IN ()`, que PostgreSQL refuse.
            return [];
        }

        return await _dbContext.Offers
            .AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductOffer>> ListBySkuAsync(
        string sku, CancellationToken cancellationToken = default)
    {
        var reference = Sku.Create(sku);
        if (reference.IsFailure)
        {
            // Une référence illisible ne désigne aucune variante : liste vide,
            // pas d'exception. L'appelant — Inventory — traite un SKU qu'il tient
            // d'ailleurs, et une erreur ici ferait échouer un signalement de
            // rupture pour une donnée qui ne nous appartient pas.
            return [];
        }

        var valeur = reference.Value;

        // LA VARIANTE PORTE LE SKU, PAS L'OFFRE — d'où la jointure.
        //
        // On passe par `SelectMany(p => p.Variants)` plutôt que par une propriété
        // de navigation : `ProductOffer` n'en a AUCUNE vers `Product`, et c'est
        // délibéré (voir l'encadré de l'agrégat). L'offre le référence par
        // identifiant pour qu'un changement de prix ne charge pas la fiche
        // entière.
        var variantIds = await _dbContext.Products
            .AsNoTracking()
            .SelectMany(p => p.Variants)
            .Where(v => v.Sku == valeur)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        if (variantIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.Offers
            .AsNoTracking()
            .Where(o => variantIds.Contains(o.VariantId) && o.Status != OfferStatus.Archived)
            .ToListAsync(cancellationToken);
    }
}
