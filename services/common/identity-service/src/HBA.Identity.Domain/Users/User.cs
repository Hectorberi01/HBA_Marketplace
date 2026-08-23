using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Identity.Domain.Users.Events;

namespace HBA.Identity.Domain.Users;

/// <summary>
/// Compte d'un acteur (acheteur, vendeur, admin). Source d'identité pour tout le
/// système (cf. dossier, module Identity). Agrégat racine : possède ses rôles
/// assignés et ses refresh tokens.
/// </summary>
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

        Raise(new UserRegisteredDomainEvent(id.Value, email.Value, firstName));
    }

    public string FirstName { get; private set; } = default!;
    public string LastName { get; private set; } = default!;
    public Email Email { get; private set; } = default!;
    public PhoneNumber PhoneNumber { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserStatus Status { get; private set; }
    public bool EmailVerified { get; private set; }

    /// <summary>
    /// Renseigné si l'e-mail a été marqué vérifié PAR UN ADMINISTRATEUR, et non par le
    /// titulaire cliquant sur un lien reçu dans sa boîte.
    ///
    /// Les deux mettent <see cref="EmailVerified"/> à vrai, mais ils ne valent pas la
    /// même chose. Cliquer sur un lien PROUVE qu'on relève cette adresse. Un
    /// administrateur, lui, ATTESTE — sur la foi d'un échange, d'un document, d'un appel.
    /// C'est légitime, et c'est aujourd'hui le seul chemin possible faute de service
    /// d'e-mailing. Mais c'est une confiance, pas une preuve.
    ///
    /// Sans cette colonne, les deux seraient indiscernables. Le jour où un envoi
    /// d'e-mails sera branché, on ne saurait plus dire quelles adresses ont réellement
    /// été confirmées — et on n'aurait aucun moyen de demander aux autres de le faire.
    /// Nulle = vérification authentique (ou pas de vérification du tout).
    /// </summary>
    public DateTime? EmailVerifiedByAdminOnUtc { get; private set; }

    public bool MfaEnabled { get; private set; }
    public string? MfaSecret { get; private set; }
    public Guid SecurityStamp { get; private set; }
    public string? EmailVerificationTokenHash { get; private set; }
    public DateTime? EmailVerificationExpiresOnUtc { get; private set; }
    public string? PasswordResetTokenHash { get; private set; }
    public DateTime? PasswordResetExpiresOnUtc { get; private set; }

    /// <summary>
    /// Nombre d'essais infructueux sur le jeton de réinitialisation EN COURS.
    ///
    /// PERSISTÉ, ET C'EST TOUT L'INTÉRÊT.
    ///
    /// Un compteur gardé en mémoire se remettrait à zéro à chaque redémarrage et,
    /// pire, ne serait pas partagé entre les cinq hôtes : un attaquant alternerait
    /// simplement les hôtes pour multiplier son quota. Un compteur qui ne survit
    /// pas est un compteur décoratif.
    /// </summary>
    public int PasswordResetAttempts { get; private set; }

    /// <summary>
    /// Essais tolérés avant destruction du jeton.
    ///
    /// Cinq : de quoi absorber une faute de frappe et un copier-coller
    /// malheureux, très loin des milliers d'essais que réclame un code à six
    /// chiffres.
    /// </summary>
    public const int MaxPasswordResetAttempts = 5;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ÉCHECS D'AUTHENTIFICATION CONSÉCUTIFS SUR CE COMPTE.
    ///
    /// CE COMPTEUR N'EXISTAIT PAS : ON POUVAIT ESSAYER DES MOTS DE PASSE
    /// INDÉFINIMENT.
    ///
    /// Le seul rempart était le limiteur par ADRESSE IP — et
    /// <c>AuthRateLimiter</c> écrit lui-même, noir sur blanc, ce qu'il ne fait
    /// pas : « un plafond par IP, même bien réglé, ne protège pas un COMPTE
    /// précis d'une attaque lente et distribuée (dix mots de passe par heure,
    /// depuis mille adresses) ». C'était exact, et ce n'était pas corrigé.
    ///
    /// Le compte visé n'est pas choisi au hasard : l'adresse d'un administrateur
    /// se devine. Et les cinq hôtes partagent la clé de signature — un compte
    /// pris vaut partout.
    ///
    /// EN BASE, ET NON EN MÉMOIRE. Même raison que
    /// <see cref="PasswordResetAttempts"/> : un compteur qui ne survit pas au
    /// redémarrage, et qui n'est pas partagé entre les cinq hôtes, est un
    /// compteur décoratif — il suffit d'alterner les hôtes pour le contourner.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>
    /// Instant jusqu'auquel la connexion est refusée. Nulle = pas de verrou.
    ///
    /// TEMPORAIRE, ET C'EST TOUT L'ARBITRAGE.
    ///
    /// Un verrou définitif — « compte bloqué, contactez le support » — retourne
    /// la protection contre son propriétaire : il suffit de connaître l'adresse
    /// d'un administrateur et d'échouer cinq fois pour l'empêcher d'entrer, tous
    /// les jours. La protection deviendrait l'arme.
    ///
    /// Quinze minutes ramènent l'attaquant à vingt essais par heure sur un compte
    /// donné, quelle que soit sa réserve d'adresses IP. Le titulaire qui s'est
    /// trompé, lui, prend un café.
    /// </summary>
    public DateTime? LockedUntilUtc { get; private set; }

    /// <summary>
    /// Échecs tolérés avant verrouillage.
    ///
    /// Dix et non cinq : ce compteur agrège les fautes de frappe d'un mot de
    /// passe LONG, parfois saisi sur un clavier de téléphone. Cinq verrouillerait
    /// des titulaires légitimes pour un gain nul — dix essais toutes les quinze
    /// minutes reste dérisoire face à un dictionnaire.
    /// </summary>
    public const int MaxFailedLoginAttempts = 10;

    /// <summary>Durée du verrou. Voir <see cref="LockedUntilUtc"/>.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public DateTime CreatedOnUtc { get; private set; }

    /// <summary>
    /// Date de suppression du compte (anonymisation), à la demande du titulaire.
    ///
    /// Null pour tout compte vivant. C'est la SEULE trace temporelle qui subsiste : elle
    /// permet de prouver, en cas de réclamation, que la demande a bien été honorée — et
    /// quand. Sans elle, vous auriez effacé les données ET la preuve de les avoir
    /// effacées.
    /// </summary>
    public DateTime? DeletedOnUtc { get; private set; }

    /// <summary>
    /// Version des conditions générales acceptée par l'utilisateur, et date de
    /// cette acceptation. Null tant qu'il n'a rien accepté.
    ///
    /// Le consentement se PROUVE, sinon il n'existe pas. Le Code du numérique fait
    /// dépendre de l'acceptation des conditions la validité du contrat conclu en
    /// ligne : le jour d'un litige, il faut pouvoir dire QUI a accepté QUOI et
    /// QUAND. Un drapeau stocké dans le téléphone ne prouve rien — il disparaît à
    /// la réinstallation et se modifie sur un appareil rooté. La trace vit donc ici,
    /// côté serveur, sur l'agrégat User.
    ///
    /// On garde la VERSION, pas un simple booléen : le jour où les conditions
    /// changent, il faut savoir qui a accepté l'ancienne rédaction — et pouvoir
    /// redemander l'accord à ceux-là seulement.
    /// </summary>
    public string? AcceptedTermsVersion { get; private set; }

    public DateTime? AcceptedTermsOnUtc { get; private set; }

    public IReadOnlyCollection<UserRoleAssignment> RoleAssignments => _roleAssignments.AsReadOnly();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    /// <summary>Identifiants des rôles assignés (raccourci de lecture).</summary>
    public IReadOnlyCollection<Guid> RoleIds => _roleAssignments.Select(a => a.RoleId).ToList().AsReadOnly();

    /// <summary>
    /// Enregistre l'acceptation d'une version des conditions générales.
    ///
    /// IDEMPOTENT : ré-accepter la version déjà acceptée ne change RIEN, pas même
    /// la date. Sinon, un client qui rejoue l'appel effacerait la date du
    /// consentement d'origine — précisément la donnée qu'on cherche à conserver.
    /// </summary>
    public Result AcceptTerms(string version, DateTime onUtc)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            // `Result` (non générique) n'a pas de conversion implicite depuis Error :
            // seul `Result<T>` en a une. On passe donc par Result.Failure.
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

    /// <summary>
    /// Crée un compte en attente de vérification. Le hash du mot de passe et le
    /// hash du token de vérification sont calculés par l'Application (services
    /// d'infrastructure) ; le domaine ne voit jamais les secrets en clair.
    /// </summary>
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

    /// <summary>
    /// Confirme l'e-mail à partir du hash du token reçu par lien.
    ///
    /// N'ACTIVE PLUS LE COMPTE. Prouver qu'on relève bien cette boîte aux lettres
    /// n'est pas la même chose qu'être autorisé à entrer — c'est l'administrateur qui
    /// décide, via <see cref="Approve"/>.
    ///
    /// Cette méthode est aujourd'hui dormante : aucun service d'e-mailing n'est
    /// branché, donc personne ne reçoit de lien. Elle a néanmoins été corrigée
    /// maintenant, et non « le jour venu » : si elle avait gardé son `Status = Active`,
    /// le premier développeur qui câblera un envoi d'e-mails rendrait l'approbation
    /// administrateur contournable — sans le savoir, et sans que rien ne le signale.
    /// </summary>
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
        // L'e-mail vérifié est un FAIT ; l'activation est une DÉCISION.

        Raise(new UserEmailConfirmedDomainEvent(Id.Value, Email.Value));
        return Result.Success();
    }

    /// <summary>
    /// (Ré)émet un code de vérification e-mail : remplace tout code en attente.
    ///
    /// N'affecte PAS <see cref="EmailVerified"/> : un acheteur déjà vérifié qui se
    /// lance dans la vente reste vérifié et connecté. Le code sert ici à PROUVER LA
    /// POSSESSION de la boîte au moment de rattacher une boutique (anti-usurpation),
    /// pas à revalider une adresse déjà validée.
    /// </summary>
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

    /// <summary>
    /// Consomme un code de vérification e-mail SANS COURT-CIRCUIT.
    ///
    /// Contrairement à <see cref="ConfirmEmail"/>, qui renvoie succès d'emblée si
    /// l'e-mail est déjà vérifié (double-clic sur le lien, idempotence), cette
    /// méthode EXIGE que le code corresponde même sur un compte déjà vérifié. Sans
    /// cela, n'importe qui pourrait rattacher une boutique à un acheteur existant
    /// sans jamais prouver qu'il relève sa boîte. C'est la brique de l'auto-inscription
    /// vendeur.
    /// </summary>
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

    /// <summary>
    /// Un administrateur atteste que l'adresse appartient bien au titulaire.
    ///
    /// À distinguer de <see cref="ConfirmEmail"/>, juste au-dessus : là, le titulaire
    /// cliquait un lien reçu dans sa boîte — c'était une PREUVE. Ici, un humain se porte
    /// garant sur la foi d'un échange, d'un document, d'un appel. C'est une CONFIANCE.
    ///
    /// La distinction est conservée dans <see cref="EmailVerifiedByAdminOnUtc"/>. Ne
    /// pas la conserver reviendrait à effacer, dans les données, la différence entre
    /// « prouvé » et « cru sur parole » — et personne, plus tard, ne pourrait la
    /// reconstituer.
    ///
    /// Le jeton de vérification en attente est purgé : il n'a plus d'objet, et un jeton
    /// qui traîne est un jeton qu'on peut rejouer.
    ///
    /// Idempotente. N'active PAS le compte : l'activation reste une décision distincte
    /// (<see cref="Approve"/>). Vérifier une adresse et autoriser l'accès sont deux
    /// choses différentes ; les confondre a déjà coûté assez cher dans ce module.
    /// </summary>
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
        // comparaison serait toujours vraie et l'événement partirait à chaque appel.
        var nomModifie = prenom != FirstName || nom != LastName;

        FirstName = prenom;
        LastName = nom;
        PhoneNumber = phoneNumber;

        if (nomModifie)
        {
            // Le module User tient le profil affiché. Il ne peut pas écouter Identity
            // lui-même — UsersBoundaryTests le lui interdit — donc l'événement sort
            // par l'outbox et le composition root le traduit en renommage.
            Raise(new UserProfileUpdatedDomainEvent(Id.Value, FirstName, LastName));
        }

        return Result.Success();
    }

    /// <summary>Change le mot de passe : nouvelle empreinte, rotation du security stamp, révocation des refresh tokens.</summary>
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

    /// <summary>Initie une réinitialisation de mot de passe : stocke le hash du jeton + son expiration.</summary>
    public Result BeginPasswordReset(string tokenHash, DateTime expiresOnUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return Result.Failure(Error.Validation("identity.user.reset_token_required", "Jeton de réinitialisation manquant."));
        }

        PasswordResetTokenHash = tokenHash;
        PasswordResetExpiresOnUtc = expiresOnUtc;

        // REMISE À ZÉRO OBLIGATOIRE.
        //
        // Le compteur porte sur le jeton EN COURS. Sans cette ligne, un titulaire
        // qui s'est trompé cinq fois puis redemande un code se verrait refuser le
        // nouveau dès le premier essai — et ne comprendrait pas pourquoi.
        //
        // Elle n'ouvre pas de contournement : redemander un code exige de passer
        // par la route de demande, qui est limitée par IP et qui, elle, envoie le
        // nouveau code DANS LA BOÎTE DU TITULAIRE. L'attaquant ne le voit pas.
        PasswordResetAttempts = 0;

        return Result.Success();
    }

    /// <summary>Réinitialise le mot de passe à partir du hash du jeton fourni (usage unique).</summary>
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
            // ═════════════════════════════════════════════════════════════════
            // SANS CE COMPTEUR, N'IMPORTE QUEL COMPTE ÉTAIT PRENABLE.
            //
            // Le jeton est un code NUMÉRIQUE À SIX CHIFFRES valable une heure —
            // un million de possibilités. Jusqu'ici, un code faux ne coûtait
            // rien : il ne verrouillait pas, n'invalidait pas, ne ralentissait
            // pas. Le seul rempart était la limite de trente requêtes par minute
            // et PAR IP, comptée en mémoire, par instance.
            //
            // Soit mille huit cents essais par heure et par adresse. Environ deux
            // cent quatre-vingts adresses suffisaient pour une chance sur deux
            // dans la fenêtre, sur un compte CHOISI — y compris un compte
            // administrateur, dont l'adresse se devine. Les cinq hôtes partagent
            // la clé de signature : le compte pris valait partout.
            //
            // Cinq essais, puis le jeton MEURT. Le titulaire légitime en
            // redemande un ; l'attaquant, lui, doit recommencer une demande à
            // chaque tranche de cinq — et c'est cette demande-là que la limite
            // par IP freine réellement.
            // ═════════════════════════════════════════════════════════════════
            PasswordResetAttempts++;

            if (PasswordResetAttempts >= MaxPasswordResetAttempts)
            {
                // Le jeton est détruit ICI, dans le domaine. Le laisser à
                // l'appelant reviendrait à espérer que chaque chemin d'entrée y
                // pense — et il suffirait d'un qui l'oublie.
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

    /// <summary>
    /// Efface le jeton de réinitialisation et remet le compteur à zéro.
    ///
    /// Les trois champs bougent ENSEMBLE. Un compteur laissé à cinq sur un jeton
    /// effacé condamnerait la demande suivante avant le premier essai ; un jeton
    /// effacé sans compteur remis rendrait le plafond permanent.
    /// </summary>
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
    ///
    /// Cette méthode ne touche PAS <see cref="EmailVerified"/>, et c'est
    /// délibéré. Un administrateur qui valide un compte ne prouve pas que l'adresse
    /// e-mail appartient bien à cette personne — il atteste que le compte est
    /// légitime, ce qui n'est pas la même chose. Écrire `EmailVerified = true` ici
    /// fabriquerait un fait qui n'a jamais eu lieu ; le jour où un service d'envoi
    /// d'e-mails sera branché, on ne saurait plus distinguer les adresses réellement
    /// confirmées de celles qu'un admin a laissé passer.
    ///
    /// C'est <see cref="Status"/> qui gouverne l'accès. <see cref="EmailVerified"/>
    /// reste un constat : aujourd'hui faux pour tout le monde, faute d'e-mailing.
    ///
    /// Idempotente : approuver deux fois n'est pas une erreur (double-clic, rejeu).
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
    ///
    /// Révoque les jetons : un compte suspendu dont la session reste ouverte n'est
    /// pas suspendu.
    /// </summary>
    public Result Suspend()
    {
        Status = UserStatus.Suspended;
        RevokeAllRefreshTokens();
        return Result.Success();
    }

    /// <summary>
    /// Levée de la suspension.
    ///
    /// L'ancienne garde exigeait <c>EmailVerified</c>. Elle ne tenait que parce que
    /// l'inscription trichait — elle appelait `ConfirmEmail` d'office, faute de
    /// service d'e-mailing. Maintenant que cette triche est retirée, la garde aurait
    /// rendu TOUTE réactivation impossible : plus personne n'a l'e-mail vérifié.
    ///
    /// Le bon critère est le statut, pas l'e-mail.
    /// </summary>
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

    /// <summary>
    /// SUPPRESSION DU COMPTE à la demande de son titulaire — par anonymisation.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// POURQUOI ON N'EFFACE PAS LA LIGNE
    ///
    /// Les commandes doivent être conservées pour la comptabilité et le fisc, plusieurs
    /// années durant. Chacune référence son acheteur. Supprimer la ligne « utilisateur »
    /// briserait ces références et rendrait vos livres incohérents — donc inexploitables
    /// en cas de contrôle.
    ///
    /// Ce que la loi et Apple exigent, ce n'est pas la disparition de la LIGNE : c'est
    /// celle des DONNÉES PERSONNELLES. On remplace donc tout ce qui identifie la
    /// personne, et l'on ne garde qu'une coquille comptable qui ne désigne plus
    /// personne.
    ///
    /// CE QUI DISPARAÎT : nom, prénom, e-mail, téléphone, mot de passe, secret MFA,
    /// jetons de session et de réinitialisation.
    /// CE QUI RESTE : l'identifiant technique et la date de création — le strict
    /// nécessaire pour que les commandes passées restent rattachables à « un » acheteur.
    ///
    /// IRRÉVERSIBLE, et sans retour possible : les données d'origine n'existent plus.
    /// Aucune méthode ne fait sortir de l'état Deleted, et c'est délibéré.
    ///
    /// L'E-MAIL ANONYME EST DÉRIVÉ DE L'IDENTIFIANT, car la colonne est UNIQUE. Deux
    /// suppressions ne doivent pas entrer en collision. Le téléphone, lui, est identique
    /// pour tous les comptes supprimés : son index unique est FILTRÉ pour les exclure
    /// (voir UserConfiguration). Sans ce filtre, la deuxième suppression échouerait —
    /// et l'utilisateur aurait reçu une erreur sans comprendre.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result Anonymize(DateTime nowUtc)
    {
        if (Status == UserStatus.Deleted)
        {
            // Idempotent : un double appel (rejeu, double tap) ne doit pas échouer.
            // Il n'y a d'ailleurs plus rien à anonymiser.
            return Result.Success();
        }

        // L'e-mail doit rester un e-mail valide (le value object le vérifie) ET rester
        // unique. On le dérive donc de l'identifiant. Le domaine « .invalid » est
        // réservé par l'IETF : il ne peut appartenir à personne, et aucun courrier ne
        // partira jamais vers lui par mégarde.
        var anonymousEmail = Email.Create($"deleted-{Id.Value:N}@deleted.invalid");
        if (anonymousEmail.IsFailure)
        {
            return Result.Failure(anonymousEmail.Error);
        }

        // Le téléphone doit rester au format attendu (8 à 15 chiffres). Même valeur pour
        // tous les comptes supprimés : l'unicité est levée par l'index filtré.
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
        // passe » — donc acceptée. On y met une valeur qu'aucun mot de passe ne peut
        // produire, et de toute façon la connexion est barrée par le statut.
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

        // Les jetons de RAFRAÎCHISSEMENT sont révoqués : plus aucune session ne peut
        // être renouvelée, sur aucun appareil.
        RevokeAllRefreshTokens();

        // HONNÊTETÉ SUR CE QUI N'EST PAS FAIT.
        //
        // Les jetons d'ACCÈS déjà émis restent techniquement valides jusqu'à leur
        // expiration (quelques dizaines de minutes) : ils sont vérifiés par signature,
        // sans relecture en base. Le `SecurityStamp` ci-dessous existe pour cela — mais
        // il n'est aujourd'hui **jamais lu** à la validation (vérifié : il n'apparaît
        // que comme colonne). Le changer ne révoque donc RIEN pour l'instant.
        //
        // On le fait tourner quand même, pour que le jour où la validation le
        // consultera, les comptes supprimés soient couverts sans y repenser.
        //
        // La fenêtre résiduelle est bornée par la durée de vie du jeton d'accès, et
        // elle se referme d'elle-même : aucun renouvellement n'est possible.
        SecurityStamp = Guid.NewGuid();

        // Les données personnelles hors de ce schéma — profil, carnet d'adresses —
        // doivent disparaître aussi. Ce module ne peut pas les toucher : il ne
        // connaît pas User. L'événement porte la demande jusqu'à lui.
        Raise(new UserAnonymizedDomainEvent(Id.Value));

        return Result.Success();
    }

    // ------------------------------------------------- Verrouillage du compte

    /// <summary>
    /// Le compte est-il verrouillé à cet instant ?
    ///
    /// NE REMET PAS LE COMPTEUR À ZÉRO quand le verrou a expiré, et c'est
    /// délibéré : c'est <see cref="RegisterFailedLogin"/> qui s'en charge, au
    /// moment où il en a besoin. Une lecture qui modifie l'état est une lecture
    /// qu'on n'ose plus appeler deux fois.
    /// </summary>
    public bool IsLockedOut(DateTime nowUtc) => LockedUntilUtc is { } until && until > nowUtc;

    /// <summary>
    /// Enregistre un échec d'authentification et verrouille au-delà du plafond.
    ///
    /// Renvoie <c>true</c> si CET échec vient de déclencher le verrou — ce que
    /// l'appelant utilise pour n'émettre la métrique qu'une fois, au moment du
    /// basculement, plutôt qu'à chaque tentative suivante.
    ///
    /// APPELÉ AUSSI POUR UN CODE MFA FAUX, ET C'EST VOULU.
    ///
    /// Un code MFA erroné arrive APRÈS un mot de passe juste : c'est un signal
    /// plus fort qu'un mot de passe faux, pas plus faible. Lui donner son propre
    /// compteur, plus permissif, reviendrait à mieux protéger les comptes sans
    /// double authentification que ceux qui en ont une.
    /// </summary>
    public bool RegisterFailedLogin(DateTime nowUtc)
    {
        // ═════════════════════════════════════════════════════════════════════
        // UN COMPTE DÉJÀ VERROUILLÉ NE SE REVERROUILLE PAS.
        //
        // Sans ce retour anticipé, chaque tentative pendant le verrou REPOUSSAIT
        // son échéance de quinze minutes. Un attaquant qui continue de frapper
        // maintenait donc le titulaire dehors indéfiniment — le déni de service
        // que la durée limitée était précisément censée empêcher. La protection
        // serait redevenue l'arme.
        //
        // Le compteur ne bouge pas non plus : il a déjà produit son effet, et
        // continuer de l'incrémenter n'apporterait rien qu'un grand nombre.
        // ═════════════════════════════════════════════════════════════════════
        if (IsLockedOut(nowUtc))
        {
            return false;
        }

        // Un verrou expiré repart de zéro. Sans cette remise, un compte ayant
        // atteint le plafond une fois se reverrouillerait au premier échec
        // suivant, pour toujours — le verrou temporaire deviendrait définitif
        // par accumulation.
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

    /// <summary>
    /// Une authentification a réussi : le compteur retombe.
    ///
    /// Le plafond porte sur des échecs CONSÉCUTIFS. Sans cette remise, un
    /// utilisateur qui se trompe deux fois par jour finirait verrouillé au bout
    /// d'une semaine sans avoir subi la moindre attaque.
    /// </summary>
    public void RegisterSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedUntilUtc = null;
    }

    // ----------------------------------------------------------------- Refresh

    /// <param name="authenticatedAtUtc">
    /// L'instant de l'authentification EFFECTIVE — celui de la connexion, ou de la
    /// réauthentification. Sur une rotation, l'appelant recopie celui du jeton
    /// consommé : voir <see cref="RefreshToken.AuthenticatedAtUtc"/>.
    /// </param>
    /// <param name="authMethods">Méthodes employées, séparées par des espaces (`pwd`, `pwd otp`).</param>
    public RefreshToken IssueRefreshToken(
        string tokenHash, DateTime expiresOnUtc, DateTime authenticatedAtUtc, string authMethods)
    {
        var token = new RefreshToken(Guid.NewGuid(), tokenHash, expiresOnUtc, authenticatedAtUtc, authMethods);
        _refreshTokens.Add(token);
        return token;
    }

    /// <summary>
    /// Lecture pure : ce jeton est-il actif ?
    ///
    /// N'EST PLUS LE CHEMIN D'AUTHENTIFICATION. <c>UseRefreshToken</c> l'a
    /// remplacé, parce que celui-ci ne distingue pas « révoqué » de « expiré » —
    /// il rend null dans les deux cas, ce qui rendait le rejeu invisible.
    ///
    /// Conservé pour les vérifications (tests, diagnostic). Le réintroduire dans
    /// un chemin d'authentification ferait retomber le trou.
    /// </summary>
    public RefreshToken? FindActiveRefreshToken(string tokenHash, DateTime nowUtc)
        => _refreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash && t.IsActive(nowUtc));

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CONSOMME UN REFRESH TOKEN — ET DÉTECTE LE VOL.
    ///
    /// LE REJEU N'ÉTAIT PAS DÉTECTÉ.
    ///
    /// La rotation existait : présenter un jeton le révoque et en rend un neuf.
    /// Mais présenter un jeton DÉJÀ révoqué renvoyait un 401 muet, et rien
    /// d'autre. Or c'est la signature même d'un vol.
    ///
    /// Le scénario : quelqu'un copie le refresh token du titulaire (journal,
    /// sauvegarde, téléphone prêté). Deux porteurs, une seule chaîne. Le premier
    /// qui s'en sert la fait tourner ; le second présente un jeton périmé. On ne
    /// sait pas lequel est le voleur — mais on sait qu'il y en a un.
    ///
    /// La réponse est de couper la chaîne ENTIÈRE. Le voleur perd son accès ; le
    /// titulaire se reconnecte avec son mot de passe, que le voleur n'a pas. Ne
    /// rien faire laissait au contraire le voleur tranquille : son jeton à lui
    /// restait valide, et c'est le titulaire qui était déconnecté sans comprendre.
    ///
    /// « RÉVOQUÉ » ET « EXPIRÉ » NE SE CONFONDENT PAS.
    ///
    /// Un jeton simplement expiré est le cas ORDINAIRE : quelqu'un revient après
    /// un mois. Traiter cela comme un vol déconnecterait de vrais utilisateurs
    /// de tous leurs appareils, régulièrement, sans raison.
    ///
    /// LA SANCTION EST APPLIQUÉE ICI, dans le domaine, et non laissée à
    /// l'appelant : sinon elle repose sur le fait que chaque chemin d'entrée y
    /// pense, et il suffirait d'un qui l'oublie.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="session">
    /// Le contexte d'authentification du jeton CONSOMMÉ, quand la rotation aboutit.
    ///
    /// IL DOIT ÊTRE RECOPIÉ SUR LE JETON ÉMIS, ET NON RECALCULÉ.
    ///
    /// C'est ce report qui fait que `auth_time` ne rajeunit pas au rafraîchissement.
    /// Le reconstruire avec `DateTime.UtcNow` rendrait le step-up du §37 purement
    /// décoratif : un client qui rafraîchit régulièrement passerait toujours.
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
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX EFFETS, ET AUCUN NE SUFFIT SEUL.
    ///
    /// `RevokeAllRefreshTokens` empêche d'obtenir un NOUVEAU jeton d'accès. Il ne
    /// touche pas à ceux DÉJÀ ÉMIS, qui restent cryptographiquement valides jusqu'à
    /// leur expiration : révoquer les sessions d'un compte compromis lui laisserait
    /// donc un quart d'heure d'accès complet.
    ///
    /// La rotation du tampon de sécurité ferme cette fenêtre — `ValidateAccessToken`
    /// compare le tampon porté par le jeton à celui du compte et refuse les jetons
    /// antérieurs. À l'inverse, faire tourner le tampon SANS révoquer les jetons de
    /// rafraîchissement laisserait l'attaquant s'en émettre un neuf aussitôt.
    ///
    /// Les deux, ou rien.
    /// ═════════════════════════════════════════════════════════════════════════
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

    /// <summary>Confirme l'activation MFA après vérification d'un code TOTP par l'Application.</summary>
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
