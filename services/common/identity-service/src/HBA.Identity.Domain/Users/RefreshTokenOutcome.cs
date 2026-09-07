namespace HBA.Identity.Domain.Users;

/// <summary>Issue de la présentation d'un refresh token.</summary>
public enum RefreshTokenOutcome
{
    /// <summary>Le hash présenté n'existe pas sur ce compte.</summary>
    Unknown = 0,

    /// <summary>Jeton valide : il vient d'être révoqué au profit d'un nouveau.</summary>
    Rotated = 1,

    /// <summary>Jeton arrivé au terme de sa validité, jamais utilisé depuis.</summary>
    Expired = 2,

    /// <summary>
    /// Jeton DÉJÀ CONSOMMÉ. Deux porteurs pour une même chaîne : quelqu'un détient
    /// une copie.
    /// </summary>
    Replayed = 3
}
