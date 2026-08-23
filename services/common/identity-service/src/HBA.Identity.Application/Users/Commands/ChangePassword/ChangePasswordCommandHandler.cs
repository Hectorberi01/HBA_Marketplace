using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Commands.ChangePassword;

/// <summary>Vérifie le mot de passe actuel, hash le nouveau et le confie à l'agrégat.</summary>
internal sealed class ChangePasswordCommandHandler : ICommandHandler<ChangePasswordCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public ChangePasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        if (!_passwordHasher.Verify(user.PasswordHash, command.CurrentPassword))
        {
            return Result.Failure(Error.Unauthorized("identity.auth.invalid_credentials", "Mot de passe actuel incorrect."));
        }

        var result = user.ChangePassword(_passwordHasher.Hash(command.NewPassword));
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
