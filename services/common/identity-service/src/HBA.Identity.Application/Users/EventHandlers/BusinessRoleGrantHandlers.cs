using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Roles;
using HBA.Identity.Domain.Users;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Identity.Application.Users.EventHandlers;

/// <summary>
/// Attribue un rôle métier à un compte, sur foi d'un événement d'un autre service.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// PERSONNE N'ATTRIBUAIT CES RÔLES. C'ÉTAIT LE TROU CENTRAL.
///
/// Les rôles `Seller`, `FoodPartner` et `Driver` étaient SEMÉS par
/// `IdentityDataSeeder` et attribués par AUCUN code : seul « Buyer » l'était, à
/// l'inscription. Conséquence, invisible tant qu'on ne se connectait pas : les
/// BFF Merchant, Restaurant et Driver répondaient 403 à TOUT LE MONDE, y compris
/// au fondateur d'un restaurant validé.
///
/// Rien ne le signalait. Le vendeur s'inscrivait, son dossier était approuvé, sa
/// boutique ouverte — et son application refusait de s'ouvrir sans que le moindre
/// journal ne relie les deux faits.
///
/// PAR ÉVÉNEMENT D'INTÉGRATION, ET NON PAR APPEL DIRECT.
///
/// L'alternative — merchant-service appelant identity-service pour poser un rôle —
/// aurait donné à trois services le droit de modifier les autorisations d'un
/// compte. C'est identity-service, et lui seul, qui décide de ce qu'un compte a
/// le droit de faire ; les autres se contentent d'annoncer un FAIT MÉTIER
/// (« ce vendeur est inscrit », « ce restaurant est validé »).
///
/// Les trois événements existaient déjà et étaient publiés. Le contrat de
/// `RestaurantApprovedIntegrationEvent` documente même son `OwnerUserId` par
/// « le compte HBA du restaurateur — celui qui reçoit le rôle » : l'intention
/// était écrite, le consommateur n'a jamais été branché.
///
/// ON NE LÈVE JAMAIS.
///
/// Ces gestionnaires réagissent à un fait acquis : le vendeur EST inscrit, le
/// restaurant EST validé. Échouer ferait rejouer l'événement indéfiniment par
/// l'outbox sans jamais aboutir si la cause est un rôle absent en base. On
/// journalise en erreur — le symptôme, lui, est bruyant côté utilisateur.
///
/// L'opération est idempotente : `User.AssignRole` ignore un rôle déjà présent.
/// Un événement rejoué ne produit rien.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class BusinessRoleGrant
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly ILogger<BusinessRoleGrant> _logger;

    public BusinessRoleGrant(
        IUserRepository users,
        IRoleRepository roles,
        IIdentityUnitOfWork unitOfWork,
        ILogger<BusinessRoleGrant> logger)
    {
        _users = users;
        _roles = roles;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task GrantAsync(
        Guid userId, string roleName, string reason, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(new UserId(userId), cancellationToken);

        if (user is null)
        {
            _logger.LogError(
                "Rôle « {Role} » NON attribué ({Raison}) : aucun compte {UserId}. "
                + "L'application concernée refusera l'accès à ce partenaire.",
                roleName, reason, userId);
            return;
        }

        var role = await _roles.GetByNameAsync(roleName, cancellationToken);

        if (role is null)
        {
            // CRITICAL, ET PAS ERROR. CE N'EST PAS UN COMPTE, C'EST LA PLATEFORME.
            //
            // Le rôle est semé au démarrage par `IdentityDataSeeder`. Son absence
            // signifie que l'amorçage n'a pas eu lieu — un défaut d'installation,
            // pas une donnée manquante. Et il ne touche pas CE partenaire : il
            // touche TOUS ceux qui s'inscriront tant que la table restera vide.
            //
            // Rejouer n'y changerait rien (voir « ON NE LÈVE JAMAIS » ci-dessus),
            // d'où le journal plutôt que l'exception. Mais au niveau qui réveille.
            _logger.LogCritical(
                "Rôle « {Role} » INTROUVABLE en base ({Raison}, compte {UserId}). "
                + "L'amorçage des rôles système n'a pas eu lieu : AUCUN partenaire ne "
                + "recevra ce rôle tant que la table « identity.roles » ne sera pas semée.",
                roleName, reason, userId);
            return;
        }

        var result = user.AssignRole(role.Id.Value);

        if (result.IsFailure)
        {
            _logger.LogError(
                "Rôle « {Role} » refusé pour le compte {UserId} ({Raison}) : {Erreur}.",
                roleName, userId, reason, result.Error.Message);
            return;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Rôle « {Role} » attribué au compte {UserId} ({Raison}).", roleName, userId, reason);
    }

    /// <summary>
    /// Retire un rôle métier.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE N'EST PAS LE SYMÉTRIQUE DE `GrantAsync`, ET L'APPELANT DOIT LE SAVOIR.
    ///
    /// Un rôle métier peut avoir PLUSIEURS causes. `Seller` est accordé à
    /// l'inscription d'un vendeur ET au rattachement à une équipe : le retirer
    /// parce qu'une des deux a disparu enfermerait dehors quelqu'un pour qui
    /// l'autre tient toujours — un comptable révoqué chez un commerçant, mais
    /// vendeur lui-même, qui perdrait l'accès à SON PROPRE dossier sans que rien
    /// n'y ait été fait.
    ///
    /// La décision « la dernière cause a disparu » se prend donc chez celui qui les
    /// connaît. Cette méthode exécute, elle ne juge pas.
    ///
    /// MÊME DISCIPLINE QUE `GrantAsync` : ON NE LÈVE JAMAIS.
    ///
    /// Un compte absent ou un rôle non semé sont journalisés, pas propagés :
    /// échouer ferait rejouer l'événement indéfiniment par l'outbox sans jamais
    /// aboutir. `User.RemoveRole` est idempotent — un rejeu ne produit rien.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task RevokeAsync(
        Guid userId, string roleName, string reason, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(new UserId(userId), cancellationToken);

        if (user is null)
        {
            // Moins grave qu'à l'octroi : un compte absent n'a aucun rôle à perdre.
            _logger.LogWarning(
                "Rôle « {Role} » non retiré ({Raison}) : aucun compte {UserId}.",
                roleName, reason, userId);
            return;
        }

        var role = await _roles.GetByNameAsync(roleName, cancellationToken);

        if (role is null)
        {
            _logger.LogCritical(
                "Rôle « {Role} » INTROUVABLE en base ({Raison}, compte {UserId}). "
                + "L'amorçage des rôles système n'a pas eu lieu : aucun retrait ne peut "
                + "aboutir tant que la table « identity.roles » ne sera pas semée.",
                roleName, reason, userId);
            return;
        }

        user.RemoveRole(role.Id.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // LES SESSIONS EN COURS NE SONT PAS RÉVOQUÉES, ET C'EST DÉLIBÉRÉ.
        //
        // Un jeton déjà émis porte la claim et continuera de franchir
        // `MapSellerGroup` jusqu'à son expiration. Ce n'est pas un trou : la garde
        // qui décide est la vérification d'appartenance, côté merchant, et elle
        // refuse dès la révocation. Appeler `RevokeUserSessionsAsync` déconnecterait
        // le compte de TOUTE la plateforme — de ses achats compris — pour un
        // retrait qui ne concerne qu'un dossier vendeur.
        _logger.LogInformation(
            "Rôle « {Role} » retiré du compte {UserId} ({Raison}).", roleName, userId, reason);
    }
}

/// <summary>
/// Vendeur inscrit → rôle `Seller`.
/// </summary>
/// <remarks>
/// À L'INSCRIPTION, PAS À L'APPROBATION DU KYB.
///
/// Un vendeur dont le dossier est en cours d'instruction doit pouvoir ouvrir son
/// application : y déposer ses pièces, suivre l'avancement, préparer sa boutique.
/// Attendre l'approbation le laisserait dehors précisément pendant la période où
/// il a le plus besoin d'y entrer.
///
/// Ce que le rôle ouvre, c'est la SURFACE de l'application partenaire. Ce que le
/// KYB conditionne — vendre, encaisser — est vérifié par merchant-service sur le
/// statut du vendeur, pas par ce rôle.
/// </remarks>
public sealed class GrantSellerRoleHandler : IIntegrationEventHandler<SellerRegisteredIntegrationEvent>
{
    public const string RoleName = "Seller";

    private readonly BusinessRoleGrant _grant;

    public GrantSellerRoleHandler(BusinessRoleGrant grant) => _grant = grant;

    public Task HandleAsync(
        SellerRegisteredIntegrationEvent e, CancellationToken cancellationToken = default)
        => _grant.GrantAsync(e.UserId, RoleName, "inscription vendeur", cancellationToken);
}

/// <summary>
/// Restaurant validé → rôle `FoodPartner`.
/// </summary>
/// <remarks>
/// ICI, C'EST BIEN L'APPROBATION — CONTRAIREMENT AU VENDEUR.
///
/// La dissymétrie est voulue. Un dossier de restaurant est déposé par un
/// candidat ; tant qu'il n'est pas validé, l'établissement n'existe pas pour la
/// plateforme. Il n'y a pas d'équivalent du dépôt de pièces à faire entre-temps.
///
/// CONSÉQUENCE À CONNAÎTRE : LE PERSONNEL N'EST PAS COUVERT.
///
/// Seul `OwnerUserId` reçoit le rôle. Un cuisinier ou un caissier ajouté par le
/// §8 n'en obtient aucun, et l'écran de cuisine — qui est fait POUR eux — leur
/// reste fermé. Le combler demande un événement « membre ajouté » que food-service
/// ne publie pas encore.
/// </remarks>
public sealed class GrantFoodPartnerRoleHandler : IIntegrationEventHandler<RestaurantApprovedIntegrationEvent>
{
    public const string RoleName = "FoodPartner";

    private readonly BusinessRoleGrant _grant;

    public GrantFoodPartnerRoleHandler(BusinessRoleGrant grant) => _grant = grant;

    public Task HandleAsync(
        RestaurantApprovedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _grant.GrantAsync(e.OwnerUserId, RoleName, "validation du restaurant", cancellationToken);
}

/// <summary>
/// Livreur vérifié → rôle `Driver`.
/// </summary>
/// <remarks>
/// Ici l'attente est justifiée : conduire pour la plateforme suppose des pièces
/// contrôlées — permis, assurance, véhicule. Ouvrir l'application avant la
/// vérification laisserait accepter des courses à quelqu'un dont rien n'est
/// établi.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE HANDLER ÉCOUTE `HBA.Drivers.Contracts`, PLUS `HBA.Deliveries.Contracts`.
///
/// `DriverVerifiedIntegrationEvent` était déclaré dans LES DEUX, aux champs
/// identiques (`DriverId`, `UserId`). `KafkaEventNaming.EventType` ne regarde que
/// le nom de CLASSE et l'enveloppe Kafka ne transporte que ce nom : les deux
/// rendaient « driver.verified », et `ResolveEventType` retenait le premier par
/// ordre alphabétique du nom complet.
///
/// CE QUE ÇA PROVOQUAIT : si le type retenu n'était pas celui pour lequel ce
/// handler est enregistré, `GetServices` n'en trouvait AUCUN et l'événement passait
/// SANS EFFET — pas d'exception, pas d'échec de désérialisation, l'offset committé
/// juste après. Le rôle `Driver` n'était pas attribué, le BFF livreur répondait 403
/// à un livreur pourtant vérifié, et rien ne reliait le refus à la vérification.
/// C'est exactement le trou que tout ce fichier existe pour fermer.
///
/// POURQUOI DRIVERS : l'agrégat décrit est LE LIVREUR, pas la course. La
/// déclaration côté Deliveries a été retirée ; delivery-service publie désormais
/// ce type-ci.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GrantDriverRoleHandler : IIntegrationEventHandler<DriverVerifiedIntegrationEvent>
{
    public const string RoleName = "Driver";

    private readonly BusinessRoleGrant _grant;

    public GrantDriverRoleHandler(BusinessRoleGrant grant) => _grant = grant;

    public Task HandleAsync(
        DriverVerifiedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _grant.GrantAsync(e.UserId, RoleName, "vérification du livreur", cancellationToken);
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// MEMBRE RATTACHÉ À UNE ÉQUIPE VENDEUR → RÔLE `Seller`.
///
/// SANS CE CONSOMMATEUR, TOUT LE MODULE DES MEMBRES RESTE INERTE.
///
/// `MapSellerGroup` filtre sur la claim de rôle du jeton, et rien d'autre :
///
///     .RequireAuthorization(policy => policy.RequireRole(SellerRole, AdminRole, ModeratorRole))
///
/// Un membre parfaitement écrit en base — rôles corrects, permissions calculées,
/// appartenance active — est donc refoulé par le ROUTAGE. Avant tout handler,
/// avant que la moindre permission de ce module ne soit consultée, et avec un 403
/// au corps vide. Les tables, les gardes et les capacités du chantier membres ne
/// servent à rien tant que cette ligne n'existe pas.
///
/// C'est exactement le trou que `GrantFoodPartnerRoleHandler` documente pour le
/// personnel de restaurant, et qui reste ouvert de son côté : « seul `OwnerUserId`
/// reçoit le rôle […] l'écran de cuisine, qui est fait POUR eux, leur reste
/// fermé ». Le combler côté vendeur donne le gabarit pour l'y combler aussi.
///
/// ATTRIBUÉ À L'ENTRÉE DANS L'ÉQUIPE, PAS À L'INVITATION.
///
/// L'invitation ne prouve rien : elle est émise sur une adresse, éventuellement
/// sans compte en face. C'est l'acceptation — jeton valide, non expiré, adresse
/// concordante — qui établit le fait.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class GrantSellerRoleToMemberHandler
    : IIntegrationEventHandler<SellerMemberJoinedIntegrationEvent>
{
    private readonly BusinessRoleGrant _grant;

    public GrantSellerRoleToMemberHandler(BusinessRoleGrant grant) => _grant = grant;

    public Task HandleAsync(
        SellerMemberJoinedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _grant.GrantAsync(
            e.UserId, GrantSellerRoleHandler.RoleName, "rattachement à une équipe vendeur", cancellationToken);
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// MEMBRE SORTI D'UNE ÉQUIPE → RÔLE `Seller` RETIRÉ, MAIS PAS TOUJOURS.
///
/// LA RÉVOCATION N'EST PAS LE SYMÉTRIQUE DE L'OCTROI.
///
/// Le rôle `Seller` a deux causes possibles : être vendeur soi-même, ou appartenir
/// à l'équipe d'un vendeur. En perdre une ne veut pas dire les avoir perdues
/// toutes. Un comptable révoqué chez un commerçant peut être vendeur par ailleurs,
/// ou comptable chez un confrère : lui retirer le rôle l'enfermerait dehors de son
/// propre dossier, sur lequel personne n'a rien fait — la panne la moins
/// diagnosticable qui soit, puisque la cause est ailleurs que le symptôme.
///
/// LE DRAPEAU VIENT DE seller-service, ET IL NE POUVAIT VENIR QUE DE LÀ.
///
/// identity ne connaît pas les appartenances. L'interroger dans l'autre sens
/// créerait un appel de service circulaire — merchant dépend déjà d'identity — sur
/// le chemin d'un événement, c'est-à-dire au pire endroit possible.
///
/// ET RIEN N'EST FAIT SUR UNE SUSPENSION.
///
/// Une suspension est temporaire : retirer puis rendre le rôle à chaque
/// aller-retour produirait de la charge et des fenêtres d'incohérence pour un
/// résultat identique. Un membre suspendu franchit `MapSellerGroup` et se fait
/// refuser par la vérification d'appartenance, avec un motif lisible — ce qui est
/// exactement ce qu'on veut lui dire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RevokeSellerRoleOnMemberRemovedHandler
    : IIntegrationEventHandler<SellerMemberRevokedIntegrationEvent>
{
    private readonly BusinessRoleGrant _grant;
    private readonly ILogger<RevokeSellerRoleOnMemberRemovedHandler> _logger;

    public RevokeSellerRoleOnMemberRemovedHandler(
        BusinessRoleGrant grant, ILogger<RevokeSellerRoleOnMemberRemovedHandler> logger)
    {
        _grant = grant;
        _logger = logger;
    }

    public Task HandleAsync(
        SellerMemberRevokedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (e.HasOtherSellerMembership)
        {
            _logger.LogInformation(
                "Rôle « {Role} » conservé pour le compte {UserId} : il appartient encore à "
                + "une autre équipe vendeur (sortie du vendeur {SellerId}).",
                GrantSellerRoleHandler.RoleName, e.UserId, e.SellerId);

            return Task.CompletedTask;
        }

        return _grant.RevokeAsync(
            e.UserId, GrantSellerRoleHandler.RoleName, "sortie de la dernière équipe vendeur", cancellationToken);
    }
}
