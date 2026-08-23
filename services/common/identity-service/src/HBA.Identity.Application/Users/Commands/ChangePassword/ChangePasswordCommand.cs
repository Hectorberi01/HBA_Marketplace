using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.ChangePassword;

/// <summary>Change le mot de passe après vérification du mot de passe actuel.</summary>
public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : ICommand;
