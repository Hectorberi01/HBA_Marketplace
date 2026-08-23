using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Observability;
using HBA.Shared.IntegrationEvents;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Financial.Payments.Domain.Payments.Events;

namespace HBA.Financial.Payments.Application.Payments.EventHandlers;

/// <summary>Publie « paiement encaissé » (Ordering confirme) + métrique de succès.</summary>
public sealed class PaymentCapturedDomainEventHandler : IDomainEventHandler<PaymentCapturedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IPaymentMetrics _metrics;

    public PaymentCapturedDomainEventHandler(IIntegrationEventPublisher publisher, IPaymentMetrics metrics)
    {
        _publisher = publisher;
        _metrics = metrics;
    }

    public async Task HandleAsync(PaymentCapturedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _metrics.Success(
            domainEvent.Provider,
            domainEvent.Method,
            domainEvent.Currency,
            MoneyUnits.ToMinorUnits(domainEvent.Amount, domainEvent.Currency));

        await _publisher.PublishAsync(
            new PaymentCapturedIntegrationEvent
            {
                PaymentId = domainEvent.PaymentId,
                OrderId = domainEvent.OrderId,
                OrderType = domainEvent.OrderType
            },
            cancellationToken);
    }
}

/// <summary>Publie « paiement échoué » (Ordering annule) + métrique d'échec.</summary>
public sealed class PaymentFailedDomainEventHandler : IDomainEventHandler<PaymentFailedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IPaymentMetrics _metrics;

    public PaymentFailedDomainEventHandler(IIntegrationEventPublisher publisher, IPaymentMetrics metrics)
    {
        _publisher = publisher;
        _metrics = metrics;
    }

    public async Task HandleAsync(PaymentFailedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _metrics.Failed(domainEvent.Provider, domainEvent.Method, domainEvent.Currency, domainEvent.Reason);

        await _publisher.PublishAsync(
            new PaymentFailedIntegrationEvent
            {
                PaymentId = domainEvent.PaymentId,
                OrderId = domainEvent.OrderId,
                OrderType = domainEvent.OrderType,
                Reason = domainEvent.Reason
            },
            cancellationToken);
    }
}

/// <summary>Publie « paiement remboursé » + métrique de remboursement.</summary>
public sealed class PaymentRefundedDomainEventHandler : IDomainEventHandler<PaymentRefundedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IPaymentMetrics _metrics;

    public PaymentRefundedDomainEventHandler(IIntegrationEventPublisher publisher, IPaymentMetrics metrics)
    {
        _publisher = publisher;
        _metrics = metrics;
    }

    public async Task HandleAsync(PaymentRefundedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _metrics.Refund(domainEvent.Provider, domainEvent.Currency, MoneyUnits.ToMinorUnits(domainEvent.Amount, domainEvent.Currency));

        await _publisher.PublishAsync(
            new PaymentRefundedIntegrationEvent
            {
                PaymentId = domainEvent.PaymentId,
                OrderId = domainEvent.OrderId,
                RefundId = domainEvent.RefundId,
                ReturnId = domainEvent.ReturnId,
                ExternalRefundId = domainEvent.ExternalRefundId,
                OrderType = domainEvent.OrderType,
                BuyerId = domainEvent.BuyerId,
                Amount = domainEvent.Amount,
                Currency = domainEvent.Currency,
                IdempotencyKey = domainEvent.IdempotencyKey,
                ProviderRefundId = domainEvent.ProviderRefundId
            },
            cancellationToken);
    }
}

public sealed class PaymentRefundFailedDomainEventHandler : IDomainEventHandler<PaymentRefundFailedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public PaymentRefundFailedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public async Task HandleAsync(PaymentRefundFailedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => await _publisher.PublishAsync(
            new PaymentRefundFailedIntegrationEvent
            {
                PaymentId = domainEvent.PaymentId,
                OrderId = domainEvent.OrderId,
                OrderType = domainEvent.OrderType,
                RefundId = domainEvent.RefundId,
                ReturnId = domainEvent.ReturnId,
                ExternalRefundId = domainEvent.ExternalRefundId,
                Amount = domainEvent.Amount,
                Currency = domainEvent.Currency,
                IdempotencyKey = domainEvent.IdempotencyKey,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}


/// <summary>
/// Publie `payment.created` du §10.12. L'événement manquait : rien ne signalait
/// qu'une intention de paiement avait été ouverte, seulement son issue.
///
/// Il n'a pas de consommateur aujourd'hui, et c'est assumé — il porte la piste
/// d'audit du parcours de paiement, celle qui permet de compter les intentions
/// abandonnées. Publier un fait n'oblige personne à l'écouter ; ne pas le publier
/// prive tout le monde du choix.
/// </summary>
public sealed class PaymentInitiatedDomainEventHandler : IDomainEventHandler<PaymentInitiatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public PaymentInitiatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public async Task HandleAsync(
        PaymentInitiatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => await _publisher.PublishAsync(
            new PaymentCreatedIntegrationEvent
            {
                PaymentId = domainEvent.PaymentId,
                OrderId = domainEvent.OrderId,
                OrderType = domainEvent.OrderType,
                BuyerId = domainEvent.BuyerId,
                Amount = domainEvent.Amount,
                Currency = domainEvent.Currency,
                Provider = domainEvent.Provider
            },
            cancellationToken);
}
