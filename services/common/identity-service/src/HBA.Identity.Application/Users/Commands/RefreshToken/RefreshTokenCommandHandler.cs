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
///
/// Un jeton DÉJÀ RÉVOQUÉ n'est plus traité comme un simple 401 : c'est la
/// signature d'un vol, et toute la chaîne du compte est coupée. Voir
/// User.UseRefreshToken.
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
        //
        // Après, on aurait révoqué le jeton d'un compte suspendu au passage :
        // à sa réactivation, l'utilisateur serait déconnecté sans raison
        // apparente. Un refus ne doit pas laisser de trace.
        if (user.Status == UserStatus.Suspended)
        {
            return Result.Failure<AuthTokens>(Error.Forbidden("identity.auth.suspended", "Ce compte est suspendu."));
        }

        // Rotation ET détection de rejeu, en une seule décision prise dans le
        // domaine — voir User.UseRefreshToken pour le raisonnement.
        var outcome = user.UseRefreshToken(tokenHash, DateTime.UtcNow, out var session);

        if (outcome is RefreshTokenOutcome.Replayed)
        {
            // ═════════════════════════════════════════════════════════════════
            // JETON DÉJÀ CONSOMMÉ : TOUTE LA CHAÎNE VIENT D'ÊTRE COUPÉE.
            //
            // Il FAUT sauvegarder avant de répondre. Sans ce SaveChanges, la
            // révocation en cascade décidée par le domaine mourrait avec le scope
            // de la requête : on répondrait 401 au porteur du jeton périmé et le
            // voleur garderait le sien. Exactement l'inverse du but.
            //
            // La réponse reste un 401 indistinct. Dire « nous avons détecté un
            // rejeu » n'aiderait que celui qui teste des jetons volés.
            // ═════════════════════════════════════════════════════════════════
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _security.TokenValidationFailed("refresh_replayed");
            return Result.Failure<AuthTokens>(InvalidToken);
        }

        if (outcome is not RefreshTokenOutcome.Rotated)
        {
            // Inconnu ou expiré : rien à sauvegarder, rien à sanctionner. Un
            // jeton expiré est le cas ORDINAIRE — quelqu'un revient après une
            // longue absence.
            _security.TokenValidationFailed(
                outcome == RefreshTokenOutcome.Expired ? "refresh_expired" : "refresh_unknown");

            return Result.Failure<AuthTokens>(InvalidToken);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE CONTEXTE D'AUTHENTIFICATION EST RECOPIÉ, JAMAIS RECALCULÉ.
        //
        // Écrire ici `AuthenticationSnapshot.ByPassword(DateTime.UtcNow)` compilerait
        // et paraîtrait juste — le jeton EST émis maintenant. Mais `auth_time`
        // rajeunirait à chaque rotation, et le step-up du §37 deviendrait
        // décoratif : un client qui rafraîchit toutes les quatre minutes passerait
        // indéfiniment le contrôle de mot de passe récent qui garde les virements.
        //
        // Le rafraîchissement prolonge une session ; il ne prouve rien de neuf sur
        // l'identité du porteur. C'est précisément la distinction que `auth_time`
        // existe pour porter.
        //
        // `session` est non nul dès lors que l'issue est `Rotated` — les trois
        // autres chemins ont déjà rendu.
        // ═════════════════════════════════════════════════════════════════════
        var tokens = await _tokenIssuer.IssueAsync(user, session!.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return tokens;
    }
}
