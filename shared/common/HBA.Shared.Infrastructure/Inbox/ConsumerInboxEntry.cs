namespace HBA.Shared.Infrastructure.Inbox;

/// <summary>
/// Trace d'un événement Kafka déjà traité par un consumer donné (§19.5).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI LA CLÉ EST (eventId, consumerName) ET NON eventId SEUL.
///
/// Le §19.5 présente `event_id` comme clé primaire de `consumer_inbox`. Pris au
/// pied de la lettre, cela signifierait qu'un événement traité par le Notification
/// Service serait considéré comme déjà traité par le Wallet Service — le premier
/// consumer servi ferait taire tous les autres.
///
/// Le §11.3 lève l'ambiguïté en exigeant `unique(event_id, consumer_name)` : c'est
/// bien le COUPLE qui identifie un traitement. La colonne `consumer_name` du §19.5
/// n'a d'ailleurs aucun sens si elle ne participe pas à la clé.
///
/// La table est locale au service, dans son propre schéma : l'insertion de cette
/// ligne et la transaction métier doivent être ATOMIQUES. C'est tout l'intérêt du
/// dispositif — si le service redémarre entre les deux, le rejeu Kafka retrouve
/// soit les deux, soit aucun, jamais l'effet métier sans sa trace.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ConsumerInboxEntry
{
    /// <summary>`eventId` de l'enveloppe Kafka (§19.1). UUID v7 côté producteur.</summary>
    public Guid EventId { get; init; }

    /// <summary>Nom du consumer / handler, ex. `wallet-service.payment-succeeded`.</summary>
    public string ConsumerName { get; init; } = default!;

    /// <summary>Type d'événement, conservé pour le diagnostic et les rejeux ciblés.</summary>
    public string EventType { get; init; } = default!;

    /// <summary>Date du traitement réussi.</summary>
    public DateTime ProcessedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Corrélation distribuée, pour relier cette consommation au flux d'origine.</summary>
    public string? CorrelationId { get; init; }
}
