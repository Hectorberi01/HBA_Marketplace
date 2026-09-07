using HBA.Shared.Domain.Primitives;

namespace HBA.Orders.Domain.Orders;

/// <summary>Une ligne reprise, telle que return-refund l'annonce.</summary>
public sealed record ReturnSettlementLineDraft(Guid OrderItemId, int Quantity);

/// <summary>
/// Ce qu'UN dossier de retour a définitivement retiré à cette commande : l'argent
/// rendu, et les exemplaires repris ligne à ligne.
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

    /// <summary>Le dossier de retour chez return-refund.</summary>
    public Guid ReturnRequestId { get; private set; }

    /// <summary>Ce que ce dossier a rendu au client, tous versements confondus.</summary>
    public decimal RefundedAmount { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    /// <summary>Date du dernier message pris en compte.</summary>
    public DateTime LastSeenAtUtc { get; private set; }

    public IReadOnlyCollection<OrderReturnSettlementLine> Lines => _lines.AsReadOnly();

    /// <summary>Ce que ce dossier a repris sur une ligne de commande donnée.</summary>
    public int QuantityFor(Guid orderItemId)
        => _lines.FirstOrDefault(l => l.OrderItemId == orderItemId)?.Quantity ?? 0;

    /// <summary>Prend en compte un message.</summary>
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
