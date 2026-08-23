namespace HBA.Shared.Infrastructure.Inbox;

/// <summary>
/// Garde d'idempotence côté consumer (§19.5).
///
/// La séquence imposée par le cahier des charges est : vérifier l'inbox, exécuter
/// la transaction métier SI l'événement est neuf, insérer la trace, committer, puis
/// acquitter Kafka. L'ordre compte — acquitter avant de committer perd le message
/// au premier redémarrage, committer sans trace le rejoue.
/// </summary>
public interface IConsumerInbox
{
    /// <summary>Vrai si ce couple (événement, consumer) a déjà été traité avec succès.</summary>
    Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enregistre la trace de traitement DANS la transaction métier en cours.
    /// L'implémentation ne doit pas ouvrir sa propre transaction ni committer :
    /// c'est l'atomicité avec l'effet métier qui fait toute la valeur du dispositif.
    /// </summary>
    Task MarkProcessedAsync(
        Guid eventId,
        string consumerName,
        string eventType,
        string? correlationId,
        CancellationToken cancellationToken = default);
}
