using HBA.Merchants.Contracts;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Communication.Notifications.Application.Notifications;
using HBA.Communication.Notifications.Application.Notifications.EventHandlers;

namespace HBA.Communication.Notifications.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// CE QUI ARRIVE À UN MEMBRE, IL DOIT L'APPRENDRE — ET PAS EN SE COGNANT À UN 403.
/// </summary>
internal static class MemberNotifications
{
    /// <summary>
    /// Type de rattachement porté par la notification, pour le filtrage côté
    /// application.
    /// </summary>
    public const string RelatedType = "SellerMembership";

    /// <summary>Le nom de la boutique-mère, ou un repli neutre.</summary>
    public static async Task<string> EnseigneAsync(
        ISellerModuleApi sellers, Guid sellerId, CancellationToken ct)
    {
        var vendeur = await sellers.GetSellerAsync(sellerId, ct);
        return string.IsNullOrWhiteSpace(vendeur?.ShopName) ? "votre employeur" : vendeur!.ShopName;
    }

    public static async Task<string> BoutiqueAsync(
        ISellerModuleApi sellers, Guid storeId, CancellationToken ct)
    {
        var boutique = await sellers.GetStoreAsync(storeId, ct);
        return string.IsNullOrWhiteSpace(boutique?.Name) ? "une boutique" : boutique!.Name;
    }
}

/// <summary>L'invité vient d'accepter : il découvre son accès.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SellerMemberJoinedNotificationHandler")]
public sealed class SellerMemberJoinedNotificationHandler
    : IIntegrationEventHandler<SellerMemberJoinedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<SellerMemberJoinedNotificationHandler> _logger;

    public SellerMemberJoinedNotificationHandler(
        NotificationDispatcher dispatcher,
        ISellerModuleApi sellers,
        ILogger<SellerMemberJoinedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(
        SellerMemberJoinedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var enseigne = await MemberNotifications.EnseigneAsync(
            _sellers, integrationEvent.SellerId, cancellationToken);

        await _dispatcher.NotifyAsync(
            integrationEvent.UserId,
            $"Bienvenue dans l'équipe de {enseigne}",
            "Votre accès est actif. L'espace vendeur vous montre ce que vos rôles vous permettent de faire.",
            MemberNotifications.RelatedType,
            integrationEvent.MemberId,
            cancellationToken);

        _logger.LogInformation(
            "Membre {MemberId} du vendeur {SellerId} notifié de son arrivée.",
            integrationEvent.MemberId, integrationEvent.SellerId);
    }
}

/// <summary>Les rôles ont changé.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SellerMemberRolesUpdatedNotificationHandler")]
public sealed class SellerMemberRolesUpdatedNotificationHandler
    : IIntegrationEventHandler<SellerMemberRolesUpdatedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;

    public SellerMemberRolesUpdatedNotificationHandler(
        NotificationDispatcher dispatcher, ISellerModuleApi sellers)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
    }

    public async Task HandleAsync(
        SellerMemberRolesUpdatedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var enseigne = await MemberNotifications.EnseigneAsync(
            _sellers, integrationEvent.SellerId, cancellationToken);

        await _dispatcher.NotifyAsync(
            integrationEvent.UserId,
            $"Vos accès chez {enseigne} ont changé",
            "Vos rôles viennent d'être modifiés. Ouvrez « Mes accès » pour voir ce que vous pouvez faire "
            + "désormais — certaines actions ont pu vous être retirées.",
            MemberNotifications.RelatedType,
            integrationEvent.MemberId,
            cancellationToken);
    }
}

/// <summary>Le membre est affecté à une boutique.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SellerMemberStoreAssignedNotificationHandler")]
public sealed class SellerMemberStoreAssignedNotificationHandler
    : IIntegrationEventHandler<SellerMemberStoreAssignedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;

    public SellerMemberStoreAssignedNotificationHandler(
        NotificationDispatcher dispatcher, ISellerModuleApi sellers)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
    }

    public async Task HandleAsync(
        SellerMemberStoreAssignedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var boutique = await MemberNotifications.BoutiqueAsync(
            _sellers, integrationEvent.StoreId, cancellationToken);

        await _dispatcher.NotifyAsync(
            integrationEvent.UserId,
            $"Vous travaillez maintenant sur {boutique}",
            "Vos droits s'appliquent à cette boutique. Elle apparaît dans votre espace vendeur.",
            MemberNotifications.RelatedType,
            integrationEvent.MemberId,
            cancellationToken);
    }
}

/// <summary>Le membre est retiré d'une boutique.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SellerMemberStoreUnassignedNotificationHandler")]
public sealed class SellerMemberStoreUnassignedNotificationHandler
    : IIntegrationEventHandler<SellerMemberStoreUnassignedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;

    public SellerMemberStoreUnassignedNotificationHandler(
        NotificationDispatcher dispatcher, ISellerModuleApi sellers)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
    }

    public async Task HandleAsync(
        SellerMemberStoreUnassignedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var boutique = await MemberNotifications.BoutiqueAsync(
            _sellers, integrationEvent.StoreId, cancellationToken);

        await _dispatcher.NotifyAsync(
            integrationEvent.UserId,
            $"Vous n'êtes plus rattaché à {boutique}",
            "Vos droits sur cette boutique ont été retirés. Vos autres rattachements, s'il y en a, "
            + "ne changent pas.",
            MemberNotifications.RelatedType,
            integrationEvent.MemberId,
            cancellationToken);
    }
}

