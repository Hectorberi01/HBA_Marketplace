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

/// <summary>
/// Mémorisation des requêtes idempotentes (§5).
///
/// L'implémentation vit dans chaque service, sur sa propre base : un store partagé
/// serait une base commune à tous les services, ce que le §9 interdit — et un point
/// de panne unique sur le chemin de tous les paiements.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Réserve la clé pour cet utilisateur et cet endpoint, ou dit quoi faire si
    /// elle existe déjà. L'insertion doit s'appuyer sur la contrainte d'unicité de
    /// la base et non sur un « SELECT puis INSERT » : deux requêtes simultanées
    /// passeraient toutes les deux le SELECT, et l'idempotence n'aurait servi à rien
    /// précisément dans le cas qu'elle est censée couvrir.
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
    /// réessayer. Sans cela, une panne transitoire condamnerait la clé pendant
    /// 24 h et le client recevrait des 409 sans comprendre pourquoi.
    /// </summary>
    Task AbandonAsync(
        string key,
        string scope,
        string endpoint,
        CancellationToken cancellationToken = default);
}
