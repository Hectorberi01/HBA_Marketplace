using System.Collections.Concurrent;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Dispatch.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Dispatch.Application;

public sealed class DispatchStore
{
    private readonly ConcurrentDictionary<Guid, DispatchJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, Assignment> _assignments = new();
    private readonly ConcurrentDictionary<Guid, List<DriverCandidate>> _candidates = new();

    public async Task<DispatchJob> RequestAsync(
        RequestDispatchRequest request,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var job = new DispatchJob(
            Guid.NewGuid(),
            request.DeliveryId,
            request.Pickup,
            request.Dropoff,
            request.VehicleRequirement,
            request.Priority,
            "OFFERING",
            1,
            DateTimeOffset.UtcNow.AddSeconds(45),
            DateTimeOffset.UtcNow,
            3000);

        _jobs[request.DeliveryId] = job;
        _candidates[request.DeliveryId] = BuildCandidates(request.DeliveryId);
        var requested = job with { Candidates = _candidates[request.DeliveryId] };

        await publisher.PublishAsync(new DispatchStartedIntegrationEvent
        {
            DispatchJobId = requested.Id,
            DeliveryId = requested.DeliveryId
        }, cancellationToken);

        foreach (var candidate in requested.Candidates)
        {
            await publisher.PublishAsync(new DispatchOfferCreatedIntegrationEvent
            {
                DeliveryId = requested.DeliveryId,
                DriverId = candidate.DriverId
            }, cancellationToken);
        }

        return requested;
    }

