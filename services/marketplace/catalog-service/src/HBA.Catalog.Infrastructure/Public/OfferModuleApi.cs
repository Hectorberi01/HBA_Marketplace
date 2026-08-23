using Microsoft.EntityFrameworkCore;
using HBA.Catalog.Contracts;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Infrastructure.Persistence;

namespace HBA.Catalog.Infrastructure.Public;

/// <summary>Implémentation in-process de la lecture des offres.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// PAS DE CACHE ICI, CONTRAIREMENT À `CatalogModuleApi`.
///
/// Les fiches produit sont mises en cache parce qu'elles changent rarement et
/// sont lues à chaque affichage. Un PRIX est l'inverse : c'est ce qui change le
/// plus souvent, et un prix périmé de trente secondes est un prix faux — affiché
/// à l'acheteur, puis figé dans son panier.
///
/// Le jour où la charge l'exigera, la bonne réponse sera une invalidation sur
/// `ProductOfferPriceChangedDomainEvent`, pas une durée de vie.
///
/// LE SKU COÛTE UNE SECONDE REQUÊTE, ET C'EST ASSUMÉ.
///
/// L'offre porte un `VariantId`, pas un SKU. Chaque projection résout donc les
/// références par un appel à `IProductRepository.GetSkusByVariantIdsAsync` — EN
/// LOT, jamais une par une. C'est la seule dépendance de ce fichier vers
/// l'agrégat produit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class OfferModuleApi : IOfferModuleApi
{
    private readonly IProductOfferRepository _offers;
    private readonly CatalogDbContext _dbContext;

    public OfferModuleApi(IProductOfferRepository offers, CatalogDbContext dbContext)
    {
        _offers = offers;
        _dbContext = dbContext;
    }

    public async Task<OfferSummary?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken = default)
    {
        var offer = await _offers.GetByIdAsync(new OfferId(offerId), cancellationToken);
        if (offer is null)
        {
            return null;
        }

        var skus = await ResolveSkusAsync([offer.VariantId], cancellationToken);
        return ToContract(offer, skus);
    }

    public async Task<IReadOnlyDictionary<Guid, OfferSummary>> GetOffersAsync(
        IReadOnlyCollection<Guid> offerIds, CancellationToken cancellationToken = default)
    {
        if (offerIds.Count == 0)
        {
            return new Dictionary<Guid, OfferSummary>();
        }

        var offers = await _offers.ListByIdsAsync(
            offerIds.Select(id => new OfferId(id)).ToList(), cancellationToken);

        var skus = await ResolveSkusAsync(offers.Select(o => o.VariantId), cancellationToken);

        // INDEXÉ PAR IDENTIFIANT D'OFFRE, et les absents ne figurent pas.
        //
        // Un dictionnaire incomplet est la bonne réponse : l'appelant demande huit
        // offres, en reçoit sept, et sait laquelle manque. Rendre une liste
        // l'obligerait à faire lui-même la correspondance, et rendre une entrée
        // nulle lui ferait croire à une offre vide.
        return offers.ToDictionary(o => o.Id.Value, o => ToContract(o, skus));
    }

    public async Task<IReadOnlyList<OfferSummary>> ListPurchasableOffersAsync(
        Guid productId, CancellationToken cancellationToken = default)
    {
        // Le tri par prix croissant vit dans le dépôt : c'est l'ordre de la Buy
        // Box, et le refaire ici le dédoublerait.
        var offers = await _offers.ListActiveByProductAsync(productId, cancellationToken);
        var skus = await ResolveSkusAsync(offers.Select(o => o.VariantId), cancellationToken);

        return offers.Select(o => ToContract(o, skus)).ToList();
    }

    public async Task<IReadOnlyList<OfferSummary>> ListOffersBySkuAsync(
        string sku, CancellationToken cancellationToken = default)
    {
        var offers = await _offers.ListBySkuAsync(sku, cancellationToken);
        var skus = await ResolveSkusAsync(offers.Select(o => o.VariantId), cancellationToken);

        return offers.Select(o => ToContract(o, skus)).ToList();
    }

    /// <summary>Les SKU des variantes citées, en UNE requête.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ResolveSkusAsync(
        IEnumerable<Guid> variantIds, CancellationToken cancellationToken)
    {
        var ids = variantIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // `v.Sku` ET NON `v.Sku.Value` : voir l'encadré de
        // `ProductRepository.GetSkusByVariantIdsAsync`. `Sku` porte un
        // convertisseur de valeur, et EF ne sait pas descendre dedans — la
        // requête lève à l'exécution, jamais à la compilation.
        var lignes = await _dbContext.Products
            .AsNoTracking()
            .SelectMany(p => p.Variants)
            .Where(v => ids.Contains(v.Id))
            .Select(v => new { v.Id, v.Sku })
            .ToListAsync(cancellationToken);

        return lignes.ToDictionary(l => l.Id, l => l.Sku.Value);
    }

    private static OfferSummary ToContract(ProductOffer o, IReadOnlyDictionary<Guid, string> skus)
        => new(
            Id: o.Id.Value,
            ProductId: o.ProductId,
            VariantId: o.VariantId,
            StoreId: o.StoreId,
            SellerId: o.SellerId,

            // `null` SI LA VARIANTE A DISPARU, et non une chaîne vide : le
            // contrat distingue « pas de référence » de « référence vide », et
            // Inventory ne doit pas chercher un SKU qui n'existe pas.
            Sku: skus.GetValueOrDefault(o.VariantId),

            BuyerPrice: o.BuyerPrice.Amount,
            PromotionalPrice: o.PromotionalPrice?.Amount,
            EffectivePrice: o.EffectivePrice.Amount,
            PromotionEndsOnUtc: o.PromotionEndsOnUtc,
            Currency: o.BuyerPrice.Currency,
            Status: o.Status.ToString(),

            // CALCULÉ PAR LE DOMAINE, PAS DÉDUIT DU STATUT ICI.
            // `OfferStatusTransitions.IsPurchasable` est la règle ; la recopier
            // sous la forme `Status == "Active"` créerait un second endroit à
            // corriger le jour où un état de plus devient achetable.
            IsPurchasable: OfferStatusTransitions.IsPurchasable(o.Status),

            Condition: o.Condition.ToString(),
            HandlingTimeDays: o.HandlingTimeDays,
            ShipFromLocationId: o.ShipFromLocationId);
}
