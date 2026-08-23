using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.UpdateProfile;

/// <summary>Met à jour le prénom, le nom et le numéro de téléphone d'un compte.</summary>
public sealed record UpdateUserProfileCommand(Guid UserId, string FirstName, string LastName, string PhoneNumber) : ICommand;
