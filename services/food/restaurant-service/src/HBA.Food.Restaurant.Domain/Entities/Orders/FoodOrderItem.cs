using HBA.Shared.Domain.Primitives;

namespace HBA.Food.Domain.Orders;

/// <summary>
/// Une option retenue, FIGÉE telle qu'elle était au moment de la commande.
///
/// Le libellé et l'écart de prix sont recopiés, pas référencés. Le restaurateur
/// renommera « Piment » en « Piment fort » et passera le supplément de 200 à
/// 300 F ; la commande d'hier doit continuer de dire ce que le client avait sous
/// les yeux, et ce qu'il a payé.
/// </summary>
public sealed class FoodOrderItemOption : Entity<Guid>
{
    private FoodOrderItemOption()
    {
    }

    /// <summary>
    /// PUBLIC, ALORS QUE LES MUTATIONS DE LIGNE RESTENT INTERNAL.
    ///
    /// Le snapshot se fabrique dans la couche APPLICATION — c'est là que l'article
    /// est relu dans la carte et son prix recalculé (§13). Un constructeur
    /// `internal` l'en empêchait : Application est un autre assembly.
    ///
    /// La tentation était d'ouvrir tout le domaine par un `InternalsVisibleTo`.
    /// Ç'aurait aussi exposé <c>Start</c>, <c>MarkReady</c> et <c>Reopen</c> — et
    /// l'Application aurait pu marquer une ligne prête sans passer par
    /// <c>FoodOrder</c>, donc sans <c>EnterPreparation</c>. L'invariant du §20
    /// — « pas de Ready sans préparation » — serait tombé pour une commodité de
    /// visibilité.
    ///
    /// Seuls les CONSTRUCTEURS s'ouvrent. Les transitions restent fermées.
    /// </summary>
    public FoodOrderItemOption(Guid id, Guid optionId, string groupName, string optionName, decimal priceDelta)
        : base(id)
    {
        OptionId = optionId;
        GroupName = groupName;
        OptionName = optionName;
        PriceDelta = priceDelta;
    }

    /// <summary>Référence vers l'option de la carte. Peut ne plus exister : c'est le snapshot qui fait foi.</summary>
    public Guid OptionId { get; private set; }

