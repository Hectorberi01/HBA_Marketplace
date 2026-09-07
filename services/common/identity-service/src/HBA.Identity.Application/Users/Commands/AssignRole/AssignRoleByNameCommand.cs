using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Roles;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users.Commands.AssignRole;

/// <summary>ATTRIBUE UN RÔLE DÉSIGNÉ PAR SON NOM.</summary>
public sealed record AssignRoleByNameCommand(Guid UserId, string RoleName) : ICommand;

internal sealed class AssignRoleByNameCommandHandler : ICommandHandler<AssignRoleByNameCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public AssignRoleByNameCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AssignRoleByNameCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound(
                "identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        var role = await _roleRepository.GetByNameAsync(command.RoleName, cancellationToken);
        if (role is null)
        {
            return Result.Failure(Error.NotFound(
                "identity.role.not_found", $"Rôle « {command.RoleName} » introuvable."));
        }

        // AssignRole est IDEMPOTENTE côté agrégat : un rôle déjà porté n'est pas
        // ajouté deux fois et ne lève pas d'événement.
        var result = user.AssignRole(role.Id.Value);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
