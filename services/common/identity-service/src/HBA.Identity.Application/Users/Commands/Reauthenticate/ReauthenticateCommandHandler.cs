using HBA.Identity.Application.Abstractions;
using HBA.Identity.Application.Models;
using HBA.Identity.Domain.Users;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Observability;
using HBA.Shared.Domain.Results;

namespace HBA.Identity.Application.Users.Commands.Reauthenticate;

/// <summary>
/// Vérifie le mot de passe du porteur du jeton et réémet la paire avec un <c>
/// auth_time</c> neuf.
/// </summary>
internal sealed class ReauthenticateCommandHandler : ICommandHandler<ReauthenticateCommand, AuthTokens>
{
    // UNE SEULE ERREUR POUR TOUS LES REFUS, ET C'EST LA MÊME QU'À LA CONNEXION.
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

        // LE VERROU DE LA CONNEXION S'APPLIQUE ICI AUSSI.
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
        var session = AuthenticationSnapshot.ByPassword(DateTime.UtcNow);

        var tokens = await _tokenIssuer.IssueAsync(user, session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _security.LoginSuccess(Method, ClientType);
        return tokens;
    }
}
