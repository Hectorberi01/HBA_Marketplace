namespace HBA.Shared.Infrastructure.Idempotency;

/// <summary>
/// Résultat mémorisé d'une requête portant un en-tête <c>Idempotency-Key</c> (§5).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CETTE TABLE EMPÊCHE, CONCRÈTEMENT.
///
/// Un client mobile poste `/api/v1/food/orders/checkout`, le réseau tombe pendant
/// la réponse, l'application réessaie. Sans mémorisation, la seconde requête crée
/// une SECONDE commande et un SECOND paiement. Le client est débité deux fois pour
/// un repas, et aucune trace ne dit que c'était la même intention.
///
/// La clé est (clé d'idempotence, utilisateur, endpoint) et non la clé seule :
/// deux utilisateurs peuvent légitimement générer la même clé, et une même clé
/// rejouée sur un AUTRE endpoint est une erreur du client, pas une reprise.
///
/// L'empreinte de la requête est conservée pour détecter le cas vicieux : même clé,
/// corps différent. Ce n'est pas une reprise, c'est une collision — elle doit
/// rendre 409 CONFLICT, jamais la réponse mémorisée d'une autre requête.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class IdempotencyRecord
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

    /// <summary>
    /// Null tant que la première exécution est en cours. Une seconde requête qui
    /// trouve une ligne non terminée ne doit ni attendre ni exécuter : elle rend
    /// 409, parce que la première n'a pas encore de réponse à rejouer.
    /// </summary>
    public DateTime? CompletedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Date d'expiration. Une clé d'idempotence n'a pas vocation à être éternelle :
    /// 24 h couvre largement les reprises réseau et les files d'attente d'un client
    /// hors ligne, sans faire grossir la table indéfiniment.
    /// </summary>
    public DateTime ExpiresAtUtc { get; init; } = DateTime.UtcNow.AddHours(24);
}
