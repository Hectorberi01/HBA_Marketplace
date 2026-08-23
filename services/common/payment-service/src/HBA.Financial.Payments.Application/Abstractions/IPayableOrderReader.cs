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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LIRE LA COMMANDE À PAYER, DANS L'UNIVERS QUI LA PORTE.
///
/// ÉCRIT PARCE QU'UNE COMMANDE DE REPAS NE POUVAIT PAS ÊTRE PAYÉE (ISSUE-059).
///
/// `InitiatePaymentCommandHandler` lisait la commande par `IOrderingModuleApi`,
/// qui ne connaît que la marketplace, et figeait `PaymentOrderType.Marketplace`
/// à la création du paiement. Une commande de repas donnait donc
/// « commande introuvable » — et rien, dans toute la chaîne food, n'amenait
/// jamais un `MealOrder` au-delà de `AwaitingPayment`.
///
/// Le plus trompeur n'était pas là : `ConfirmMealOrderOnPaymentCapturedHandler`
/// existait, était enregistré, et filtrait sur `OrderType == "FOOD"`. Comme
/// AUCUN paiement ne portait jamais cette valeur, le filtre ne passait jamais.
/// Le câblage était complet et l'effet nul — exactement le mode de défaillance
/// que ce dépôt a déjà rencontré trois fois.
///
/// UN SEUL CHEMIN DE PAIEMENT, PAS DEUX.
///
/// L'autre option était un handler d'initiation par univers. Elle aurait
/// dupliqué la garde de propriété, la garde « paiement déjà en cours », la
/// réconciliation auprès du PSP et l'ouverture de session — quatre morceaux
/// délicats, en deux exemplaires qui auraient divergé. Ce qui diffère entre les
/// deux univers tient en une lecture ; c'est donc la lecture qu'on abstrait, et
/// elle seule.
///
/// CE QUE CETTE ABSTRACTION NE COUVRE PAS.
///
/// Elle ne rend que le strict nécessaire au paiement. Les lignes, le restaurant,
/// l'adresse, le devis de course n'y sont pas : payment-service n'a pas à
/// connaître la forme d'une commande, et chaque champ ajouté ici serait un champ
/// que les DEUX univers devraient désormais savoir produire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IPayableOrderReader
{
    /// <summary>
    /// Rend la commande, ou <c>null</c> si l'univers indiqué ne la connaît pas.
    ///
    /// `null` ne veut PAS dire « elle est peut-être dans l'autre univers ». Le
    /// type vient de l'appelant, qui sait ce qu'il paie ; on ne cherche jamais
    /// dans les deux à la suite. Un repli silencieux d'un univers sur l'autre
    /// rendrait un paiement Food indistinguable d'un paiement Marketplace au
    /// premier identifiant qui se ressemble.
    /// </summary>
    Task<PayableOrder?> ReadAsync(
        PaymentOrderType orderType, Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implémentation par délégation aux deux API de module. Aucune logique propre :
/// tout ce qu'elle fait est choisir l'interlocuteur et rapprocher deux formes de
/// résumé qui portent les mêmes faits sous des noms différents
/// (<c>GrandTotal</c> ici, <c>TotalAmount</c> là).
/// </summary>
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
                // séparément ET additionnés. On paie le total, pas le sous-total.
                return commande is null
                    ? null
                    : new PayableOrder(
                        commande.OrderId, commande.BuyerId, commande.Status,
                        commande.TotalAmount, commande.Currency);
            }

            default:
                // Une valeur d'énumération ajoutée sans être traitée ici doit se voir
                // tout de suite. Rendre `null` la ferait passer pour une commande
                // introuvable, et le troisième univers naîtrait impayable en silence.
                throw new ArgumentOutOfRangeException(
                    nameof(orderType), orderType,
                    "Univers de commande inconnu : aucune lecture n'est définie pour cette valeur.");
        }
    }
}
