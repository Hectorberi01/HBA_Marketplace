using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Roles.Commands.DeleteRole;

/// <summary>Supprime un rôle non-système.</summary>
public sealed record DeleteRoleCommand(Guid RoleId) : ICommand;
