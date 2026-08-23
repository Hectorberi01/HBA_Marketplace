namespace HBA.Catalog.Contracts;

/// <summary>Une offre, telle que catalog-service la publie aux autres services.</summary>
/// <param name="Sku">
/// La référence de la variante vendue.
///
/// ELLE NE VIENT PAS DE L'OFFRE. `ProductOffer` porte un `VariantId` ; c'est la
/// VARIANTE qui porte le SKU. Le champ est résolu par jointure à la projection,
/// et c'est le seul de ce contrat qui coûte une seconde lecture.
///
/// Il est là parce qu'Inventory indexe le stock par SKU : sans lui, un appelant
/// devrait redemander la variante pour savoir quoi décompter.
/// </param>
/// <param name="EffectivePrice">
/// Ce que l'acheteur paie AUJOURD'HUI. Calculé côté serveur — voir
/// <c>ProductOffer.EffectivePrice</c>, qui compare l'échéance de promotion à
/// l'heure courante. Le recalculer côté appelant ferait exister deux règles de
/// promotion, qui divergeraient à la première évolution.
/// </param>
public sealed record OfferSummary(
    Guid Id,
    Guid ProductId,
    Guid VariantId,
    Guid StoreId,
    Guid SellerId,
    string? Sku,
    decimal BuyerPrice,
    decimal? PromotionalPrice,
    decimal EffectivePrice,
    DateTime? PromotionEndsOnUtc,
    string Currency,
    string Status,
    bool IsPurchasable,
    string Condition,
    int HandlingTimeDays,
    Guid ShipFromLocationId);

/// <summary>
/// Lecture des offres — le PRIX, servi par catalog-service.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UN CONTRAT ICI PLUTÔT QUE DE RÉUTILISER `HBA.Products.Contracts`.
///
/// `OfferSummary` existe déjà là-bas, à l'identique, et `IProductsModuleApi` y
/// déclare ces quatre lectures. La tentation était donc de référencer ce projet
/// depuis `HBA.Catalog.Contracts` — ou d'y déplacer le type.
///
/// Les deux auraient été des erreurs, pour la même raison : **le contrat de fil
/// est déjà la frontière**. `catalog.proto` déclare les quatre RPC d'offre sur
/// `CatalogApi`, avec son propre `message OfferSummary`. Le serveur projette donc
/// domaine → CE contrat → proto ; le client projette proto →
/// `HBA.Products.Contracts.OfferSummary`, qu'il n'a pas à changer.
///
/// Les deux types ne se rencontrent jamais. Aucune référence de projet n'est
/// nécessaire, `commerce-service` et `communication-service` ne bougent pas, et
/// chacun des deux côtés peut évoluer sans casser l'autre — ce qui est très
/// exactement ce à quoi sert un contrat de fil.
///
/// CE QUI RESTE VRAI : `HBA.Products.Contracts` est un VESTIGE. Il porte un
/// `ProductSummary` qui fait doublon avec celui de catalog-service, et son nom
/// désigne un module qui ne sera jamais extrait. Le replier n'est pas le travail
/// de cette phase, mais c'est un travail.
///
/// LECTURE SEULE. Créer une offre ou changer un prix passe par une commande
/// MediatR, pas par cette interface.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IOfferModuleApi
{
    Task<OfferSummary?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, OfferSummary>> GetOffersAsync(
        IReadOnlyCollection<Guid> offerIds, CancellationToken cancellationToken = default);

    /// <summary>Les offres achetables d'un produit — la Buy Box, triée par prix.</summary>
    Task<IReadOnlyList<OfferSummary>> ListPurchasableOffersAsync(
        Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les offres qui vendent une référence donnée.
    ///
    /// UNE LISTE, PAS UNE OFFRE : le SKU n'est unique qu'au sein d'un produit.
    /// </summary>
    Task<IReadOnlyList<OfferSummary>> ListOffersBySkuAsync(
        string sku, CancellationToken cancellationToken = default);
}
