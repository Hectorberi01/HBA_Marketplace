namespace HBA.Identity.Domain.Users;

/// <summary>Statut d'un compte (cf. dossier, User).</summary>
public enum UserStatus
{
    PendingVerification = 0,
    Active = 1,
    Suspended = 2,

    /// <summary>
    /// Compte SUPPRIMÉ à la demande de son titulaire — anonymisé, et définitivement
    /// inutilisable.
    /// </summary>
    Deleted = 3
}
