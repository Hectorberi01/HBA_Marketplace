namespace HBA.Shared.Domain.Events;

/// <summary>Event de domaine : un fait métier qui s'est produit DANS un module.</summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredOnUtc { get; }
}

/// <summary>Base pratique pour les events de domaine.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
