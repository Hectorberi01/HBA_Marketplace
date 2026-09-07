using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.RegisterUser;

/// <summary>Inscrit un nouvel utilisateur.</summary>
/// <param name="CreatedByAdmin">
/// L'inscription vient-elle de la console d'administration, ou du public ?
/// </param>
public sealed record RegisterUserCommand(
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Password,
    bool CreatedByAdmin = false) : ICommand<Guid>;
