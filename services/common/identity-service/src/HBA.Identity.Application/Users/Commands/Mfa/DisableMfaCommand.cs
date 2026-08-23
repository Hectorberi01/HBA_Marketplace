using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.Mfa;

/// <summary>Désactive la MFA après vérification d'un code TOTP valide.</summary>
public sealed record DisableMfaCommand(Guid UserId, string Code) : ICommand;
