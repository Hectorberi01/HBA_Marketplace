using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Inventory.Domain.Common;
using HBA.Inventory.Domain.Stock.Events;

namespace HBA.Inventory.Domain.Stock;

public readonly record struct InventoryItemId(Guid Value)
{
    public static InventoryItemId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Stock d'un SKU sur une localisation donnée.</summary>
public sealed class InventoryItem : AggregateRoot<InventoryItemId>
{
    private readonly List<StockReservation> _reservations = new();

    private InventoryItem()
    {
    }

    private InventoryItem(InventoryItemId id, Sku sku, Guid locationId, int onHand, int reorderThreshold)
        : base(id)
    {
        Sku = sku;
        LocationId = locationId;
        OnHand = onHand;
        ReorderThreshold = reorderThreshold;

        Raise(new InventoryItemCreatedDomainEvent(id.Value, sku.Value, locationId));
    }

    public Sku Sku { get; private set; } = default!;
    public Guid LocationId { get; private set; }
    public int OnHand { get; private set; }
    public int ReorderThreshold { get; private set; }

    /// <summary>Compteur de mouvements de stock.</summary>
    public int StockVersion { get; private set; }

    public IReadOnlyCollection<StockReservation> Reservations => _reservations.AsReadOnly();

    /// <summary>Quantité réservée.</summary>
    public int Reserved => _reservations.Where(r => r.IsActive).Sum(r => r.Quantity);

    /// <summary>Quantité réellement disponible à la vente.</summary>
    public int Available => OnHand - Reserved;

    public bool IsLowStock => Available <= ReorderThreshold;

    /// <summary>
    /// Salit la ligne parente pour que le verrou optimiste (`xmin`) soit RÉELLEMENT
    /// vérifié — y compris quand la mutation ne touche que des lignes enfants.
    /// </summary>
    private void Touch() => StockVersion++;

    public static Result<InventoryItem> Create(Sku sku, Guid locationId, int onHand, int reorderThreshold)
    {
        if (locationId == Guid.Empty)
        {
            return Error.Validation("inventory.item.location_required", "La localisation est obligatoire.");
        }

        if (onHand < 0)
        {
            return Error.Validation("inventory.item.onhand_negative", "Le stock physique ne peut pas être négatif.");
        }

        if (reorderThreshold < 0)
        {
            return Error.Validation("inventory.item.threshold_negative", "Le seuil de réapprovisionnement ne peut pas être négatif.");
        }

        return new InventoryItem(InventoryItemId.New(), sku, locationId, onHand, reorderThreshold);
    }

    /// <summary>Entrée de stock (réception fournisseur / vendeur).</summary>
    public Result<StockMovement> Receive(int quantity, Guid? actorUserId, string? reason, DateTime nowUtc)
    {
        if (quantity <= 0)
        {
            return Result.Failure<StockMovement>(Error.Validation("inventory.item.quantity_invalid", "La quantité doit être positive."));
        }

        // ON MÉMORISE L'ÉTAT AVANT, PAS APRÈS.
        var etaitEpuise = Available == 0;

        OnHand += quantity;
        Touch();

        if (etaitEpuise && Available > 0)
        {
            Raise(new StockReplenishedDomainEvent(Id.Value, Sku.Value, LocationId));
        }

        return StockMovement.Enregistrer(
            Id.Value, Sku.Value, LocationId, StockMovementKind.Received,
            quantity, OnHand, actorUserId, reason, reference: null, nowUtc);
    }

    /// <summary>Réserve du stock pour une commande, si disponible.</summary>
    public Result Reserve(Guid orderId, int quantity, DateTime expiresAtUtc)
    {
        if (quantity <= 0)
        {
            return Result.Failure(Error.Validation("inventory.item.quantity_invalid", "La quantité doit être positive."));
        }

        var existante = _reservations.FirstOrDefault(r => r.OrderId == orderId && r.IsActive);

        // Ce que cette commande immobilise déjà : il est déduit d'`Available`, donc
        // il doit être rendu au disponible avant de juger la nouvelle quantité.
        var dejaTenuParCetteCommande = existante?.Quantity ?? 0;

        if (Available + dejaTenuParCetteCommande < quantity)
        {
            return Result.Failure(Error.Conflict("inventory.item.insufficient_stock", "Stock insuffisant pour la réservation."));
        }

        // ON MÉMORISE L'ÉTAT AVANT. Une réservation qui RÉTRÉCIT au rejeu remet du
        // stock en vente : c'est la même transition « épuisé → disponible » que
        // `Receive`, et elle mérite le même événement.
        var etaitEpuise = Available == 0;

        bool aChange;

        if (existante is null)
        {
            _reservations.Add(new StockReservation(Guid.NewGuid(), orderId, quantity, expiresAtUtc));
            aChange = true;
        }
        else
        {
            var echeance = expiresAtUtc > existante.ExpiresAtUtc ? expiresAtUtc : existante.ExpiresAtUtc;
            aChange = existante.Quantity != quantity || echeance != existante.ExpiresAtUtc;
            existante.Restate(quantity, echeance);
        }

        if (!aChange)
        {
            // Rejeu strictement identique : rien n'a bougé.
            return Result.Success();
        }

        // INDISPENSABLE. L'ajout ci-dessus est une INSERTION dans une table enfant
        // : sans Touch(), EF n'émet aucun UPDATE sur inventory_items, la clause
        // `AND xmin = …` n'est jamais évaluée, et deux réservations concurrentes du
        // dernier article passent toutes les deux.
        Touch();

        // La quantité annoncée est le TOTAL désormais réservé par cette commande
        // sur cet article, pas un delta : l'événement décrit un état, et un
        // consommateur qui rejoue le message doit retrouver la même vérité.
        Raise(new StockReservedDomainEvent(Id.Value, Sku.Value, orderId, quantity));

        if (!etaitEpuise && Available == 0)
        {
            Raise(new StockDepletedDomainEvent(Id.Value, Sku.Value, LocationId));
        }

        if (etaitEpuise && Available > 0)
        {
            Raise(new StockReplenishedDomainEvent(Id.Value, Sku.Value, LocationId));
        }

        return Result.Success();
    }

    /// <summary>Libère les réservations d'une commande (paiement échoué / annulation).</summary>
    public Result ReleaseReservation(Guid orderId, DateTime nowUtc)
    {
        var aLiberer = _reservations.Where(r => r.OrderId == orderId && r.IsActive).ToList();
        if (aLiberer.Count == 0)
        {
            // Rien d'actif : commande déjà libérée, déjà expirée, ou déjà VENDUE.
            // Aucune écriture, donc pas de Touch() — voir Reserve().
            return Result.Success();
        }

        var etaitEpuise = Available == 0;

        foreach (var reservation in aLiberer)
        {
            reservation.Release(nowUtc);
        }

        Touch(); // Mutation d'enfants uniquement : même piège que Reserve().

        if (etaitEpuise && Available > 0)
        {
            // Même transition qu'une réception, par une troisième porte : l'offre
            // avait été retirée de la vente par `StockDepleted`, il faut la
            // relancer.
            Raise(new StockReplenishedDomainEvent(Id.Value, Sku.Value, LocationId));
        }

        return Result.Success();
    }

    /// <summary>
    /// Confirme la vente : décrémente le stock physique et solde les réservations.
    /// </summary>
    public Result<StockMovement?> ConfirmReservation(Guid orderId, DateTime nowUtc)
    {
        var actives = _reservations.Where(r => r.OrderId == orderId && r.IsActive).ToList();

        if (actives.Count == 0)
        {
            var dejaVendue = _reservations.Any(r => r.OrderId == orderId && r.Status == ReservationStatus.Confirmed);
            if (dejaVendue)
            {
                return Result.Success<StockMovement?>(null);
            }

            return Result.Failure<StockMovement?>(Error.NotFound("inventory.item.reservation_not_found", "Aucune réservation pour cette commande."));
        }

        var reserved = actives.Sum(r => r.Quantity);

        OnHand -= reserved;

        foreach (var reservation in actives)
        {
            reservation.Confirm(nowUtc);
        }

        // `Reserved` baisse d'autant que `OnHand` : `Available` est donc INCHANGÉ
        // par une confirmation.
        Touch();

        // Aucun acteur : c'est le processus de commande qui confirme, pas une
        // personne.
        return StockMovement.Enregistrer(
            Id.Value, Sku.Value, LocationId, StockMovementKind.Sold,
            -reserved, OnHand, actorUserId: null, reason: null,
            reference: $"order:{orderId:N}", nowUtc);
    }

    /// <summary>
    /// Passe en `Expired` les réservations `Active` dont l'échéance est dépassée,
    /// et rend le volume ainsi rendu à la vente.
    /// </summary>
    public StockExpirySweep ExpireReservations(DateTime nowUtc)
    {
        var expirables = _reservations.Where(r => r.IsExpirableAt(nowUtc)).ToList();
        if (expirables.Count == 0)
        {
            return default;
        }

        var etaitEpuise = Available == 0;
        var volume = expirables.Sum(r => r.Quantity);

        foreach (var reservation in expirables)
        {
            reservation.Expire(nowUtc);
        }

        Touch(); // Mutation d'enfants uniquement : même piège que Reserve().

        if (etaitEpuise && Available > 0)
        {
            // Sans cet événement, le balayage rendrait le stock vendable en base
            // pendant que l'offre resterait affichée « en rupture » : ISSUE-031
            // serait corrigée dans inventory et invisible pour l'acheteur.
            Raise(new StockReplenishedDomainEvent(Id.Value, Sku.Value, LocationId));
        }

        return new StockExpirySweep(expirables.Count, volume);
    }

    /// <summary>Ajuste le stock physique (inventaire, casse, retour).</summary>
    public Result<StockMovement> AdjustOnHand(int delta, Guid? actorUserId, string? reason, DateTime nowUtc)
    {
        if (delta == 0)
        {
            // Un ajustement nul écrirait une ligne de journal qui ne dit rien, et
            // ferait passer un formulaire mal rempli pour une décision.
            return Result.Failure<StockMovement>(Error.Validation(
                "inventory.item.adjust_zero", "Un ajustement de zéro ne veut rien dire."));
        }

        if (OnHand + delta < Reserved)
        {
            return Result.Failure<StockMovement>(Error.Conflict("inventory.item.adjust_below_reserved", "L'ajustement passerait le stock sous le réservé."));
        }

        // Même transition, par l'autre porte : un inventaire physique ou un retour
        // client peut ramener du stock sans passer par une réception.
        var etaitEpuise = Available == 0;

        OnHand += delta;
        Touch();

        if (etaitEpuise && Available > 0)
        {
            Raise(new StockReplenishedDomainEvent(Id.Value, Sku.Value, LocationId));
        }

        return StockMovement.Enregistrer(
            Id.Value, Sku.Value, LocationId, StockMovementKind.Adjusted,
            delta, OnHand, actorUserId, reason, reference: null, nowUtc);
    }

    /// <summary>LE TRANSFERT ENTRE DEUX LIEUX DU MÊME VENDEUR (ISSUE-044).</summary>
    public static Result<(StockMovement Sortie, StockMovement Entree)> Transfer(
        InventoryItem source,
        InventoryItem destination,
        int quantity,
        Guid? actorUserId,
        string? reason,
        DateTime nowUtc)
    {
        if (quantity <= 0)
        {
            return Result.Failure<(StockMovement, StockMovement)>(Error.Validation(
                "inventory.transfer.quantity_invalid", "La quantité doit être positive."));
        }

        if (source.Id == destination.Id)
        {
            return Result.Failure<(StockMovement, StockMovement)>(Error.Validation(
                "inventory.transfer.same_item", "La source et la destination sont le même article."));
        }

        if (!source.Sku.Equals(destination.Sku))
        {
            return Result.Failure<(StockMovement, StockMovement)>(Error.Validation(
                "inventory.transfer.sku_mismatch",
                "Un transfert déplace la même référence d'un lieu à un autre."));
        }

        if (source.LocationId == destination.LocationId)
        {
            return Result.Failure<(StockMovement, StockMovement)>(Error.Validation(
                "inventory.transfer.same_location", "La source et la destination sont le même lieu."));
        }

        if (source.Available < quantity)
        {
            return Result.Failure<(StockMovement, StockMovement)>(Error.Conflict(
                "inventory.transfer.insufficient_available",
                $"Seules {source.Available} unité(s) sont disponibles au lieu de départ ; "
                + "le reste est réservé à des commandes en cours."));
        }

        var reference = $"transfer:{Guid.NewGuid():N}";

        var sourceEtaitDisponible = source.Available > 0;

        source.OnHand -= quantity;
        source.Touch();

        // Même règle que partout : l'événement ne part que sur la TRANSITION.
        if (sourceEtaitDisponible && source.Available == 0)
        {
            source.Raise(new StockDepletedDomainEvent(source.Id.Value, source.Sku.Value, source.LocationId));
        }

        var destinationEtaitEpuisee = destination.Available == 0;

        destination.OnHand += quantity;
        destination.Touch();

        if (destinationEtaitEpuisee && destination.Available > 0)
        {
            destination.Raise(new StockReplenishedDomainEvent(
                destination.Id.Value, destination.Sku.Value, destination.LocationId));
        }

        return (
            StockMovement.Enregistrer(
                source.Id.Value, source.Sku.Value, source.LocationId, StockMovementKind.TransferOut,
                -quantity, source.OnHand, actorUserId, reason, reference, nowUtc),
            StockMovement.Enregistrer(
                destination.Id.Value, destination.Sku.Value, destination.LocationId,
                StockMovementKind.TransferIn,
                quantity, destination.OnHand, actorUserId, reason, reference, nowUtc));
    }

    public Result SetReorderThreshold(int reorderThreshold)
    {
        if (reorderThreshold < 0)
        {
            return Result.Failure(Error.Validation("inventory.item.threshold_negative", "Le seuil ne peut pas être négatif."));
        }

        ReorderThreshold = reorderThreshold;
        Touch();
        return Result.Success();
    }
}
