using HBA.Shared.Infrastructure.Persistence;
using HBA.Deliveries.Infrastructure.Persistence;
using HBA.Deliveries.Infrastructure.Persistence.Inbox;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Retry;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Outbox`.

namespace HBA.Deliveries.Infrastructure.Persistence.Outbox;

/// <summary>
/// Ligne d'outbox : un IntegrationEvent sérialisé, écrit dans la MÊME transaction
/// que le changement d'état métier.
/// </summary>
public sealed class OutboxMessage : IMessageDOutbox
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Nom de type stable « FullName, AssemblyName » (sans version).</summary>
    public string Type { get; init; } = default!;

    /// <summary>Charge utile JSON de l'event.</summary>
    public string Content { get; init; } = default!;

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    public DateTime? ProcessedOnUtc { get; set; }

    /// <summary>Message de la dernière erreur rencontrée.</summary>
    public string? Error { get; set; }

    // LES TROIS CHAMPS CI-DESSOUS N'EXISTAIENT PAS. SANS EUX, L'OUTBOX N'AVAIT NI
    // PLAFOND DE TENTATIVES, NI TEMPORISATION, NI ISSUE.

    /// <summary>Nombre de tentatives de publication déjà échouées.</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Instant à partir duquel une nouvelle tentative est permise (backoff
    /// exponentiel).
    /// </summary>
    public DateTime? NextAttemptAtUtc { get; set; }

    /// <summary>Instant de mise en lettre morte.</summary>
    public DateTime? DeadLetteredOnUtc { get; set; }

    /// <summary>CONTEXTE DE TRACE DE LA REQUÊTE QUI A PRODUIT CE MESSAGE (format W3C).</summary>
    public string? TraceParent { get; set; }

    /// <summary>
    /// L'identifiant de corrélation métier (`x-correlation-id`) de la requête qui a
    /// produit cet événement.
    /// </summary>
    public string? CorrelationId { get; set; }
}
