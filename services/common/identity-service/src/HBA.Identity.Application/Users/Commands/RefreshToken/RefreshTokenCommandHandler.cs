using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Observability;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Application.Models;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Commands.RefreshToken;

/// <summary>
/// Valide le refresh token (par son hash), le révoque (rotation) et émet une
/// nouvelle paire.
/// </summary>
internal sealed class RefreshTokenCommandHandler : ICommandHandler<RefreshTokenCommand, AuthTokens>
{
    private static readonly Error InvalidToken =
        Error.Unauthorized("identity.auth.refresh_invalid", "Refresh token invalide ou expiré.");

    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly AuthTokenIssuer _tokenIssuer;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly ISecurityMetrics _security;

    public RefreshTokenCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        AuthTokenIssuer tokenIssuer,
        IIdentityUnitOfWork unitOfWork,
        ISecurityMetrics security)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _tokenIssuer = tokenIssuer;
        _unitOfWork = unitOfWork;
        _security = security;
    }

    public async Task<Result<AuthTokens>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var tokenHash = _tokenGenerator.Hash(command.RefreshToken);

        var user = await _userRepository.GetByRefreshTokenHashAsync(tokenHash, cancellationToken);
        if (user is null)
        {
            _security.TokenValidationFailed("refresh_unknown");
            return Result.Failure<AuthTokens>(InvalidToken);
        }

        // LE STATUT SE VÉRIFIE AVANT DE CONSOMMER LE JETON.
        if (user.Status == UserStatus.Suspended)
        {
            return Result.Failure<AuthTokens>(Error.Forbidden("identity.auth.suspended", "Ce compte est suspendu."));
        }

        // Rotation ET détection de rejeu, en une seule décision prise dans le
        // domaine — voir User.UseRefreshToken pour le raisonnement.
        var outcome = user.UseRefreshToken(tokenHash, DateTime.UtcNow, out var session);

        if (outcome is RefreshTokenOutcome.Replayed)
        {
            // JETON DÉJÀ CONSOMMÉ : TOUTE LA CHAÎNE VIENT D'ÊTRE COUPÉE.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _security.TokenValidationFailed("refresh_replayed");
            return Result.Failure<AuthTokens>(InvalidToken);
        }

        if (outcome is not RefreshTokenOutcome.Rotated)
        {
            // Inconnu ou expiré : rien à sauvegarder, rien à sanctionner.
            _security.TokenValidationFailed(
                outcome == RefreshTokenOutcome.Expired ? "refresh_expired" : "refresh_unknown");

            return Result.Failure<AuthTokens>(InvalidToken);
        }

        // LE CONTEXTE D'AUTHENTIFICATION EST RECOPIÉ, JAMAIS RECALCULÉ.
        var tokens = await _tokenIssuer.IssueAsync(user, session!.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return tokens;
    }
}
