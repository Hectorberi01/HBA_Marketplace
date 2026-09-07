namespace HBA.Gateway.Api.Options;

/// <summary>VÉRIFICATION DE RÉVOCATION DES JETONS — RÉGLAGES (ISSUE-022, décision D27).</summary>
public sealed class TokenRevocationOptions
{
    public const string SectionName = "TokenRevocation";

    /// <summary>NE PAS METTRE À `false` POUR « DÉBOGUER ».</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Durée de mémorisation d'un verdict rendu par identity.</summary>
    public int CacheSeconds { get; init; } = 30;

    /// <summary>Durée de mémorisation d'un ÉCHEC (identity injoignable).</summary>
    public int FailOpenCacheSeconds { get; init; } = 5;

    /// <summary>Délai d'attente de l'appel à identity.</summary>
    public int TimeoutMilliseconds { get; init; } = 1500;
}
