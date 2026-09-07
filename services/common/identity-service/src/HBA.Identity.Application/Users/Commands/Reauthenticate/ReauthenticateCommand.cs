using HBA.Identity.Application.Models;
using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.Reauthenticate;

/// <summary>
/// Rejoue la preuve d'identité d'une session DÉJÀ ouverte, et rend une paire de
/// jetons dont l'<c>auth_time</c> est neuf.
/// </summary>
/// <param name="UserId">Vient du JETON, jamais du corps de la requête.</param>
public sealed record ReauthenticateCommand(Guid UserId, string Password) : ICommand<AuthTokens>;
