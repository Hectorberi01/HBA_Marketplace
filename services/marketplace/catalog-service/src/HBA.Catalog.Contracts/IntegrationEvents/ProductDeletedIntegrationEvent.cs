using HBA.Shared.IntegrationEvents;

namespace HBA.Catalog.Contracts.IntegrationEvents;

/// <summary>
/// Publié quand un produit est supprimé. Consommé par Search pour retirer le
/// produit de l'index (évite les entrées orphelines en vitrine).
/// </summary>
public sealed record ProductDeletedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
}
