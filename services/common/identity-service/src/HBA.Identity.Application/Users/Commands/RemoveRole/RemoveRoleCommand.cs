using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.RemoveRole;

/// <summary>Retire un rôle d'un utilisateur.</summary>
public sealed record RemoveRoleCommand(Guid UserId, Guid RoleId) : ICommand;
