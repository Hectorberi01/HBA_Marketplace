using HBA.Marketplace.ReturnRefund.Domain.ValueObjects;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Domain.Policies;

/// <summary>
/// Une ligne REMBOURSABLE, telle que la commande la décrit : la quantité qu'on
/// accepte de reprendre et le prix unitaire RÉELLEMENT PAYÉ pour elle.
/// </summary>
public sealed record RefundableLine(int Quantity, Money UnitPaidAmount);

public static class RefundCalculationPolicy
{
    /// <summary>Calcule le détail remboursable À PARTIR DE LA COMMANDE.</summary>
    /// <param name="lines">Lignes remboursables, quantités déjà bornées par la commande.</param>
    /// <param name="policy">Politique figée à l'ouverture du dossier (frais de restockage).</param>
    /// <param name="previousRefunds">Ce que CE dossier a déjà engagé — `Pending` compris.</param>
    public static RefundBreakdown Compute(
        IReadOnlyCollection<RefundableLine> lines,
        PolicySnapshot policy,
        Money previousRefunds)
    {
        var currency = previousRefunds.Currency;
        var zero = Money.Zero(currency);

        var items = decimal.Round(
            lines.Sum(line => line.UnitPaidAmount.Amount * Math.Max(0, line.Quantity)),
            2);

        // Le frais de restockage est un pourcentage de la MARCHANDISE reprise, et
        // il est plafonné par elle : un pourcentage mal saisi (120 %) rendrait un
        // détail négatif, que `Total()` écrase ensuite à zéro — donc un plafond nul
        // et un remboursement impossible, sans que rien ne dise pourquoi.
        var restockingFee = decimal.Round(items * Math.Clamp(policy.RestockingFeePercent, 0m, 100m) / 100m, 2);

        return new RefundBreakdown(
            Items: new Money(items, currency),
            Tax: zero,
            OriginalShipping: zero,
            DiscountAllocation: zero,
            RestockingFee: new Money(restockingFee, currency),
            ReturnShippingCharge: zero,
            PreviousRefunds: previousRefunds);
    }

    /// <summary>Le montant décidé tient-il dans ce que la commande permet de rendre ?</summary>
    /// <param name="requested">Montant saisi par le vendeur ou l'administrateur.</param>
    /// <param name="breakdown">
    /// Détail calculé par <see cref="Compute"/> , donc issu de la COMMANDE. Passer
    /// ici un détail construit à partir de <paramref name="requested"/> réduit le
    /// premier contrôle à une tautologie — c'est le défaut ISSUE-049.
    /// </param>
    /// <param name="capturedRemainingCeiling">
    /// Encaissé sur la commande moins ce qui en a déjà été rendu, tel
    /// qu'order-service le déclare (`CapturedAmount − AlreadyRefundedAmount`).
    /// </param>
    /// <param name="alreadyRefunded">
    /// Ce que CE dossier a engagé et qu'order-service ne voit PAS encore —
    /// `Pending` et `Processing`.
    /// </param>
    public static Result Validate(Money requested, RefundBreakdown breakdown, decimal capturedRemainingCeiling, decimal alreadyRefunded)
    {
        if (!string.Equals(requested.Currency, breakdown.Items.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.Validation("refund.currency_mismatch", "La devise du remboursement ne correspond pas au detail."));
        }

        var calculated = breakdown.Total();
        if (requested.Amount > calculated.Amount)
        {
            return Result.Failure(Error.BusinessRule("refund.amount_exceeds_calculated", "Le montant decide depasse le montant calcule cote serveur."));
        }

        if (requested.Amount + alreadyRefunded > capturedRemainingCeiling)
        {
            return Result.Failure(Error.BusinessRule("refund.amount_exceeds_available", "Le cumul des remboursements depasse le montant capture disponible."));
        }

        return Result.Success();
    }
}
