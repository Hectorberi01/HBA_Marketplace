namespace HBA.Financial.Wallet.Contracts;

/// <summary>Reversement à un vendeur dans un lot.</summary>
public sealed record PayoutSummary(
    Guid Id,
    Guid SellerId,
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal NetAmount,
    string Currency,
    string Status,
    string? ProviderRef,
    DateTime? PaidAtUtc);

/// <summary>Lot de reversements d'une période.</summary>
public sealed record SettlementBatchSummary(
    Guid Id,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    string Currency,
    decimal TotalNet,
    string Status,
    DateTime CreatedAtUtc,
    IReadOnlyList<PayoutSummary> Payouts);

/// <summary>Relevé d'un vendeur sur une période (ventes, commissions, net).</summary>
/// <param name="ProviderFees">
/// AJOUTÉ APRÈS COUP. `GrossSales - Commissions` ne donne PAS `NetPayout` : il
/// faut aussi retirer les frais du prestataire de paiement. Sans ce champ, le
/// résumé ne s'équilibrait pas et rien ne permettait de savoir pourquoi.
/// </param>
public sealed record SellerStatementSummary(
    Guid SellerId,
    decimal GrossSales,
    decimal Commissions,
    decimal ProviderFees,
    decimal NetPayout,
    string Currency,
    int LineCount);

/// <summary>
/// Ligne détaillée d'un gain vendeur sur la période (une par gain comptabilisé) :
/// sert à produire les écritures détaillées d'un relevé (vente + commission).
/// </summary>
public sealed record SellerStatementLine(
    Guid EarningId,
    Guid OrderId,
    DateTime CreatedAtUtc,
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal ProviderFeeAmount,
    decimal NetAmount,
    string Currency,
    string Status);
