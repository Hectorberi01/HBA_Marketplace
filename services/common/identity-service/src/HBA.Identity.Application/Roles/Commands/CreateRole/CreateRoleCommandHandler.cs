using HBA.Shared.Application.Abstractions;
using HBA.Identity.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Domain.Roles;

namespace HBA.Identity.Application.Roles.Commands.CreateRole;

/// <summary>Vérifie l'unicité du nom puis crée le rôle.</summary>
internal sealed class CreateRoleCommandHandler : ICommandHandler<CreateRoleCommand, Guid>
{
    private readonly IRoleRepository _roleRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public CreateRoleCommandHandler(IRoleRepository roleRepository, IIdentityUnitOfWork unitOfWork)
    {
        _roleRepository = roleRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        if (await _roleRepository.NameExistsAsync(command.Name.Trim(), cancellationToken))
        {
            return Error.Conflict("identity.role.name_taken", $"Le rôle « {command.Name} » existe déjà.");
        }

        var result = Role.Create(command.Name, command.Description, isSystem: false, command.Permissions);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _roleRepository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result.Value.Id.Value;
    }
}
