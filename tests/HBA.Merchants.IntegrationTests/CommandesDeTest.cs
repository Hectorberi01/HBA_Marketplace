using System.Collections.Concurrent;
using HBA.Ordering.Contracts;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// order-service EN MÉMOIRE — PILOTABLE, COMME LES DEUX AUTRES FAUX.
///
/// `SellerSalesCountHandler` ne lit pas le compte dans l'événement : il le
/// REDEMANDE à order-service, parce que poser une valeur recalculée est idempotent
/// là où incrémenter double-compterait au premier rejeu. Ce faux est donc ce qui
/// permet à un test de dire « order-service répond 12 » et de vérifier que le
/// vendeur porte 12 — puis « il répond 7 » et de vérifier qu'il porte 7, ce qu'un
/// compteur incrémental ne saurait pas faire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class CommandesDeTest : IOrderingModuleApi
{
    private readonly ConcurrentDictionary<Guid, int> _ventes = new();

    /// <summary>Fixe ce que order-service répondra pour ce vendeur.</summary>
    public void FixerVentes(Guid sellerId, int quantite) => _ventes[sellerId] = quantite;

    public Task<int> GetSellerSalesCountAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => Task.FromResult(_ventes.TryGetValue(sellerId, out var q) ? q : 0);

    // LE RESTE LÈVE. seller-service ne lit pas de commande et ne connaît pas
    // l'historique d'un acheteur. Une valeur neutre rendue en silence ferait passer
    // un futur chemin de code sans qu'on l'ait éprouvé.

    public Task<OrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("seller-service ne lit pas une commande.");

    public Task<OrderReturnContext?> GetOrderReturnContextAsync(Guid orderId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("seller-service ne lit pas un contexte de retour.");

    public Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("seller-service ne connaît pas l'historique d'un acheteur.");
}
