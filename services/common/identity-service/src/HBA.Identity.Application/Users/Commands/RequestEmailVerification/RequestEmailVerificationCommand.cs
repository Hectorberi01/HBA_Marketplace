using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.RequestEmailVerification;

/// <summary>
/// (Ré)émet un code de vérification e-mail à 6 chiffres pour un compte EXISTANT et
/// l'envoie par e-mail (via l'outbox).
/// </summary>
public sealed record RequestEmailVerificationCommand(Guid UserId) : ICommand;
