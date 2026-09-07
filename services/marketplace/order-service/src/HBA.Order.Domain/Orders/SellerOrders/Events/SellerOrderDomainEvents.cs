using HBA.Shared.Domain.Events;

namespace HBA.Orders.Domain.Orders.SellerOrders.Events;

/// <summary>
/// Une ligne que le vendeur n'honorera pas : de quoi la reprendre en stock et la
/// rembourser.
/// </summary>
public sealed record SellerOrderRefusedLine(
    Guid OrderLineId,
    Guid ProductId,
    string Sku,
    Guid ShipFromLocationId,
    int Quantity,
    decimal LineTotal);

/// <summary>UN VENDEUR N'HONORERA PAS SA PART D'UNE COMMANDE DÉJÀ PAYÉE.</summary>
/// <param name="Amount">
/// Le montant PAYÉ pour cette part — somme des <c> LineTotal</c> du vendeur,
/// remises comprises.
/// </param>
public sealed record SellerOrderRefusedDomainEvent(
    Guid SellerOrderId,
    Guid OrderId,
    Guid BuyerId,
    Guid SellerId,
    string Currency,
    string Outcome,
    string Reason,
    decimal Amount,
    IReadOnlyCollection<SellerOrderRefusedLine> Lines) : DomainEvent;