/// <summary>L'accès est suspendu.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SellerMemberSuspendedNotificationHandler")]
public sealed class SellerMemberSuspendedNotificationHandler
    : IIntegrationEventHandler<SellerMemberSuspendedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;

    public SellerMemberSuspendedNotificationHandler(
        NotificationDispatcher dispatcher, ISellerModuleApi sellers)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
    }

    public async Task HandleAsync(
        SellerMemberSuspendedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var enseigne = await MemberNotifications.EnseigneAsync(
            _sellers, integrationEvent.SellerId, cancellationToken);

        await _dispatcher.NotifyAsync(
            integrationEvent.UserId,
            $"Votre accès chez {enseigne} est suspendu",
            "Vous ne pouvez plus agir sur ce dossier pour le moment. Votre compte n'est pas affecté ; "
            + "adressez-vous à votre employeur pour en connaître la raison.",
            MemberNotifications.RelatedType,
            integrationEvent.MemberId,
            cancellationToken);
    }
}

/// <summary>L'accès est rouvert.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SellerMemberActivatedNotificationHandler")]
public sealed class SellerMemberActivatedNotificationHandler
    : IIntegrationEventHandler<SellerMemberActivatedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;

    public SellerMemberActivatedNotificationHandler(
        NotificationDispatcher dispatcher, ISellerModuleApi sellers)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
    }

    public async Task HandleAsync(
        SellerMemberActivatedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var enseigne = await MemberNotifications.EnseigneAsync(
            _sellers, integrationEvent.SellerId, cancellationToken);

        await _dispatcher.NotifyAsync(
            integrationEvent.UserId,
            $"Votre accès chez {enseigne} est rétabli",
            "Vous pouvez de nouveau travailler sur ce dossier.",
            MemberNotifications.RelatedType,
            integrationEvent.MemberId,
            cancellationToken);
    }
}

/// <summary>Le membre est sorti de l'équipe.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SellerMemberRevokedNotificationHandler")]
public sealed class SellerMemberRevokedNotificationHandler
    : IIntegrationEventHandler<SellerMemberRevokedIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;
    private readonly ILogger<SellerMemberRevokedNotificationHandler> _logger;

    public SellerMemberRevokedNotificationHandler(
        NotificationDispatcher dispatcher,
        ISellerModuleApi sellers,
        ILogger<SellerMemberRevokedNotificationHandler> logger)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
        _logger = logger;
    }

    public async Task HandleAsync(
        SellerMemberRevokedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var enseigne = await MemberNotifications.EnseigneAsync(
            _sellers, integrationEvent.SellerId, cancellationToken);

        await _dispatcher.NotifyAsync(
            integrationEvent.UserId,
            $"Votre accès chez {enseigne} a pris fin",
            "Vous ne faites plus partie de cette équipe. Votre compte HBAExpress reste actif et vos "
            + "commandes personnelles ne sont pas affectées.",
            MemberNotifications.RelatedType,
            integrationEvent.MemberId,
            cancellationToken,
            alsoEmail: true);

        _logger.LogInformation(
            "Sortie du membre {MemberId} du vendeur {SellerId} notifiée.",
            integrationEvent.MemberId, integrationEvent.SellerId);
    }
}

/// <summary>La propriété du dossier a changé de porteur — les DEUX comptes l'apprennent.</summary>
[NomDeConsommateur("HBA.Communication.Notifications.Application.Notifications.EventHandlers.SellerOwnershipTransferredNotificationHandler")]
public sealed class SellerOwnershipTransferredNotificationHandler
    : IIntegrationEventHandler<SellerOwnershipTransferredIntegrationEvent>
{
    private readonly NotificationDispatcher _dispatcher;
    private readonly ISellerModuleApi _sellers;

    public SellerOwnershipTransferredNotificationHandler(
        NotificationDispatcher dispatcher, ISellerModuleApi sellers)
    {
        _dispatcher = dispatcher;
        _sellers = sellers;
    }

    public async Task HandleAsync(
        SellerOwnershipTransferredIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        var enseigne = await MemberNotifications.EnseigneAsync(
            _sellers, integrationEvent.SellerId, cancellationToken);

        // AVEC E-MAIL POUR LES DEUX. La notification dans l'application suppose
        // qu'on l'ouvre ; ce geste-là doit atteindre quelqu'un qui ne s'y
        // connecterait pas de la semaine.
        await _dispatcher.NotifyAsync(
            integrationEvent.NewOwnerUserId,
            $"Vous êtes désormais propriétaire de {enseigne}",
            "La propriété du dossier vous a été transférée. Vous pouvez maintenant fermer le "
            + "dossier, changer le compte de reversement et transférer la propriété à votre tour. "
            + "Si vous ne vous attendiez pas à ce changement, prévenez immédiatement le support.",
            MemberNotifications.RelatedType,
            integrationEvent.NewOwnerMemberId,
            cancellationToken,
            alsoEmail: true);

        await _dispatcher.NotifyAsync(
            integrationEvent.PreviousOwnerUserId,
            $"Vous n'êtes plus propriétaire de {enseigne}",
            "La propriété du dossier a été transférée à un autre membre de l'équipe. Vous restez "
            + "membre, avec les droits d'administration, mais vous ne pouvez plus fermer le dossier "
            + "ni changer le compte de reversement. Si vous n'êtes pas à l'origine de ce transfert, "
            + "prévenez immédiatement le support : il ne peut être annulé que par le nouveau "
            + "propriétaire.",
            MemberNotifications.RelatedType,
            integrationEvent.PreviousOwnerMemberId,
            cancellationToken,
            alsoEmail: true);
    }
}
