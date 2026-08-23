using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Contracts.IntegrationEvents;
using HBA.Catalog.Domain.Brands.Events;

namespace HBA.Catalog.Application.Brands.EventHandlers;

/// <summary>
/// Frontière domaine → intégration pour les deux événements de marque du §19.
///
/// SANS EUX, UNE DEMANDE DORT DANS UNE TABLE QUE PERSONNE N'OUVRE.
///
/// Le premier consommateur attendu est la notification d'administration. Un
/// vendeur qui demande une marque et n'obtient jamais de réponse ne redemande pas :
/// il choisit une marque approchante au catalogue, et c'est le référentiel que ce
/// mécanisme protégeait qui se dégrade.
/// </summary>
public sealed class BrandRequestedDomainEventHandler : IDomainEventHandler<BrandRequestedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public BrandRequestedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(BrandRequestedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(new BrandRequestedIntegrationEvent
        {
            RequestId = e.RequestId,
            SellerId = e.SellerId,
            Name = e.Name
        }, cancellationToken);
}

public sealed class BrandRequestApprovedDomainEventHandler : IDomainEventHandler<BrandRequestApprovedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public BrandRequestApprovedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(BrandRequestApprovedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(new BrandRequestApprovedIntegrationEvent
        {
            RequestId = e.RequestId,
            SellerId = e.SellerId,
            BrandId = e.BrandId,
            RequestedName = e.RequestedName
        }, cancellationToken);
}
