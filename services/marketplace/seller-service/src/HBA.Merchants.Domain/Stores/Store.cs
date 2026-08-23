using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Stores.Events;

namespace HBA.Merchants.Domain.Stores;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE BOUTIQUE.
///
/// CE CONCEPT N'EXISTAIT PAS, ET `ProductOffer.StoreId` MENTAIT DEPUIS LE
/// PREMIER JOUR.
///
/// Le champ était peuplé avec l'identifiant du VENDEUR — le BFF Vendeur le
/// documentait lui-même : « la boutique est aujourd'hui identifiée par le
/// vendeur ». La promesse « multi-boutiques » du cahier était donc inapplicable :
/// un marchand tenant une boutique à Cotonou et une à Porto-Novo ne pouvait
/// séparer ni ses offres, ni ses horaires, ni son point de retrait.
///
/// AGRÉGAT RACINE, PAS ENFANT DE SELLER.
///
/// Elle a son propre cycle de vie (ouverte, fermée, suspendue), elle est
/// référencée de l'extérieur par les offres, et un vendeur peut en avoir
/// plusieurs. En faire un enfant de <c>Seller</c> obligerait à charger tout le
/// vendeur — dossier KYB compris — pour changer un horaire.
///
/// ELLE NE PORTE PAS D'ADRESSE, ET C'EST LE POINT D'ARCHITECTURE.
///
/// Le lieu physique existe déjà : <c>FulfillmentLocation</c> (module Inventory)
/// porte l'adresse béninoise, les coordonnées GPS et le téléphone du point de
/// retrait — et c'est LUI que HBA Delivery interroge pour bâtir la course.
/// Recopier ces champs ici créerait deux adresses pour un même lieu, qui
/// divergeraient au premier déménagement. La boutique RÉFÉRENCE son lieu.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
    /// (<c>Inventory.FulfillmentLocation</c>). Nul tant qu'aucun n'a été rattaché.
    ///
    /// SIMPLE GUID, PAS DE RÉFÉRENCE TYPÉE. Sellers ne connaît pas Inventory —
    /// l'existence du lieu est vérifiée par la couche Application, qui a le droit
    /// d'appeler les deux modules. Un type importé ici couplerait le domaine
    /// commercial au domaine logistique pour un contrôle qui ne lui appartient pas.
    /// </summary>
    public Guid? FulfillmentLocationId { get; private set; }

    /// <summary>Motif de la dernière fermeture ou suspension. Nul si ouverte.</summary>
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

    /// <summary>
    /// Reprend une boutique dont l'identifiant est IMPOSÉ.
    ///
    /// RÉSERVÉ À LA MIGRATION DE REPRISE, et il faut comprendre pourquoi.
    ///
    /// Les offres existantes portent déjà un <c>StoreId</c> — qui vaut
    /// l'identifiant du vendeur. Créer la boutique de reprise avec CE
    /// identifiant-là laisse toutes ces offres justes, sans réécrire une seule
    /// ligne de la table des offres.
    ///
    /// La coïncidence « id de boutique = id de vendeur » ne vaut donc que pour la
    /// première boutique de chaque vendeur existant. Les suivantes reçoivent un
    /// identifiant neuf. C'est un artefact de reprise assumé, pas une règle : rien
    /// dans le code ne doit jamais déduire l'un de l'autre.
    /// </summary>
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

    /// <summary>
    /// Rattache le lieu d'où partent les colis.
    ///
    /// L'existence du lieu, et son appartenance à ce vendeur, sont vérifiées par
    /// l'Application : ce domaine ne connaît pas Inventory.
    /// </summary>
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

    /// <summary>
    /// Remplace la grille horaire.
    ///
    /// REMPLACEMENT, PAS AJOUT. Un écran d'horaires se saisit en entier : une
    /// méthode qui ajoute obligerait l'appelant à effacer d'abord, et le jour où
    /// il l'oublierait la boutique afficherait deux grilles superposées.
    /// </summary>
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

    /// <summary>
    /// Ouvre la boutique : ses offres redeviennent achetables.
    ///
    /// UNE BOUTIQUE SUSPENDUE NE S'OUVRE PAS ELLE-MÊME. C'est la différence
    /// entre une fermeture décidée par le vendeur et une sanction : sans cette
    /// garde, la sanction durerait le temps d'un clic.
    /// </summary>
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
        // payé. On refuse ici plutôt que là.
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

    /// <summary>Fermeture décidée par le vendeur (congés, travaux). Réversible.</summary>
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

    /// <summary>Fermeture imposée par la plateforme. Seule la plateforme la lève.</summary>
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
        //
        // Une sanction et des congés arrivaient sous le même type : le consommateur
        // ne pouvait les distinguer qu'en lisant un motif en texte libre. Voir
        // l'encadré de `StoreSuspendedDomainEvent`.
        Raise(new StoreSuspendedDomainEvent(Id.Value, SellerId, StatusReason));
        return Result.Success();
    }

    /// <summary>
    /// Lève une suspension. La boutique repasse en <see cref="StoreStatus.Closed"/>,
    /// PAS en Open.
    ///
    /// ELLE NE ROUVRE PAS TOUTE SEULE, et c'est voulu : la plateforme lève sa
    /// sanction, elle ne décide pas à la place du vendeur qu'il est prêt à vendre.
    /// C'est lui qui rouvre, quand son stock et ses prix sont à jour.
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
        //
        // La boutique reste hors vente, donc rien d'urgent ne s'ensuit — c'est
        // pourquoi l'absence n'avait jamais gêné. Mais un service qui a mémorisé
        // « cette boutique est sanctionnée », pour l'exclure d'un classement ou
        // d'une mise en avant, ne l'apprenait jamais autrement qu'en relisant tout.
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
