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

/// <summary>Émet un code à usage unique et le transmet par le canal demandé.</summary>
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
            // de révéler l'absence.
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

        // ICI, LE CODE ÉTAIT JETÉ (ISSUE-062).
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
/// Les jetons de session, ou <c> null</c> si la vérification n'a pas abouti.
/// </param>
public sealed record OtpVerificationDto(bool Verified, string Channel, AuthTokens? Tokens);

internal sealed class VerifyOtpCommandHandler : ICommandHandler<VerifyOtpCommand, OtpVerificationDto>
{
    /// <summary>
    /// Le refus rendu quand le code est bon mais que le COMPTE ne peut pas entrer.
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
            // Même réponse qu'un mauvais code, et même code d'erreur : distinguer «
            // défi inconnu » de « code faux » dirait à un attaquant que
            // l'identifiant qu'il a tiré au hasard n'existe pas — donc, par
            // élimination, lesquels existent.
            return Result.Failure<OtpVerificationDto>(Error.Validation(
                "identity.mfa.invalid_code", "Code invalide ou expiré."));
        }

        var code = (command.Code ?? string.Empty).Trim();
        var outcome = challenge.Verify(hash => _tokens.Hash(code) == hash);

        // Sauvegardé DANS TOUS LES CAS : c'est l'incrément du compteur de
        // tentatives qui protège du balayage.
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

    /// <summary>Le code est bon : reste à savoir si le COMPTE a le droit d'entrer.</summary>
    private async Task<Result<OtpVerificationDto>> OuvrirLaSessionAsync(
        MfaChallenge challenge, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(new UserId(challenge.UserId), cancellationToken);

        // Le défi référence un compte qui n'existe plus.
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

        // `amr` DIT `otp` SEUL, PAS `pwd` NI `mfa`.
        var session = new AuthenticationSnapshot(DateTime.UtcNow, AuthenticationSnapshot.OneTimeCode);

        var tokens = await _tokenIssuer.IssueAsync(user, session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new OtpVerificationDto(true, challenge.Channel, tokens));
    }
}
