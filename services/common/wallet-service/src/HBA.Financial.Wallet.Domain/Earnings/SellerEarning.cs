using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Domain.Earnings;

/// <summary>Identité forte d'un gain vendeur.</summary>
public readonly record struct SellerEarningId(Guid Value)
{
    public static SellerEarningId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Statut d'un gain dans le cycle de reversement : Accrued (comptabilisé à la
/// confirmation, fonds en escrow) → Released (livraison confirmée, escrow libéré,
/// payable) → Settled (inclus dans un lot de payout).
/// </summary>
public enum EarningStatus
{
    Accrued = 0,
    Released = 1,
    Settled = 2,
    Reversed = 3
}

/// <summary>Ce qu'une reprise a RÉELLEMENT pu inscrire sur un gain, APRÈS bornage.</summary>
public sealed record EarningReversal(
    decimal GrossAmount, decimal CommissionAmount, decimal ProviderFeeAmount, decimal NetAmount);

/// <summary>Gain vendeur accumulé pour une ligne de commande confirmée.</summary>
public sealed class SellerEarning : AggregateRoot<SellerEarningId>
{
    private SellerEarning()
    {
    }

    private SellerEarning(
        SellerEarningId id, Guid orderId, Guid offerId, Guid sellerId, Guid productId,
        decimal grossAmount, decimal commissionAmount, decimal providerFeeAmount, string currency)
        : base(id)
    {
        OrderId = orderId;
        OfferId = offerId;
        SellerId = sellerId;
        ProductId = productId;
        GrossAmount = grossAmount;
        CommissionAmount = commissionAmount;
        ProviderFeeAmount = providerFeeAmount;
        NetAmount = grossAmount - commissionAmount - providerFeeAmount;
        Currency = currency;
        Status = EarningStatus.Accrued;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid OrderId { get; private set; }
    public Guid OfferId { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal CommissionAmount { get; private set; }
    public decimal ProviderFeeAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public EarningStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public Guid? SettlementBatchId { get; private set; }

    /// <summary>LE RETRAIT QUI A SOLDÉ CE GAIN — L'AUTRE MOITIÉ DU CYCLE, QUI MANQUAIT.</summary>
    public Guid? SettledByWithdrawalId { get; private set; }

    /// <summary>CE QUI A DÉJÀ ÉTÉ REPRIS SUR CE GAIN — QUATRE CUMULS, PAS UN DRAPEAU.</summary>
    public decimal ReversedGrossAmount { get; private set; }

    /// <inheritdoc cref="ReversedGrossAmount"/>
    public decimal ReversedCommissionAmount { get; private set; }

    /// <inheritdoc cref="ReversedGrossAmount"/>
    public decimal ReversedProviderFeeAmount { get; private set; }

    /// <inheritdoc cref="ReversedGrossAmount"/>
    public decimal ReversedNetAmount { get; private set; }

    /// <summary>CE QUI RESTE DÛ AU VENDEUR SUR CE GAIN — LA SEULE BASE PAYABLE.</summary>
    public decimal RemainingGrossAmount => Math.Max(0m, GrossAmount - ReversedGrossAmount);

    /// <inheritdoc cref="RemainingGrossAmount"/>
    public decimal RemainingCommissionAmount => Math.Max(0m, CommissionAmount - ReversedCommissionAmount);

    /// <inheritdoc cref="RemainingGrossAmount"/>
    public decimal RemainingProviderFeeAmount => Math.Max(0m, ProviderFeeAmount - ReversedProviderFeeAmount);

    /// <inheritdoc cref="RemainingGrossAmount"/>
    public decimal RemainingNetAmount => Math.Max(0m, NetAmount - ReversedNetAmount);

    public static Result<SellerEarning> Create(
        Guid orderId, Guid offerId, Guid sellerId, Guid productId,
        decimal grossAmount, decimal commissionAmount, decimal providerFeeAmount, string currency)
    {
        if (sellerId == Guid.Empty)
        {
            return Error.Validation("settlement.seller_required", "Le vendeur est obligatoire.");
        }

        if (grossAmount < 0m || commissionAmount < 0m || providerFeeAmount < 0m)
        {
            return Error.Validation("settlement.amount_invalid", "Les montants ne peuvent pas être négatifs.");
        }

        return new SellerEarning(
            SellerEarningId.New(), orderId, offerId, sellerId, productId,
            grossAmount, commissionAmount, providerFeeAmount, currency.Trim().ToUpperInvariant());
    }

    /// <summary>Rend le gain payable à la livraison confirmée (escrow libéré).</summary>
    public void Release()
    {
        if (Status != EarningStatus.Accrued)
        {
            return;
        }

        Status = EarningStatus.Released;
        ReleasedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Rattache le gain à un lot de reversement (devient soldé).</summary>
    public bool MarkSettled(Guid settlementBatchId)
    {
        if (Status != EarningStatus.Released)
        {
            return false;
        }

        Status = EarningStatus.Settled;
        SettlementBatchId = settlementBatchId;
        SettledByWithdrawalId = null;
        return true;
    }

    /// <summary>
    /// Solde le gain par un RETRAIT à la demande : l'argent sort du portefeuille
    /// tout de suite, le gain ne doit donc plus jamais entrer dans un lot.
    /// </summary>
    public bool MarkSettledByWithdrawal(Guid withdrawalId)
    {
        if (Status != EarningStatus.Released)
        {
            return false;
        }

        Status = EarningStatus.Settled;
        SettledByWithdrawalId = withdrawalId;
        SettlementBatchId = null;
        return true;
    }

    /// <summary>
    /// Détache le gain de son lot ou de son retrait et le rend à nouveau payable
    /// (annulation d'un lot avant tout versement ; retrait refusé ou échoué).
    /// </summary>
    public void Unsettle()
    {
        if (Status != EarningStatus.Settled)
        {
            return;
        }

        Status = EarningStatus.Released;
        SettlementBatchId = null;
        SettledByWithdrawalId = null;
    }

    /// <summary>REPREND TOUT OU PARTIE DU GAIN, APRÈS UNE VENTE REMBOURSÉE.</summary>
    /// <returns>
    /// Les montants effectivement inscrits, éventuellement rabotés ; un échec si le
    /// gain était déjà entièrement repris ou si un montant demandé est négatif.
    /// </returns>
    public Result<EarningReversal> Reverse(
        decimal grossAmount, decimal commissionAmount, decimal providerFeeAmount, decimal netAmount)
    {
        if (grossAmount < 0m || commissionAmount < 0m || providerFeeAmount < 0m || netAmount < 0m)
        {
            return Error.Validation(
                "settlement.earning.reversal_invalid", "Les montants d'une reprise ne peuvent pas être négatifs.");
        }

        if (Status == EarningStatus.Reversed || (GrossAmount > 0m && ReversedGrossAmount >= GrossAmount))
        {
            return Error.Conflict(
                "settlement.earning.already_reversed", "Ce gain a déjà été entièrement repris.");
        }

        var brut = Math.Min(grossAmount, Math.Max(0m, GrossAmount - ReversedGrossAmount));
        var commission = Math.Min(commissionAmount, Math.Max(0m, CommissionAmount - ReversedCommissionAmount));
        var frais = Math.Min(providerFeeAmount, Math.Max(0m, ProviderFeeAmount - ReversedProviderFeeAmount));
        var net = Math.Min(netAmount, Math.Max(0m, NetAmount - ReversedNetAmount));

        ReversedGrossAmount += brut;
        ReversedCommissionAmount += commission;
        ReversedProviderFeeAmount += frais;
        ReversedNetAmount += net;

        if (ReversedGrossAmount >= GrossAmount)
        {
            // Le gain sort du circuit : « Reversed » n'est ni « Released » ni «
            // Settled », donc ni `ListReleasedInPeriodAsync` ni
            // `ListReleasedBySellerAsync` ne le rendent — il ne peut plus entrer
            // dans un lot ni être consommé par un retrait.
            Status = EarningStatus.Reversed;
        }

        return new EarningReversal(brut, commission, frais, net);
    }
}
