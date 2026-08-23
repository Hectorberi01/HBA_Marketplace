using HBA.Shared.Domain.Primitives;

namespace HBA.Orders.Domain.Orders;

/// <summary>Une ligne reprise, telle que return-refund l'annonce.</summary>
public sealed record ReturnSettlementLineDraft(Guid OrderItemId, int Quantity);

/// <summary>
/// Ce qu'UN dossier de retour a définitivement retiré à cette commande :
/// l'argent rendu, et les exemplaires repris ligne à ligne.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CETTE TABLE EXISTE (ISSUE-014).
///
/// `OrderingModuleApi.GetOrderReturnContextAsync` — la lecture sur laquelle
/// return-refund fonde CHAQUE ouverture de dossier et CHAQUE plafond de
/// remboursement — répondait `AlreadyReturnedQuantity: 0` et
/// `AlreadyRefundedAmount: 0m` EN DUR. Non par négligence de calcul : order-service
/// n'avait littéralement aucune source. Il ne possède pas les retours, et rien ne
/// lui en parlait.
///
/// La conséquence n'était pas théorique. Chaque nouvelle demande repartait de
/// zéro : le même exemplaire pouvait être retourné et remboursé autant de fois
/// qu'on ouvrait de dossiers, chacun validé par un plafond qui ignorait les
/// précédents.
///
/// POURQUOI DANS L'AGRÉGAT COMMANDE, ET NON DANS UNE VUE.
///
/// Parce que c'est un FAIT de la commande : ce qu'elle a rendu. Il est lu au même
/// instant et dans la même transaction que ses lignes, par le même dépôt. Une
/// projection séparée aurait ouvert l'écart habituel — un retour enregistré, une
/// commande qui l'ignore encore, et un second remboursement validé dans
/// l'intervalle.
///
/// ON POSE, ON N'ADDITIONNE PAS.
///
/// `RefundedAmount` et les quantités sont CUMULÉS PAR DOSSIER à la source
/// (`ReturnRefundedIntegrationEvent.ReturnTotalRefundedAmount` et `Lines`). Le
/// consommateur retient le maximum vu, dossier par dossier, au lieu d'additionner
/// les messages. Un message rejoué — Kafka en livre — n'impute donc rien de plus,
/// et un message arrivé dans le désordre ne fait pas RECULER le compteur. C'est la
/// garde qui tient même si l'inbox venait à manquer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class OrderReturnSettlement : Entity<Guid>
{
    private readonly List<OrderReturnSettlementLine> _lines = new();

    private OrderReturnSettlement()
    {
    }

    internal OrderReturnSettlement(Guid id, Guid returnRequestId, DateTime nowUtc)
        : base(id)
    {
        ReturnRequestId = returnRequestId;
        RecordedAtUtc = nowUtc;
        LastSeenAtUtc = nowUtc;
    }

    /// <summary>Le dossier de retour chez return-refund. Unique pour une commande.</summary>
    public Guid ReturnRequestId { get; private set; }

    /// <summary>Ce que ce dossier a rendu au client, tous versements confondus.</summary>
    public decimal RefundedAmount { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    /// <summary>Date du dernier message pris en compte. Sert au diagnostic, pas au calcul.</summary>
    public DateTime LastSeenAtUtc { get; private set; }

    public IReadOnlyCollection<OrderReturnSettlementLine> Lines => _lines.AsReadOnly();

    /// <summary>Ce que ce dossier a repris sur une ligne de commande donnée.</summary>
    public int QuantityFor(Guid orderItemId)
        => _lines.FirstOrDefault(l => l.OrderItemId == orderItemId)?.Quantity ?? 0;

    /// <summary>
    /// Prend en compte un message. Renvoie vrai si quelque chose a bougé.
    /// </summary>
    /// <remarks>
    /// Le maximum, et non la dernière valeur : deux partitions Kafka ne garantissent
    /// aucun ordre entre elles, et un message ancien remis après un récent ferait
    /// sinon rebaisser le montant remboursé — donc remonter le plafond.
    /// </remarks>
    internal bool Retenir(decimal totalRefunded, IReadOnlyCollection<ReturnSettlementLineDraft> lines, DateTime nowUtc)
    {
        var change = false;
        LastSeenAtUtc = nowUtc;

        if (totalRefunded > RefundedAmount)
        {
            RefundedAmount = totalRefunded;
            change = true;
        }

        foreach (var ligne in lines)
        {
            if (ligne.Quantity <= 0)
            {
                continue;
            }

            var existante = _lines.FirstOrDefault(l => l.OrderItemId == ligne.OrderItemId);
            if (existante is null)
            {
                _lines.Add(new OrderReturnSettlementLine(Guid.NewGuid(), ligne.OrderItemId, ligne.Quantity));
                change = true;
                continue;
            }

            change |= existante.Retenir(ligne.Quantity);
        }

        return change;
    }
}

/// <summary>Quantité reprise par un dossier sur une ligne de commande.</summary>
public sealed class OrderReturnSettlementLine : Entity<Guid>
{
    private OrderReturnSettlementLine()
    {
    }

    internal OrderReturnSettlementLine(Guid id, Guid orderItemId, int quantity)
        : base(id)
    {
        OrderItemId = orderItemId;
        Quantity = quantity;
    }

    /// <summary>
    /// L'identifiant de la LIGNE de commande, pas du produit : une même référence
    /// peut figurer sur deux lignes, et le rapprochement porterait sur la mauvaise.
    /// </summary>
    public Guid OrderItemId { get; private set; }

    public int Quantity { get; private set; }

    internal bool Retenir(int quantity)
    {
        if (quantity <= Quantity)
        {
            return false;
        }

        Quantity = quantity;
        return true;
    }
}
