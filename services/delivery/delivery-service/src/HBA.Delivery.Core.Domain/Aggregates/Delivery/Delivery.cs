using HBA.Deliveries.Domain.Deliveries.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>UNE COURSE — LE CŒUR DU MOTEUR LOGISTIQUE.</summary>
public sealed class Delivery : AggregateRoot<DeliveryId>
{
    /// <summary>Nombre de propositions avant de rendre la main au dispatch humain.</summary>
    public const int MaxDispatchAttempts = 5;

    /// <summary>Horizon maximal d'une course programmée.</summary>
    public const int MaxScheduleHorizonDays = 7;

    private readonly List<DeliveryAssignment> _assignments = new();

    private Delivery(
        DeliveryId id,
        string reference,
        DeliverySource source,
        DeliveryType type,
        DeliveryStop pickup,
        DeliveryStop dropoff,
        DeliveryPackage package,
        ProofOfDeliveryKind requiredProof,
        Guid? partnerId,
        DateTime? scheduledForUtc)
        : base(id)
    {
        Reference = reference;
        Source = source;
        PartnerId = partnerId;
        Type = type;
        Pickup = pickup;
        Dropoff = dropoff;
        Package = package;
        RequiredProof = requiredProof;
        ScheduledForUtc = scheduledForUtc;

        // Le code est émis À LA CRÉATION, pas à la remise : il doit être communiqué
        // au destinataire pendant que la course roule.
        IssuedPin = requiredProof is ProofOfDeliveryKind.Pin ? ProofOfDelivery.IssuePin() : null;

        Status = DeliveryStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private Delivery()
    {
        Reference = string.Empty;
        Pickup = null!;
        Dropoff = null!;
        Package = null!;
    }

    /// <summary>Référence du donneur d'ordre.</summary>
    public string Reference { get; private set; }

    public DeliverySource Source { get; private set; }

    /// <summary>QUEL PARTENAIRE — RENSEIGNÉ SI, ET SEULEMENT SI, LA SOURCE EST EXTERNE.</summary>
    public Guid? PartnerId { get; private set; }

    /// <summary>Devis dont cette course est issue.</summary>
    public Guid? QuoteId { get; private set; }

    /// <summary>Prix convenu, RECOPIÉ depuis le devis plutôt que lu à travers lui.</summary>
    public decimal? Price { get; private set; }

    public string? Currency { get; private set; }

    /// <summary>PART DU LIVREUR — FIGÉE À LA REMISE, JAMAIS RECALCULÉE.</summary>
    public decimal? DriverEarning { get; private set; }

    /// <summary>Taux appliqué au moment de la remise, entre 0 et 1.</summary>
    public decimal? DriverShareRate { get; private set; }

    public DeliveryType Type { get; private set; }

    public DeliveryStatus Status { get; private set; }

    public DeliveryStop Pickup { get; private set; }

    public DeliveryStop Dropoff { get; private set; }

    public DeliveryPackage Package { get; private set; }

    public ProofOfDeliveryKind RequiredProof { get; private set; }

    /// <summary>Livreur actuellement en charge.</summary>
    public DriverId? AssignedDriverId { get; private set; }

    public IReadOnlyCollection<DeliveryAssignment> Assignments => _assignments.AsReadOnly();

    /// <summary>Nombre de propositions déjà faites, refus compris.</summary>
    public int DispatchAttempts => _assignments.Count;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public DateTime? PickedUpAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Preuve effectivement recueillie à la remise.</summary>
    public ProofOfDelivery? Proof { get; private set; }

    /// <summary>LE CODE REMIS AU DESTINATAIRE — ÉMIS ICI, JAMAIS MONTRÉ AU LIVREUR.</summary>
    public string? IssuedPin { get; private set; }

    /// <summary>TENTATIVES DE PREUVE INFRUCTUEUSES — LE COMPTEUR QUI MANQUAIT.</summary>
    public int FailedProofAttempts { get; private set; }

    /// <summary>Au-delà, la preuve par code est verrouillée et un humain doit intervenir.</summary>
    public const int MaxFailedProofAttempts = 5;

    /// <summary>La preuve par code est-elle épuisée ?</summary>
    public bool IsProofLocked => FailedProofAttempts >= MaxFailedProofAttempts;

    /// <summary>QUAND LA COURSE DOIT ÊTRE LIVRÉE — POUR LE TYPE Scheduled UNIQUEMENT.</summary>
    public DateTime? ScheduledForUtc { get; private set; }

    /// <summary>Combien de temps AVANT l'heure promise on commence à chercher un livreur.</summary>
    public static readonly TimeSpan ScheduledDispatchLeadTime = TimeSpan.FromMinutes(45);

    /// <summary>Délai laissé au livreur pour répondre à une proposition.</summary>
    public static readonly TimeSpan OfferTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Instant à partir duquel une course programmée peut être dispatchée.</summary>
    public DateTime? DispatchWindowOpensAtUtc =>
        ScheduledForUtc is { } due ? due - ScheduledDispatchLeadTime : null;

    /// <summary>La course est close : plus aucune transition n'est possible.</summary>
    public bool IsTerminal => Status is DeliveryStatus.Delivered or DeliveryStatus.Cancelled;

    /// <summary>Rattache la course à son devis et fige le montant facturé.</summary>
    public void AttachQuote(Guid quoteId, decimal price, string currency)
    {
        QuoteId = quoteId;
        Price = price;
        Currency = currency;
    }

    /// <summary>Durée totale, de la création à la remise.</summary>
    public TimeSpan? TotalDuration => DeliveredAtUtc is null ? null : DeliveredAtUtc - CreatedAtUtc;

    // CRÉATION

    public static Result<Delivery> Create(
        string? reference,
        DeliverySource source,
        DeliveryType type,
        DeliveryStop pickup,
        DeliveryStop dropoff,
        DeliveryPackage package,
        decimal? declaredValue = null,
        bool isCashOnDelivery = false,
        Guid? partnerId = null,
        DateTime? scheduledForUtc = null,
        DateTime? nowUtc = null)
    {
        var trimmedReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (trimmedReference is null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.reference_required", "Une référence de commande est requise."));
        }

        if (pickup is null || dropoff is null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.stops_required", "Le point de collecte et le point de remise sont requis."));
        }

        // COLLECTE ET REMISE AU MÊME ENDROIT : ON REFUSE.
        if (string.Equals(pickup.CommuneCode, dropoff.CommuneCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pickup.Landmark, dropoff.Landmark, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.same_stop", "Le point de collecte et le point de remise sont identiques."));
        }

        // Une valeur négative n'est pas une déclaration basse, c'est une erreur
        // d'intégration.
        if (declaredValue is { } valeurDeclaree && valeurDeclaree < 0)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.declared_value_negative",
                    "La valeur déclarée des marchandises ne peut pas être négative."));
        }

