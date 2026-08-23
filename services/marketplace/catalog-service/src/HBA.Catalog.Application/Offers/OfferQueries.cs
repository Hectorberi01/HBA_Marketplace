using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Application.Offers;

/// <summary>Une offre, telle que la lit l'espace vendeur.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// SIX CHAMPS ONT ÉTÉ AJOUTÉS APRÈS COUP, ET LA RAISON MÉRITE D'ÊTRE LUE.
///
/// La première version de ce contrat transposait celui du monolithe sans le
/// confronter à ce que l'ÉCRAN consomme. Résultat : `productName`, `sku`,
/// `condition`, `commissionAmount`, `providerFeeAmount` et `promotionEndsOnUtc`
/// manquaient tous — six champs que `Offer.fromJson` lit déjà côté Flutter.
///
/// La leçon est la même que pour `OfferSummary` en 3.4 : un contrat se vérifie
/// contre son CONSOMMATEUR, pas contre son ancêtre.
///
/// LES NOMS DE PRIX NE REPRENNENT PAS CEUX DU MONOLITHE.
///
/// L'application lit `productPrice` et `compareAt` — des noms de BFF, qui ne
/// disent ni ce qu'ils contiennent ni pour qui. Ici : `BuyerPrice` (le prix
/// affiché hors promotion), `PromotionalPrice` (nul s'il n'y en a pas) et
/// `EffectivePrice` (celui qu'on facture). C'est au client de s'aligner, pas au
/// contrat neuf de porter la dette de l'ancien.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="ProductName">
/// Le libellé du produit vendu. NE VIENT PAS DE L'OFFRE — elle référence le
/// produit par identifiant. Résolu par une lecture EN LOT, jamais fiche par
/// fiche.
/// </param>
/// <param name="Sku">
/// La référence de la variante. NE VIENT PAS DE L'OFFRE non plus : c'est la
/// VARIANTE qui la porte. C'est elle qui rattache le stock côté Inventory.
/// </param>
/// <param name="CommissionAmount">
/// Part plateforme figée au dernier calcul de prix. Affichée au vendeur pour
/// qu'il sache ce qu'il touche réellement — sans elle, l'écart entre son prix et
/// le prix acheteur reste inexpliqué.
/// </param>
public sealed record OfferDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    Guid VariantId,
    string? Sku,
    Guid StoreId,
    Guid SellerId,
    decimal SellerPrice,
    decimal BuyerPrice,
    decimal? PromotionalPrice,
    decimal EffectivePrice,
    DateTime? PromotionEndsOnUtc,
    decimal CommissionAmount,
    decimal ProviderFeeAmount,
    string Currency,
    string Status,
    string? StatusReason,
    string Condition,
    int HandlingTimeDays);

/// <summary>
/// Les offres ACHETABLES d'un produit — la Buy Box.
/// </summary>
/// <remarks>
/// NE REND QUE LES `Active`, TRIÉES PAR PRIX CROISSANT. Le tri vit dans le
/// dépôt : le laisser à l'appelant ferait classer les vendeurs différemment
/// selon l'écran.
/// </remarks>
public sealed record ListProductOffersQuery(Guid ProductId) : IQuery<IReadOnlyList<OfferDto>>;

/// <summary>Les offres d'une boutique, archivées exclues — « Mes mises en vente ».</summary>
public sealed record ListStoreOffersQuery(Guid StoreId) : IQuery<IReadOnlyList<OfferDto>>;

internal sealed class OfferQueryHandler
    : IQueryHandler<ListProductOffersQuery, IReadOnlyList<OfferDto>>,
      IQueryHandler<ListStoreOffersQuery, IReadOnlyList<OfferDto>>
{
    private readonly IProductOfferRepository _offers;
    private readonly IProductRepository _products;

    public OfferQueryHandler(IProductOfferRepository offers, IProductRepository products)
    {
        _offers = offers;
        _products = products;
    }

    public async Task<Result<IReadOnlyList<OfferDto>>> Handle(ListProductOffersQuery query, CancellationToken ct)
        => Result.Success(await ProjectAsync(
            await _offers.ListActiveByProductAsync(query.ProductId, ct), ct));

    public async Task<Result<IReadOnlyList<OfferDto>>> Handle(ListStoreOffersQuery query, CancellationToken ct)
        => Result.Success(await ProjectAsync(
            await _offers.ListByStoreAsync(query.StoreId, cancellationToken: ct), ct));

    /// <summary>Résout noms et SKU en DEUX requêtes, quelle que soit la taille de la liste.</summary>
    /// <remarks>
    /// C'EST TOUT L'INTÉRÊT DE LA PROJECTION GROUPÉE. Résoudre le nom et la
    /// référence offre par offre ferait 2N requêtes pour N mises en vente — sur
    /// l'écran d'un vendeur qui en a soixante, c'est cent vingt allers-retours
    /// là où deux suffisent.
    /// </remarks>
    private async Task<IReadOnlyList<OfferDto>> ProjectAsync(
        IReadOnlyList<ProductOffer> offers, CancellationToken ct)
    {
        if (offers.Count == 0)
        {
            return [];
        }

        var noms = await _products.GetNamesByIdsAsync(
            offers.Select(o => o.ProductId).Distinct().ToList(), ct);

        var skus = await _products.GetSkusByVariantIdsAsync(
            offers.Select(o => o.VariantId).Distinct().ToList(), ct);

        return offers.Select(o => ToDto(o, noms, skus)).ToList();
    }

    private static OfferDto ToDto(
        ProductOffer o,
        IReadOnlyDictionary<Guid, string> noms,
        IReadOnlyDictionary<Guid, string> skus)
        => new(
            Id: o.Id.Value,
            ProductId: o.ProductId,

            // REPLI SUR UNE CHAÎNE VIDE, PAS SUR « Produit inconnu ».
            //
            // Une fiche supprimée pendant que ses offres survivent est un état
            // transitoire ; inventer un libellé le figerait à l'écran. Le client
            // a déjà son propre repli, et lui seul sait ce qu'il veut afficher.
            ProductName: noms.GetValueOrDefault(o.ProductId, string.Empty),

            VariantId: o.VariantId,
            Sku: skus.GetValueOrDefault(o.VariantId),
            StoreId: o.StoreId,
            SellerId: o.SellerId,
            SellerPrice: o.SellerPrice.Amount,
            BuyerPrice: o.BuyerPrice.Amount,
            PromotionalPrice: o.PromotionalPrice?.Amount,
            EffectivePrice: o.EffectivePrice.Amount,
            PromotionEndsOnUtc: o.PromotionEndsOnUtc,
            CommissionAmount: o.CommissionAmount,
            ProviderFeeAmount: o.ProviderFeeAmount,
            Currency: o.BuyerPrice.Currency,
            Status: o.Status.ToString(),
            StatusReason: o.StatusReason,
            Condition: o.Condition.ToString(),
            HandlingTimeDays: o.HandlingTimeDays);
}
