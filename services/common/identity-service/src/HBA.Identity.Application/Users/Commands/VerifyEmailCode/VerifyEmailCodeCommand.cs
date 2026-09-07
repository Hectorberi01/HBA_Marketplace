using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.VerifyEmailCode;

/// <summary>Vérifie un code e-mail à 6 chiffres et marque l'adresse comme vérifiée.</summary>
public sealed record VerifyEmailCodeCommand(Guid UserId, string Code) : ICommand;
