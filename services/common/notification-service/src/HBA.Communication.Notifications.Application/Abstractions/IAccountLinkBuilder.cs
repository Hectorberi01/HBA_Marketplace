namespace HBA.Communication.Notifications.Application.Abstractions;

/// <summary>Fabrique les liens cliquables des e-mails de compte.</summary>
public interface IAccountLinkBuilder
{
    /// <summary>Lien de confirmation d'adresse e-mail.</summary>
    string EmailVerification(Guid userId, string token);

    /// <summary>Lien de choix d'un nouveau mot de passe.</summary>
    string PasswordReset(string email, string token);

    /// <summary>Lien d'acceptation d'une invitation à rejoindre l'équipe d'un vendeur.</summary>
    string SellerInvitation(string token);
}
