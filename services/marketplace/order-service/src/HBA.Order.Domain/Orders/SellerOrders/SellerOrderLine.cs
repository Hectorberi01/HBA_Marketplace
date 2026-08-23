using HBA.Shared.Domain.Primitives;

namespace HBA.Orders.Domain.Orders.SellerOrders;

/// <summary>
/// Une ligne de la commande, vue par le vendeur qui la vend.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE COPIE, PAS UNE CLÉ ÉTRANGÈRE VERS <see cref="OrderLine"/>.
///
/// La tentation était de ne stocker que <see cref="OrderLineId"/> et de relire la
/// ligne d'origine à chaque affichage. Deux raisons de ne pas le faire :
///
///   • L'ÉVÉNEMENT DE REFUS doit être auto-suffisant. Il porte le SKU,
///     l'emplacement d'expédition et le montant pour que trois autres services
///     puissent agir sans rappeler order-service — voir
///     `SellerOrderRefusedDomainEvent`. Les recharger au moment de publier
///     rendrait la publication dépendante d'une lecture qui peut échouer, à
///     l'intérieur d'un `SaveChanges` ;
///   • LE CARNET DU VENDEUR se lit sans jointure vers `order_lines`, donc sans
///     risque d'y voir passer les lignes d'un concurrent. C'est la projection qui
///     a déjà fuité une fois (voir `OrderMapper.ToSellerSummary`), et ce qui n'est
///     pas chargé ne peut pas fuiter.
///
/// <see cref="OrderLineId"/> RESTE, ET C'EST LE LIEN QUI COMPTE.
///
/// C'est par lui qu'un retour se rapproche (`OrderReturnSettlementLine` désigne
/// la LIGNE, pas le produit — une même référence peut figurer deux fois). Sans
/// lui, la part d'un vendeur ne serait rapprochable de rien.
///
/// CES VALEURS NE CHANGENT JAMAIS APRÈS LA CRÉATION. C'est ce qui permet à
/// <see cref="SellerOrder"/> de se passer d'un compteur à la `StockVersion` :
/// aucune de ses transitions n'écrit sur un enfant, donc toutes écrivent sur le
/// parent, donc le verrou optimiste est réellement évalué.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SellerOrderLine : Entity<Guid>
{
    private SellerOrderLine()
    {
    }

    internal SellerOrderLine(
        Guid id,
        Guid orderLineId,
        Guid productId,
        string sku,
        Guid shipFromLocationId,
        int quantity,
        decimal unitPaidAmount)
        : base(id)
    {
        OrderLineId = orderLineId;
        ProductId = productId;
        Sku = sku;
        ShipFromLocationId = shipFromLocationId;
        Quantity = quantity;
        UnitPaidAmount = unitPaidAmount;
    }

    /// <summary>La ligne d'origine dans <c>ordering.order_lines</c>.</summary>
    public Guid OrderLineId { get; private set; }

    public Guid ProductId { get; private set; }

    /// <summary>Non nul, possiblement vide — même convention qu'`OrderLine.Sku`.</summary>
    public string Sku { get; private set; } = default!;

    /// <summary>D'où part le colis. Sans lui, aucun stock ne se rend (voir l'événement de refus).</summary>
    public Guid ShipFromLocationId { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Prix unitaire FINAL payé, remises comprises. Figé avec la commande.</summary>
    public decimal UnitPaidAmount { get; private set; }

    /// <summary>Total payé pour cette ligne. Calculé : sans l'`Ignore`, EF réclamerait une colonne.</summary>
    public decimal LineTotal => UnitPaidAmount * Quantity;
}
