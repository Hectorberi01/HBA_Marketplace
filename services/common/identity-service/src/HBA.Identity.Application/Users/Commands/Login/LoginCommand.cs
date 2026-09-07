using HBA.Shared.Application.Messaging;
using HBA.Identity.Application.Models;

namespace HBA.Identity.Application.Users.Commands.Login;

/// <summary>Authentifie un utilisateur.</summary>
/// <param name="RequiredRoles">Rôles admis sur la surface appelante.</param>
public sealed record LoginCommand(
    string Email,
    string Password,
    string? MfaCode = null,
    IReadOnlyCollection<string>? RequiredRoles = null) : ICommand<LoginResponse>;
