using HBA.Communication.Notifications.Application.Abstractions;

namespace HBA.Communication.Notifications.Infrastructure.Email;

/// <summary>
/// Construit les liens des e-mails de compte à partir de
/// <see cref="EmailOptions.AppBaseUrl"/> .
/// </summary>
public sealed class AccountLinkBuilder : IAccountLinkBuilder
{
    private readonly EmailOptions _options;

    public AccountLinkBuilder(EmailOptions options) => _options = options;

    public string EmailVerification(Guid userId, string token)
        => _options.Link($"verifier-email?uid={Uri.EscapeDataString(userId.ToString())}&token={Uri.EscapeDataString(token)}");

    public string PasswordReset(string email, string token)
        => _options.Link($"reinitialiser-mot-de-passe?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}");

    /// <summary>SEUL LE JETON VOYAGE — NI VENDEUR, NI ADRESSE DANS L'URL.</summary>
    public string SellerInvitation(string token)
        => _options.Link($"rejoindre-equipe?token={Uri.EscapeDataString(token)}");
}
