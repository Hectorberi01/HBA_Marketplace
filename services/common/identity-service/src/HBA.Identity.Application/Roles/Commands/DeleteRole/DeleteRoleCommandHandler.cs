using HBA.Shared.Application.Abstractions;
using HBA.Identity.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Domain.Roles;

namespace HBA.Identity.Application.Roles.Commands.DeleteRole;

/// <summary>Supprime le rôle ; refuse les rôles système.</summary>
internal sealed class DeleteRoleCommandHandler : ICommandHandler<DeleteRoleCommand>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public DeleteRoleCommandHandler(IRoleRepository roleRepository, IIdentityUnitOfWork unitOfWork)
    {
        _roleRepository = roleRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(new RoleId(command.RoleId), cancellationToken);
        if (role is null)
        {
            return Result.Failure(Error.NotFound("identity.role.not_found", $"Rôle {command.RoleId} introuvable."));
        }

        if (role.IsSystem)
        {
            return Result.Failure(Error.Conflict("identity.role.system_protected", "Un rôle système ne peut pas être supprimé."));
        }

        _roleRepository.Remove(role);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
