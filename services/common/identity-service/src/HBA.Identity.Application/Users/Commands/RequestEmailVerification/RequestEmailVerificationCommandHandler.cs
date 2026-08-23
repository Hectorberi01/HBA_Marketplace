using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Identity.Domain.Users;
using HBA.Shared.Application.Abstractions;

namespace HBA.Identity.Application.Users.Commands.RequestEmailVerification;

/// <summary>
/// Charge le compte, génère un code numérique (seul le hash est stocké), l'inscrit
/// comme code de vérification en attente, puis publie l'event d'envoi d'e-mail.
/// N'active pas le compte et ne touche pas à <c>EmailVerified</c>.
/// </summary>
internal sealed class RequestEmailVerificationCommandHandler : ICommandHandler<RequestEmailVerificationCommand>
{
    /// <summary>
    /// LE CODE NE TRAVERSE PLUS LE BUS EN CLAIR.
    ///
    /// Il partait tel quel dans l'événement, donc dans
    /// `identity.outbox_messages.Content` — table jamais purgée — puis sur un
    /// topic Kafka retenu sept jours. Une lecture de l'un ou l'autre valait
    /// prise de compte. Il est désormais chiffré ici, et déchiffré par le seul
    /// service qui doit l'envoyer.
    /// </summary>
    private readonly ISecretProtector _protecteur;

    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IAuthTokenSettings _tokenSettings;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public RequestEmailVerificationCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IAuthTokenSettings tokenSettings,
        IIntegrationEventPublisher publisher,
        IIdentityUnitOfWork unitOfWork,
        ISecretProtector protecteur)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _tokenSettings = tokenSettings;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
        _protecteur = protecteur;
    }

    public async Task<Result> Handle(RequestEmailVerificationCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        var (code, codeHash) = _tokenGenerator.GenerateNumericCode();
        var expiresOnUtc = DateTime.UtcNow.Add(_tokenSettings.EmailVerificationLifetime);

        var begin = user.BeginEmailVerification(codeHash, expiresOnUtc);
        if (begin.IsFailure)
        {
            return begin;
        }

        await _publisher.PublishAsync(
            new EmailVerificationRequestedIntegrationEvent
            {
                UserId = user.Id.Value,
                Email = user.Email.Value,
                FirstName = user.FirstName,
                ProtectedVerificationToken = _protecteur.Protect(code)
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
