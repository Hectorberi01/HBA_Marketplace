namespace HBA.Inventory.Domain.Stock;

/// <summary>
/// Ce qui a fait bouger le stock physique.
/// </summary>
/// <remarks>
/// SEULS LES MOUVEMENTS DE `OnHand` FIGURENT ICI. Une RÉSERVATION ne déplace
/// rien : elle rend indisponible ce qui est toujours sur l'étagère. La libérer ou
/// la laisser expirer non plus. C'est pour cela qu'il n'y a ni `Reserved` ni
/// `Released` dans cette énumération — les y mettre ferait un journal où la somme
/// des deltas ne vaudrait plus le stock.
/// </remarks>
public enum StockMovementKind
{
    /// <summary>Réception : livraison fournisseur, réassort du vendeur.</summary>
    Received = 0,

    /// <summary>Ajustement : inventaire physique, casse, retour client.</summary>
    Adjusted = 1,

    /// <summary>
    /// Vente : une réservation est confirmée, la marchandise part.
    ///
    /// INCLUSE DÉLIBÉRÉMENT, malgré la redondance apparente avec le livre des
    /// commandes. Un journal de mouvements qui n'expliquerait pas OÙ le stock est
    /// parti ne serait pas un journal de mouvements : le vendeur ne pourrait pas
    /// rapprocher son `OnHand` de ses entrées, et le premier écart resterait
    /// inexplicable.
    /// </summary>
    Sold = 2,

    /// <summary>Sortie vers un autre lieu d'expédition du même vendeur.</summary>
    TransferOut = 3,

    /// <summary>Entrée depuis un autre lieu d'expédition du même vendeur.</summary>
    TransferIn = 4
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE JOURNAL DES MOUVEMENTS DE STOCK (ISSUE-044).
///
/// ÉCRIT PARCE QUE RIEN NE GARDAIT TRACE D'UN AJUSTEMENT.
///
/// `AdjustOnHand(int delta)` ne prenait NI ACTEUR NI MOTIF. `AdjustStockCommand`
/// portait deux champs : l'article et le delta. `InventoryItem` n'a même pas de
/// `UpdatedOnUtc`. Un stock qui passait de 400 à 12 ne laissait donc aucune trace
/// de qui l'avait fait, quand, ni pourquoi — et `StockVersion`, dont le nom dit
/// « compteur de mouvements », est un jeton de verrou optimiste dont le propre
/// commentaire précise que « la valeur elle-même n'est lue par personne ».
///
/// Deux permissions le promettaient pourtant depuis toujours :
/// `STOCK_MOVEMENT_VIEW` et `INVENTORY_TRANSFER`, toutes deux attribuées à
/// `INVENTORY_MANAGER` — dont la description dit « Stocks, ajustements,
/// transferts ». Aucune des deux ne gardait la moindre route.
///
/// UNE TABLE À PART, PAS UNE COLLECTION DE `InventoryItem`.
///
/// Un transfert touche DEUX articles : en faire un enfant obligerait à écrire la
/// moitié du mouvement dans chacun, et la lecture « qu'est-il arrivé à ce stock »
/// devrait recoller les deux. Surtout, les mouvements s'accumulent sans borne : les
/// charger avec l'agrégat rendrait chaque réservation de plus en plus lente.
///
/// CE N'EST PAS LE JOURNAL D'AUDIT, ET LES DEUX SONT UTILES.
///
/// `audit_entries` répond à « QUI a touché à quoi » et vaut pour toutes les
/// entités d'un schéma ; il ne porte ni delta, ni motif, ni solde. Celui-ci répond
/// à « COMBIEN, D'OÙ, VERS OÙ, ET POURQUOI », et il est LU PAR LE VENDEUR — le
/// journal d'audit, lui, est un outil d'exploitation. `inventory` n'a d'ailleurs
/// pas de journal d'audit.
///
/// `OnHandAfter` EST STOCKÉ, PAS RECALCULÉ.
///
/// Reconstituer le solde en sommant les deltas supposerait que le journal soit
/// complet depuis le premier jour. Il commence ici, sur des articles qui ont déjà
/// un stock : la somme des deltas ne vaudra jamais `OnHand`. Le solde d'après
/// chaque ligne rend la lecture utilisable dès la première, et rend visible tout
/// écart entre le journal et la réalité.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// Recopié depuis l'article.
    ///
    /// DÉNORMALISÉ VOLONTAIREMENT : la question du vendeur est « qu'est-il
    /// arrivé à CETTE référence », pas « à cet identifiant d'article ». Sans cette
    /// colonne, chaque lecture du journal ferait une jointure pour retrouver un
    /// texte qui ne change jamais.
    /// </summary>
    public string Sku { get; private init; } = default!;

    /// <summary>
    /// Le lieu, lui aussi recopié — et c'est ce qui rend un transfert lisible : la
    /// sortie porte le lieu source, l'entrée le lieu de destination.
    /// </summary>
    public Guid LocationId { get; private init; }

    public StockMovementKind Kind { get; private init; }

    /// <summary>
    /// Signé. Négatif pour une vente ou une sortie de transfert, positif sinon —
    /// un ajustement peut être des deux signes.
    /// </summary>
    public int Delta { get; private init; }

    /// <summary>Le stock physique APRÈS ce mouvement. Voir l'encadré du type.</summary>
    public int OnHandAfter { get; private init; }

    /// <summary>
    /// Qui. NUL quand personne n'est derrière — une vente confirmée par le
    /// processus de commande, un balayage d'expiration.
    ///
    /// NUL EST UNE INFORMATION, pas une absence à combler. Inventer un
    /// identifiant ferait passer un traitement automatique pour un employé, et le
    /// jour où le vendeur chercherait qui a vidé son entrepôt, il trouverait un
    /// compte qui n'existe pas.
    /// </summary>
    public Guid? ActorUserId { get; private init; }

    /// <summary>
    /// Pourquoi, en texte libre. C'est LA colonne que l'absence de journal rendait
    /// impossible : « casse », « inventaire trimestriel », « retour client abîmé ».
    /// </summary>
    public string? Reason { get; private init; }

    /// <summary>
    /// À quoi le mouvement se rattache : « order:{id} » pour une vente,
    /// « transfer:{id} » pour les deux moitiés d'un transfert. C'est ce qui permet
    /// de recoller une sortie et son entrée.
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

    /// <summary>
    /// Les mouvements d'un article, du plus récent au plus ancien.
    ///
    /// BORNÉE, ET LA BORNE EST OBLIGATOIRE. Un article très tournant accumule
    /// des milliers de lignes ; une lecture non bornée est exactement le défaut que
    /// la vague 8 recense ailleurs dans ce dépôt. On la ferme avant qu'elle existe.
    /// </summary>
    Task<IReadOnlyList<StockMovement>> ListByItemAsync(
        Guid inventoryItemId, int take, CancellationToken cancellationToken = default);
}
