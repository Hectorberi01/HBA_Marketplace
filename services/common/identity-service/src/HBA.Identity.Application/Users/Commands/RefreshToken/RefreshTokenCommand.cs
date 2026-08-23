using HBA.Shared.Application.Messaging;
using HBA.Identity.Application.Models;

namespace HBA.Identity.Application.Users.Commands.RefreshToken;

/// <summary>Échange un refresh token valide contre une nouvelle paire de jetons (rotation).</summary>
public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthTokens>;
