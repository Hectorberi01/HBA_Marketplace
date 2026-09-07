namespace HBA.Shared.Infrastructure.Idempotency;

/// <summary>Issue de la tentative de réservation d'une clé d'idempotence.</summary>
public enum IdempotencyOutcome
{
    /// <summary>Clé neuve : l'appelant doit exécuter le traitement.</summary>
    Proceed = 0,

    /// <summary>Requête déjà terminée : rejouer la réponse mémorisée.</summary>
    Replay = 1,

    /// <summary>Première exécution encore en cours : rendre 409 CONFLICT.</summary>
    InFlight = 2,

    /// <summary>Même clé, corps différent : rendre 409 CONFLICT.</summary>
    Mismatch = 3
}

/// <summary>Réservation d'une clé et réponse mémorisée si elle existe.</summary>
public sealed record IdempotencyReservation(
    IdempotencyOutcome Outcome,
    int StatusCode = 0,
    string? ResponseBody = null);

/// <summary>Mémorisation des requêtes idempotentes (§5).</summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Réserve la clé pour cet utilisateur et cet endpoint, ou dit quoi faire si
    /// elle existe déjà.
    /// </summary>
    Task<IdempotencyReservation> TryBeginAsync(
        string key,
        string scope,
        string endpoint,
        string requestFingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>Mémorise la réponse rendue, pour les tentatives suivantes.</summary>
    Task CompleteAsync(
        string key,
        string scope,
        string endpoint,
        int statusCode,
        string? responseBody,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Libère une clé dont le traitement a échoué, pour que le client puisse
    /// réessayer.
    /// </summary>
    Task AbandonAsync(
        string key,
        string scope,
        string endpoint,
        CancellationToken cancellationToken = default);
}
