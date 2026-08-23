using HBA.Shared.Application.Abstractions;
using HBA.Identity.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Commands.SuspendUser;

/// <summary>Suspend le compte et révoque ses refresh tokens.</summary>
internal sealed class SuspendUserCommandHandler : ICommandHandler<SuspendUserCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public SuspendUserCommandHandler(IUserRepository userRepository, IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SuspendUserCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        user.Suspend();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
