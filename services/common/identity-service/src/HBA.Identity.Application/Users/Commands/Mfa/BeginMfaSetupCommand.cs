using HBA.Shared.Application.Messaging;
using HBA.Identity.Application.Models;

namespace HBA.Identity.Application.Users.Commands.Mfa;

/// <summary>Initie l'activation MFA : génère un secret TOTP et l'URI otpauth (QR code).</summary>
public sealed record BeginMfaSetupCommand(Guid UserId) : ICommand<MfaSetupResponse>;
