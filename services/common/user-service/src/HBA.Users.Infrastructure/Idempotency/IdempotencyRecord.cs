using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Users.Infrastructure.Persistence;
using HBA.Users.Infrastructure.Persistence.Outbox;
using HBA.Users.Infrastructure.Persistence.Inbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Retry;
using HBA.Users.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Idempotency`.

namespace HBA.Users.Infrastructure.Idempotency;

/// <summary>
/// Résultat mémorisé d'une requête portant un en-tête <c> Idempotency-Key</c> (§5).
/// </summary>
public sealed class IdempotencyRecord : IEnregistrementDIdempotence
{
    /// <summary>Valeur de l'en-tête <c>Idempotency-Key</c> fournie par le client.</summary>
    public string Key { get; init; } = default!;

    /// <summary>Utilisateur authentifié, ou chaîne vide pour un appel public.</summary>
    public string Scope { get; init; } = default!;

    /// <summary>Méthode et chemin, ex. `POST /api/v1/food/orders/checkout`.</summary>
    public string Endpoint { get; init; } = default!;

    /// <summary>Empreinte SHA-256 du corps de la requête d'origine.</summary>
    public string RequestFingerprint { get; init; } = default!;

    /// <summary>Status HTTP rendu la première fois.</summary>
    public int StatusCode { get; set; }

    /// <summary>Corps de réponse mémorisé, rejoué tel quel aux tentatives suivantes.</summary>
    public string? ResponseBody { get; set; }

    /// <summary>Null tant que la première exécution est en cours.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Date d'expiration. Une clé d'idempotence n'a pas vocation à être éternelle :
    /// 24 h couvre largement les reprises réseau et les files d'attente d'un client
    /// hors ligne, sans faire grossir la table indéfiniment.
    /// </summary>
    public DateTime ExpiresAtUtc { get; init; } = DateTime.UtcNow.AddHours(24);
}
