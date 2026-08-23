namespace HBA.Identity.Domain.Users;

/// <summary>
/// Issue de la présentation d'un refresh token.
///
/// Quatre cas et non deux : c'est la distinction entre <see cref="Expired"/> et
/// <see cref="Replayed"/> qui porte toute la valeur de ce type. Les confondre —
/// ce que faisait le code, qui répondait 401 dans les deux cas — revient soit à
/// ignorer un vol, soit à déconnecter de tous ses appareils un utilisateur
/// simplement revenu après un mois.
/// </summary>
public enum RefreshTokenOutcome
{
    /// <summary>Le hash présenté n'existe pas sur ce compte.</summary>
    Unknown = 0,

    /// <summary>Jeton valide : il vient d'être révoqué au profit d'un nouveau.</summary>
    Rotated = 1,

    /// <summary>
    /// Jeton arrivé au terme de sa validité, jamais utilisé depuis. Cas ordinaire :
    /// l'utilisateur revient après une longue absence. Aucune sanction.
    /// </summary>
    Expired = 2,

    /// <summary>
    /// Jeton DÉJÀ CONSOMMÉ. Deux porteurs pour une même chaîne : quelqu'un
    /// détient une copie. Toute la famille de jetons du compte vient d'être
    /// révoquée — voir <c>User.UseRefreshToken</c>.
    /// </summary>
    Replayed = 3
}
