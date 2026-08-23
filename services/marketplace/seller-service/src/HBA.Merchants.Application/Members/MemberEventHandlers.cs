using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Merchants.Domain.Members.Events;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Merchants.Application.Members;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES ÉVÉNEMENTS D'APPARTENANCE, DU DOMAINE VERS LE BUS.
///
/// SEPT HANDLERS ICI, ET UN HUITIÈME QUI N'Y EST PAS — L'INVITATION.
///
/// Elle est publiée depuis `MemberCommandHandler`, pas depuis un événement de
/// domaine, et pour une raison de fond : son événement doit porter le JETON EN
/// CLAIR, or l'agrégat ne le connaît pas. Il ne reçoit que son empreinte — c'est
/// tout l'intérêt du §7. Faire remonter le secret dans un événement de domaine
/// obligerait à le loger dans l'agrégat sans le persister, c'est-à-dire à créer un
/// champ dont l'unique raison d'être serait de contourner sa propre conception.
///
/// CES HANDLERS NE FONT QUE TRADUIRE. AUCUNE DÉCISION ICI.
///
/// Un contrôle posé à cet étage s'exécuterait APRÈS l'enregistrement de la
/// mutation : il ne pourrait plus rien empêcher, seulement produire un événement
/// incohérent avec la base. Les décisions sont dans l'agrégat, les vérifications
/// de contexte dans le handler de commande.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

/// <summary>
/// Publie « accès suspendu ».
/// <para>
/// CET ÉVÉNEMENT PROMET UNE COUPURE IMMÉDIATE QUE L'INFRASTRUCTURE NE TIENT PAS
/// ENCORE. Le cache d'autorisation est en mémoire, par instance : dans un groupe de
/// consommateurs, une seule réplique le reçoit et se purge. Les autres continuent
/// de servir les droits périmés jusqu'au TTL. Le lot 0a — brancher Redis — est ce
/// qui rend la promesse vraie.
/// </para>
/// </summary>
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

/// <summary>
/// Publie « membre sorti ».
/// <para>
/// SON CONSOMMATEUR NE DOIT PAS RETIRER LE RÔLE `Seller` SANS VÉRIFIER : le
/// compte peut être propriétaire d'un autre dossier. La révocation n'est pas le
/// symétrique de l'octroi, et c'est dit sur le contrat.
/// </para>
/// </summary>
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

/// <summary>
/// Publie le transfert de propriété : les deux comptes doivent l'apprendre.
/// </summary>
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
