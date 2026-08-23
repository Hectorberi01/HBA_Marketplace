using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Commands.ApproveUser;

/// <summary>Active le compte. Idempotent : approuver un compte déjà actif ne lève pas.</summary>
internal sealed class ApproveUserCommandHandler : ICommandHandler<ApproveUserCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public ApproveUserCommandHandler(IUserRepository userRepository, IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ApproveUserCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        // `Approve()` accepte aussi un compte suspendu : c'est le retour en arrière
        // d'un refus. Volontaire — un administrateur qui s'est trompé doit pouvoir
        // se dédire sans passer par une seconde commande.
        var result = user.Approve();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
