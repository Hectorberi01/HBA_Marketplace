using HBA.Identity.Application.Abstractions;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Identity.Domain.Users;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;

namespace HBA.Identity.Application.Users.Commands.Logout;

/// <summary>
/// Déconnexion sur simple présentation du jeton de rafraîchissement (§10.1).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI ELLE N'EXIGE PAS DE JETON D'ACCÈS VALIDE.
///
/// `LogoutCommand` prend un `UserId` : elle suppose donc un appelant déjà
/// authentifié. Mais on se déconnecte précisément quand on doute de sa session —
/// jeton expiré, appareil perdu, connexion suspecte. Exiger un jeton d'accès
/// valide pour révoquer une session refuse le service à celui qui en a le plus
/// besoin, et le pousse à ne rien faire.
///
/// La preuve d'identité est ici le jeton de rafraîchissement lui-même : le
/// détenir suffit à en demander la révocation. C'est sans risque — la seule chose
/// qu'un tiers puisse en faire est de fermer une session qu'il pourrait de toute
/// façon utiliser.
///
/// RÉPONSE IDENTIQUE QUAND LE JETON EST INCONNU.
///
/// Un 404 sur jeton inconnu transformerait l'endpoint en oracle : on saurait
/// distinguer un jeton révoqué d'un jeton inventé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record LogoutByRefreshTokenCommand(string? RefreshToken) : ICommand;

internal sealed class LogoutByRefreshTokenCommandHandler : ICommandHandler<LogoutByRefreshTokenCommand>
{
    private readonly IUserRepository _users;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly IIntegrationEventPublisher _publisher;

    public LogoutByRefreshTokenCommandHandler(
        IUserRepository users,
        ISecureTokenGenerator tokens,
        IIdentityUnitOfWork unitOfWork,
        IIntegrationEventPublisher publisher)
    {
        _users = users;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<Result> Handle(LogoutByRefreshTokenCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Failure(Error.Validation(
                "identity.auth.refresh_token_required", "Le jeton de rafraîchissement est obligatoire."));
        }

        var hash = _tokens.Hash(command.RefreshToken.Trim());
        var user = await _users.GetByRefreshTokenHashAsync(hash, cancellationToken);

        if (user is null)
        {
            // Jeton inconnu ou déjà révoqué : succès. La déconnexion est idempotente
            // par nature — l'état visé est « plus de session », et il est atteint.
            return Result.Success();
        }

        user.RevokeRefreshToken(hash);

        await _publisher.PublishAsync(
            new TokenRevokedIntegrationEvent
            {
                UserId = user.Id.Value,
                Reason = "LOGOUT",
                RevokedCount = 1
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
