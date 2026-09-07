namespace HBA.Gateway.Application.Contracts.Financial;

/// <summary>Portefeuille d'un livreur — miroir de <c>DriverWalletView</c>.</summary>
public sealed record DriverWallet(
    Guid DriverId,
    decimal AvailableBalance,
    decimal LifetimeEarned,
    string Currency);

/// <summary>Un mouvement de portefeuille — miroir de <c>WalletTransactionView</c>.</summary>
public sealed record WalletTransaction(
    Guid Id,
    string Direction,
    decimal Amount,
    string Currency,
    string Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTime CreatedAtUtc);

/// <summary>Portefeuille d'un vendeur — miroir de <c>SellerWalletView</c>.</summary>
public sealed record SellerWallet(
    Guid SellerId,
    decimal PendingBalance,
    decimal AvailableBalance,
    decimal PendingWithdrawal,
    string Currency);
