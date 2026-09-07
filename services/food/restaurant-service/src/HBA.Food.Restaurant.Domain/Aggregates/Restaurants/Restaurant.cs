using HBA.Food.Domain.Restaurants.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Restaurants;

public readonly record struct RestaurantId(Guid Value)
{
    public static RestaurantId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>UN ÉTABLISSEMENT HBA FOOD.</summary>
public sealed class Restaurant : AggregateRoot<RestaurantId>
{
    private readonly List<ServiceHours> _serviceHours = new();
    private readonly List<SpecialOpeningHour> _specialHours = new();

    private Restaurant()
    {
    }

    private Restaurant(RestaurantId id, Guid ownerUserId, string name, string phone)
        : base(id)
    {
        OwnerUserId = ownerUserId;
        Name = name;
        Phone = phone;
        Status = RestaurantStatus.Draft;
        PreparationMinutes = DefaultPreparationMinutes;

        // Manuel par défaut : un maquis qui découvre l'application ne doit pas se
        // retrouver engagé sur des commandes qu'il n'a pas vues passer.
        AcceptanceMode = OrderAcceptanceMode.Manual;
        MaximumActiveOrders = DefaultMaximumActiveOrders;

        CreatedOnUtc = DateTime.UtcNow;

        Raise(new RestaurantRegisteredDomainEvent(id.Value, ownerUserId, name));
    }

    /// <summary>Compte HBA du restaurateur.</summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>LE DOSSIER VENDEUR QUI ENCAISSE LES RECETTES DE CET ÉTABLISSEMENT.</summary>
    public Guid? PayoutSellerId { get; private set; }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    /// <summary>
    /// LE LOGO, PAR RÉFÉRENCE AU SERVICE MÉDIA (cahier Food §3, cahier Media §1).
    /// </summary>
    public Guid? LogoMediaId { get; private set; }

    /// <summary>Image de couverture (§3).</summary>
    public Guid? CoverMediaId { get; private set; }

    /// <summary>TRANSITOIRE — l'URL d'avant la bascule vers le service média.</summary>
    public string? LegacyLogoUrl { get; private set; }

    /// <summary>Adresse publique de <see cref="LogoMediaId"/>, recopiée au dépôt.</summary>
    public string? LogoPublicUrl { get; private set; }

    /// <summary>Numéro de l'ÉTABLISSEMENT — celui qu'appelle un livreur devant la porte.</summary>
    public string Phone { get; private set; } = default!;

    public RestaurantStatus Status { get; private set; }
    public string? StatusReason { get; private set; }

    /// <summary>Lieu de collecte (<c>Inventory.FulfillmentLocation</c>).</summary>
    public Guid? FulfillmentLocationId { get; private set; }

    /// <summary>Délai de préparation annoncé, en minutes.</summary>
    public int PreparationMinutes { get; private set; }

    /// <summary>Fin de la pause déclarée par le restaurateur.</summary>
    public DateTime? PausedUntilUtc { get; private set; }

    /// <summary>Manuel ou automatique (§3).</summary>
    public OrderAcceptanceMode AcceptanceMode { get; private set; }

    /// <summary>Montant minimum d'une commande, hors livraison (§3).</summary>
    public decimal? MinimumOrderAmount { get; private set; }

    /// <summary>Combien de commandes la cuisine peut tenir en parallèle (§14).</summary>
    public int? MaximumActiveOrders { get; private set; }

    /// <summary>À saturation, refuser les nouvelles commandes plutôt que de les empiler.</summary>
    public bool BlocksOrdersWhenSaturated { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    public IReadOnlyCollection<ServiceHours> ServiceHours => _serviceHours.AsReadOnly();

    /// <summary>
    /// Les exceptions datées (§4) : jours fériés, inventaires, fermetures
    /// ponctuelles.
    /// </summary>
    public IReadOnlyCollection<SpecialOpeningHour> SpecialHours => _specialHours.AsReadOnly();

    /// <summary>CET ÉTABLISSEMENT A-T-IL SA PLACE DANS LA VITRINE ?</summary>
    public bool IsPubliclyVisible => Status == RestaurantStatus.Active;

    /// <summary>
    /// Délai par défaut : un plat se prépare rarement en moins d'un quart d'heure.
    /// </summary>
    public const int DefaultPreparationMinutes = 30;

    /// <summary>
    /// Bornes du délai annoncé. Au-delà de trois heures, ce n'est plus de la
    /// restauration livrée.
    /// </summary>
    public const int MinPreparationMinutes = 5;
    public const int MaxPreparationMinutes = 180;

    /// <summary>Plafond par défaut, repris du cahier (§14).</summary>
    public const int DefaultMaximumActiveOrders = 15;

    /// <summary>
    /// À partir de quelle proportion du plafond la cuisine est « en forte demande
    /// ».
    /// </summary>
    public const double HighLoadRatio = 0.7;

    /// <summary>LA CHARGE DE LA CUISINE (cahier §14).</summary>
    public KitchenLoad AssessLoad(int activeOrders)
    {
        if (MaximumActiveOrders is not { } plafond || plafond <= 0)
        {
            // Sans plafond déclaré, il n'y a pas de saturation à constater.
            return new KitchenLoad(KitchenLoadLevel.Normal, activeOrders, 0, false, false);
        }

        if (activeOrders >= plafond)
        {
            return new KitchenLoad(
                KitchenLoadLevel.Saturated,
                activeOrders,
                PreparationMinutes,
                AutoAcceptSuspended: true,
                BlocksNewOrders: BlocksOrdersWhenSaturated);
        }

        if (activeOrders >= (int)Math.Ceiling(plafond * HighLoadRatio))
        {
            return new KitchenLoad(
                KitchenLoadLevel.High,
                activeOrders,
                PreparationMinutes / 2,

                // L'AUTO-ACCEPTATION SURVIT À LA FORTE DEMANDE. Le cahier ne la
                // coupe qu'à SATURATION. La couper plus tôt renverrait le
                // restaurateur à des acceptations manuelles au pire moment — celui
                // où il a le moins le temps de regarder un écran.
                AutoAcceptSuspended: false,
                BlocksNewOrders: false);
        }

        return new KitchenLoad(KitchenLoadLevel.Normal, activeOrders, 0, false, false);
    }

    /// <summary>Déclare une exception datée (§4).</summary>
    public Result SetSpecialHours(SpecialOpeningHour exception)
    {
        _specialHours.RemoveAll(e => e.Date == exception.Date);
        _specialHours.Add(exception);
        Touch();

        return Result.Success();
    }

    /// <summary>Retire l'exception d'une date : le jour redevient un jour ordinaire.</summary>
    public Result ClearSpecialHours(DateOnly date)
    {
        _specialHours.RemoveAll(e => e.Date == date);
        Touch();
        return Result.Success();
    }

    /// <summary>Oublie les exceptions PASSÉES.</summary>
    public int PurgePastSpecialHours(DateTime nowUtc)
        => _specialHours.RemoveAll(e => e.Date < BeninTime.LocalDate(nowUtc));

    /// <summary>Rattache le logo et la couverture (§3).</summary>
    /// <param name="logoPublicUrl">L'adresse publique du logo, recopiée au rattachement.</param>
    public Result SetMedia(Guid? logoMediaId, Guid? coverMediaId, string? logoPublicUrl = null)
    {
        LogoMediaId = logoMediaId == Guid.Empty ? null : logoMediaId;
        CoverMediaId = coverMediaId == Guid.Empty ? null : coverMediaId;

        if (LogoMediaId is null)
        {
            // Retirer le logo retire son adresse : la garder afficherait encore
            // l'image d'un restaurant qui n'en a plus.
            LogoPublicUrl = null;
        }
        else
        {
            // Le média prend le relais : l'URL héritée n'a plus lieu d'être, et la
            // laisser ferait réapparaître l'ancien logo au premier retrait du
            // nouveau.
            LegacyLogoUrl = null;
            LogoPublicUrl = string.IsNullOrWhiteSpace(logoPublicUrl) ? null : logoPublicUrl.Trim();
        }

        Touch();
        return Result.Success();
    }

    /// <summary>Manuel ou automatique (§3).</summary>
    public Result SetAcceptanceMode(OrderAcceptanceMode mode)
    {
        AcceptanceMode = mode;
        Touch();
        return Result.Success();
    }

    /// <summary>Minimum de commande et plafond de charge (§3, §14).</summary>
    public Result SetOrderLimits(decimal? minimumOrderAmount, int? maximumActiveOrders, bool blockWhenSaturated)
    {
        if (minimumOrderAmount is { } minimum && minimum < 0m)
        {
            return Result.Failure(Error.Validation(
                "food.restaurant.minimum_invalid", "Le minimum de commande ne peut pas être négatif."));
        }

        if (maximumActiveOrders is { } plafond && plafond < 1)
        {
            // Zéro voudrait dire « aucune commande acceptée », ce qui se dit par
            // une pause ou une fermeture — pas par un plafond.
            return Result.Failure(Error.Validation(
                "food.restaurant.max_orders_invalid",
                "Le plafond de commandes doit valoir au moins 1. Laissez-le vide pour ne pas en fixer."));
        }

        MinimumOrderAmount = minimumOrderAmount is > 0m ? minimumOrderAmount : null;
        MaximumActiveOrders = maximumActiveOrders;
        BlocksOrdersWhenSaturated = blockWhenSaturated;
        Touch();

        return Result.Success();
    }

    public static Result<Restaurant> Register(Guid ownerUserId, string name, string phone)
    {
        if (ownerUserId == Guid.Empty)
        {
            return Error.Validation("food.restaurant.owner_required", "Le compte du restaurateur est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("food.restaurant.name_required", "Le nom de l'établissement est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(phone) || phone.Trim().Length is < 8 or > 20)
        {
            // Obligatoire dès la création, contrairement à bien des champs : sans
            // numéro, un livreur devant une porte close n'a personne à appeler.
            return Error.Validation("food.restaurant.phone_required", "Le téléphone de l'établissement est obligatoire.");
        }

        return new Restaurant(RestaurantId.New(), ownerUserId, name.Trim(), phone.Trim());
    }

    public Result UpdateProfile(string name, string? description, string phone)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("food.restaurant.name_required", "Le nom de l'établissement est obligatoire."));
        }

        if (string.IsNullOrWhiteSpace(phone) || phone.Trim().Length is < 8 or > 20)
        {
            return Result.Failure(Error.Validation("food.restaurant.phone_required", "Le téléphone de l'établissement est obligatoire."));
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        // Le logo ne se règle plus par le profil : il se téléverse, et
        // `SetMediaCommand` le rattache.
        Phone = phone.Trim();
        Touch();

        return Result.Success();
    }

    public Result AttachFulfillmentLocation(Guid fulfillmentLocationId)
    {
        if (fulfillmentLocationId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("food.restaurant.location_required", "Le lieu de collecte est obligatoire."));
        }

        FulfillmentLocationId = fulfillmentLocationId;
        Touch();
        return Result.Success();
    }

    /// <summary>Fixe le délai de préparation annoncé.</summary>
    public Result SetPreparationTime(int minutes)
    {
        if (minutes is < MinPreparationMinutes or > MaxPreparationMinutes)
        {
            return Result.Failure(Error.Validation(
                "food.restaurant.preparation_invalid",
                $"Le délai de préparation doit être compris entre {MinPreparationMinutes} et {MaxPreparationMinutes} minutes."));
        }

        PreparationMinutes = minutes;
        Touch();
        return Result.Success();
    }

    /// <summary>Remplace la grille de service.</summary>
    public Result SetServiceHours(IReadOnlyList<ServiceHours> hours)
    {
        foreach (var (creneau, index) in hours.Select((h, i) => (h, i)))
        {
            if (hours.Where((_, j) => j != index).Any(creneau.Overlaps))
            {
                return Result.Failure(Error.Validation(
                    "food.restaurant.hours_overlap", $"Deux créneaux se chevauchent le {creneau.Day}."));
            }
        }

        _serviceHours.Clear();
        _serviceHours.AddRange(hours);
        Touch();

        return Result.Success();
    }

    // ── Cycle de vie ────────────────────────────────────────────────────────

    /// <summary>Le restaurateur soumet son établissement à la validation de HBA.</summary>
    public Result SubmitForApproval()
    {
        if (Status is not (RestaurantStatus.Draft or RestaurantStatus.Closed))
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.not_submittable", "Cet établissement n'est pas en attente de soumission."));
        }

        if (_serviceHours.Count == 0)
        {
            // Sans horaires, CanAcceptOrders refuserait TOUJOURS : le restaurant
            // serait validé, visible, et n'accepterait jamais rien.
            return Result.Failure(Error.Conflict(
                "food.restaurant.hours_required", "Renseignez vos heures de service avant de soumettre l'établissement."));
        }

        if (FulfillmentLocationId is null)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.location_required", "Renseignez l'adresse de collecte avant de soumettre l'établissement."));
        }

        // ENCAISSER SANS POUVOIR REVERSER EST LE PIRE DES ÉTATS.
        if (PayoutSellerId is null)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.payout_required",
                "Rattachez un dossier vendeur validé avant de soumettre l'établissement : c'est lui qui recevra les recettes."));
        }

        Status = RestaurantStatus.PendingApproval;
        StatusReason = null;
        Touch();

        return Result.Success();
    }

    /// <summary>HBA valide l'établissement : il entre en service.</summary>
    public Result Approve()
    {
        if (Status != RestaurantStatus.PendingApproval)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.not_pending", "Cet établissement n'attend pas de validation."));
        }

        Status = RestaurantStatus.Active;
        StatusReason = null;
        Touch();

        Raise(new RestaurantApprovedDomainEvent(Id.Value, OwnerUserId, Name));
        return Result.Success();
    }

    /// <summary>
    /// HBA refuse le dossier. Le motif est transmis au restaurateur : sans lui, il
    /// resoumet le même dossier et les deux s'épuisent.
    /// </summary>
    public Result Reject(string? reason)
    {
        if (Status != RestaurantStatus.PendingApproval)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.not_pending", "Cet établissement n'attend pas de validation."));
        }

        Status = RestaurantStatus.Draft;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();

        Raise(new RestaurantRejectedDomainEvent(Id.Value, OwnerUserId, StatusReason));
        return Result.Success();
    }

    /// <summary>L'exploitation écarte l'établissement.</summary>
    public Result Suspend(string? reason)
    {
        // IDEMPOTENT : réappliquer une sanction déjà en vigueur n'est pas une
        // erreur de l'exploitation, et ne doit pas renotifier le restaurateur.
        if (Status == RestaurantStatus.Suspended)
        {
            return Result.Success();
        }

        if (Status != RestaurantStatus.Active)
        {
            // ÉCHEC, ET NON SUCCÈS SILENCIEUX : l'exploitation a cliqué sur le
            // mauvais bouton.
            return Result.Failure(Error.Conflict(
                "food.restaurant.not_active",
                "Seul un établissement en service peut être suspendu. "
                + "Un dossier en attente se refuse, un brouillon n'est visible de personne."));
        }

        Status = RestaurantStatus.Suspended;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();

        Raise(new RestaurantSuspendedDomainEvent(Id.Value, OwnerUserId, StatusReason));
        return Result.Success();
    }

    /// <summary>
    /// Lève la suspension. L'établissement redevient ACTIF, et non « en attente » :
    /// son dossier avait déjà été validé, et lui refaire passer la validation le
    /// punirait une seconde fois pour une sanction qu'on retire.
    /// </summary>
    public Result LiftSuspension()
    {
        if (Status != RestaurantStatus.Suspended)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.not_suspended", "Cet établissement n'est pas suspendu."));
        }

        Status = RestaurantStatus.Active;
        StatusReason = null;
        Touch();

        Raise(new RestaurantReopenedDomainEvent(Id.Value, OwnerUserId));
        return Result.Success();
    }

    /// <summary>Le restaurateur quitte la plateforme.</summary>
    public Result Close(string? reason)
    {
        if (Status == RestaurantStatus.Suspended)
        {
            // Sans cette garde, un restaurateur sanctionné « fermerait » puis
            // resoumettrait son dossier : la suspension se contournerait par un
            // détour.
            return Result.Failure(Error.Conflict(
                "food.restaurant.suspended", "Un établissement suspendu ne peut pas être fermé par son propriétaire."));
        }

        if (Status == RestaurantStatus.Closed)
        {
            return Result.Success();
        }

        Status = RestaurantStatus.Closed;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();

        Raise(new RestaurantClosedDomainEvent(Id.Value, OwnerUserId));
        return Result.Success();
    }

    // ── Pause et disponibilité ──────────────────────────────────────────────

    /// <summary>Le restaurateur met le service en pause pour un temps court.</summary>
    public Result PauseUntil(DateTime untilUtc, DateTime nowUtc)
    {
        if (untilUtc <= nowUtc)
        {
            return Result.Failure(Error.Validation(
                "food.restaurant.pause_invalid", "La fin de pause doit être dans le futur."));
        }

        if (untilUtc > nowUtc.AddHours(MaxPauseHours))
        {
            return Result.Failure(Error.Validation(
                "food.restaurant.pause_too_long",
                $"Une pause ne peut excéder {MaxPauseHours} h. Au-delà, fermez l'établissement : "
                + "vos clients sauront que vous ne servez pas aujourd'hui."));
        }

        PausedUntilUtc = untilUtc;
        Touch();
        return Result.Success();
    }

    /// <summary>Fin anticipée de la pause.</summary>
    public Result Resume()
    {
        PausedUntilUtc = null;
        Touch();
        return Result.Success();
    }

    /// <summary>Durée maximale d'une pause.</summary>
    public const int MaxPauseHours = 12;

    /// <summary>PEUT-IL PRENDRE UNE COMMANDE, MAINTENANT ?</summary>
    public OrderingBlockedReason CanAcceptOrders(DateTime nowUtc)
    {
        if (Status != RestaurantStatus.Active)
        {
            return OrderingBlockedReason.NotInService;
        }

        // La pause l'emporte sur les horaires : elle est déclarée PENDANT le
        // service, et c'est justement à ce moment qu'elle doit valoir.
        if (PausedUntilUtc is { } fin && fin > nowUtc)
        {
            return OrderingBlockedReason.TemporarilyPaused;
        }

        return IsWithinServiceHours(nowUtc)
            ? OrderingBlockedReason.None
            : OrderingBlockedReason.OutsideServiceHours;
    }

    /// <summary>LA RÉPONSE COMPLÈTE : L'ÉTABLISSEMENT **ET** SA CARTE.</summary>
    /// <param name="hasOrderableItem">
    /// Reste-t-il au moins UN article commandable ? À l'appelant de l'établir en
    /// n'écartant ni les sections masquées ni les options épuisées — voir <c>
    /// MenuItem.IsOrderableAt</c>.
    /// </param>
    public OrderingBlockedReason CanAcceptOrders(DateTime nowUtc, bool hasOrderableItem)
    {
        var raison = CanAcceptOrders(nowUtc);
        if (raison != OrderingBlockedReason.None)
        {
            return raison;
        }

        return hasOrderableItem ? OrderingBlockedReason.None : OrderingBlockedReason.NothingAvailable;
    }

    /// <summary>L'instant tombe-t-il dans un créneau de service ?</summary>
    private bool IsWithinServiceHours(DateTime nowUtc)
    {
        var local = BeninTime.ToLocal(nowUtc);
        var heure = TimeOnly.FromDateTime(local);

        // L'EXCEPTION DATÉE PRIME SUR L'HORAIRE HEBDOMADAIRE, ET C'EST TOUT SON
        // INTÉRÊT.
        var exception = _specialHours.FirstOrDefault(e => e.Date == DateOnly.FromDateTime(local));
        if (exception is not null)
        {
            return exception.Covers(heure);
        }

        return _serviceHours.Any(c => c.Covers(local.DayOfWeek, heure));
    }

    /// <summary>Décalage horaire du Bénin (UTC+1), constant : pas d'heure d'été.</summary>
    /// <summary>CONSERVÉE POUR LES APPELANTS EXISTANTS, MAIS LA SOURCE EST AILLEURS.</summary>
    public const int BeninUtcOffsetHours = BeninTime.UtcOffsetHours;

    /// <summary>QUAND FINIT « AUJOURD'HUI » POUR CE RESTAURANT ?</summary>
    public DateTime EndOfServiceDayUtc(DateTime nowUtc)
    {
        var local = BeninTime.ToLocal(nowUtc);

        // Minuit suivant, en heure locale.
        var fin = local.Date.AddDays(1);

        // Puis on absorbe LA queue du service : un créneau qui commence à 0 h pile
        // le lendemain est la seconde moitié d'une soirée commencée la veille.
        var queue = _serviceHours.FirstOrDefault(c =>
            c.Day == fin.DayOfWeek
            && c.OpensAt == TimeOnly.MinValue
            && c.ClosesAt != TimeOnly.MaxValue);

        if (queue is not null)
        {
            fin = fin.Date.Add(queue.ClosesAt.ToTimeSpan());
        }

        return BeninTime.ToUtc(fin);
    }

    /// <summary>Rattache le dossier vendeur qui encaissera les recettes.</summary>
    public Result AttachPayoutSeller(Guid sellerId)
    {
        if (sellerId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "food.restaurant.payout_seller_required", "Le dossier vendeur est obligatoire."));
        }

        // ON NE CHANGE PAS DE COMPTE ENCAISSEUR EN PLEIN SERVICE.
        if (PayoutSellerId is { } actuel && actuel != sellerId && Status != RestaurantStatus.Draft)
        {
            return Result.Failure(Error.Conflict(
                "food.restaurant.payout_seller_locked",
                "Mettez l'établissement en pause avant de changer le compte qui reçoit les recettes."));
        }

        PayoutSellerId = sellerId;
        Touch();
        return Result.Success();
    }

    private void Touch() => UpdatedOnUtc = DateTime.UtcNow;
}

/// <summary>Accès aux établissements.</summary>
public interface IRestaurantRepository
{
    Task<Restaurant?> GetByIdAsync(RestaurantId id, CancellationToken cancellationToken = default);

    /// <summary>L'établissement d'un restaurateur, résolu depuis son compte HBA.</summary>
    Task<Restaurant?> GetByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Restaurant>> ListByStatusAsync(
        RestaurantStatus status, int take, CancellationToken cancellationToken = default);

    /// <summary>La VITRINE : les établissements qu'un client a le droit de voir, paginés.</summary>
    Task<IReadOnlyList<Restaurant>> ListPubliclyVisibleAsync(
        int skip, int take, CancellationToken cancellationToken = default);

    Task AddAsync(Restaurant restaurant, CancellationToken cancellationToken = default);
}
