using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Merchants.Domain.Sellers.Events;

namespace HBA.Merchants.Application.Sellers.EventHandlers;

/// <summary>Publie l'event d'intégration « vendeur onboardé ».</summary>
public sealed class SellerRegisteredDomainEventHandler : IDomainEventHandler<SellerRegisteredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerRegisteredDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerRegisteredDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerRegisteredIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId,
                ShopName = domainEvent.ShopName
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « vendeur activé » (Catalog, Notifications…).</summary>
public sealed class SellerActivatedDomainEventHandler : IDomainEventHandler<SellerActivatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerActivatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerActivatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerActivatedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « vendeur fermé » (Catalog retire les produits de la vente).</summary>
public sealed class SellerClosedDomainEventHandler : IDomainEventHandler<SellerClosedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerClosedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerClosedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerClosedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « dossier KYB refusé » (Notifications prévient le vendeur).</summary>
public sealed class SellerKybRejectedDomainEventHandler : IDomainEventHandler<SellerKybRejectedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerKybRejectedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerKybRejectedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerKybRejectedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « vendeur suspendu » (Products retire le catalogue).</summary>
public sealed class SellerSuspendedDomainEventHandler : IDomainEventHandler<SellerSuspendedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerSuspendedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerSuspendedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerSuspendedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « suspension levée ».</summary>
public sealed class SellerSuspensionLiftedDomainEventHandler : IDomainEventHandler<SellerSuspensionLiftedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerSuspensionLiftedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerSuspensionLiftedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerSuspensionLiftedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « vendeur réactivé ».</summary>
public sealed class SellerReactivatedDomainEventHandler : IDomainEventHandler<SellerReactivatedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerReactivatedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerReactivatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerReactivatedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>Publie l'event d'intégration « vendeur supprimé » (Catalog purge les produits).</summary>
public sealed class SellerDeletedDomainEventHandler : IDomainEventHandler<SellerDeletedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerDeletedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerDeletedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerDeletedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}

/// <summary>
/// Publie « pièce KYB retirée », pour que le fichier soit réellement effacé.
///
/// SANS CE PUBLICATEUR, LA SUPPRESSION D'UNE PIÈCE D'IDENTITÉ EST UN MENSONGE.
///
/// L'agrégat lève l'événement correctement ; s'il ne sort pas du module, la ligne
/// disparaît de la base et le fichier reste dans le bucket privé — plus référencé
/// par rien. C'est exactement le défaut que ce dépôt a déjà corrigé ailleurs :
/// un événement levé que personne n'écoute.
/// </summary>
public sealed class KybDocumentRemovedDomainEventHandler : IDomainEventHandler<KybDocumentRemovedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public KybDocumentRemovedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(KybDocumentRemovedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new KybDocumentRemovedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId,
                MediaId = domainEvent.MediaId
            },
            cancellationToken);
}

/// <summary>
/// Publie « dossier KYB soumis » (§10.3 : `merchant.kyc.submitted`).
///
/// CE MAILLON N'EXISTAIT PAS, ET SON ABSENCE ÉTAIT ASYMÉTRIQUE.
///
/// Le service publiait le REFUS du dossier et rien d'autre du parcours KYB.
/// Notifications ne pouvait donc annoncer au vendeur que la mauvaise nouvelle, et
/// l'exploitation n'avait aucun signal pour alimenter sa file de validation : elle
/// la découvrait en la rafraîchissant.
/// </summary>
public sealed class SellerKybSubmittedDomainEventHandler : IDomainEventHandler<SellerKybSubmittedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerKybSubmittedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerKybSubmittedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerKybSubmittedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId,
                DocumentCount = domainEvent.DocumentCount
            },
            cancellationToken);
}

/// <summary>
/// Publie « dossier KYB validé » (§10.3 : `merchant.kyc.approved`).
///
/// `SellerKybVerifiedDomainEvent` ÉTAIT LEVÉ DEPUIS L'ORIGINE, SANS AUCUN
///    GESTIONNAIRE.
///
/// Il partait donc dans le vide à chaque validation. Rien ne le signalait : un
/// événement de domaine sans destinataire ne lève pas, ne journalise pas, et
/// disparaît à la fin de l'unité de travail. On ne s'en aperçoit qu'en cherchant
/// pourquoi le vendeur n'a jamais reçu la bonne nouvelle.
/// </summary>
public sealed class SellerKybVerifiedDomainEventHandler : IDomainEventHandler<SellerKybVerifiedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public SellerKybVerifiedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(SellerKybVerifiedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new SellerKybApprovedIntegrationEvent
            {
                SellerId = domainEvent.SellerId,
                UserId = domainEvent.UserId
            },
            cancellationToken);
}
