using HBA.Identity.Application.Abstractions;
using HBA.Identity.Application.Models;
using HBA.Identity.Domain.Users;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Observability;
using HBA.Shared.Domain.Results;

namespace HBA.Identity.Application.Users.Commands.Reauthenticate;

/// <summary>
/// Vérifie le mot de passe du porteur du jeton et réémet la paire avec un
/// <c>auth_time</c> neuf. Voir <see cref="ReauthenticateCommand"/>.
/// </summary>
internal sealed class ReauthenticateCommandHandler : ICommandHandler<ReauthenticateCommand, AuthTokens>
{
    // UNE SEULE ERREUR POUR TOUS LES REFUS, ET C'EST LA MÊME QU'À LA CONNEXION.
    //
    // Compte introuvable, supprimé, suspendu, verrouillé, mot de passe faux : la
    // réponse ne change pas. L'appelant est déjà authentifié, donc la fuite n'est
    // pas la même qu'au login — mais un message qui distingue « verrouillé » de
    // « mot de passe faux » dit à qui teste des mots de passe quand s'arrêter et
    // quand insister, et cela vaut aussi sur un compte volé.
    private static readonly Error Refuse =
        Error.Unauthorized("identity.auth.invalid_credentials", "Mot de passe invalide.");

    private const string Method = "reauthenticate";
    private const string ClientType = "unknown";

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AuthTokenIssuer _tokenIssuer;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly ISecurityMetrics _security;

    public ReauthenticateCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        AuthTokenIssuer tokenIssuer,
        IIdentityUnitOfWork unitOfWork,
        ISecurityMetrics security)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
        _unitOfWork = unitOfWork;
        _security = security;
    }

    public async Task<Result<AuthTokens>> Handle(
        ReauthenticateCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);

        if (user is null || user.Status is UserStatus.Deleted or UserStatus.Suspended)
        {
            _security.LoginFailed(Method, "invalid_credentials", ClientType);
            return Result.Failure<AuthTokens>(Refuse);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE VERROU DE LA CONNEXION S'APPLIQUE ICI AUSSI.
        //
        // Sans ce contrôle, cette route serait le contournement le plus court du
        // verrouillage : elle accepte un mot de passe, elle est joignable, et elle
        // ne demande qu'un jeton d'accès — que l'attaquant possède déjà dans le
        // scénario même qui justifie le step-up. On aurait ajouté une protection
        // et ouvert, du même geste, un oracle de force brute sans limite.
        //
        // Le compteur d'échecs est le MÊME que celui de la connexion. Deux
        // compteurs séparés doubleraient le nombre d'essais avant verrouillage.
        // ═════════════════════════════════════════════════════════════════════
        if (user.IsLockedOut(DateTime.UtcNow))
        {
            _security.LoginFailed(Method, "locked", ClientType);
            return Result.Failure<AuthTokens>(Refuse);
        }

        if (!_passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            var justLocked = user.RegisterFailedLogin(DateTime.UtcNow);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (justLocked)
            {
                _security.AccountLocked(Method);
            }

            _security.LoginFailed(Method, "invalid_credentials", ClientType);
            return Result.Failure<AuthTokens>(Refuse);
        }

        user.RegisterSuccessfulLogin();

        // `pwd` SEUL, MÊME SI LE COMPTE PORTE UNE MFA ACTIVÉE.
        //
        // Le claim `amr` dit ce qui vient de se passer, pas ce qui est configuré.
        // Y écrire `otp` sans avoir vérifié de code ferait mentir le jeton — et un
        // appelant qui exigerait `mfa` avant un geste sensible se croirait protégé
        // par un facteur qui n'a pas joué.
        var session = AuthenticationSnapshot.ByPassword(DateTime.UtcNow);

        var tokens = await _tokenIssuer.IssueAsync(user, session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _security.LoginSuccess(Method, ClientType);
        return tokens;
    }
}
