using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.PasswordReset;

/// <summary>Demande de réinitialisation du mot de passe.</summary>
public sealed record RequestPasswordResetCommand(string Email) : ICommand;
