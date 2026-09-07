using HBA.Shared.IntegrationEvents;

// LA FILE N'EST PAS L'OUTBOX : ELLE EST CE QUE L'OUTBOX DRAINE.

namespace HBA.Shared.Infrastructure.Events;

/// <summary>File scopée d'events d'intégration.</summary>
public sealed class IntegrationEventQueue : IIntegrationEventPublisher
{
    private readonly List<IntegrationEvent> _events = new();

    public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        _events.Add(integrationEvent);
        return Task.CompletedTask;
    }

    /// <summary>Retire et renvoie tous les events en attente.</summary>
    public IReadOnlyList<IntegrationEvent> DequeueAll()
    {
        var drained = _events.ToList();
        _events.Clear();
        return drained;
    }
}
