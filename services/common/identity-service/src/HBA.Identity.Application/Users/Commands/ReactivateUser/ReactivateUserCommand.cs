using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.ReactivateUser;

/// <summary>Réactive un compte suspendu (e-mail déjà vérifié requis).</summary>
public sealed record ReactivateUserCommand(Guid UserId) : ICommand;
