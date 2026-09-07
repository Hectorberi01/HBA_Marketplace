using HBA.Identity.Application.Abstractions;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Identity.Domain.Users;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;
using HBA.Shared.Application.Abstractions;

namespace HBA.Identity.Application.Users.Commands.RequestEmailVerification;

/// <summary>
/// Renvoie un code de vérification à partir de l'ADRESSE, pour un compte qui n'est
/// pas connecté.
/// </summary>
public sealed record RequestEmailVerificationByEmailCommand(string Email) : ICommand;

internal sealed class RequestEmailVerificationByEmailCommandHandler
    : ICommandHandler<RequestEmailVerificationByEmailCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IAuthTokenSettings _tokenSettings;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IIdentityUnitOfWork _unitOfWork;

    /// <summary>LE CODE NE TRAVERSE PLUS LE BUS EN CLAIR.</summary>
    private readonly ISecretProtector _protecteur;

    public RequestEmailVerificationByEmailCommandHandler(
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

    public async Task<Result> Handle(
        RequestEmailVerificationByEmailCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            // Anti-énumération : exactement la même réponse qu'un envoi réussi.
            return Result.Success();
        }

        // Un compte déjà vérifié n'a pas de code à recevoir.
        if (user.EmailVerified)
        {
            return Result.Success();
        }

        var (code, codeHash) = _tokenGenerator.GenerateNumericCode();
        var expiresOnUtc = DateTime.UtcNow.Add(_tokenSettings.EmailVerificationLifetime);

        var begin = user.BeginEmailVerification(codeHash, expiresOnUtc);

        if (begin.IsFailure)
        {
            // Refus du domaine — par exemple une demande trop rapprochée.
            return Result.Success();
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