        // SOURCE ET PARTENAIRE VONT ENSEMBLE — DANS LES DEUX SENS.
        if (source is DeliverySource.ExternalPartner && partnerId is null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.partner_required",
                    "Une course de source externe doit désigner le partenaire qui la demande."));
        }

        if (source is not DeliverySource.ExternalPartner && partnerId is not null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.partner_unexpected",
                    "Seule une course de source externe peut désigner un partenaire."));
        }

        // TYPE ET DATE VONT ENSEMBLE — DANS LES DEUX SENS.
        if (type is DeliveryType.Scheduled && scheduledForUtc is null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.schedule_required",
                    "Une course programmée doit indiquer l'heure de livraison souhaitée."));
        }

        if (type is not DeliveryType.Scheduled && scheduledForUtc is not null)
        {
            return Result.Failure<Delivery>(
                Error.Validation("delivery.schedule_unexpected",
                    "Seule une course de type « Scheduled » peut porter une heure de livraison."));
        }

        if (scheduledForUtc is { } due)
        {
            var now = nowUtc ?? DateTime.UtcNow;

            // Le créneau doit laisser le temps de trouver un livreur ET de rouler.
            if (due <= now + ScheduledDispatchLeadTime)
            {
                return Result.Failure<Delivery>(
                    Error.Validation("delivery.schedule_too_soon",
                        $"Une course programmée doit être demandée au moins "
                        + $"{ScheduledDispatchLeadTime.TotalMinutes:0} minutes à l'avance."));
            }

            if (due > now.AddDays(MaxScheduleHorizonDays))
            {
                return Result.Failure<Delivery>(
                    Error.Validation("delivery.schedule_too_far",
                        $"Une course ne peut pas être programmée à plus de {MaxScheduleHorizonDays} jours."));
            }
        }

        // LA POLITIQUE DE PREUVE EST APPLIQUÉE ICI, PAS REÇUE — ISSUE-057.
        var requiredProof = ProofPolicy.RequiredFor(declaredValue, isCashOnDelivery);

        var delivery = new Delivery(
            DeliveryId.New(), trimmedReference, source, type, pickup, dropoff, package,
            requiredProof, partnerId, scheduledForUtc);

        delivery.Raise(new DeliveryCreatedDomainEvent(
            delivery.Id.Value, delivery.Reference, delivery.Source, delivery.Type));

        return delivery;
    }

    // DISPATCH

    /// <summary>Ouvre la recherche d'un livreur.</summary>
    /// <param name="nowUtc">
    /// L'instant courant. Passé en paramètre parce que l'agrégat n'a pas d'horloge
    /// — c'est ce qui rend la règle testable sans attendre quarante-cinq minutes.
    /// </param>
    public Result StartSearching(DateTime? nowUtc = null)
    {
        if (Status is not (DeliveryStatus.Pending or DeliveryStatus.NoDriverAvailable))
        {
            return InvalidTransition(nameof(StartSearching));
        }

        if (DispatchWindowOpensAtUtc is { } opensAt && (nowUtc ?? DateTime.UtcNow) < opensAt)
        {
            return Result.Failure(Error.Conflict(
                "delivery.scheduled_not_due",
                $"Cette course est programmée pour le {ScheduledForUtc:u} : la recherche d'un livreur "
                + $"s'ouvrira le {opensAt:u}."));
        }

        Status = DeliveryStatus.SearchingDriver;
        Raise(new DeliverySearchingDriverDomainEvent(Id.Value, DispatchAttempts + 1));
        return Result.Success();
    }

    /// <summary>Propose la mission à un livreur.</summary>
    public Result AssignTo(DriverId driverId)
    {
        if (Status is not DeliveryStatus.SearchingDriver)
        {
            return InvalidTransition(nameof(AssignTo));
        }

        // Ne jamais reproposer à quelqu'un qui a déjà refusé : il refusera encore,
        // et chaque tour perdu se paie en attente client.
        if (_assignments.Any(a => a.DriverId == driverId && a.Outcome is AssignmentOutcome.Rejected))
        {
            return Result.Failure(
                Error.Conflict("delivery.driver_already_refused", "Ce livreur a déjà refusé cette course."));
        }

        _assignments.Add(DeliveryAssignment.Offer(driverId, DispatchAttempts + 1));
        Status = DeliveryStatus.DriverAssigned;
        Raise(new DeliveryAssignedDomainEvent(Id.Value, driverId.Value));
        return Result.Success();
    }

    /// <summary>Le livreur accepte.</summary>
    public Result AcceptByDriver(DriverId driverId)
    {
        if (Status is not DeliveryStatus.DriverAssigned)
        {
            return InvalidTransition(nameof(AcceptByDriver));
        }

        var offer = CurrentOffer(driverId);
        if (offer is null)
        {
            return Result.Failure(
                Error.Conflict("delivery.not_offered_to_driver", "Cette course n'est pas proposée à ce livreur."));
        }

        offer.Accept();
        AssignedDriverId = driverId;
        AcceptedAtUtc = DateTime.UtcNow;
        Status = DeliveryStatus.DriverAccepted;
        Raise(new DeliveryAcceptedDomainEvent(Id.Value, Reference, Source, driverId.Value));
        return Result.Success();
    }

    /// <summary>Le livreur refuse, ou ne répond pas à temps.</summary>
    public Result RejectByDriver(DriverId driverId, string? reason = null, bool expired = false)
    {
        if (Status is not DeliveryStatus.DriverAssigned)
        {
            return InvalidTransition(nameof(RejectByDriver));
        }

        var offer = CurrentOffer(driverId);
        if (offer is null)
        {
            return Result.Failure(
                Error.Conflict("delivery.not_offered_to_driver", "Cette course n'est pas proposée à ce livreur."));
        }

        if (expired)
        {
            offer.Expire();
        }
        else
        {
            offer.Reject(reason);
        }

        Raise(new DeliveryRejectedByDriverDomainEvent(Id.Value, driverId.Value, reason));

        if (DispatchAttempts >= MaxDispatchAttempts)
        {
            Status = DeliveryStatus.NoDriverAvailable;
            Raise(new DeliveryNoDriverAvailableDomainEvent(Id.Value, Reference, Source, DispatchAttempts));
        }
        else
        {
            Status = DeliveryStatus.SearchingDriver;
            Raise(new DeliverySearchingDriverDomainEvent(Id.Value, DispatchAttempts + 1));
        }

        return Result.Success();
    }

    /// <summary>
    /// L'exploitation retire la mission au livreur en charge : panne, absence
    /// prolongée, signalement.
    /// </summary>
    public Result RevokeAssignment(string? reason)
    {
        if (Status is not (DeliveryStatus.DriverAccepted or DeliveryStatus.ArrivedAtPickup))
        {
            return InvalidTransition(nameof(RevokeAssignment));
        }

        var current = _assignments.LastOrDefault(a => a.Outcome is AssignmentOutcome.Accepted);
        current?.Revoke(reason);

        AssignedDriverId = null;
        AcceptedAtUtc = null;
        Status = DeliveryStatus.SearchingDriver;
        Raise(new DeliverySearchingDriverDomainEvent(Id.Value, DispatchAttempts + 1));
        return Result.Success();
    }

    // EXÉCUTION

    public Result MarkArrivedAtPickup()
        => Advance(DeliveryStatus.DriverAccepted, DeliveryStatus.ArrivedAtPickup, nameof(MarkArrivedAtPickup));

    public Result MarkPickedUp()
    {
        // On tolère la collecte SANS passage par « arrivé au point de collecte » :
        // un livreur pressé oublie ce bouton, et refuser bloquerait une course
        // physiquement déjà en cours.
        if (Status is not (DeliveryStatus.ArrivedAtPickup or DeliveryStatus.DriverAccepted))
        {
            return InvalidTransition(nameof(MarkPickedUp));
        }

        PickedUpAtUtc = DateTime.UtcNow;
        Status = DeliveryStatus.PickedUp;
        // Le code part avec l'événement : c'est l'agrégat qui l'a tiré, c'est lui
        // qui le connaît.
        Raise(new DeliveryPickedUpDomainEvent(
            Id.Value, Reference, Source, AssignedDriverId!.Value.Value, IssuedPin));
        return Result.Success();
    }

    public Result MarkInTransit()
        => Advance(DeliveryStatus.PickedUp, DeliveryStatus.InTransit, nameof(MarkInTransit));

    public Result MarkArrivedAtDropoff()
    {
        if (Status is not (DeliveryStatus.InTransit or DeliveryStatus.PickedUp))
        {
            return InvalidTransition(nameof(MarkArrivedAtDropoff));
        }

        Status = DeliveryStatus.ArrivedAtDropoff;
        return Result.Success();
    }

    /// <summary>Remise au destinataire.</summary>
    /// <param name="driverShareRate">Part du prix revenant au livreur, entre 0 et 1.</param>
    public Result MarkDelivered(string? proofValue, decimal driverShareRate)
    {
        if (Status is not (DeliveryStatus.ArrivedAtDropoff or DeliveryStatus.InTransit))
        {
            return InvalidTransition(nameof(MarkDelivered));
        }

        // LA PREUVE EST MAINTENANT VÉRIFIÉE, PAS SEULEMENT PRÉSENTE.
        ProofOfDelivery? captured = null;

        if (RequiredProof is not ProofOfDeliveryKind.None)
        {
            if (IsProofLocked)
            {
                return Result.Failure(Error.Conflict(
                    "delivery.proof.locked",
                    $"La preuve de cette course est verrouillée après {MaxFailedProofAttempts} tentatives "
                    + "infructueuses. Contactez le support pour la débloquer."));
            }

            var proof = ProofOfDelivery.Capture(RequiredProof, proofValue, IssuedPin, DateTime.UtcNow);
            if (proof.IsFailure)
            {
                // ON NE COMPTE QUE LES MAUVAISES RÉPONSES, PAS LES ABSENCES.
                if (proof.Error.Code is "delivery.proof.pin_mismatch")
                {
                    FailedProofAttempts++;
                }

                return Result.Failure(proof.Error);
            }

            captured = proof.Value;
        }

        if (driverShareRate is < 0m or > 1m)
        {
            return Result.Failure(
                Error.Validation("delivery.share_rate_invalid",
                    "La part du livreur doit être comprise entre 0 et 1."));
        }

        Proof = captured;
        DeliveredAtUtc = DateTime.UtcNow;
        Status = DeliveryStatus.Delivered;

        // PAS DE PRIX, PAS DE GAIN — ET C'EST UN SIGNAL, PAS UN DÉTAIL.
        if (Price is { } price)
        {
            DriverShareRate = driverShareRate;
            DriverEarning = Math.Round(price * driverShareRate, 0, MidpointRounding.AwayFromZero);
        }

        Raise(new DeliveryCompletedDomainEvent(
            Id.Value, Reference, Source, AssignedDriverId!.Value.Value, DeliveredAtUtc.Value,
            DriverEarning, Currency));

        return Result.Success();
    }

    /// <summary>
    /// Annulation. Impossible une fois le colis collecté : à ce stade, la
    /// marchandise est chez le livreur et il faut un RETOUR, qui est une autre
    /// course — pas l'effacement de celle-ci.
    /// </summary>
    public Result Cancel(string? reason)
    {
        if (IsTerminal)
        {
            return Result.Failure(
                Error.Conflict("delivery.already_closed", "Cette course est déjà close."));
        }

        if (Status is DeliveryStatus.PickedUp or DeliveryStatus.InTransit or DeliveryStatus.ArrivedAtDropoff)
        {
            return Result.Failure(
                Error.Conflict("delivery.already_picked_up",
                    "Le colis est déjà pris en charge : créez une course de retour plutôt que d'annuler."));
        }

        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = DeliveryStatus.Cancelled;
        Raise(new DeliveryCancelledDomainEvent(Id.Value, Reference, Source, CancellationReason));
        return Result.Success();
    }

    private DeliveryAssignment? CurrentOffer(DriverId driverId)
        => _assignments.LastOrDefault(a => a.DriverId == driverId && a.Outcome is AssignmentOutcome.Offered);

    private Result Advance(DeliveryStatus from, DeliveryStatus to, string operation)
    {
        if (Status != from)
        {
            return InvalidTransition(operation);
        }

        Status = to;
        return Result.Success();
    }

    private Result InvalidTransition(string operation)
        => Result.Failure(Error.Conflict(
            "delivery.invalid_transition",
            $"Opération « {operation} » impossible depuis l'état « {Status} »."));
}
