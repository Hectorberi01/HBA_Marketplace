using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Financial;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Financial;

/// <inheritdoc cref="IFinancialClient" />
public sealed class FinancialClient : ServiceHttpClient, IFinancialClient
{
    public FinancialClient(HttpClient http, ILogger<FinancialClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Financial;

    public Task<ServiceResult<DriverWallet>> GetDriverWalletAsync(
        Guid driverId, CancellationToken cancellationToken)
        => GetAsync<DriverWallet>(
            $"/api/financial/wallets/drivers/{driverId}", cancellationToken);

    public Task<ServiceResult<IReadOnlyList<WalletTransaction>>> ListDriverTransactionsAsync(
        Guid driverId, int take, CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<WalletTransaction>>(
            $"/api/financial/wallets/drivers/{driverId}/transactions?take={take}", cancellationToken);

    public Task<ServiceResult<SellerWallet>> GetSellerWalletAsync(
        Guid sellerId, CancellationToken cancellationToken)
        => GetAsync<SellerWallet>(
            $"/api/financial/wallets/sellers/{sellerId}", cancellationToken);
}
