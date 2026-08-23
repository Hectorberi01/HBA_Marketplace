using HBA.Identity.Application.Abstractions;
using HBA.Identity.Application.Models;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Identity.Domain.Mfa;
using HBA.Identity.Domain.Users;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;

namespace HBA.Identity.Application.Users.Commands.Otp;

/// <summary>Défi émis, tel que rendu au client.</summary>
/// <param name="ChallengeId">À renvoyer avec le code sur `POST /auth/verify-otp`.</param>
/// <param name="ExpiresAtUtc">Permet au client d'afficher un compte à rebours.</param>
public sealed record OtpChallengeDto(Guid ChallengeId, string Channel, DateTime ExpiresAtUtc);

/// <summary>
/// Émet un code à usage unique et le transmet par le canal demandé.
///
/// LA RÉPONSE NE DIT PAS SI LE COMPTE EXISTE.
///
/// Rendre 404 sur un e-mail inconnu transformerait cet endpoint en oracle
/// d'existence de comptes : un script y déroulerait une liste d'adresses pour
/// savoir lesquelles sont inscrites. Un défi est donc TOUJOURS rendu, avec un
/// identifiant, et seul un compte réel reçoit effectivement un code.
/// </summary>
public sealed record IssueOtpChallengeCommand(string? Login, string? Channel) : ICommand<OtpChallengeDto>;

