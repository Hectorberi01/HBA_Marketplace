namespace HBA.Gateway.Application.Contracts.Financial;

/// <summary>Portefeuille d'un livreur — miroir de <c>DriverWalletView</c>.</summary>
/// <remarks>
/// `LifetimeEarned` N'EST PAS `AvailableBalance`, ET L'ÉCART EST LE POINT.
///
/// Le solde seul ne dit rien de ce qu'on a gagné : un retrait le remet à zéro.
/// C'est le cumul que le livreur regarde pour savoir si le métier le nourrit —
/// le commentaire du contrat amont le dit mieux que moi.
/// </remarks>
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
/// <remarks>
/// TROIS SOLDES, ET LES CONFONDRE PROMET DE L'ARGENT QUI N'EXISTE PAS.
///
/// `PendingBalance` : encaissé, pas encore libéré (délai de réclamation).
/// `AvailableBalance` : retirable maintenant.
/// `PendingWithdrawal` : déjà demandé, en cours de virement — donc plus
/// disponible, et pourtant pas encore parti.
/// </remarks>
public sealed record SellerWallet(
    Guid SellerId,
    decimal PendingBalance,
    decimal AvailableBalance,
    decimal PendingWithdrawal,
    string Currency);
