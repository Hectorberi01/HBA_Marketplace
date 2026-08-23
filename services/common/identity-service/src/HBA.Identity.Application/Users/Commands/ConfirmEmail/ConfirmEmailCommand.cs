using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.ConfirmEmail;

/// <summary>Confirme l'e-mail d'un compte à partir du jeton reçu par lien.</summary>
public sealed record ConfirmEmailCommand(Guid UserId, string Token) : ICommand;