    public string GroupName { get; private set; } = default!;
    public string OptionName { get; private set; } = default!;
    public decimal PriceDelta { get; private set; }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE LIGNE DE COMMANDE FOOD — ET LA RÈGLE DE SNAPSHOT DU CAHIER (§13).
///
/// « Le nom, le prix, les options et les instructions commandées doivent être
/// figés dans la commande/ticket afin qu'une modification ultérieure du menu ne
/// change jamais une commande déjà passée. »
///
/// C'EST AUSSI CE QUI REND LA SUPPRESSION D'UN ARTICLE SANS DANGER.
///
/// `DeleteMenuItemCommand` annonçait cette dette : « le jour où le panier et la
/// commande porteront un MenuItemId, une ligne devra figer le libellé et le
/// prix ». C'est ce fichier qui l'honore. Un plat supprimé de la carte laisse
/// intactes toutes les commandes qui le contenaient — <c>MenuItemId</c> n'est
/// qu'une trace, jamais une dépendance de lecture.
///
/// LE PRIX EST FIGÉ APRÈS CALCUL, PAS AVANT.
///
/// <c>UnitPrice</c> est le montant rendu par <c>MenuItem.PriceSelection</c> :
/// base + écarts d'options, validé dans le domaine. Les options ci-dessous ne
/// servent qu'à AFFICHER le détail — les resommer ici donnerait un second calcul
/// qui pourrait diverger du premier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class FoodOrderItem : Entity<Guid>
{
    private readonly List<FoodOrderItemOption> _options = new();

    private FoodOrderItem()
    {
    }

    /// <summary>
    /// PUBLIC, POUR LA MÊME RAISON QUE CELUI DE <see cref="FoodOrderItemOption"/>
    /// — ET LES TRANSITIONS DE CUISINE RESTENT INTERNAL.
    ///
    /// Construire une ligne figée est le geste de l'Application ; la faire passer
    /// de « à préparer » à « prête » est celui de l'agrégat, et de lui seul.
    /// </summary>
    public FoodOrderItem(
        Guid id,
        Guid menuItemId,
        string nameSnapshot,
        decimal unitPrice,
        string currency,
        int quantity,
        string? notes,
        Guid? preparationStationId,
        int preparationMinutes,
        IEnumerable<FoodOrderItemOption> options)
        : base(id)
    {
        MenuItemId = menuItemId;
        NameSnapshot = nameSnapshot;
        UnitPrice = unitPrice;
        Currency = currency;
        Quantity = quantity;
        Notes = notes;
        PreparationStationId = preparationStationId;
        PreparationMinutes = preparationMinutes;
        Status = KitchenItemStatus.Pending;
        _options.AddRange(options);
    }

    /// <summary>Trace vers la carte. L'article peut avoir été supprimé depuis.</summary>
    public Guid MenuItemId { get; private set; }

    /// <summary>Le nom TEL QU'AFFICHÉ au client. C'est lui que lit le cuisinier sur le ticket.</summary>
    public string NameSnapshot { get; private set; } = default!;

    /// <summary>Prix unitaire options COMPRISES, figé au moment de la commande.</summary>
    public decimal UnitPrice { get; private set; }

    public string Currency { get; private set; } = default!;
    public int Quantity { get; private set; }

    /// <summary>« sans oignon », « bien cuit ». Le cahier les fige comme le reste (§13).</summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// Poste de préparation, FIGÉ lui aussi.
    ///
    /// Sans ce snapshot, changer un plat de poste en pleine soirée déplacerait
    /// des tickets DÉJÀ EN COURS d'un écran à l'autre — le grillardin verrait
    /// disparaître ce qu'il est en train de cuire.
    /// </summary>
    public Guid? PreparationStationId { get; private set; }

    /// <summary>Temps de préparation retenu pour cette ligne, en minutes.</summary>
    public int PreparationMinutes { get; private set; }

    public KitchenItemStatus Status { get; private set; }

    public IReadOnlyCollection<FoodOrderItemOption> Options => _options.AsReadOnly();

    /// <summary>Ce que la ligne coûte. Multiplié APRÈS le calcul unitaire, jamais avant.</summary>
    public decimal LineTotal => UnitPrice * Quantity;

    internal bool Start()
    {
        if (Status != KitchenItemStatus.Pending)
        {
            return false;
        }

        Status = KitchenItemStatus.Preparing;
        return true;
    }

    /// <summary>
    /// Marque la ligne prête.
    ///
    /// ACCEPTE LE PASSAGE DIRECT DEPUIS « À PRÉPARER », délibérément.
    ///
    /// Le §20 interdit « Ready sans passage par le workflow de préparation ». La
    /// lecture stricte — refuser tant que le cuisinier n'a pas appuyé sur
    /// « commencer » — punirait le barman qui sert un Coca en cinq secondes, et
    /// finirait par deux appuis machinaux qui ne mesurent plus rien.
    ///
    /// L'invariant est tenu AILLEURS et par construction : c'est
    /// <c>FoodOrder</c> qui passe en préparation dès la première ligne touchée, et
    /// qui n'atteint « prêt » qu'ensuite. La commande ne saute donc jamais l'étape,
    /// même quand le geste du cuisinier, lui, la saute.
    /// </summary>
    internal bool MarkReady()
    {
        if (Status == KitchenItemStatus.Ready)
        {
            return false;
        }

        Status = KitchenItemStatus.Ready;
        return true;
    }

    /// <summary>
    /// La ligne repart en préparation : plat renversé, erreur de saisie.
    ///
    /// SANS CE CHEMIN, UNE COMMANDE MARQUÉE PRÊTE PAR ERREUR EST DÉFINITIVE.
    ///
    /// Un livreur est appelé, arrive, et le passe est vide. Le cuisinier n'a alors
    /// aucun geste à sa disposition : ni « annuler », qui perdrait la commande, ni
    /// « recommencer ». C'est le seul retour en arrière du ticket, et il rend
    /// atteignable la bascule inverse de <c>FoodOrder.SettleReadiness</c> — sans
    /// lui, cette branche serait du code que rien n'appelle.
    /// </summary>
    internal bool Reopen()
    {
        if (Status != KitchenItemStatus.Ready)
        {
            return false;
        }

        Status = KitchenItemStatus.Preparing;
        return true;
    }
}
