namespace HBA.Shared.IntegrationEvents;

/// <summary>
/// Event d'intégration : un fait publié sur le bus, consommé par d'autres modules
/// de façon asynchrone et découplée.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Handler d'un event d'intégration côté consommateur.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}

/// <summary>Publication d'un event d'intégration.</summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
