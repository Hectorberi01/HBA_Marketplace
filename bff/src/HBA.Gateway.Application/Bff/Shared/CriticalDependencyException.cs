namespace HBA.Gateway.Application.Bff.Shared;

/// <summary>
/// Une dépendance <see cref="DependencyCriticality.Critical"/> a échoué : l'écran
/// ne peut pas être rendu.
/// </summary>
public sealed class CriticalDependencyException : Exception
{
    public CriticalDependencyException(string source, int statusCode, string? reason)
        : base($"Dépendance critique indisponible : {source}")
    {
        Source = source;
        StatusCode = statusCode;
        Reason = reason;
    }

    /// <summary>Nom logique de la dépendance.</summary>
    public new string Source { get; }

    public int StatusCode { get; }

    public string? Reason { get; }
}

/// <summary>La ressource demandée n'existe pas — traduit en 404.</summary>
public sealed class BffResourceNotFoundException : Exception
{
    public BffResourceNotFoundException(string resource, object id)
        : base($"{resource} introuvable : {id}")
    {
        Resource = resource;
    }

    public string Resource { get; }
}
