using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Identity.Domain.Users.Events;

namespace HBA.Identity.Domain.Users;

/// <summary>Compte d'un acteur (acheteur, vendeur, admin).</summary>
public sealed class User : AggregateRoot<UserId>
{
    private readonly List<UserRoleAssignment> _roleAssignments = new();
    private readonly List<RefreshToken> _refreshTokens = new();

    private User()
    {
    }

    private User(
        UserId id,
        string firstName,
        string lastName,
        Email email,
        PhoneNumber phoneNumber,
        string passwordHash,
        string emailVerificationTokenHash,
        DateTime emailVerificationExpiresOnUtc)
        : base(id)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        PhoneNumber = phoneNumber;
        PasswordHash = passwordHash;
        Status = UserStatus.PendingVerification;
        EmailVerified = false;
        MfaEnabled = false;
        SecurityStamp = Guid.NewGuid();
        EmailVerificationTokenHash = emailVerificationTokenHash;
        EmailVerificationExpiresOnUtc = emailVerificationExpiresOnUtc;
        CreatedOnUtc = DateTime.UtcNow;

        Raise(new UserRegisteredDomainEvent(id.Value, email.Value, firstName, lastName));
    }

    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public Email Email { get; private set; } = default!;
    public PhoneNumber PhoneNumber { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserStatus Status { get; private set; }
    public bool EmailVerified { get; private set; }

    /// <summary>
    /// Renseigné si l'e-mail a été marqué vérifié PAR UN ADMINISTRATEUR, et non par
    /// le titulaire cliquant sur un lien reçu dans sa boîte.
    /// </summary>
    public DateTime? EmailVerifiedByAdminOnUtc { get; private set; }

    public bool MfaEnabled { get; private set; }
    public string? MfaSecret { get; private set; }
    public Guid SecurityStamp { get; private set; }
    public string? EmailVerificationTokenHash { get; private set; }
    public DateTime? EmailVerificationExpiresOnUtc { get; private set; }
    public string? PasswordResetTokenHash { get; private set; }
    public DateTime? PasswordResetExpiresOnUtc { get; private set; }

    /// <summary>Nombre d'essais infructueux sur le jeton de réinitialisation EN COURS.</summary>
    public int PasswordResetAttempts { get; private set; }

    /// <summary>Essais tolérés avant destruction du jeton.</summary>
    public const int MaxPasswordResetAttempts = 5;

    /// <summary>ÉCHECS D'AUTHENTIFICATION CONSÉCUTIFS SUR CE COMPTE.</summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>Instant jusqu'auquel la connexion est refusée.</summary>
    public DateTime? LockedUntilUtc { get; private set; }

    /// <summary>Échecs tolérés avant verrouillage.</summary>
    public const int MaxFailedLoginAttempts = 10;

    /// <summary>Durée du verrou. Voir <see cref="LockedUntilUtc"/>.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public DateTime CreatedOnUtc { get; private set; }

    /// <summary>Date de suppression du compte (anonymisation), à la demande du titulaire.</summary>
    public DateTime? DeletedOnUtc { get; private set; }

    /// <summary>
    /// Version des conditions générales acceptée par l'utilisateur, et date de
    /// cette acceptation.
    /// </summary>
    public string? AcceptedTermsVersion { get; private set; }

    public DateTime? AcceptedTermsOnUtc { get; private set; }

    public IReadOnlyCollection<UserRoleAssignment> RoleAssignments => _roleAssignments.AsReadOnly();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    /// <summary>Identifiants des rôles assignés (raccourci de lecture).</summary>
    public IReadOnlyCollection<Guid> RoleIds => _roleAssignments.Select(a => a.RoleId).ToList().AsReadOnly();

    /// <summary>Enregistre l'acceptation d'une version des conditions générales.</summary>
    public Result AcceptTerms(string version, DateTime onUtc)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            // `Result` (non générique) n'a pas de conversion implicite depuis Error
            // : seul `Result<T>` en a une.
            return Result.Failure(
                Error.Validation("identity.user.terms_version_required", "La version des conditions est obligatoire."));
        }

        var accepted = version.Trim();
        if (AcceptedTermsVersion == accepted)
        {
            return Result.Success();
        }

        AcceptedTermsVersion = accepted;
        AcceptedTermsOnUtc = onUtc;
        return Result.Success();
    }

    /// <summary>Crée un compte en attente de vérification.</summary>
    public static Result<User> Register(
        string firstName,
        string lastName,
        Email email,
        PhoneNumber phoneNumber,
        string passwordHash,
        string emailVerificationTokenHash,
        DateTime emailVerificationExpiresOnUtc)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            return Error.Validation("identity.user.first_name_required", "Le prénom est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            return Error.Validation("identity.user.last_name_required", "Le nom est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return Error.Validation("identity.user.password_required", "Le mot de passe est obligatoire.");
        }

        return new User(
            UserId.New(),
            firstName.Trim(),
            lastName.Trim(),
            email,
            phoneNumber,
            passwordHash,
            emailVerificationTokenHash,
            emailVerificationExpiresOnUtc);
    }

    /// <summary>Confirme l'e-mail à partir du hash du token reçu par lien.</summary>
    public Result ConfirmEmail(string providedTokenHash, DateTime nowUtc)
    {
        if (EmailVerified)
        {
            return Result.Success();
        }

        if (EmailVerificationTokenHash is null || EmailVerificationExpiresOnUtc is null)
        {
            return Result.Failure(Error.Conflict("identity.user.no_verification_pending", "Aucune vérification d'e-mail en attente."));
        }

        if (EmailVerificationExpiresOnUtc < nowUtc)
        {
            return Result.Failure(Error.Validation("identity.user.verification_expired", "Le lien de vérification a expiré."));
        }

        if (!FixedTimeEquals(EmailVerificationTokenHash, providedTokenHash))
        {
            return Result.Failure(Error.Validation("identity.user.verification_invalid", "Lien de vérification invalide."));
        }

        EmailVerified = true;
        EmailVerificationTokenHash = null;
        EmailVerificationExpiresOnUtc = null;

        // Aucune bascule de `Status` ici : voir le commentaire de la méthode.

        Raise(new UserEmailConfirmedDomainEvent(Id.Value, Email.Value));
        return Result.Success();
    }

    /// <summary>(Ré)émet un code de vérification e-mail : remplace tout code en attente.</summary>
    public Result BeginEmailVerification(string tokenHash, DateTime expiresOnUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return Result.Failure(Error.Validation("identity.user.token_required", "Le jeton de vérification est obligatoire."));
        }

        EmailVerificationTokenHash = tokenHash;
        EmailVerificationExpiresOnUtc = expiresOnUtc;
        return Result.Success();
    }

    /// <summary>Consomme un code de vérification e-mail SANS COURT-CIRCUIT.</summary>
    public Result ConsumeEmailVerificationCode(string providedTokenHash, DateTime nowUtc)
    {
        if (EmailVerificationTokenHash is null || EmailVerificationExpiresOnUtc is null)
        {
            return Result.Failure(Error.Conflict("identity.user.no_verification_pending", "Aucun code de vérification en attente."));
        }

        if (EmailVerificationExpiresOnUtc < nowUtc)
        {
            return Result.Failure(Error.Validation("identity.user.verification_expired", "Le code de vérification a expiré."));
        }

        if (!FixedTimeEquals(EmailVerificationTokenHash, providedTokenHash))
        {
            return Result.Failure(Error.Validation("identity.user.verification_invalid", "Code de vérification invalide."));
        }

        EmailVerified = true;
        EmailVerificationTokenHash = null;
        EmailVerificationExpiresOnUtc = null;

        Raise(new UserEmailConfirmedDomainEvent(Id.Value, Email.Value));
        return Result.Success();
    }

    /// <summary>Un administrateur atteste que l'adresse appartient bien au titulaire.</summary>
    public Result MarkEmailVerifiedByAdmin(DateTime nowUtc)
    {
        if (EmailVerified)
        {
            return Result.Success();
        }

        EmailVerified = true;
        EmailVerifiedByAdminOnUtc = nowUtc;
        EmailVerificationTokenHash = null;
        EmailVerificationExpiresOnUtc = null;

        Raise(new UserEmailConfirmedDomainEvent(Id.Value, Email.Value));
        return Result.Success();
    }

    public Result UpdateProfile(string firstName, string lastName, PhoneNumber phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            return Result.Failure(Error.Validation("identity.user.first_name_required", "Le prénom est obligatoire."));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            return Result.Failure(Error.Validation("identity.user.last_name_required", "Le nom est obligatoire."));
        }

        var prenom = firstName.Trim();
        var nom = lastName.Trim();

        // Le nom a-t-il RÉELLEMENT changé ? Comparé AVANT affectation — après, la
        // comparaison serait toujours vraie et l'événement partirait à chaque
        // appel.
        var nomModifie = prenom != FirstName || nom != LastName;

        FirstName = prenom;
        LastName = nom;
        PhoneNumber = phoneNumber;

        if (nomModifie)
        {
            // Le module User tient le profil affiché.
            Raise(new UserProfileUpdatedDomainEvent(Id.Value, FirstName, LastName));
        }

        return Result.Success();
    }

    /// <summary>
    /// Change le mot de passe : nouvelle empreinte, rotation du security stamp,
    /// révocation des refresh tokens.
    /// </summary>
    public Result ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
        {
            return Result.Failure(Error.Validation("identity.user.password_required", "Le mot de passe est obligatoire."));
        }

        PasswordHash = newPasswordHash;
        RegenerateSecurityStamp();
        RevokeAllRefreshTokens();

        Raise(new UserPasswordChangedDomainEvent(Id.Value));
        return Result.Success();
    }

    /// <summary>
    /// Initie une réinitialisation de mot de passe : stocke le hash du jeton + son
    /// expiration.
    /// </summary>
    public Result BeginPasswordReset(string tokenHash, DateTime expiresOnUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return Result.Failure(Error.Validation("identity.user.reset_token_required", "Jeton de réinitialisation manquant."));
        }

        PasswordResetTokenHash = tokenHash;
        PasswordResetExpiresOnUtc = expiresOnUtc;

        // REMISE À ZÉRO OBLIGATOIRE.
        PasswordResetAttempts = 0;

        return Result.Success();
    }

    /// <summary>
    /// Réinitialise le mot de passe à partir du hash du jeton fourni (usage
    /// unique).
    /// </summary>
    public Result ResetPassword(string providedTokenHash, string newPasswordHash, DateTime nowUtc)
    {
        if (PasswordResetTokenHash is null || PasswordResetExpiresOnUtc is null)
        {
            return Result.Failure(Error.Conflict("identity.user.no_reset_pending", "Aucune réinitialisation en attente."));
        }

        if (PasswordResetExpiresOnUtc < nowUtc)
        {
            return Result.Failure(Error.Validation("identity.user.reset_expired", "Le lien de réinitialisation a expiré."));
        }

        if (!FixedTimeEquals(PasswordResetTokenHash, providedTokenHash))
        {
            // SANS CE COMPTEUR, N'IMPORTE QUEL COMPTE ÉTAIT PRENABLE.
            PasswordResetAttempts++;

            if (PasswordResetAttempts >= MaxPasswordResetAttempts)
            {
                // Le jeton est détruit ICI, dans le domaine.
                InvalidatePasswordReset();

                return Result.Failure(Error.Validation(
                    "identity.user.reset_invalid",
                    "Lien de réinitialisation invalide."));
            }

            return Result.Failure(Error.Validation("identity.user.reset_invalid", "Lien de réinitialisation invalide."));
        }

        if (string.IsNullOrWhiteSpace(newPasswordHash))
        {
            return Result.Failure(Error.Validation("identity.user.password_required", "Le mot de passe est obligatoire."));
        }

        PasswordHash = newPasswordHash;
        InvalidatePasswordReset();
        RegenerateSecurityStamp();
        RevokeAllRefreshTokens();
        Raise(new UserPasswordChangedDomainEvent(Id.Value));
        return Result.Success();
    }

    /// <summary>Efface le jeton de réinitialisation et remet le compteur à zéro.</summary>
    private void InvalidatePasswordReset()
    {
        PasswordResetTokenHash = null;
        PasswordResetExpiresOnUtc = null;
        PasswordResetAttempts = 0;
    }

    public Result AssignRole(Guid roleId)
    {
        if (roleId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("identity.user.role_required", "Le rôle est obligatoire."));
        }

        if (_roleAssignments.All(a => a.RoleId != roleId))
        {
            _roleAssignments.Add(new UserRoleAssignment(Guid.NewGuid(), roleId));
            Raise(new UserRoleAssignedDomainEvent(Id.Value, roleId));
        }

        return Result.Success();
    }

    public Result RemoveRole(Guid roleId)
    {
        var assignment = _roleAssignments.FirstOrDefault(a => a.RoleId == roleId);
        if (assignment is not null)
        {
            _roleAssignments.Remove(assignment);
        }

        return Result.Success();
    }

    /// <summary>
    /// Approbation par un administrateur : le compte peut désormais se connecter.
    /// </summary>
    public Result Approve()
    {
        if (Status == UserStatus.Active)
        {
            return Result.Success();
        }

        Status = UserStatus.Active;
        return Result.Success();
    }

    /// <summary>
    /// Refus ou sanction. Réversible : rien n'est effacé, et la trace de la
    /// tentative d'inscription reste consultable — c'est justement ce qu'on veut
    /// conserver d'un compte frauduleux.
    /// </summary>
    public Result Suspend()
    {
        Status = UserStatus.Suspended;
        RevokeAllRefreshTokens();
        return Result.Success();
    }

    /// <summary>Levée de la suspension.</summary>
    public Result Reactivate()
    {
        if (Status != UserStatus.Suspended)
        {
            return Result.Failure(Error.Conflict(
                "identity.user.not_suspended", "Ce compte n'est pas suspendu."));
        }

        Status = UserStatus.Active;
        return Result.Success();
    }

    /// <summary>SUPPRESSION DU COMPTE à la demande de son titulaire — par anonymisation.</summary>
    public Result Anonymize(DateTime nowUtc)
    {
        if (Status == UserStatus.Deleted)
        {
            // Idempotent : un double appel (rejeu, double tap) ne doit pas échouer.
            return Result.Success();
        }

        // L'e-mail doit rester un e-mail valide (le value object le vérifie) ET
        // rester unique.
        var anonymousEmail = Email.Create($"deleted-{Id.Value:N}@deleted.invalid");
        if (anonymousEmail.IsFailure)
        {
            return Result.Failure(anonymousEmail.Error);
        }

        // Le téléphone doit rester au format attendu (8 à 15 chiffres).
        var anonymousPhone = PhoneNumber.Create("00000000");
        if (anonymousPhone.IsFailure)
        {
            return Result.Failure(anonymousPhone.Error);
        }

        FirstName = "Compte";
        LastName = "supprimé";
        Email = anonymousEmail.Value;
        PhoneNumber = anonymousPhone.Value;

        // Le hachage est REMPLACÉ, pas vidé : une chaîne vide pourrait, selon
        // l'implémentation d'un vérificateur, être considérée comme « pas de mot de
        // passe » — donc acceptée.
        PasswordHash = "DELETED";

        MfaEnabled = false;
        MfaSecret = null;

        EmailVerified = false;
        EmailVerificationTokenHash = null;
        EmailVerificationExpiresOnUtc = null;
        InvalidatePasswordReset();
        EmailVerifiedByAdminOnUtc = null;

        Status = UserStatus.Deleted;
        DeletedOnUtc = nowUtc;

        // Les jetons de RAFRAÎCHISSEMENT sont révoqués : plus aucune session ne
        // peut être renouvelée, sur aucun appareil.
        RevokeAllRefreshTokens();

        // HONNÊTETÉ SUR CE QUI N'EST PAS FAIT.
        SecurityStamp = Guid.NewGuid();

        // Les données personnelles hors de ce schéma — profil, carnet d'adresses —
        // doivent disparaître aussi.
        Raise(new UserAnonymizedDomainEvent(Id.Value));

        return Result.Success();
    }

    // ------------------------------------------------- Verrouillage du compte

    /// <summary>Le compte est-il verrouillé à cet instant ?</summary>
    public bool IsLockedOut(DateTime nowUtc) => LockedUntilUtc is { } until && until > nowUtc;

    /// <summary>Enregistre un échec d'authentification et verrouille au-delà du plafond.</summary>
    public bool RegisterFailedLogin(DateTime nowUtc)
    {
        // UN COMPTE DÉJÀ VERROUILLÉ NE SE REVERROUILLE PAS.
        if (IsLockedOut(nowUtc))
        {
            return false;
        }

        // Un verrou expiré repart de zéro.
        if (LockedUntilUtc is not null)
        {
            LockedUntilUtc = null;
            FailedLoginAttempts = 0;
        }

        FailedLoginAttempts++;

        if (FailedLoginAttempts < MaxFailedLoginAttempts)
        {
            return false;
        }

        LockedUntilUtc = nowUtc.Add(LockoutDuration);
        return true;
    }

    /// <summary>Une authentification a réussi : le compteur retombe.</summary>
    public void RegisterSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedUntilUtc = null;
    }

    // ----------------------------------------------------------------- Refresh

    /// <param name="authenticatedAtUtc">
    /// L'instant de l'authentification EFFECTIVE — celui de la connexion, ou de la
    /// réauthentification.
    /// </param>
    /// <param name="authMethods">
    /// Méthodes employées, séparées par des espaces (`pwd`, `pwd otp`).
    /// </param>
    public RefreshToken IssueRefreshToken(
        string tokenHash, DateTime expiresOnUtc, DateTime authenticatedAtUtc, string authMethods)
    {
        var token = new RefreshToken(Guid.NewGuid(), tokenHash, expiresOnUtc, authenticatedAtUtc, authMethods);
        _refreshTokens.Add(token);
        return token;
    }

    /// <summary>Lecture pure : ce jeton est-il actif ?</summary>
    public RefreshToken? FindActiveRefreshToken(string tokenHash, DateTime nowUtc)
        => _refreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash && t.IsActive(nowUtc));

    /// <summary>CONSOMME UN REFRESH TOKEN — ET DÉTECTE LE VOL.</summary>
    /// <param name="session">
    /// Le contexte d'authentification du jeton CONSOMMÉ, quand la rotation aboutit.
    /// </param>
    public RefreshTokenOutcome UseRefreshToken(
        string tokenHash, DateTime nowUtc, out AuthenticationSnapshot? session)
    {
        session = null;

        var token = _refreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash);

        if (token is null)
        {
            return RefreshTokenOutcome.Unknown;
        }

        if (token.RevokedOnUtc is not null)
        {
            RevokeAllRefreshTokens();
            return RefreshTokenOutcome.Replayed;
        }

        if (token.ExpiresOnUtc <= nowUtc)
        {
            return RefreshTokenOutcome.Expired;
        }

        // Rotation : le jeton présenté meurt, l'appelant en émet un neuf.
        session = new AuthenticationSnapshot(token.AuthenticatedAtUtc, token.AuthMethods);
        token.Revoke();
        return RefreshTokenOutcome.Rotated;
    }

    public Result RevokeRefreshToken(string tokenHash)
    {
        var token = _refreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash);
        token?.Revoke();
        return Result.Success();
    }

    public void RevokeAllRefreshTokens()
    {
        foreach (var token in _refreshTokens.Where(t => t.RevokedOnUtc is null))
        {
            token.Revoke();
        }
    }

    /// <summary>
    /// Révocation complète des sessions, pour le RPC `RevokeUserSessions` du §10.1.
    /// </summary>
    /// <returns>Nombre de jetons de rafraîchissement effectivement révoqués.</returns>
    public int RevokeAllSessions()
    {
        var actifs = _refreshTokens.Count(t => t.RevokedOnUtc is null);

        RevokeAllRefreshTokens();
        RegenerateSecurityStamp();

        return actifs;
    }

    // --------------------------------------------------------------------- MFA

    /// <summary>Initie l'activation MFA : stocke le secret TOTP, encore non confirmé.</summary>
    public Result BeginMfaSetup(string secret)
    {
        if (MfaEnabled)
        {
            return Result.Failure(Error.Conflict("identity.user.mfa_already_enabled", "La double authentification est déjà active."));
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            return Result.Failure(Error.Validation("identity.user.mfa_secret_required", "Secret MFA manquant."));
        }

        MfaSecret = secret;
        return Result.Success();
    }

    /// <summary>
    /// Confirme l'activation MFA après vérification d'un code TOTP par
    /// l'Application.
    /// </summary>
    public Result ConfirmMfaSetup()
    {
        if (string.IsNullOrWhiteSpace(MfaSecret))
        {
            return Result.Failure(Error.Conflict("identity.user.mfa_not_initiated", "Aucune activation MFA initiée."));
        }

        MfaEnabled = true;
        RegenerateSecurityStamp();
        return Result.Success();
    }

    public Result DisableMfa()
    {
        MfaEnabled = false;
        MfaSecret = null;
        RegenerateSecurityStamp();
        return Result.Success();
    }

    private void RegenerateSecurityStamp() => SecurityStamp = Guid.NewGuid();

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }
}
