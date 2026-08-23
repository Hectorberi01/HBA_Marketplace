using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Domain.Payments;

namespace HBA.Financial.Payments.Application.Payments.EventHandlers;

/// <summary>
/// À la livraison confirmée, libère l'escrow du paiement de la commande : les
/// fonds encaissés deviennent reversables au vendeur. Idempotent (sans effet si
/// déjà libéré, ou si le paiement n'est pas encaissé).
/// </summary>
public sealed class ReleaseEscrowOnOrderDeliveredHandler : IIntegrationEventHandler<OrderDeliveredIntegrationEvent>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public ReleaseEscrowOnOrderDeliveredHandler(IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(OrderDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // L'univers est imposé par le TYPE de l'événement : `OrderDelivered` vient
        // d'order-service, donc de la marketplace. Le déduire de l'événement plutôt
        // que de le chercher évite qu'une livraison de repas libère un jour l'escrow
        // d'une commande marketplace portant le même identifiant.
        var payment = await _repository.GetByOrderAsync(
            PaymentOrderType.Marketplace, integrationEvent.OrderId, cancellationToken);
        if (payment is null || payment.Status != PaymentStatus.Captured)
        {
            return;
        }

        var result = payment.ReleaseEscrow();
        if (result.IsSuccess)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Même geste, pour une commande de repas remise.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ÉCRIT PARCE QUE L'ARGENT D'UN REPAS NE SORTAIT JAMAIS DE L'ESCROW.
///
/// `MealOrderDeliveredIntegrationEvent` était publié et n'avait AUCUN
/// consommateur. Tant que le food n'avait pas de chemin de paiement, cela ne se
/// voyait pas : il n'y avait aucun escrow à libérer. Le lot 6.1 ouvre ce chemin,
/// et sans ce gestionnaire il l'ouvrirait sur une impasse — le client débité, le
/// restaurateur jamais reversable, et rien dans les journaux pour le dire.
///
/// IL NE FALLAIT PAS ÉTENDRE LE GESTIONNAIRE MARKETPLACE.
///
/// Les deux événements sont des types distincts, publiés par des services
/// distincts. Un seul gestionnaire abonné aux deux devrait déduire l'univers de
/// l'instance reçue — c'est-à-dire refaire, en moins lisible, ce que le système
/// de types fait déjà ici gratuitement.
///
/// CE QUE CELA NE COUVRE PAS.
///
/// La libération suppose un paiement `Captured`. Une commande de repas remise
/// alors que son paiement a échoué ou n'a jamais eu lieu passe donc en silence —
/// c'est le comportement du jumeau marketplace, conservé tel quel. Ce cas ne
/// devrait pas exister (une commande n'est confirmée qu'après encaissement) ;
/// s'il apparaît, il se verra au reversement manquant, pas ici.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ReleaseEscrowOnMealOrderDeliveredHandler
    : IIntegrationEventHandler<MealOrderDeliveredIntegrationEvent>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public ReleaseEscrowOnMealOrderDeliveredHandler(
        IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        MealOrderDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var payment = await _repository.GetByOrderAsync(
            PaymentOrderType.Food, integrationEvent.OrderId, cancellationToken);
        if (payment is null || payment.Status != PaymentStatus.Captured)
        {
            return;
        }

        var result = payment.ReleaseEscrow();
        if (result.IsSuccess)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
