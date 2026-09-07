using HBA.Financial.Payments.Domain.Payments;
using HBA.FoodOrders.Contracts;
using HBA.Orders.Contracts;

namespace HBA.Financial.Payments.Application.Abstractions;

/// <summary>
/// Ce que le paiement a besoin de savoir d'une commande, quel que soit l'univers
/// dont elle vient : à qui elle appartient, si elle attend d'être payée, et
/// combien.
/// </summary>
public sealed record PayableOrder(
    Guid OrderId,
    Guid BuyerId,
    string Status,
    decimal GrandTotal,
    string Currency);

/// <summary>LIRE LA COMMANDE À PAYER, DANS L'UNIVERS QUI LA PORTE.</summary>
public interface IPayableOrderReader
{
    /// <summary>Rend la commande, ou <c>null</c> si l'univers indiqué ne la connaît pas.</summary>
    Task<PayableOrder?> ReadAsync(
        PaymentOrderType orderType, Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>Implémentation par délégation aux deux API de module.</summary>
public sealed class PayableOrderReader : IPayableOrderReader
{
    private readonly IOrderingModuleApi _marketplace;
    private readonly IMealOrderModuleApi _repas;

    public PayableOrderReader(IOrderingModuleApi marketplace, IMealOrderModuleApi repas)
    {
        _marketplace = marketplace;
        _repas = repas;
    }

    public async Task<PayableOrder?> ReadAsync(
        PaymentOrderType orderType, Guid orderId, CancellationToken cancellationToken = default)
    {
        switch (orderType)
        {
            case PaymentOrderType.Marketplace:
            {
                var commande = await _marketplace.GetOrderAsync(orderId, cancellationToken);
                return commande is null
                    ? null
                    : new PayableOrder(
                        commande.Id, commande.BuyerId, commande.Status,
                        commande.GrandTotal, commande.Currency);
            }

            case PaymentOrderType.Food:
            {
                var commande = await _repas.GetOrderAsync(orderId, cancellationToken);

                // `TotalAmount` porte déjà les frais de course : voir
                // `MealOrderSummary`, où `Subtotal` et `ShippingFee` sont rendus
                // séparément ET additionnés.
                return commande is null
                    ? null
                    : new PayableOrder(
                        commande.OrderId, commande.BuyerId, commande.Status,
                        commande.TotalAmount, commande.Currency);
            }

            default:
                // Une valeur d'énumération ajoutée sans être traitée ici doit se
                // voir tout de suite.
                throw new ArgumentOutOfRangeException(
                    nameof(orderType), orderType,
                    "Univers de commande inconnu : aucune lecture n'est définie pour cette valeur.");
        }
    }
}