    public async Task<DispatchJob> RetryAsync(
        Guid deliveryId,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var current = _jobs.GetValueOrDefault(deliveryId)
            ?? new DispatchJob(Guid.NewGuid(), deliveryId, new GeoPoint(0, 0), new GeoPoint(0, 0), null, 0, "PENDING", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1500);

        var retried = current with
        {
            Status = "OFFERING",
            Attempt = current.Attempt + 1,
            SearchRadiusMeters = current.SearchRadiusMeters + 1500,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(45)
        };

        // ON LIBÈRE L'AFFECTATION PRÉCÉDENTE, ET C'EST INDISPENSABLE DEPUIS
        // QUE `AssignAsync` REFUSE D'ÉCRASER (ISSUE-028).
        //
        // Un nouveau tour de dispatch signifie que le tour d'avant a échoué :
        // refus, silence, révocation. Sans cette ligne, `TryAdd` retomberait
        // éternellement sur l'affectation morte du tour précédent, et la course
        // ne pourrait PLUS JAMAIS être affectée à quelqu'un d'autre. La correction
        // de la double affectation aurait alors créé une course qui ne part jamais
        // — un défaut pire que celui qu'on ferme.
        _assignments.TryRemove(deliveryId, out _);

        _jobs[deliveryId] = retried;
        _candidates[deliveryId] = BuildCandidates(deliveryId);
        var requested = retried with { Candidates = _candidates[deliveryId] };

        await publisher.PublishAsync(new DispatchStartedIntegrationEvent
        {
            DispatchJobId = requested.Id,
            DeliveryId = requested.DeliveryId
        }, cancellationToken);

        foreach (var candidate in requested.Candidates)
        {
            await publisher.PublishAsync(new DispatchOfferCreatedIntegrationEvent
            {
                DeliveryId = requested.DeliveryId,
                DriverId = candidate.DriverId
            }, cancellationToken);
        }

        return requested;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA PREMIÈRE AFFECTATION GAGNE — ISSUE-028.
    ///
    /// CE QUI ÉTAIT CASSÉ.
    ///
    /// La ligne était `_assignments[deliveryId] = assignment` : une AFFECTATION
    /// SANS RELECTURE. Elle écrasait, en silence, une affectation déjà prononcée.
    ///
    /// Et ce n'est pas un cas de bord : `RequestAsync` publie un
    /// `DispatchOfferCreatedIntegrationEvent` PAR CANDIDAT, et `BuildCandidates`
    /// en rend DEUX. Deux livreurs reçoivent donc la même proposition, par
    /// construction. Les deux acceptent, les deux appels arrivent, le second
    /// écrase le premier — et les DEUX repartent avec un `Assignment` marqué
    /// « ASSIGNED » entre les mains, puisque la méthode le leur rendait.
    ///
    /// Sur le terrain : deux motos à la boutique, deux rémunérations engagées, un
    /// colis remis deux fois ou pas du tout.
    ///
    /// `TryAdd` PLUTÔT QUE `[…] =`. `ConcurrentDictionary` garantit qu'UN SEUL
    /// appelant obtient `true`, même si dix fils entrent ici au même instant.
    /// C'est la seule primitive de cette maquette qui sache arbitrer, et elle
    /// suffit — à condition de LIRE ce qu'elle rend, ce que la version d'origine
    /// ne faisait pas.
    ///
    /// RÉAFFECTER LE MÊME LIVREUR EST ACCEPTÉ, ET C'EST VOULU : un rejeu de
    /// requête (délai dépassé côté appelant, nouvelle tentative) doit rendre
    /// l'affectation existante, pas un conflit. Un livreur DIFFÉRENT est refusé.
    ///
    /// CE QUE CETTE CORRECTION NE FAIT PAS, ET IL FAUT LE DIRE.
    ///
    /// Ce service n'a AUCUNE base : ni `DbContext`, ni migration. Tout ce que
    /// cette classe protège vit dans un dictionnaire de processus et DISPARAÎT AU
    /// REDÉMARRAGE — et n'est même pas partagé entre deux réplicas. La garde est
    /// donc réelle DANS un processus, et nulle entre deux. Elle empêche le défaut
    /// de survivre à l'implémentation de ce service ; elle ne protège pas une
    /// production répliquée.
    ///
    /// La garde qui tient VRAIMENT est en base, dans delivery-service :
    /// `ux_deliveries_engaged_driver` et le jeton `xmin` sur `deliveries`. C'est
    /// là que l'affectation est un fait, et c'est ce que le lot 5.2 devra brancher
    /// quand ce service saura persister.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <returns>
    /// L'affectation, et <c>false</c> si la course était DÉJÀ affectée à un AUTRE
    /// livreur — auquel cas l'affectation rendue est celle qui a gagné, et aucun
    /// événement n'est publié.
    /// </returns>
    public async Task<(bool Assigned, Assignment Assignment)> AssignAsync(
        Guid deliveryId,
        Guid driverId,
        string mode,
        IIntegrationEventPublisher publisher,
        CancellationToken cancellationToken = default)
    {
        var candidat = new Assignment(Guid.NewGuid(), deliveryId, driverId, null, DateTimeOffset.UtcNow, mode, "ASSIGNED");

        if (!_assignments.TryAdd(deliveryId, candidat))
        {
            var deja = _assignments[deliveryId];

            // Même livreur : c'est un rejeu, pas un conflit. On rend l'affectation
            // qui existe, sans republier — republier ferait notifier deux fois le
            // livreur et compter deux fois la course côté consommateurs.
            return deja.DriverId == driverId
                ? (true, deja)
                : (false, deja);
        }

        var assignment = candidat;

        if (_jobs.TryGetValue(deliveryId, out var job))
        {
            _jobs[deliveryId] = job with { Status = "ASSIGNED" };
        }

        // ═════════════════════════════════════════════════════════════════════
        // CE TYPE VIENT DE `HBA.Deliveries.Contracts`, PLUS DE CELUI DE DISPATCH.
        //
        // Dispatch déclarait son PROPRE `DeliveryAssignedIntegrationEvent`, aux
        // champs identiques (`DeliveryId`, `DriverId`), pendant que Deliveries
        // déclarait le sien. `KafkaEventNaming.EventType` ne regarde que le nom de
        // CLASSE et l'enveloppe Kafka ne transporte que ce nom : les deux rendaient
        // « delivery.assigned », indiscernables à la réception.
        //
        // CE QUE ÇA PROVOQUAIT : `ResolveEventType` retenait le premier des deux par
        // ordre alphabétique du nom complet, et un gestionnaire enregistré pour
        // l'AUTRE type n'était JAMAIS appelé — sans exception, sans échec de
        // désérialisation, avec l'offset committé juste après. Le seul consommateur,
        // `NotifyDriverOnDeliveryAssignedHandler`, écoute la version Deliveries : il
        // était servi par chance, « HBA.Deliveries… » précédant « HBA.Dispatch… ».
        // Renommer un espace de noms suffisait à faire taire la notification de
        // proposition, muettement.
        //
        // POURQUOI DELIVERIES : l'agrégat décrit est LA COURSE. Dispatch prononce
        // l'affectation, il n'est pas propriétaire du fait. Ses propres événements
        // — `dispatch.started`, `dispatch.offer-created` — restent chez lui, publiés
        // quelques lignes plus haut.
        // ═════════════════════════════════════════════════════════════════════
        await publisher.PublishAsync(new DeliveryAssignedIntegrationEvent
        {
            DeliveryId = deliveryId,
            DriverId = driverId
        }, cancellationToken);

        return (true, assignment);
    }

    public void Cancel(Guid deliveryId)
    {
        // Même raison que dans `RetryAsync` : une course annulée doit pouvoir
        // repartir si elle est relancée. L'affectation ne survit pas à l'annulation.
        _assignments.TryRemove(deliveryId, out _);

        if (_jobs.TryGetValue(deliveryId, out var job))
        {
            _jobs[deliveryId] = job with { Status = "CANCELLED" };
        }
    }

    public bool TryGetJob(Guid deliveryId, out DispatchJob? job)
    {
        if (!_jobs.TryGetValue(deliveryId, out var value))
        {
            job = null;
            return false;
        }

        job = value with { Candidates = _candidates.GetValueOrDefault(deliveryId) ?? [] };
        return true;
    }

    public bool TryGetAssignment(Guid deliveryId, out Assignment? assignment) =>
        _assignments.TryGetValue(deliveryId, out assignment);

    private static List<DriverCandidate> BuildCandidates(Guid deliveryId) =>
    [
        new DriverCandidate(deliveryId, Guid.Parse("00000000-0000-7000-0000-000000000017"), 920, 240, 0.91m, 1, DateTimeOffset.UtcNow),
        new DriverCandidate(deliveryId, Guid.Parse("00000000-0000-7000-0000-000000000018"), 1450, 420, 0.78m, 2, DateTimeOffset.UtcNow)
    ];
}

public sealed record GeoPoint(double Latitude, double Longitude);

public sealed record DispatchJob(
    Guid Id,
    Guid DeliveryId,
    GeoPoint Pickup,
    GeoPoint Dropoff,
    string? VehicleRequirement,
    int Priority,
    string Status,
    int Attempt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    int SearchRadiusMeters)
{
    public IReadOnlyList<DriverCandidate> Candidates { get; init; } = [];
}

public sealed record DriverCandidate(
    Guid DispatchJobId,
    Guid DriverId,
    int DistanceToPickupMeters,
    int EtaSeconds,
    decimal Score,
    int Rank,
    DateTimeOffset EvaluatedAt);

public sealed record Assignment(Guid Id, Guid DeliveryId, Guid DriverId, Guid? OfferId, DateTimeOffset AssignedAt, string AssignmentMode, string Status);
public sealed record RequestDispatchRequest(Guid DeliveryId, GeoPoint Pickup, GeoPoint Dropoff, string? VehicleRequirement, int Priority);
public sealed record ManualAssignRequest(Guid DriverId);
