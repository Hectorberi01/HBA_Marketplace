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

/// <summary>
/// Stock d'un SKU sur une localisation donnée. Disponible = OnHand − Reserved
/// (cf. dossier, InventoryItem). Agrégat racine : possède ses réservations.
/// </summary>
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

    /// <summary>
    /// Compteur de mouvements de stock. Incrémenté par TOUTE mutation de l'agrégat.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE COLONNE EXISTE POUR RENDRE LE VERROU OPTIMISTE EFFECTIF. NE PAS LA RETIRER.
    ///
    /// `Reserve()`, `ReleaseReservation()` et `ExpireReservations()` ne modifient AUCUNE
    /// colonne de cette table : ils insèrent ou MARQUENT des lignes ENFANTS
    /// (stock_reservations — depuis ISSUE-045 on ne les supprime plus, on change leur
    /// statut ; le raisonnement est le même, une écriture d'enfant reste une écriture
    /// d'enfant). EF ne génère donc, pour ces opérations, aucun `UPDATE inventory_items`
    /// — et un jeton de concurrence n'est vérifié que dans la clause WHERE d'un UPDATE.
    ///
    /// Conséquence, sans ce compteur : le jeton `xmin` serait posé sur
    /// l'agrégat, visible dans la configuration, rassurant à la relecture — et
    /// TOTALEMENT INERTE sur le seul chemin qui compte. Deux acheteurs réservant le
    /// dernier article au même instant liraient tous deux `Available = 1`, passeraient
    /// tous deux le contrôle, insèreraient tous deux leur réservation. Survente.
    ///
    /// En incrémentant ce compteur, chaque mutation salit la ligne parente : EF émet
    /// `UPDATE inventory_items SET "StockVersion" = … WHERE "Id" = … AND xmin = …`, le
    /// second écrivain touche 0 ligne, et lève `DbUpdateConcurrencyException` → 409.
    ///
    /// La valeur elle-même n'est lue par personne. C'est le fait de l'écrire qui protège.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public int StockVersion { get; private set; }

    public IReadOnlyCollection<StockReservation> Reservations => _reservations.AsReadOnly();

    /// <summary>
    /// Quantité réservée.
    ///
    /// SEULES LES RÉSERVATIONS `Active` COMPTENT, ET C'EST VITAL (ISSUE-045).
    ///
    /// Depuis qu'on ne SUPPRIME plus les lignes mais qu'on les MARQUE, cette
    /// collection contient tout l'historique de l'article : les libérées, les
    /// expirées, et surtout les CONFIRMÉES — dont le stock a déjà quitté `OnHand`.
    ///
    /// Une somme sur toutes les lignes compterait donc chaque vente DEUX fois :
    /// une fois retirée d'`OnHand`, une fois encore ajoutée à `Reserved`. Le
    /// disponible plongerait sous zéro et tout le stock vendable de la plateforme
    /// disparaîtrait d'un coup, silencieusement, à la première migration.
    /// </summary>
    public int Reserved => _reservations.Where(r => r.IsActive).Sum(r => r.Quantity);

    /// <summary>Quantité réellement disponible à la vente.</summary>
    public int Available => OnHand - Reserved;

    public bool IsLowStock => Available <= ReorderThreshold;

    /// <summary>
    /// Salit la ligne parente pour que le verrou optimiste (`xmin`) soit RÉELLEMENT
    /// vérifié — y compris quand la mutation ne touche que des lignes enfants.
    /// Voir le commentaire de <see cref="StockVersion"/>. Appelé par TOUTE mutation.
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

    /// <summary>
    /// Entrée de stock (réception fournisseur / vendeur).
    /// </summary>
    /// <remarks>
    /// ELLE REND DÉSORMAIS LE MOUVEMENT, ET L'APPELANT DOIT L'ENREGISTRER
    /// (ISSUE-044).
    ///
    /// Rien ne gardait trace d'une entrée ou d'un ajustement : ni acteur, ni motif,
    /// ni horodatage — `InventoryItem` n'a même pas de `UpdatedOnUtc`. Voir
    /// <see cref="StockMovement"/>.
    ///
    /// POURQUOI RENDRE LE MOUVEMENT PLUTÔT QUE L'AJOUTER À UNE COLLECTION.
    ///
    /// Les mouvements vivent dans leur propre table : les tenir en collection les
    /// ferait charger avec l'agrégat à chaque réservation, et ils s'accumulent sans
    /// borne. Le rendre force l'appelant à le persister — un oubli est visible à la
    /// lecture du handler, là qu'une collection ignorée ne se verrait nulle part.
    /// </remarks>
    public Result<StockMovement> Receive(int quantity, Guid? actorUserId, string? reason, DateTime nowUtc)
    {
        if (quantity <= 0)
        {
            return Result.Failure<StockMovement>(Error.Validation("inventory.item.quantity_invalid", "La quantité doit être positive."));
        }

        // ON MÉMORISE L'ÉTAT AVANT, PAS APRÈS.
        //
        // L'événement ne doit partir que sur la TRANSITION « épuisé → disponible ».
        // Le lever à chaque réception rejouerait la relance des offres à chaque
        // livraison de réassort, pour rien.
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

    /// <summary>
    /// Réserve du stock pour une commande, si disponible. IDEMPOTENT sur le couple
    /// (cet article, cette commande).
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QUI ÉTAIT CASSÉ : UN REJEU RÉSERVAIT DEUX FOIS (ISSUE-075, CRITICAL).
    ///
    /// Aucune vérification d'une réservation existante n'était faite : chaque
    /// appel INSÉRAIT une ligne de plus. Or l'appelant, `PlaceOrderCommandHandler`,
    /// vit derrière une échéance de 5 s. Un dépassement suivi d'un rejeu — que le
    /// client fait de lui-même, ou que fait le réseau — immobilisait DEUX fois le
    /// stock pour UNE seule vente. La moitié disparaissait sans qu'aucune commande
    /// ne la porte, donc sans que rien ne la libère jamais.
    ///
    /// ON POSE LA QUANTITÉ, ON NE L'AJOUTE PAS.
    ///
    /// S'il existe déjà une réservation `Active` pour cette commande, la nouvelle
    /// quantité REMPLACE l'ancienne. Un rejeu à l'identique ne change donc
    /// strictement rien : ni la ligne, ni `Reserved`, ni `StockVersion`, ni les
    /// événements. C'est la définition même de l'idempotence — et c'est aussi ce
    /// qui évite d'émettre un second `StockReserved` vers l'outbox à chaque
    /// tentative.
    ///
    /// LE DISPONIBLE NE DOIT PAS COMPTER DEUX FOIS CETTE COMMANDE-CI.
    ///
    /// Quand la quantité AUGMENTE (5 → 8), comparer `Available` à 8 serait faux :
    /// `Available` retranche déjà les 5 que cette même commande détient. On
    /// refuserait une extension parfaitement possible. On compare donc à
    /// `Available + déjà réservé par cette commande`, qui est le stock réellement
    /// mobilisable pour elle.
    ///
    /// LA FENÊTRE D'EXPIRATION NE SE RACCOURCIT JAMAIS.
    ///
    /// Un rejeu apporte une nouvelle échéance, calculée par l'appelant à l'instant
    /// présent. On garde la PLUS LOINTAINE des deux. Prendre systématiquement la
    /// nouvelle laisserait une horloge décalée, ou un rejeu tardif d'un message en
    /// file, RACCOURCIR la fenêtre d'une commande en cours — le balayeur
    /// d'expiration libérerait alors du stock sous les pieds d'un paiement qui est
    /// encore en train d'aboutir.
    ///
    /// CETTE MÉTHODE NE FERME PAS LA COURSE, L'INDEX LE FAIT.
    ///
    /// Deux rejeux SIMULTANÉS lisent tous deux « aucune réservation active » et
    /// insèrent tous deux. C'est l'index unique partiel
    /// `ux_stock_reservations_active_order` (migration `StatutDeReservation`) qui
    /// referme cette fenêtre, exactement comme `IX_orders_CartId` le fait pour la
    /// création de commande.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
            // Rejeu strictement identique : rien n'a bougé. Ne PAS appeler Touch()
            // ici — salir la ligne parente pour un no-op provoquerait des conflits
            // 409 entre deux rejeux inoffensifs, et rien ne serait protégé.
            return Result.Success();
        }

        // INDISPENSABLE. L'ajout ci-dessus est une INSERTION dans une table enfant :
        // sans Touch(), EF n'émet aucun UPDATE sur inventory_items, la clause
        // `AND xmin = …` n'est jamais évaluée, et deux réservations concurrentes du
        // dernier article passent toutes les deux. C'est la survente.
        Touch();

        // La quantité annoncée est le TOTAL désormais réservé par cette commande sur
        // cet article, pas un delta : l'événement décrit un état, et un consommateur
        // qui rejoue le message doit retrouver la même vérité.
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

    /// <summary>
    /// Libère les réservations d'une commande (paiement échoué / annulation).
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE RÉSERVATION `Confirmed` N'EST JAMAIS RELÂCHÉE. JAMAIS.
    ///
    /// C'est le danger que l'audit nomme sur `POST /api/inventory/reservations/release`.
    /// Une réservation confirmée est du stock VENDU : `OnHand` a déjà été
    /// décrémenté, l'acheteur a payé, le colis part. La rendre à la vente ne
    /// « libère » rien — elle remet en rayon une marchandise qui n'est plus là, et
    /// la plateforme la vend une seconde fois. Un client attend alors un colis qui
    /// n'existe pas.
    ///
    /// Avant, la question ne se posait même pas : `ConfirmReservation` SUPPRIMAIT
    /// les lignes, donc `RemoveAll` ne trouvait plus rien et l'appel passait pour
    /// inoffensif. Le jour où l'on cesse de supprimer — c'est-à-dire aujourd'hui —
    /// le filtre sur `IsActive` devient la SEULE chose qui empêche la double vente.
    ///
    /// ON REND `Success` MÊME QUAND RIEN N'EST LIBÉRÉ, ET C'EST VOULU.
    ///
    /// La libération est une COMPENSATION : elle est appelée en boucle par
    /// `PlaceOrderCommandHandler` et par l'annulation de commande, souvent sur des
    /// lignes qui n'ont rien réservé. Un refus métier ferait échouer une
    /// compensation qui a, en réalité, exactement obtenu ce qu'elle voulait : plus
    /// rien d'actif pour cette commande. Ce n'est pas un silence — l'historique
    /// garde la trace, et le statut dit pourquoi il n'y avait rien à rendre.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
            // avait été retirée de la vente par `StockDepleted`, il faut la relancer.
            Raise(new StockReplenishedDomainEvent(Id.Value, Sku.Value, LocationId));
        }

        return Result.Success();
    }

    /// <summary>
    /// Confirme la vente : décrémente le stock physique et solde les réservations.
    ///
    /// REJOUABLE SANS DÉCRÉMENTER DEUX FOIS.
    ///
    /// `ConfirmReservationAsync` est appelée en boucle par `OrderLifecycleCommands`,
    /// sur un chemin qui peut être rejoué (webhook de PSP, reprise de saga). Avec
    /// des lignes supprimées, un second appel ne trouvait rien et rendait
    /// `NotFound` — donc `false` à l'appelant, qui lisait « la confirmation a
    /// échoué » alors que la vente était faite. Maintenant la ligne `Confirmed`
    /// subsiste : on la reconnaît, on ne retouche pas `OnHand`, et on rend
    /// `Success`.
    ///
    /// `NotFound` reste réservé au vrai cas d'absence : aucune réservation d'aucune
    /// sorte pour cette commande.
    /// </summary>
    /// <remarks>
    /// ELLE REND UN MOUVEMENT NULLABLE, ET LE NUL EST LE CAS IDEMPOTENT.
    ///
    /// Une confirmation rejouée sur une réservation déjà confirmée ne touche pas
    /// `OnHand` : elle ne doit donc écrire aucune ligne de journal. Rendre un
    /// mouvement à zéro ferait apparaître, dans le journal du vendeur, autant de
    /// lignes vides que de rejeux Kafka.
    /// </remarks>
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
        // par une confirmation. C'est correct — la marchandise n'a jamais été
        // vendable depuis qu'elle était réservée.
        Touch();

        // Aucun acteur : c'est le processus de commande qui confirme, pas une
        // personne. La commande, elle, est nommée — c'est ce qui permet au vendeur
        // de relier une sortie de stock à une vente.
        return StockMovement.Enregistrer(
            Id.Value, Sku.Value, LocationId, StockMovementKind.Sold,
            -reserved, OnHand, actorUserId: null, reason: null,
            reference: $"order:{orderId:N}", nowUtc);
    }

    /// <summary>
    /// Passe en `Expired` les réservations `Active` dont l'échéance est dépassée, et
    /// rend le volume ainsi rendu à la vente.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// `ExpiresAtUtc` ÉTAIT ÉCRITE ET JAMAIS RELUE (ISSUE-031, CRITICAL).
    ///
    /// Aucun code, nulle part dans inventory, ne lisait cette colonne. Toute
    /// réservation non confirmée immobilisait donc son stock DÉFINITIVEMENT :
    /// chaque panier abandonné, chaque paiement laissé en plan, chaque commande
    /// tombée entre deux étapes retirait quelques unités de la vente pour
    /// toujours. L'érosion est silencieuse, cumulative, et ne se voit qu'au moment
    /// où un article affiche « rupture » avec un entrepôt plein.
    ///
    /// UNE RÉSERVATION `Confirmed` N'EST JAMAIS TOUCHÉE, MÊME EXPIRÉE.
    ///
    /// Son `ExpiresAtUtc` est dans le passé — évidemment : la vente a eu lieu il y
    /// a des semaines. Elle n'a plus aucun sens, et la reprendre rendrait à la
    /// vente un stock déjà parti. Le filtre porte sur `IsActive`, pas sur la date
    /// seule.
    ///
    /// IDEMPOTENT PAR CONSTRUCTION. Une réservation déjà `Expired` n'est plus
    /// `Active` : le tour suivant du balayeur ne la reprend pas, et rejouer le
    /// balayage n'ajoute rien. Un lot interrompu reprend là où il en était.
    /// ═════════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// Ajuste le stock physique (inventaire, casse, retour).
    /// </summary>
    /// <remarks>
    /// LE MOTIF EST LA RAISON D'ÊTRE DE CETTE SIGNATURE.
    ///
    /// Elle prenait un `int delta` et rien d'autre. Un stock passant de 400 à 12 ne
    /// laissait aucune trace de qui, quand, ni pourquoi — sur le geste précisément
    /// destiné à consigner « casse », « inventaire », « retour client abîmé ».
    /// </remarks>
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE TRANSFERT ENTRE DEUX LIEUX DU MÊME VENDEUR (ISSUE-044).
    ///
    /// `INVENTORY_TRANSFER` EXISTAIT DEPUIS TOUJOURS ET NE GARDAIT RIEN. Le
    /// rôle `INVENTORY_MANAGER` promet « Stocks, ajustements, transferts » ; le mot
    /// « transfert » n'apparaissait nulle part dans tout inventory-service.
    ///
    /// STATIQUE, PARCE QU'ELLE MUTE DEUX ARTICLES.
    ///
    /// Le stock quitte l'un ET rejoint l'autre. Une méthode d'instance ne pourrait
    /// faire qu'une moitié — et une moitié appliquée seule fait disparaître de la
    /// marchandise. Les deux mouvements portent la MÊME référence, ce qui est le
    /// seul moyen de les recoller après coup.
    ///
    /// ON NE TRANSFÈRE QUE DU DISPONIBLE, PAS DU RÉSERVÉ.
    ///
    /// `Available`, pas `OnHand` : déplacer une marchandise déjà promise à une
    /// commande ferait échouer la confirmation de cette commande dans l'autre
    /// entrepôt, sur un stock qui existe pourtant. Le vendeur doit attendre que la
    /// réservation soit confirmée ou expirée.
    ///
    /// CE QUE CETTE MÉTHODE NE VÉRIFIE PAS.
    ///
    /// Que les deux lieux appartiennent au MÊME vendeur — le domaine ne connaît pas
    /// la propriété : `InventoryItem` ne porte aucun `OwnerId`, elle se déduit par
    /// `location.OwnerId`. C'est l'endpoint qui garde les DEUX lieux, source et
    /// destination. Sans la garde sur la destination, un vendeur transférerait vers
    /// l'entrepôt d'un tiers.
    ///
    /// Et que le SKU soit le même : c'est l'appelant qui charge les deux articles.
    /// La garde est ici quand même — deux références différentes ne se transfèrent
    /// pas, elles se transforment, ce qui n'est pas un mouvement de stock.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
