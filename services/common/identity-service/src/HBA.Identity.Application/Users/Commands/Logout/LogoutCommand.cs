using HBA.Shared.Application.Messaging;

namespace HBA.Identity.Application.Users.Commands.Logout;

/// <summary>Révoque un refresh token (déconnexion d'un appareil).</summary>
public sealed record LogoutCommand(Guid UserId, string RefreshToken) : ICommand;
