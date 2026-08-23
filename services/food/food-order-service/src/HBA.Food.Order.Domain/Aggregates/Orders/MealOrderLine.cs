using HBA.Shared.Domain.Primitives;

namespace HBA.FoodOrders.Domain.Orders;

/// <summary>Une option de plat à figer dans la commande. Voir <c>MealOrderLineOption</c>.</summary>
public sealed record MealOrderLineOptionDraft(Guid OptionGroupId, Guid OptionId);

/// <summary>
/// Données d'une ligne à figer au paiement.
///
/// UNE SEULE NATURE, DONC AUCUN CHAMP OPTIONNEL DE « L'AUTRE MONDE ».
///
/// Son ancêtre `OrderLineDraft` portait quinze paramètres, dont six vides pour un
/// repas et cinq vides pour un colis, plus un `Kind` par défaut à `Goods` pour
/// dire lesquels lire. Un appelant qui oubliait de le poser fabriquait une ligne
/// de marchandise sans s'en apercevoir — et le défaut ne se voyait qu'au moment
/// où la réservation de stock partait sur un SKU vide.
/// </summary>
public sealed record MealOrderLineDraft(
    Guid MenuItemId,
    string Name,
    int Quantity,
    decimal UnitBasePrice,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalUnitPrice,
    string? Notes = null,
    IReadOnlyList<MealOrderLineOptionDraft>? Options = null);

/// <summary>
/// Ligne de commande : un instantané FIGÉ du plat et de son prix au moment du
/// paiement. Les prix ne bougent plus après — base d'un reversement au
/// restaurateur qui s'audite. Entité enfant de <see cref="MealOrder"/>.
/// </summary>
public sealed class MealOrderLine : Entity<Guid>
{
    private readonly List<MealOrderLineOption> _options = new();

    private MealOrderLine()
    {
    }

    internal MealOrderLine(Guid id, MealOrderLineDraft draft)
        : base(id)
    {
        MenuItemId = draft.MenuItemId;
        Name = draft.Name;
        Notes = draft.Notes;
        Quantity = draft.Quantity;
        UnitBasePrice = draft.UnitBasePrice;
        SellerDiscount = draft.SellerDiscount;
        PlatformDiscount = draft.PlatformDiscount;
        FinalUnitPrice = draft.FinalUnitPrice;

        foreach (var option in draft.Options ?? [])
        {
            _options.Add(new MealOrderLineOption(Guid.NewGuid(), option.OptionGroupId, option.OptionId));
        }
    }

    /// <summary>Le plat dans la carte du restaurant.</summary>
    public Guid MenuItemId { get; private set; }

    /// <summary>
    /// Le nom du plat au moment de l'achat.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// FIGÉ, ET C'EST CE QUI PERMET DE RETIRER UN PLAT SANS RÉÉCRIRE
    ///    L'HISTOIRE.
    ///
    /// `OrderLine`, côté marketplace, n'en portait aucun : l'audit du cahier
    /// panier/commande l'a relevé comme un manque réel. Une commande de l'an
    /// dernier ne pouvait s'afficher qu'en rechargeant la fiche produit — et un
    /// produit renommé réécrivait rétroactivement ce que le client avait acheté,
    /// un produit supprimé rendait la ligne muette.
    ///
    /// `FoodOrderItem`, dans restaurant-service, le figeait déjà de son côté. Le
    /// faire ici aussi n'est pas un doublon : ce sont deux instantanés à deux
    /// instants différents — ce qui a été COMMANDÉ, et ce qui a été SERVI. Ils
    /// peuvent légitimement différer, et c'est ce cas-là qu'il faut pouvoir
    /// constater.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string Name { get; private set; } = default!;

    /// <summary>« Sans piment ». Destiné à la cuisine, figé avec la commande.</summary>
    public string? Notes { get; private set; }

    /// <summary>Les options DEMANDÉES. Libellés et suppléments appartiennent à la cuisine.</summary>
    public IReadOnlyCollection<MealOrderLineOption> Options => _options.AsReadOnly();

    public int Quantity { get; private set; }

    public decimal UnitBasePrice { get; private set; }

    public decimal SellerDiscount { get; private set; }

    public decimal PlatformDiscount { get; private set; }

    public decimal FinalUnitPrice { get; private set; }

    /// <summary>Total payé pour la ligne (prix final unitaire × quantité).</summary>
    public decimal LineTotal => FinalUnitPrice * Quantity;
}
