using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Roles;
using HBA.Identity.Domain.Users;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Identity.Application.Users;
using HBA.Identity.Application.Users.EventHandlers;

namespace HBA.Identity.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Attribue un rôle métier à un compte, sur foi d'un événement d'un autre service.
/// </summary>
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

    /// <summary>Retire un rôle métier.</summary>
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
        _logger.LogInformation(
            "Rôle « {Role} » retiré du compte {UserId} ({Raison}).", roleName, userId, reason);
    }
}

/// <summary>Vendeur inscrit → rôle `Seller`.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Identity.Application.Users.EventHandlers.GrantSellerRoleHandler")]
public sealed class GrantSellerRoleHandler : IIntegrationEventHandler<SellerRegisteredIntegrationEvent>
{
    public const string RoleName = "Seller";

    private readonly BusinessRoleGrant _grant;

    public GrantSellerRoleHandler(BusinessRoleGrant grant) => _grant = grant;

    public Task HandleAsync(
        SellerRegisteredIntegrationEvent e, CancellationToken cancellationToken = default)
        => _grant.GrantAsync(e.UserId, RoleName, "inscription vendeur", cancellationToken);
}

/// <summary>Restaurant validé → rôle `FoodPartner`.</summary>
[NomDeConsommateur("HBA.Identity.Application.Users.EventHandlers.GrantFoodPartnerRoleHandler")]
public sealed class GrantFoodPartnerRoleHandler : IIntegrationEventHandler<RestaurantApprovedIntegrationEvent>
{
    public const string RoleName = "FoodPartner";

    private readonly BusinessRoleGrant _grant;

    public GrantFoodPartnerRoleHandler(BusinessRoleGrant grant) => _grant = grant;

    public Task HandleAsync(
        RestaurantApprovedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _grant.GrantAsync(e.OwnerUserId, RoleName, "validation du restaurant", cancellationToken);
}

/// <summary>Livreur vérifié → rôle `Driver`.</summary>
[NomDeConsommateur("HBA.Identity.Application.Users.EventHandlers.GrantDriverRoleHandler")]
public sealed class GrantDriverRoleHandler : IIntegrationEventHandler<DriverVerifiedIntegrationEvent>
{
    public const string RoleName = "Driver";

    private readonly BusinessRoleGrant _grant;

    public GrantDriverRoleHandler(BusinessRoleGrant grant) => _grant = grant;

    public Task HandleAsync(
        DriverVerifiedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _grant.GrantAsync(e.UserId, RoleName, "vérification du livreur", cancellationToken);
}

/// <summary>MEMBRE RATTACHÉ À UNE ÉQUIPE VENDEUR → RÔLE `Seller`.</summary>
[NomDeConsommateur("HBA.Identity.Application.Users.EventHandlers.GrantSellerRoleToMemberHandler")]
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

/// <summary>MEMBRE SORTI D'UNE ÉQUIPE → RÔLE `Seller` RETIRÉ, MAIS PAS TOUJOURS.</summary>
[NomDeConsommateur("HBA.Identity.Application.Users.EventHandlers.RevokeSellerRoleOnMemberRemovedHandler")]
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
