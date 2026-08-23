namespace HBA.Communication.Notifications.Application.Abstractions;

/// <summary>
/// Fabrique les liens cliquables des e-mails de compte.
///
/// Pourquoi un port, et pas juste une chaîne de configuration lue dans le handler :
/// l'URL publique de l'application est une préoccupation d'INFRASTRUCTURE (elle change
/// selon l'environnement), alors que « quel lien mettre dans l'e-mail de vérification »
/// est une décision APPLICATIVE. Le port sépare les deux, et rend les handlers testables
/// sans configuration.
/// </summary>
public interface IAccountLinkBuilder
{
    /// <summary>Lien de confirmation d'adresse e-mail.</summary>
    string EmailVerification(Guid userId, string token);

    /// <summary>Lien de choix d'un nouveau mot de passe.</summary>
    string PasswordReset(string email, string token);

    /// <summary>
    /// Lien d'acceptation d'une invitation à rejoindre l'équipe d'un vendeur.
    /// </summary>
    /// <remarks>
    /// IL MÈNE À UNE PAGE, JAMAIS À L'API — même raison que la réinitialisation.
    ///
    /// Les clients de messagerie et les passerelles antivirus PRÉ-CHARGENT les
    /// liens des e-mails pour les analyser. Une URL à effet de bord serait donc
    /// déclenchée avant que l'invité ne clique : l'invitation, qui est à usage
    /// unique, serait consommée par un antivirus — et l'employé trouverait un lien
    /// « déjà utilisé » sans l'avoir jamais ouvert.
    ///
    /// La page demande à l'utilisateur de se connecter (ou de créer son compte),
    /// puis appelle `POST /api/v1/merchants/invitations/accept`.
    /// </remarks>
    string SellerInvitation(string token);
}
