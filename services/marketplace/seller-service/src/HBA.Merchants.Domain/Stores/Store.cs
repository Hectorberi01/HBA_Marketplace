using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Stores.Events;

namespace HBA.Merchants.Domain.Stores;

/// <summary>UNE BOUTIQUE.</summary>
public sealed class Store : AggregateRoot<StoreId>
{
    private readonly List<StoreOpeningHour> _openingHours = new();

    private Store()
    {
    }

    private Store(StoreId id, Guid sellerId, string name, BusinessContact contact)
        : base(id)
    {
        SellerId = sellerId;
        Name = name;
        Contact = contact;
        Status = StoreStatus.Draft;
        CreatedOnUtc = DateTime.UtcNow;

        Raise(new StoreCreatedDomainEvent(id.Value, sellerId, name));
    }

    public Guid SellerId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? LogoUrl { get; private set; }
    public string? Description { get; private set; }
    public BusinessContact Contact { get; private set; } = default!;
    public StoreStatus Status { get; private set; }

    /// <summary>
    /// Le lieu d'où partent les colis de cette boutique
    /// (<c>Inventory.FulfillmentLocation</c>).
    /// </summary>
    public Guid? FulfillmentLocationId { get; private set; }

    /// <summary>Motif de la dernière fermeture ou suspension.</summary>
    public string? StatusReason { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    public IReadOnlyCollection<StoreOpeningHour> OpeningHours => _openingHours.AsReadOnly();

    /// <summary>Ses offres peuvent-elles être achetées ?</summary>
    public bool IsSelling => Status == StoreStatus.Open;

    public static Result<Store> Create(Guid sellerId, string name, BusinessContact contact)
    {
        if (sellerId == Guid.Empty)
        {
            return Error.Validation("sellers.store.seller_required", "La boutique doit appartenir à un vendeur.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("sellers.store.name_required", "Le nom de la boutique est obligatoire.");
        }

        return new Store(StoreId.New(), sellerId, name.Trim(), contact);
    }

    /// <summary>Reprend une boutique dont l'identifiant est IMPOSÉ.</summary>
    public static Result<Store> Reprise(StoreId id, Guid sellerId, string name, BusinessContact contact)
    {
        if (id.Value == Guid.Empty || sellerId == Guid.Empty)
        {
            return Error.Validation("sellers.store.seller_required", "Reprise invalide.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("sellers.store.name_required", "Le nom de la boutique est obligatoire.");
        }

        return new Store(id, sellerId, name.Trim(), contact);
    }

    public Result UpdateProfile(string name, string? logoUrl, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("sellers.store.name_required", "Le nom de la boutique est obligatoire."));
        }

        Name = name.Trim();
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Touch();

        return Result.Success();
    }

    public Result UpdateContact(BusinessContact contact)
    {
        Contact = contact;
        Touch();
        return Result.Success();
    }

    /// <summary>Rattache le lieu d'où partent les colis.</summary>
    public Result AttachFulfillmentLocation(Guid fulfillmentLocationId)
    {
        if (fulfillmentLocationId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "sellers.store.location_required", "Le lieu d'expédition est obligatoire."));
        }

        FulfillmentLocationId = fulfillmentLocationId;
        Touch();
        return Result.Success();
    }

    /// <summary>Remplace la grille horaire.</summary>
    public Result SetOpeningHours(IReadOnlyList<StoreOpeningHour> hours)
    {
        foreach (var (creneau, index) in hours.Select((h, i) => (h, i)))
        {
            // Les chevauchements sont refusés : « 9 h – 13 h » et « 12 h – 18 h »
            // le même jour ne veulent rien dire, et l'affichage devrait trancher
            // arbitrairement.
            if (hours.Where((_, j) => j != index).Any(creneau.Overlaps))
            {
                return Result.Failure(Error.Validation(
                    "sellers.store.hours_overlap",
                    $"Deux créneaux se chevauchent le {creneau.Day}."));
            }
        }

        _openingHours.Clear();
        _openingHours.AddRange(hours);
        Touch();

        return Result.Success();
    }

    /// <summary>Ouvre la boutique : ses offres redeviennent achetables.</summary>
    public Result Open()
    {
        if (Status == StoreStatus.Open)
        {
            return Result.Success();
        }

        if (Status == StoreStatus.Suspended)
        {
            return Result.Failure(Error.Conflict(
                "sellers.store.suspended",
                "Cette boutique est suspendue par la plateforme : elle ne peut pas être rouverte depuis l'espace vendeur."));
        }

        // Sans point de retrait, un colis n'a pas d'origine : HBA Delivery ne peut
        // pas bâtir la course, et l'acheteur découvrirait le blocage après avoir
        // payé.
        if (FulfillmentLocationId is null)
        {
            return Result.Failure(Error.Conflict(
                "sellers.store.location_required",
                "Rattachez un lieu d'expédition avant d'ouvrir la boutique."));
        }

        Status = StoreStatus.Open;
        StatusReason = null;
        Touch();

        Raise(new StoreOpenedDomainEvent(Id.Value, SellerId));
        return Result.Success();
    }

    /// <summary>Fermeture décidée par le vendeur (congés, travaux).</summary>
    public Result Close(string? reason = null)
    {
        if (Status is StoreStatus.Closed or StoreStatus.Suspended)
        {
            return Result.Success();
        }

        Status = StoreStatus.Closed;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();

        Raise(new StoreClosedDomainEvent(Id.Value, SellerId, StatusReason));
        return Result.Success();
    }

    /// <summary>Fermeture imposée par la plateforme.</summary>
    public Result Suspend(string? reason)
    {
        if (Status == StoreStatus.Suspended)
        {
            return Result.Success();
        }

        Status = StoreStatus.Suspended;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();

        // UN ÉVÉNEMENT PROPRE, ET NON `StoreClosed` COMME AUPARAVANT.
        Raise(new StoreSuspendedDomainEvent(Id.Value, SellerId, StatusReason));
        return Result.Success();
    }

    /// <summary>
    /// Lève une suspension. La boutique repasse en <see cref="StoreStatus.Closed"/>
    /// , PAS en Open.
    /// </summary>
    public Result LiftSuspension()
    {
        if (Status != StoreStatus.Suspended)
        {
            return Result.Failure(Error.Conflict(
                "sellers.store.not_suspended", "Cette boutique n'est pas suspendue."));
        }

        Status = StoreStatus.Closed;
        StatusReason = null;
        Touch();

        // CETTE LEVÉE N'ANNONÇAIT RIEN À PERSONNE.
        Raise(new StoreSuspensionLiftedDomainEvent(Id.Value, SellerId));
        return Result.Success();
    }

    private void Touch() => UpdatedOnUtc = DateTime.UtcNow;
}

/// <summary>Accès aux boutiques.</summary>
public interface IStoreRepository
{
    Task<Store?> GetByIdAsync(StoreId id, CancellationToken cancellationToken = default);

    /// <summary>Les boutiques d'un vendeur — le cœur du multi-boutiques.</summary>
    Task<IReadOnlyList<Store>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    Task AddAsync(Store store, CancellationToken cancellationToken = default);
}
