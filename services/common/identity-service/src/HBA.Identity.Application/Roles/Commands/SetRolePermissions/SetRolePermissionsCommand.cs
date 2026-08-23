using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Roles.Commands.SetRolePermissions;

/// <summary>Remplace l'ensemble des permissions d'un rôle.</summary>
public sealed record SetRolePermissionsCommand(Guid RoleId, IReadOnlyList<string> Permissions) : ICommand;
