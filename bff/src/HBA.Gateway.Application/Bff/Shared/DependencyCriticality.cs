namespace HBA.Gateway.Application.Bff.Shared;

/// <summary>Ce qu'il advient d'un écran quand une de ses dépendances tombe.</summary>
public enum DependencyCriticality
{
    /// <summary>Sans elle, l'écran n'a pas de sens : la réponse est un échec (503).</summary>
    Critical,

    /// <summary>L'écran reste utile, mais amputé : réponse 200 AVEC un avertissement.</summary>
    Important,

    /// <summary>Agrément : le champ vaut <c>null</c>, sans avertissement.</summary>
    Optional,
}
