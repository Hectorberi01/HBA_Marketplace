using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.MarkEmailVerified;

/// <summary>Un administrateur atteste que l'adresse e-mail appartient bien au titulaire.</summary>
public sealed record MarkEmailVerifiedCommand(Guid UserId) : ICommand;
