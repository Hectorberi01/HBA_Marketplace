using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Users;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Identity.Application.Users.Commands.ConfirmEmail;

/// <summary>Vérifie une adresse à partir de l'ADRESSE et du code à six chiffres.</summary>
public sealed record ConfirmEmailByEmailCommand(string Email, string Code) : ICommand;

internal sealed class ConfirmEmailByEmailCommandHandler : ICommandHandler<ConfirmEmailByEmailCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public ConfirmEmailByEmailCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ConfirmEmailByEmailCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        // MÊME ERREUR POUR « COMPTE INCONNU » ET « CODE FAUX ».
        var invalid = Error.Validation(
            "identity.email.invalid_code",
            "Ce code n'est pas valide, ou il a expiré.");

        if (user is null)
        {
            return Result.Failure(invalid);
        }

        var codeHash = _tokenGenerator.Hash(command.Code);
        // `nowUtc` est un PARAMÈTRE du domaine, pas un `DateTime.UtcNow` lu à
        // l'intérieur : c'est ce qui rend l'expiration testable.
        var result = user.ConfirmEmail(codeHash, DateTime.UtcNow);

        if (result.IsFailure)
        {
            // On enregistre quand même : le domaine compte les tentatives, et ce
            // compteur est ce qui borne la force brute.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure(invalid);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
