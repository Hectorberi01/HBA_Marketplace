namespace HBA.Products.Contracts;

/// <summary>
/// Une fiche produit, telle que les autres modules ont le droit de la voir.
///
/// Les énumérations sont des CHAÎNES : un consommateur ne doit pas se recompiler
/// parce qu'on a inséré un statut, et un entier se décalerait en silence le jour
/// où une valeur est ajoutée au milieu.
/// </summary>
public sealed record ProductSummary(
    Guid Id,
    Guid SellerId,
    Guid CategoryId,
    Guid? BrandId,
    string Name,
    string Slug,
    string Status,
    bool IsVisible,
    string? MainImageUrl,
    IReadOnlyList<string> Tags);

/// <summary>Une déclinaison vendable.</summary>
public sealed record VariantSummary(
    Guid Id,
    Guid ProductId,
    string? Sku,
    bool IsActive,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>
/// Une offre, telle que le panier et la vitrine la lisent.
/// </summary>
/// <param name="EffectivePrice">
/// Ce que l'acheteur paie AUJOURD'HUI : le prix promotionnel s'il court, le prix
/// courant sinon. Fourni calculé pour que le panier n'ait pas à refaire
/// l'arbitrage — c'est ainsi qu'un écran affiche un prix et qu'un autre en
/// facture un différent.
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

    /// <summary>
    /// Fin de la remise. Nulle si pas de remise, ou remise sans échéance.
    ///
    /// Sert l'affichage « −X% jusqu'au … » des cartes produit. Sans elle, la
    /// vitrine annonce une promotion sans dire jusqu'à quand — ce qui est
    /// exactement l'information qui fait décider.
    /// </summary>
    DateTime? PromotionEndsOnUtc,

    string Currency,
    string Status,
    bool IsPurchasable,

    /// <summary>
    /// État du bien : New, Used, Refurbished.
    ///
    /// Fait partie du contrat public parce que l'ACHETEUR le voit : sur une fiche
    /// portant plusieurs offres, c'est souvent ce qui explique l'écart de prix.
    /// L'omettre obligerait la vitrine à lire l'offre ailleurs, ou à ne rien
    /// afficher — et un reconditionné passerait pour un neuf moins cher.
    /// </summary>
    string Condition,

    int HandlingTimeDays,
    Guid ShipFromLocationId);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// API EN PROCESSUS DU MODULE PRODUCTS.
///
/// LECTURE SEULE. Créer un produit ou changer un prix passe par une commande
/// MediatR : un autre module ne doit pas pouvoir modifier une fiche ou un tarif
/// par un simple appel de méthode, sans validation ni événement.
///
/// LES LECTURES EN LOT NE SONT PAS UN CONFORT.
///
/// Le panier affiche une ligne par offre, la vitrine une carte par produit. Un
/// appel par ligne transforme chaque page en N+1, et cela ne se voit qu'en
/// production, quand la liste dépasse dix éléments. Les variantes « plusieurs
/// identifiants » existent donc dès le premier jour.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IProductsModuleApi
{
    Task<ProductSummary?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);

    // ═════════════════════════════════════════════════════════════════════════
    // `GetProductsAsync` (LE LOT) A ÉTÉ RETIRÉ. IL N'AVAIT AUCUN CORPS DE
    // SERVEUR, ET IL ÉTAIT LE DERNIER RPC DU DÉPÔT DANS CE CAS (lot 9.1).
    //
    // `CatalogApi.GetProducts` était déclaré dans le proto et enveloppé ici ;
    // aucun `public override GetProducts` n'existait côté serveur. Tout appel
    // aurait rendu `UNIMPLEMENTED` — la même panne que `DeliveryApi.LookupQuote`
    // et `OrderApi.ListOrdersBySeller`, qui ont coûté le parcours repas et le
    // compteur de ventes de tous les vendeurs.
    //
    // RETIRÉ PLUTÔT QU'IMPLÉMENTÉ, ET LE CHOIX EST ARGUMENTÉ.
    //
    // Personne ne l'appelait. L'implémenter correctement demande un lot batché
    // côté base ET une sémantique de cache par identifiant — `GetProductAsync`
    // passe par `_cache.GetOrCreateAsync` et charge cinq `Include` — c'est-à-dire
    // un vrai morceau de travail, pour un besoin qui n'existe pas encore.
    //
    // CE QUE CE RETRAIT NE DIT PAS : que le lot serait inutile. L'encadré
    // d'`IProductsModuleApi` explique pourquoi les variantes « plusieurs
    // identifiants » existent — éviter un N+1 par ligne de panier. Le jour où un
    // écran en aura besoin, il faudra l'écrire AVEC son corps de serveur. Un
    // contrat déclaré sans serveur n'est pas une avance : c'est un piège qui
    // compile.
    //
    // Le lot des OFFRES, lui, reste — il est implémenté et appelé.
    // ═════════════════════════════════════════════════════════════════════════

    Task<OfferSummary?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, OfferSummary>> GetOffersAsync(
        IReadOnlyCollection<Guid> offerIds, CancellationToken cancellationToken = default);

    /// <summary>Les offres achetables d'un produit — la Buy Box.</summary>
    Task<IReadOnlyList<OfferSummary>> ListPurchasableOffersAsync(
        Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrouve les offres qui vendent une référence d'inventaire donnée.
    ///
    /// RENVOIE UNE LISTE, PAS UNE OFFRE. Le SKU n'est unique qu'au sein d'un
    /// produit : deux produits distincts peuvent porter le même. Rendre une seule
    /// offre obligerait l'appelant à en choisir une au hasard — et Inventory, qui
    /// s'en sert pour signaler une rupture, en marquerait une sur deux.
    /// </summary>
    Task<IReadOnlyList<OfferSummary>> ListOffersBySkuAsync(
        string sku, CancellationToken cancellationToken = default);
}
