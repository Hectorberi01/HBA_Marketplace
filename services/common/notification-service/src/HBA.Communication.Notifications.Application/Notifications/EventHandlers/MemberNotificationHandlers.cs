using HBA.Merchants.Contracts;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Communication.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI ARRIVE À UN MEMBRE, IL DOIT L'APPRENDRE — ET PAS EN SE COGNANT À UN 403.
///
/// SEPT ÉVÉNEMENTS ÉTAIENT PUBLIÉS ET CONSOMMÉS PAR PERSONNE.
///
/// Seule l'invitation avait un consommateur. Rejoindre une équipe, changer de
/// rôle, être affecté à une boutique, en être retiré, être suspendu, réactivé,
/// révoqué : tout cela partait dans l'outbox, était marqué traité, et
/// disparaissait. MediatR et le répartiteur résolvent paresseusement — un
/// événement sans consommateur ne provoque ni erreur ni avertissement.
///
/// C'est exactement le trou décrit dans `SendSellerInvitationEmailHandler` :
/// `EmailVerificationRequestedIntegrationEvent` a été publié consciencieusement
/// pendant des mois, consommé par personne, et aucun compte n'a jamais reçu son
/// lien.
///
/// CE QUE LE SILENCE COÛTE, CONCRÈTEMENT.
///
/// Un employé rétrogradé découvre sa rétrogradation en cliquant sur un bouton qui
/// répond « votre rôle ne vous autorise pas cette action ». Il appelle son gérant,
/// qui a oublié l'avoir fait la semaine passée. Un employé suspendu croit à une
/// panne et réessaie. Un employé affecté à une nouvelle boutique ne sait pas qu'il
/// peut y travailler. Chacun de ces cas produit un appel au support pour une
/// information que la plateforme détenait au moment du geste.
///
/// IN-APP, PAS E-MAIL — À UNE EXCEPTION PRÈS.
///
/// `NotifyAsync` écrit dans la boîte de réception et pousse vers les appareils.
/// C'est le bon canal pour un changement de droits : l'intéressé est un
/// utilisateur ACTIF de l'application, et l'information n'a de sens que devant
/// l'écran où elle s'applique. La RÉVOCATION, elle, part aussi par e-mail : c'est
/// la seule dont le destinataire ne pourra plus lire la boîte de réception, son
/// accès venant d'être coupé.
///
/// LE NOM DE LA BOUTIQUE VIENT D'UN APPEL, PAS DE L'ÉVÉNEMENT.
///
/// « Vos rôles ont changé chez 3f2a-… » n'informe personne. Les événements portent
/// des identifiants — délibérément, pour ne pas se périmer — et c'est au
/// consommateur d'aller chercher ce qu'il veut afficher. L'appel est mis en cache
/// côté merchant ; il coûte moins qu'un champ qui mentirait après un renommage.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class MemberNotifications
{
    /// <summary>Type de rattachement porté par la notification, pour le filtrage côté application.</summary>
    public const string RelatedType = "SellerMembership";

    /// <summary>
    /// Le nom de la boutique-mère, ou un repli neutre.
    /// </summary>
    /// <remarks>
    /// UN REPLI, ET NON UNE EXCEPTION.
    ///
    /// merchant-service peut être indisponible au moment où l'on traite
    /// l'événement. Lever ferait rejouer le message d'outbox — donc renotifier le
    /// membre, éventuellement plusieurs fois — pour un DÉTAIL D'AFFICHAGE. Une
    /// notification sans le nom de l'enseigne reste utile ; trois notifications
    /// identiques ne le sont pas.
    /// </remarks>
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

/// <summary>
/// Les rôles ont changé.
/// </summary>
/// <remarks>
/// LA NOTIFICATION N'ÉNUMÈRE PAS LES RÔLES, ET CE N'EST PAS DE LA PARESSE.
///
/// L'événement porte des identifiants de rôles ; leurs NOMS vivent derrière une
/// route d'équipe gardée par `ROLE_VIEW`, que notification-service n'a aucun titre
/// à appeler pour le compte d'un tiers. Surtout, un nom de rôle ne dit pas ce qui
/// a été gagné ou perdu — « Gestionnaire de commandes » ne se compare pas à
/// « Employé » dans la tête de qui le lit. L'écran des accès, lui, montre la liste
/// exacte des permissions ; la notification y renvoie plutôt que de la résumer mal.
/// </remarks>
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

/// <summary>
/// Le membre est retiré d'une boutique.
/// </summary>
/// <remarks>
/// CELLE-CI COMPTE PLUS QUE SON PENDANT, DEPUIS LE CADRAGE PAR BOUTIQUE.
///
/// Tant qu'un rôle de boutique s'appliquait au vendeur entier, le retrait ne
/// changeait rien de visible. Depuis le lot F, il retire RÉELLEMENT les droits sur
/// cette boutique-là — et l'employé qui l'ignore verra des refus sur un magasin où
/// il travaillait la veille.
/// </remarks>
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

/// <summary>
/// L'accès est suspendu.
/// </summary>
/// <remarks>
/// LE MOTIF N'EST PAS DANS L'ÉVÉNEMENT, DONC PAS DANS LE MESSAGE.
///
/// `SellerMemberSuspendedIntegrationEvent` ne porte que les identifiants. Inventer
/// un motif — « pour raison administrative » — serait pire que le taire : cela
/// laisserait croire à une décision de la plateforme là où c'est l'employeur qui a
/// agi. On dit QUI a suspendu, et on renvoie vers lui.
/// </remarks>
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

/// <summary>
/// Le membre est sorti de l'équipe.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA SEULE DES SEPT QUI PART AUSSI PAR E-MAIL (`alsoEmail: true`).
///
/// Les six autres s'adressent à quelqu'un qui garde l'accès et lira sa boîte de
/// réception. Celle-ci s'adresse à quelqu'un dont l'accès VIENT D'ÊTRE COUPÉ : la
/// notification in-app arriverait dans un espace qu'il ne peut plus ouvrir. C'est
/// le cas d'école d'un message qui doit sortir de l'application pour exister.
///
/// ET ELLE PART MÊME SI LE COMPTE APPARTIENT À UNE AUTRE ÉQUIPE.
///
/// `RemainsMemberElsewhere` sert à identity, pour décider s'il faut retirer le
/// rôle `Seller` — c'est une question d'AUTORISATION. Ici la question est
/// différente : la personne a perdu SON accès à CE dossier, et cela mérite d'être
/// dit qu'elle travaille ailleurs ou non.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

/// <summary>
/// La propriété du dossier a changé de porteur — les DEUX comptes l'apprennent.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// DEUX MESSAGES POUR UN SEUL ÉVÉNEMENT, ET C'EST LA SEULE FOIS DE CE FICHIER.
///
/// Partout ailleurs, un fait concerne un membre. Ici il en concerne deux, et
/// asymétriquement : l'un gagne six permissions critiques — dont la fermeture du
/// dossier et le compte de reversement — l'autre les perd toutes.
///
/// LE CÉDANT EST PRÉVENU MÊME S'IL EST L'AUTEUR DU GESTE.
///
/// Le transfert est irréversible sans l'accord de l'autre partie : la reprendre
/// exige que le nouveau propriétaire la retransfère. Un message « vous avez cédé »
/// qui arrive alors qu'on n'a rien cédé est le seul signal d'alarme qui reste — et
/// le taire au motif que « c'est lui qui a cliqué » suppose précisément ce qu'on
/// cherche à vérifier.
///
/// ON NE NOMME PAS L'AUTRE PARTIE.
///
/// L'événement ne porte que des identifiants ; résoudre le nom demanderait une
/// lecture chez identity, et afficher un nom de compte à quelqu'un qui ne le
/// connaît pas serait une fuite pour un confort. L'enseigne suffit à situer le
/// dossier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
