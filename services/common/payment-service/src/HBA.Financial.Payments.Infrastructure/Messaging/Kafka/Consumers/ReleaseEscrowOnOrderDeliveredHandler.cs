using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Domain.Payments;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Financial.Payments.Application.Payments;
using HBA.Financial.Payments.Application.Payments.EventHandlers;

namespace HBA.Financial.Payments.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// À la livraison confirmée, libère l'escrow du paiement de la commande : les fonds
/// encaissés deviennent reversables au vendeur.
/// </summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Financial.Payments.Application.Payments.EventHandlers.ReleaseEscrowOnOrderDeliveredHandler")]
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
        // d'order-service, donc de la marketplace.
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

/// <summary>Même geste, pour une commande de repas remise.</summary>
[NomDeConsommateur("HBA.Financial.Payments.Application.Payments.EventHandlers.ReleaseEscrowOnMealOrderDeliveredHandler")]
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
