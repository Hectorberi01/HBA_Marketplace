using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Merchants.Domain.Members.Events;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Merchants.Application.Members;

/// <summary>LES ÉVÉNEMENTS D'APPARTENANCE, DU DOMAINE VERS LE BUS.</summary>
public sealed class SellerMemberJoinedDomainEventHandler
    : IDomainEventHandler<SellerMemberJoinedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerMemberJoinedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        SellerMemberJoinedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerMemberJoinedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                MemberId = domainEvent.MemberId,
                UserId = domainEvent.UserId,
                SellerRoleIds = domainEvent.SellerRoleIds,
                StoreIds = domainEvent.StoreIds
            },
            cancellationToken);
}

/// <summary>Publie « rôles modifiés » — l'événement qui invalide le cache d'autorisation.</summary>
public sealed class SellerMemberRolesChangedDomainEventHandler
    : IDomainEventHandler<SellerMemberRolesChangedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerMemberRolesChangedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        SellerMemberRolesChangedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerMemberRolesUpdatedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                MemberId = domainEvent.MemberId,
                UserId = domainEvent.UserId,
                SellerRoleIds = domainEvent.SellerRoleIds
            },
            cancellationToken);
}

public sealed class SellerMemberStoreAssignedDomainEventHandler
    : IDomainEventHandler<SellerMemberStoreAssignedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerMemberStoreAssignedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        SellerMemberStoreAssignedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerMemberStoreAssignedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                MemberId = domainEvent.MemberId,
                UserId = domainEvent.UserId,
                StoreId = domainEvent.StoreId
            },
            cancellationToken);
}

public sealed class SellerMemberStoreUnassignedDomainEventHandler
    : IDomainEventHandler<SellerMemberStoreUnassignedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerMemberStoreUnassignedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        SellerMemberStoreUnassignedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerMemberStoreUnassignedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                MemberId = domainEvent.MemberId,
                UserId = domainEvent.UserId,
                StoreId = domainEvent.StoreId
            },
            cancellationToken);
}

/// <summary>Publie « accès suspendu ».</summary>
public sealed class SellerMemberSuspendedDomainEventHandler
    : IDomainEventHandler<SellerMemberSuspendedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerMemberSuspendedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        SellerMemberSuspendedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerMemberSuspendedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                MemberId = domainEvent.MemberId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

public sealed class SellerMemberActivatedDomainEventHandler
    : IDomainEventHandler<SellerMemberActivatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerMemberActivatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        SellerMemberActivatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerMemberActivatedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                MemberId = domainEvent.MemberId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>Publie « membre sorti ».</summary>
public sealed class SellerMemberRevokedDomainEventHandler
    : IDomainEventHandler<SellerMemberRevokedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerMemberRevokedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        SellerMemberRevokedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerMemberRevokedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                MemberId = domainEvent.MemberId,
                UserId = domainEvent.UserId,

                // RECOPIÉ, JAMAIS RECALCULÉ ICI. Ce gestionnaire s'exécute AVANT
                // `base.SaveChangesAsync` : une requête y lirait l'état d'avant la
                // révocation, où le membre figure encore actif — le drapeau
                // vaudrait toujours « oui » et le rôle ne serait jamais retiré.
                HasOtherSellerMembership = domainEvent.HasOtherSellerMembership
            },
            cancellationToken);
}

/// <summary>Publie le transfert de propriété : les deux comptes doivent l'apprendre.</summary>
public sealed class SellerOwnershipTransferredDomainEventHandler
    : IDomainEventHandler<SellerOwnershipTransferredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerOwnershipTransferredDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        SellerOwnershipTransferredDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerOwnershipTransferredIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                PreviousOwnerMemberId = domainEvent.PreviousOwnerMemberId,
                PreviousOwnerUserId = domainEvent.PreviousOwnerUserId,
                NewOwnerMemberId = domainEvent.NewOwnerMemberId,
                NewOwnerUserId = domainEvent.NewOwnerUserId
            },
            cancellationToken);
}