internal sealed class IssueOtpChallengeCommandHandler
    : ICommandHandler<IssueOtpChallengeCommand, OtpChallengeDto>
{
    private readonly IUserRepository _users;
    private readonly IMfaChallengeRepository _challenges;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ISecretProtector _protecteur;

    public IssueOtpChallengeCommandHandler(
        IUserRepository users,
        IMfaChallengeRepository challenges,
        ISecureTokenGenerator tokens,
        IIdentityUnitOfWork unitOfWork,
        IIntegrationEventPublisher publisher,
        ISecretProtector protecteur)
    {
        _users = users;
        _challenges = challenges;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _protecteur = protecteur;
    }

    public async Task<Result<OtpChallengeDto>> Handle(
        IssueOtpChallengeCommand command, CancellationToken cancellationToken)
    {
        var channel = (command.Channel ?? MfaChannels.Sms).Trim().ToUpperInvariant();

        if (!MfaChannels.All.Contains(channel))
        {
            return Result.Failure<OtpChallengeDto>(Error.Validation(
                "identity.mfa.channel_unsupported", $"Canal non pris en charge : « {command.Channel} »."));
        }

        var user = string.IsNullOrWhiteSpace(command.Login)
            ? null
            : await _users.GetByEmailAsync(command.Login.Trim(), cancellationToken);

        if (user is null)
        {
            // Compte inconnu : on rend un défi qui ne correspond à rien plutôt que
            // de révéler l'absence. L'identifiant est tiré au hasard et n'existe pas
            // en base ; toute vérification échouera exactement comme un mauvais code.
            return Result.Success(new OtpChallengeDto(
                Guid.NewGuid(), channel, DateTime.UtcNow.Add(MfaChallenge.Lifetime)));
        }

        // Un seul code vivant à la fois — voir ConsumeActiveAsync : cinq demandes
        // successives laisseraient sinon cinq codes valables, et le plafond de
        // tentatives serait multiplié d'autant.
        await _challenges.ConsumeActiveAsync(user.Id.Value, cancellationToken);

        var (code, hash) = _tokens.GenerateNumericCode();
        var challenge = MfaChallenge.Issue(user.Id.Value, channel, hash);

        if (challenge.IsFailure)
        {
            return Result.Failure<OtpChallengeDto>(challenge.Error);
        }

        await _challenges.AddAsync(challenge.Value, cancellationToken);

        // ═════════════════════════════════════════════════════════════════════════
        // ICI, LE CODE ÉTAIT JETÉ (ISSUE-062).
        //
        // La ligne était `_ = code;`, précédée d'un commentaire affirmant que « le
        // code EN CLAIR ne sort pas d'ici autrement que par le canal choisi ». Il
        // n'existait AUCUN canal : pas d'`ISmsSender`, pas d'événement, pas de
        // consommateur. Le clair partait avec la pile de la méthode, et la route
        // rendait un `challengeId` parfaitement valide pour un code que personne
        // ne recevrait jamais.
        //
        // Le commentaire ne décrivait pas le code : il décrivait l'intention de
        // quelqu'un qui n'est pas allé au bout. C'est le motif le plus coûteux de
        // ce dépôt — un texte qui affirme, et que plus personne ne vérifie.
        //
        // LE CODE VOYAGE CHIFFRÉ (ISSUE-071), comme les trois autres secrets qui
        // traversent l'outbox. `ISecretProtector` chiffre ici, notification-service
        // déchiffre au dernier moment. Un dump de base ou un consommateur du topic
        // n'y lit rien.
        //
        // PUBLIÉ AVANT `SaveChangesAsync`, ET C'EST OBLIGATOIRE. Le publieur écrit
        // dans l'OUTBOX du même contexte : l'envoi et le défi sont donc dans une
        // seule transaction. Publier après aurait produit exactement les deux pannes
        // que l'outbox existe pour empêcher — un code envoyé sans défi en base
        // (invérifiable), ou un défi sans envoi (l'état d'avant).
        // ═════════════════════════════════════════════════════════════════════════
        await _publisher.PublishAsync(
            new OtpChallengeIssuedIntegrationEvent
            {
                UserId = user.Id.Value,
                Channel = challenge.Value.Channel,
                Email = user.Email.Value,
                PhoneNumber = user.PhoneNumber.Value,
                FirstName = user.FirstName,
                ProtectedCode = _protecteur.Protect(code),
                ExpiresAtUtc = challenge.Value.ExpiresAtUtc
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OtpChallengeDto(
            challenge.Value.Id, challenge.Value.Channel, challenge.Value.ExpiresAtUtc));
    }
}

/// <summary>Vérifie un code reçu par SMS ou e-mail (§10.1, `POST /auth/verify-otp`).</summary>
public sealed record VerifyOtpCommand(Guid ChallengeId, string? Code) : ICommand<OtpVerificationDto>;

/// <summary>Résultat d'une vérification.</summary>
/// <param name="Tokens">
/// Les jetons de session, ou <c>null</c> si la vérification n'a pas abouti.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE CHAMP N'EXISTAIT PAS, ET LA ROUTE NE SERVAIT DONC À RIEN (ISSUE-062).
///
/// `verify-otp` rendait `(bool Verified, string Channel)`. Même si le code avait
/// été livré — il ne l'était pas —, le vérifier n'ouvrait AUCUNE session : le
/// client apprenait « oui, c'est le bon code » et n'avait rien à en faire.
/// L'endpoint entier était décoratif.
/// ═════════════════════════════════════════════════════════════════════════════
/// </param>
public sealed record OtpVerificationDto(bool Verified, string Channel, AuthTokens? Tokens);

internal sealed class VerifyOtpCommandHandler : ICommandHandler<VerifyOtpCommand, OtpVerificationDto>
{
    /// <summary>
    /// Le refus rendu quand le code est bon mais que le COMPTE ne peut pas entrer.
    ///
    /// VOLONTAIREMENT IDENTIQUE AU REFUS D'UN MAUVAIS CODE. À ce stade, le
    /// porteur du code a prouvé qu'il lit les messages du compte — mais cette route
    /// est ANONYME, et un message distinct (« ce compte est suspendu ») dirait à qui
    /// intercepte un SMS l'état exact d'un compte qui ne lui appartient pas. Le
    /// titulaire légitime, lui, obtient la vraie raison sur `POST /auth/login`, après
    /// son mot de passe — c'est le même arbitrage que `LoginCommandHandler`.
    /// </summary>
    private static readonly Error CompteInaccessible = Error.Validation(
        "identity.mfa.invalid_code", "Code invalide ou expiré.");

    private readonly IMfaChallengeRepository _challenges;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IUserRepository _users;
    private readonly AuthTokenIssuer _tokenIssuer;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public VerifyOtpCommandHandler(
        IMfaChallengeRepository challenges,
        ISecureTokenGenerator tokens,
        IUserRepository users,
        AuthTokenIssuer tokenIssuer,
        IIdentityUnitOfWork unitOfWork)
    {
        _challenges = challenges;
        _tokens = tokens;
        _users = users;
        _tokenIssuer = tokenIssuer;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<OtpVerificationDto>> Handle(
        VerifyOtpCommand command, CancellationToken cancellationToken)
    {
        var challenge = await _challenges.GetByIdAsync(command.ChallengeId, cancellationToken);

        if (challenge is null)
        {
            // Même réponse qu'un mauvais code, et même code d'erreur : distinguer
            // « défi inconnu » de « code faux » dirait à un attaquant que
            // l'identifiant qu'il a tiré au hasard n'existe pas — donc, par
            // élimination, lesquels existent.
            return Result.Failure<OtpVerificationDto>(Error.Validation(
                "identity.mfa.invalid_code", "Code invalide ou expiré."));
        }

        var code = (command.Code ?? string.Empty).Trim();
        var outcome = challenge.Verify(hash => _tokens.Hash(code) == hash);

        // Sauvegardé DANS TOUS LES CAS : c'est l'incrément du compteur de tentatives
        // qui protège du balayage. Ne sauvegarder qu'en cas de succès rendrait le
        // plafond décoratif — cinq mille essais laisseraient le compteur à zéro.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (outcome == MfaVerificationOutcome.TooManyAttempts)
        {
            return Result.Failure<OtpVerificationDto>(
                Error.BusinessRule("identity.mfa.too_many_attempts",
                    "Trop de tentatives. Demandez un nouveau code."));
        }

        if (outcome != MfaVerificationOutcome.Verified)
        {
            return Result.Failure<OtpVerificationDto>(
                Error.Validation("identity.mfa.invalid_code", "Code invalide ou expiré."));
        }

        return await OuvrirLaSessionAsync(challenge, cancellationToken);
    }

    /// <summary>
    /// Le code est bon : reste à savoir si le COMPTE a le droit d'entrer.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// SANS CES GARDES, L'OTP SERAIT UN CONTOURNEMENT DE TOUT LE LOGIN.
    ///
    /// `LoginCommandHandler` refuse un compte supprimé, suspendu, en attente
    /// d'approbation, ou verrouillé après trop d'échecs. Cette route-ci est un
    /// SECOND CHEMIN D'ENTRÉE, anonyme, vers les mêmes jetons. Émettre sans
    /// rejouer les mêmes refus ferait de l'OTP la porte de service par laquelle
    /// un compte suspendu rentre — et l'administrateur qui l'a suspendu ne le
    /// saurait jamais.
    ///
    /// Un second chemin d'authentification doit porter les MÊMES gardes que le
    /// premier, sinon il n'ajoute pas une commodité : il annule le premier.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <remarks>
    /// CE QUE CETTE ROUTE EST, ET QU'IL FAUT SAVOIR : une connexion SANS MOT DE
    /// PASSE. Qui reçoit le code entre. La sécurité du compte devient donc celle du
    /// canal — boîte e-mail ou carte SIM. C'est le modèle assumé d'un OTP de
    /// connexion, et c'est pour cela que le plafond de cinq tentatives, le code
    /// unique vivant et l'expiration à dix minutes ne sont pas décoratifs.
    /// <para>
    /// CE QU'ELLE NE FAIT PAS : le verrouillage. Un mauvais code consomme une des
    /// cinq tentatives DU DÉFI et rien de plus — il n'incrémente pas le compteur
    /// d'échecs du compte, contrairement à un mot de passe faux. Demander cinquante
    /// défis successifs coûte cinquante SMS et ne verrouille rien. La limite est
    /// aujourd'hui portée par le limiteur `otp` de la passerelle, donc par IP :
    /// c'est une protection de débit, pas une protection de compte.
    /// </para>
    /// </remarks>
    private async Task<Result<OtpVerificationDto>> OuvrirLaSessionAsync(
        MfaChallenge challenge, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(new UserId(challenge.UserId), cancellationToken);

        // Le défi référence un compte qui n'existe plus. Ce n'est pas censé arriver ;
        // si cela arrive, ce n'est certainement pas le moment d'émettre des jetons.
        if (user is null || user.Status == UserStatus.Deleted)
        {
            return Result.Failure<OtpVerificationDto>(CompteInaccessible);
        }

        if (user.IsLockedOut(DateTime.UtcNow)
            || user.Status == UserStatus.Suspended
            || user.Status == UserStatus.PendingVerification)
        {
            return Result.Failure<OtpVerificationDto>(CompteInaccessible);
        }

        user.RegisterSuccessfulLogin();

        // ═════════════════════════════════════════════════════════════════════
        // `amr` DIT `otp` SEUL, PAS `pwd` NI `mfa`.
        //
        // Aucun mot de passe n'a été saisi sur ce chemin, et un seul facteur a
        // joué. Écrire `mfa` ferait croire à un client qui exige un facteur
        // multiple avant un virement qu'il l'a obtenu.
        //
        // ET CE CHEMIN A FORCÉ À DURCIR LE STEP-UP DU DÉPÔT ENTIER.
        //
        // `StepUpAuthentication.HasRecentAuthentication` ne lisait QUE `auth_time`,
        // alors que son propre encadré annonçait « ce compte a-t-il saisi son MOT DE
        // PASSE il y a moins de cinq minutes ». L'écart était sans conséquence tant
        // que tout jeton naissait d'un mot de passe. Ce chemin-ci est le premier qui
        // n'en exige aucun : sans correction, qui reçoit un SMS obtenait aussitôt un
        // jeton « fraîchement authentifié » et franchissait les six gardes
        // sensibles du dépôt — virement, compte bancaire, transfert de propriété
        // vendeur, mouvements de stock.
        //
        // Le prédicat exige désormais `pwd` dans l'`amr`. Un jeton issu d'ici est
        // donc une session valide et complète, mais PAS une session autorisée aux
        // gestes sensibles : l'utilisateur devra passer par
        // `POST /auth/reauthenticate`, qui rejoue le mot de passe. Une carte SIM ne
        // doit pas suffire à vider un portefeuille.
        // ═════════════════════════════════════════════════════════════════════
        var session = new AuthenticationSnapshot(DateTime.UtcNow, AuthenticationSnapshot.OneTimeCode);

        var tokens = await _tokenIssuer.IssueAsync(user, session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OtpVerificationDto(true, challenge.Channel, tokens));
    }
}
