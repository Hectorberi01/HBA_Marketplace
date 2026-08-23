using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.SuspendUser;

/// <summary>Suspend un compte (révoque ses sessions).</summary>
public sealed record SuspendUserCommand(Guid UserId) : ICommand;
