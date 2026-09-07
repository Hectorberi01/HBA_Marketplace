using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.ApproveUser;

/// <summary>
/// Validation d'un compte par un administrateur : il passe de « en attente » à «
/// actif » et peut désormais se connecter.
/// </summary>
public sealed record ApproveUserCommand(Guid UserId) : ICommand;
