using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Observability;
using HBA.Shared.Domain.Results;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Application.Models;
using HBA.Identity.Domain.Roles;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Commands.Login;

/// <summary>
/// Vérifie les identifiants et le statut, applique la MFA si activée, puis émet les
/// jetons.
/// </summary>
internal sealed class LoginCommandHandler : ICommandHandler<LoginCommand, LoginResponse>
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("identity.auth.invalid_credentials", "E-mail ou mot de passe invalide.");

    // Labels de métriques (faible cardinalité, non personnels).
    private const string Method = "password";
    private const string ClientType = "unknown";

    private static readonly Error WrongSurface = Error.Forbidden(
        "identity.auth.wrong_surface",
        "Ce compte n'a pas accès à cette application.");

    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITotpService _totpService;
    private readonly AuthTokenIssuer _tokenIssuer;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly ISecurityMetrics _security;

    public LoginCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        ITotpService totpService,
        AuthTokenIssuer tokenIssuer,
        IIdentityUnitOfWork unitOfWork,
        ISecurityMetrics security)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _totpService = totpService;
        _tokenIssuer = tokenIssuer;
        _unitOfWork = unitOfWork;
        _security = security;
    }

    public async Task<Result<LoginResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            _security.LoginFailed(Method, "invalid_credentials", ClientType);
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        var user = await _userRepository.GetByEmailAsync(emailResult.Value.Value, cancellationToken);

        // COMPTE SUPPRIMÉ : on sort AVANT la vérification du mot de passe.
        if (user is not null && user.Status == UserStatus.Deleted)
        {
            _security.LoginFailed(Method, "deleted", ClientType);
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        // LE VERROU SE VÉRIFIE AVANT LE MOT DE PASSE.
        if (user is not null && user.IsLockedOut(DateTime.UtcNow))
        {
            _security.LoginFailed(Method, "locked_out", ClientType);
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        if (user is null || !_passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            if (user is not null)
            {
                await RegisterFailureAsync(user, "password", cancellationToken);
            }

            _security.LoginFailed(Method, "invalid_credentials", ClientType);
            return Result.Failure<LoginResponse>(InvalidCredentials);
        }

        if (user.Status == UserStatus.Suspended)
        {
            _security.LoginFailed(Method, "suspended", ClientType);
            return Result.Failure<LoginResponse>(Error.Forbidden("identity.auth.suspended", "Ce compte est suspendu."));
        }

        // Le verrou d'accès est le STATUT, plus `EmailVerified`.
        if (user.Status == UserStatus.PendingVerification)
        {
            _security.LoginFailed(Method, "pending_approval", ClientType);
            return Result.Failure<LoginResponse>(Error.Forbidden(
                "identity.auth.pending_approval",
                "Votre compte est en attente de validation. Vous serez prévenu dès qu'il sera activé."));
        }

        // Le compte a-t-il le droit d'entrer sur CETTE surface ?
        if (command.RequiredRoles is { Count: > 0 })
        {
            var roleIds = user.RoleIds.Select(id => new RoleId(id)).ToList();
            var roles = await _roleRepository.GetByIdsAsync(roleIds, cancellationToken);
            var names = roles.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!command.RequiredRoles.Any(names.Contains))
            {
                _security.LoginFailed(Method, "wrong_surface", ClientType);
                return Result.Failure<LoginResponse>(WrongSurface);
            }
        }

        if (user.MfaEnabled)
        {
            if (string.IsNullOrWhiteSpace(command.MfaCode))
            {
                // Étape intermédiaire (code attendu) : ni succès ni échec.
                return new LoginResponse(MfaRequired: true, Tokens: null);
            }

            if (user.MfaSecret is null || !_totpService.VerifyCode(user.MfaSecret, command.MfaCode))
            {
                // UN CODE MFA FAUX NE COÛTAIT RIEN.
                await RegisterFailureAsync(user, "mfa", cancellationToken);

                _security.LoginFailed("password_mfa", "mfa_invalid", ClientType);
                return Result.Failure<LoginResponse>(Error.Unauthorized("identity.auth.mfa_invalid", "Code de double authentification invalide."));
            }
        }

        // Authentification complète : le compteur d'échecs retombe.
        user.RegisterSuccessfulLogin();

        // `amr` DIT CE QUI S'EST RÉELLEMENT PASSÉ, PAS CE QUI EST CONFIGURÉ.
        var session = user.MfaEnabled
            ? AuthenticationSnapshot.ByPasswordAndOtp(DateTime.UtcNow)
            : AuthenticationSnapshot.ByPassword(DateTime.UtcNow);

        var tokens = await _tokenIssuer.IssueAsync(user, session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _security.LoginSuccess(user.MfaEnabled ? "password_mfa" : Method, ClientType);
        return new LoginResponse(MfaRequired: false, Tokens: tokens);
    }

    /// <summary>Enregistre l'échec et PERSISTE immédiatement.</summary>
    private async Task RegisterFailureAsync(User user, string stage, CancellationToken cancellationToken)
    {
        var justLocked = user.RegisterFailedLogin(DateTime.UtcNow);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (justLocked)
        {
            // Émise UNE FOIS, au basculement.
            _security.AccountLocked(stage);
        }
    }
}
