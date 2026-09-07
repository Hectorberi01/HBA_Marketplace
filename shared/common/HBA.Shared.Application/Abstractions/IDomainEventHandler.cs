using HBA.Shared.Domain.Events;

namespace HBA.Shared.Application.Abstractions;

/// <summary>Handler d'un domain event, à l'INTÉRIEUR du module.</summary>
public interface IDomainEventHandler<in TDomainEvent> where TDomainEvent : IDomainEvent
{
    Task HandleAsync(TDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>Dispatche les domain events collectés sur les agrégats après persistance.</summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
