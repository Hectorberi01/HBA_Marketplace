using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Roles.Commands.CreateRole;

/// <summary>Crée un rôle avec ses permissions.</summary>
public sealed record CreateRoleCommand(
    string Name,
    string? Description = null,
    IReadOnlyList<string>? Permissions = null) : ICommand<Guid>;
