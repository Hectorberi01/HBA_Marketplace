using HBA.Gateway.Application.Contracts.Financial;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>financial-service</c>.</summary>
public interface IFinancialClient : IServiceClient
{
    /// <summary><c>GET /api/financial/wallets/drivers/{driverId}</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<DriverWallet>> GetDriverWalletAsync(
        Guid driverId, CancellationToken cancellationToken);

    /// <summary><c>GET /api/financial/wallets/drivers/{driverId}/transactions?take=</c></summary>
    Task<ServiceResult<IReadOnlyList<WalletTransaction>>> ListDriverTransactionsAsync(
        Guid driverId, int take, CancellationToken cancellationToken);

    /// <summary><c>GET /api/financial/wallets/sellers/{sellerId}</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<SellerWallet>> GetSellerWalletAsync(
        Guid sellerId, CancellationToken cancellationToken);
}
