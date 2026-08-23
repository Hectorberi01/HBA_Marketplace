using System.Collections.Concurrent;
using HBA.Shared.IntegrationEvents;
using HBA.Tracking.Contracts.IntegrationEvents;

namespace HBA.Tracking.Application;

public sealed class TrackingStore
{
    private readonly ConcurrentDictionary<Guid, TrackingSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, TrackingSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<Guid, long> _lastSequences = new();

    public async Task<TrackingSession> StartAsync(
        Guid deliveryId,
        Guid driverId,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var session = new TrackingSession(Guid.NewGuid(), deliveryId, driverId, "ACTIVE", DateTimeOffset.UtcNow, null, 0);
        _sessions[deliveryId] = session;
        _lastSequences[deliveryId] = 0;

        await publisher.PublishAsync(new TrackingSessionStartedIntegrationEvent
        {
            DeliveryId = deliveryId,
            DriverId = driverId
        }, cancellationToken);

        return session;
    }

    public async Task<TrackingSession?> StopAsync(
        Guid deliveryId,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(deliveryId, out var session))
        {
            return null;
        }

        var stopped = session with { Status = "COMPLETED", EndedAt = DateTimeOffset.UtcNow };
        _sessions[deliveryId] = stopped;

        await publisher.PublishAsync(new TrackingSessionEndedIntegrationEvent
        {
            DeliveryId = deliveryId,
            DriverId = stopped.DriverId
        }, cancellationToken);

        return stopped;
    }

    /// <summary>
    /// Enregistre un lot de positions.
    /// </summary>
    /// <remarks>
    /// CE QUI ÉTAIT CASSÉ — ISSUE-058, ET LE PIRE ÉTAIT L'AUTO-DÉMARRAGE.
    ///
    /// Cette méthode ouvrait la session ELLE-MÊME quand il n'y en avait pas, avec
    /// le `driverId` reçu — lu dans le CORPS de la requête. N'importe qui pouvait
    /// donc s'inventer livreur de n'importe quelle course, et ses positions
    /// devenaient la vérité de cette course : le client suivait un point qui
    /// n'était pas son colis.
    ///
    /// La session est désormais ouverte UNIQUEMENT par
    /// <see cref="StartAsync"/>, appelée sur le port INTERNE par delivery-service
    /// — le seul à connaître l'affectation. Sans session, on refuse.
    ///
    /// ET LE LIVREUR DOIT ÊTRE CELUI DE LA SESSION. C'est le contrôle
    /// d'affectation demandé : il est INDIRECT (on fait confiance à qui a ouvert
    /// la session) mais il est réel, parce que l'ouverture n'est pas joignable
    /// depuis l'extérieur.
    ///
    /// CE QUE ÇA NE COUVRE PAS : une session ACHEVÉE. On refuse d'y ajouter
    /// des positions — une course remise ne bouge plus — mais rien ici ne relit
    /// l'état RÉEL de la course dans delivery-service. Si l'appel de fermeture se
    /// perd, la session reste ouverte et le livreur continue d'être suivi.
    /// </remarks>
    public async Task<LocationBatchOutcome> AddLocationsAsync(
        Guid deliveryId,
        Guid driverId,
        IReadOnlyList<LocationPointRequest> points,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(deliveryId, out var courante))
        {
            return new LocationBatchOutcome(LocationBatchStatus.NoSession, 0);
        }

        if (courante.DriverId != driverId)
        {
            return new LocationBatchOutcome(LocationBatchStatus.NotAssigned, 0);
        }

        if (courante.Status != "ACTIVE")
        {
            return new LocationBatchOutcome(LocationBatchStatus.SessionEnded, 0);
        }

        var accepted = 0;
        var lastSequence = _lastSequences.GetValueOrDefault(deliveryId);
        LocationPointRequest? lastAccepted = null;

        foreach (var point in points.OrderBy(p => p.Sequence))
        {
            if (point.Sequence <= lastSequence || !IsPlausible(point))
            {
                continue;
            }

            lastSequence = point.Sequence;
            accepted++;
            lastAccepted = point;
            _snapshots[deliveryId] = new TrackingSnapshot(
                deliveryId,
                driverId,
                point.Latitude,
                point.Longitude,
                point.CapturedAt,
                540,
                new RouteProgress(0.35m, 5100));
        }

        _lastSequences[deliveryId] = lastSequence;
        if (_sessions.TryGetValue(deliveryId, out var session))
        {
            _sessions[deliveryId] = session with { LastSequence = lastSequence };
        }

        if (lastAccepted is not null)
        {
            await publisher.PublishAsync(new TrackingLocationSampledIntegrationEvent
            {
                DeliveryId = deliveryId,
                DriverId = driverId,
                Sequence = lastAccepted.Sequence,
                Latitude = lastAccepted.Latitude,
                Longitude = lastAccepted.Longitude
            }, cancellationToken);

            await publisher.PublishAsync(new DeliveryEtaUpdatedIntegrationEvent
            {
                DeliveryId = deliveryId,
                EtaSeconds = 540
            }, cancellationToken);
        }

        return new LocationBatchOutcome(LocationBatchStatus.Accepted, accepted);
    }

    /// <summary>Le livreur de la session de cette course, ou <c>null</c> s'il n'y en a pas.</summary>
    public Guid? DriverOf(Guid deliveryId) =>
        _sessions.TryGetValue(deliveryId, out var session) ? session.DriverId : null;

    public bool TryGetLatest(Guid deliveryId, out TrackingSnapshot? snapshot) =>
        _snapshots.TryGetValue(deliveryId, out snapshot);

    private static bool IsPlausible(LocationPointRequest point) =>
        point.AccuracyMeters is null or <= 150
        && point.SpeedMps is null or <= 45
        && point.CapturedAt <= DateTimeOffset.UtcNow.AddMinutes(2);
}

public sealed record TrackingSession(Guid Id, Guid DeliveryId, Guid DriverId, string Status, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, long LastSequence);
public sealed record LocationPointRequest(long Sequence, double Latitude, double Longitude, double? AccuracyMeters, double? SpeedMps, double? Heading, DateTimeOffset CapturedAt);
/// <summary>
/// Un lot de positions — SANS `DriverId`.
///
/// IL Y ÉTAIT, ET C'ÉTAIT LA FAILLE (ISSUE-058). L'identité vient du JETON,
/// jamais du corps : c'est exactement ISSUE-017/018, refermée à la vague 1 et
/// réouverte ici. Le raisonnement complet est autour des routes `/me` de
/// `FinancialEndpoints`.
/// </summary>
public sealed record LocationBatchRequest(IReadOnlyList<LocationPointRequest> Points);

/// <summary>Pourquoi un lot de positions a été accepté ou refusé.</summary>
public enum LocationBatchStatus
{
    /// <summary>Aucune session ouverte pour cette course : personne ne la suit.</summary>
    NoSession = 0,

    /// <summary>Le livreur du jeton n'est pas celui de la session.</summary>
    NotAssigned = 1,

    /// <summary>La session est close : la course ne bouge plus.</summary>
    SessionEnded = 2,

    /// <summary>Lot pris en compte (tout ou partie des points).</summary>
    Accepted = 3
}

public sealed record LocationBatchOutcome(LocationBatchStatus Status, int Accepted);
public sealed record TrackingSnapshot(Guid DeliveryId, Guid DriverId, double Latitude, double Longitude, DateTimeOffset CapturedAt, int? EtaSeconds, RouteProgress? RouteProgress);
public sealed record RouteProgress(decimal Ratio, int RemainingMeters);
public sealed record StartTrackingSessionRequest(Guid DeliveryId, Guid DriverId);
