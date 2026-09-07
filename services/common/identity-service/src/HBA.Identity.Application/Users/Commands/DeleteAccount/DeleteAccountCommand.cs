using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Commands.DeleteAccount;

/// <summary>L'utilisateur supprime SON PROPRE compte.</summary>
public sealed record DeleteAccountCommand(Guid UserId, string Password) : ICommand;

internal sealed class DeleteAccountCommandHandler : ICommandHandler<DeleteAccountCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public DeleteAccountCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", "Compte introuvable."));
        }

        // Déjà supprimé : on ne se plaint pas, il n'y a plus rien à faire.
        if (user.Status == UserStatus.Deleted)
        {
            return Result.Success();
        }

        if (!_passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            return Result.Failure(Error.Unauthorized(
                "identity.account.wrong_password",
                "Mot de passe incorrect. La suppression du compte est définitive : nous devons nous assurer que c'est bien vous."));
        }

        var result = user.Anonymize(DateTime.UtcNow);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
