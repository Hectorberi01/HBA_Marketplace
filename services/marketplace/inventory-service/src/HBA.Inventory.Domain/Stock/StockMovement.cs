namespace HBA.Inventory.Domain.Stock;

/// <summary>Ce qui a fait bouger le stock physique.</summary>
public enum StockMovementKind
{
    /// <summary>Réception : livraison fournisseur, réassort du vendeur.</summary>
    Received = 0,

    /// <summary>Ajustement : inventaire physique, casse, retour client.</summary>
    Adjusted = 1,

    /// <summary>Vente : une réservation est confirmée, la marchandise part.</summary>
    Sold = 2,

    /// <summary>Sortie vers un autre lieu d'expédition du même vendeur.</summary>
    TransferOut = 3,

    /// <summary>Entrée depuis un autre lieu d'expédition du même vendeur.</summary>
    TransferIn = 4
}

/// <summary>LE JOURNAL DES MOUVEMENTS DE STOCK (ISSUE-044).</summary>
public sealed class StockMovement
{
    private StockMovement()
    {
    }

    private StockMovement(
        Guid id, Guid inventoryItemId, string sku, Guid locationId, StockMovementKind kind,
        int delta, int onHandAfter, Guid? actorUserId, string? reason, string? reference,
        DateTime occurredOnUtc)
    {
        Id = id;
        InventoryItemId = inventoryItemId;
        Sku = sku;
        LocationId = locationId;
        Kind = kind;
        Delta = delta;
        OnHandAfter = onHandAfter;
        ActorUserId = actorUserId;
        Reason = reason;
        Reference = reference;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid Id { get; private init; }

    public Guid InventoryItemId { get; private init; }

    /// <summary>Recopié depuis l'article.</summary>
    public string Sku { get; private init; } = default!;

    /// <summary>
    /// Le lieu, lui aussi recopié — et c'est ce qui rend un transfert lisible : la
    /// sortie porte le lieu source, l'entrée le lieu de destination.
    /// </summary>
    public Guid LocationId { get; private init; }

    public StockMovementKind Kind { get; private init; }

    /// <summary>
    /// Signé. Négatif pour une vente ou une sortie de transfert, positif sinon — un
    /// ajustement peut être des deux signes.
    /// </summary>
    public int Delta { get; private init; }

    /// <summary>Le stock physique APRÈS ce mouvement.</summary>
    public int OnHandAfter { get; private init; }

    /// <summary>
    /// Qui. NUL quand personne n'est derrière — une vente confirmée par le
    /// processus de commande, un balayage d'expiration.
    /// </summary>
    public Guid? ActorUserId { get; private init; }

    /// <summary>Pourquoi, en texte libre.</summary>
    public string? Reason { get; private init; }

    /// <summary>
    /// À quoi le mouvement se rattache : « order:{id} » pour une vente, «
    /// transfer:{id} » pour les deux moitiés d'un transfert.
    /// </summary>
    public string? Reference { get; private init; }

    public DateTime OccurredOnUtc { get; private init; }

    internal static StockMovement Enregistrer(
        Guid inventoryItemId, string sku, Guid locationId, StockMovementKind kind,
        int delta, int onHandAfter, Guid? actorUserId, string? reason, string? reference,
        DateTime occurredOnUtc)
        => new(
            Guid.NewGuid(), inventoryItemId, sku, locationId, kind, delta, onHandAfter,
            actorUserId, Couper(reason, 200), Couper(reference, 100), occurredOnUtc);

    private static string? Couper(string? valeur, int max)
    {
        var propre = valeur?.Trim();
        return string.IsNullOrEmpty(propre)
            ? null
            : propre.Length > max ? propre[..max] : propre;
    }
}

/// <summary>Lecture et écriture du journal des mouvements.</summary>
public interface IStockMovementRepository
{
    Task AddAsync(StockMovement movement, CancellationToken cancellationToken = default);

    /// <summary>Les mouvements d'un article, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<StockMovement>> ListByItemAsync(
        Guid inventoryItemId, int take, CancellationToken cancellationToken = default);
}
