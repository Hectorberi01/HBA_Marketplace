namespace HBA.Gateway.Application.Bff.Shared;

/// <summary>Une réponse BFF et les dégradations qu'elle a subies.</summary>
public sealed record BffEnvelope<T>(T Data, IReadOnlyList<BffWarning> Warnings)
{
    public static BffEnvelope<T> Complete(T data) => new(data, []);

    public bool IsPartial => Warnings.Count > 0;
}

/// <summary>Une dépendance dégradée, telle qu'exposée au client.</summary>
/// <param name="Source">
/// Nom LOGIQUE de la dépendance (« Engagement »), jamais un hôte interne.
/// </param>
/// <param name="Code">Code stable et fermé : le client s'y branche pour choisir un message.</param>
public sealed record BffWarning(string Source, string Code)
{
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";

    /// <summary>
    /// La dépendance existe mais n'est pas configurée pour rendre ce service —
    /// moteur tarifaire absent, route non exposée.
    /// </summary>
    public const string NotConfigured = "NOT_CONFIGURED";

    public static BffWarning Unavailable(string source) => new(source, ServiceUnavailable);

    public static BffWarning NotConfiguredFor(string source) => new(source, NotConfigured);
}
