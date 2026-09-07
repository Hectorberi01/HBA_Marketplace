using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Observability;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Identity.Domain.Users;
using HBA.Shared.Application.Abstractions;

namespace HBA.Identity.Application.Users.Commands.PasswordReset;

internal sealed class RequestPasswordResetCommandHandler : ICommandHandler<RequestPasswordResetCommand>
{
    /// <summary>LE CODE NE TRAVERSE PLUS LE BUS EN CLAIR.</summary>
    private readonly ISecretProtector _protecteur;

    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly ISecurityMetrics _security;

    public RequestPasswordResetCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IIntegrationEventPublisher publisher,
        IIdentityUnitOfWork unitOfWork,
        ISecurityMetrics security,
        ISecretProtector protecteur)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
        _security = security;
        _protecteur = protecteur;
    }

    public async Task<Result> Handle(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            // Pas d'énumération de comptes : succès silencieux.
            return Result.Success();
        }

        // CODE numérique à 6 chiffres (comme la vérification e-mail) : bien plus
        // simple à saisir dans l'app mobile qu'un lien à copier.
        var (raw, hash) = _tokenGenerator.GenerateNumericCode();
        var begin = user.BeginPasswordReset(hash, DateTime.UtcNow.AddHours(1));
        if (begin.IsFailure)
        {
            return Result.Failure(begin.Error);
        }

        // LE JETON SORT PAR ICI, ET SEULEMENT PAR ICI.
        await _publisher.PublishAsync(
            new PasswordResetRequestedIntegrationEvent
            {
                UserId = user.Id.Value,
                Email = user.Email.Value,
                FirstName = user.FirstName,
                ProtectedResetToken = _protecteur.Protect(raw),
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _security.PasswordReset();
        return Result.Success();
    }
}
