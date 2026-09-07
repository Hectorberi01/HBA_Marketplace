using System.ComponentModel.DataAnnotations;

namespace HBA.Gateway.Api.Options;

/// <summary>Politiques de limitation de débit applicatives.</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Filet global appliqué à TOUTES les requêtes, politique nommée comprise.</summary>
    public RateLimitPolicyOptions Global { get; init; } = new() { PermitLimit = 300, WindowSeconds = 60 };

    /// <summary>
    /// Connexion, inscription, rafraîchissement, réinitialisation de mot de passe.
    /// </summary>
    public RateLimitPolicyOptions Auth { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>Envoi et vérification de code à usage unique.</summary>
    public RateLimitPolicyOptions Otp { get; init; } = new() { PermitLimit = 5, WindowSeconds = 300 };

    /// <summary>Lectures : catalogue, restaurants, avis, médias.</summary>
    public RateLimitPolicyOptions Read { get; init; } = new() { PermitLimit = 200, WindowSeconds = 60 };

    /// <summary>Écritures : panier, commandes, paiements.</summary>
    public RateLimitPolicyOptions Write { get; init; } = new() { PermitLimit = 60, WindowSeconds = 60 };
}

/// <summary>Fenêtre fixe : <c>PermitLimit</c> requêtes par <c>WindowSeconds</c> secondes.</summary>
public sealed class RateLimitPolicyOptions
{
    [Range(1, 100_000)] public int PermitLimit { get; init; } = 100;

    [Range(1, 3600)] public int WindowSeconds { get; init; } = 60;
}
