using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Communication.Contracts.IntegrationEvents;
using HBA.Communication.Domain.Conversations.Events;

namespace HBA.Communication.Application.Conversations.EventHandlers;

/// <summary>Publie « message envoyé » (Notifications alerte le destinataire).</summary>
public sealed class MessageSentDomainEventHandler : IDomainEventHandler<MessageSentDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public MessageSentDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(MessageSentDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new MessageSentIntegrationEvent
            {
                ConversationId = domainEvent.ConversationId,
                MessageId = domainEvent.MessageId,
                SenderId = domainEvent.SenderId
            },
            cancellationToken);
}
