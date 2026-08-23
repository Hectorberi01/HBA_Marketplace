namespace HBA.Orders.Contracts;

/// <summary>
/// API in-process publique du module Ordering. Permet aux autres modules
/// (Payments, Shipping…) de lire une commande sans accéder à sa base.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// IL EXISTE DEUX `IOrderingModuleApi` DANS CE DÉPÔT, DANS DEUX NAMESPACES.
///
///   • `HBA.Ordering.Contracts.IOrderingModuleApi`  — celui-ci
///     (`shared/contracts/HBA.Ordering.Contracts/`)
///   • `HBA.Orders.Contracts.IOrderingModuleApi`
///     (`services/marketplace/order-service/src/HBA.Order.Contracts/`)
///
/// Les deux sont implémentés par deux clients gRPC distincts dans le MÊME
/// fichier — `OrderingGrpcClient` et `OrdersGrpcClient` — et enregistrés côte à
/// côte dans `AddOrderingGrpcClient`.
///
/// CE N'EST PAS UN DÉTAIL DE STYLE : ÇA COÛTE DU TEMPS À CHAQUE LECTURE.
///
/// Au lot 9.1, il a fallu remonter les deux pour savoir lequel
/// `SellerSalesCountHandler` utilisait — c'est celui-ci, par son `using
/// HBA.Ordering.Contracts` — avant de pouvoir corriger l'`UNIMPLEMENTED` qui
/// laissait `SalesCount` à zéro pour tous les vendeurs. Les DEUX ont dû être
/// corrigés : le second n'a pas d'appelant, mais il portait le même piège.
///
/// C'est l'un des 77 types du dépôt déclarés dans plusieurs namespaces, et l'un
/// des plus coûteux — voir le lot 9.5 pour le relevé et ce que la réunification
/// coûterait.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public interface IOrderingModuleApi
{
    Task<OrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<OrderReturnContext?> GetOrderReturnContextAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cet acheteur a-t-il DÉJÀ passé une commande (hors commandes échouées) ?
    ///
    /// Sert exclusivement aux promotions « première commande ». Sans cette lecture,
    /// `CartPricer` passait `IsFirstOrder: false` en dur, et TOUTE promo de bienvenue
    /// était silencieusement inapplicable.
    /// </summary>
    Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Nombre d'articles VENDUS par un vendeur : somme des quantités de ses lignes
    /// dans les commandes réellement encaissées (Confirmed / Delivered). Sert à
    /// alimenter le compteur « ventes » de sa vitrine. Recalculé depuis la source,
    /// donc idempotent (aucun risque de double comptage si l'événement est rejoué).
    /// </summary>
    Task<int> GetSellerSalesCountAsync(Guid sellerId, CancellationToken cancellationToken = default);
}
