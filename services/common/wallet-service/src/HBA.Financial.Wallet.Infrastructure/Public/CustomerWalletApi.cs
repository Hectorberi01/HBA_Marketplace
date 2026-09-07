using MediatR;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Financial.Wallet.Contracts;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Infrastructure.Public;

/// <summary>IMPLÉMENTATION IN-PROCESS DE L'API PUBLIQUE DU PORTEFEUILLE CLIENT (D33).</summary>
internal sealed class CustomerWalletApi : ICustomerWalletApi
{
    private readonly ISender _sender;

    public CustomerWalletApi(ISender sender) => _sender = sender;

    public Task<Result<CustomerWalletCreditResult>> CreditRefundAsync(
        Guid customerId, decimal amount, string currency, string reason,
        string idempotencyKey, CancellationToken cancellationToken = default)
        => _sender.Send(
            new CreditCustomerRefundCommand(customerId, amount, currency, reason, idempotencyKey),
            cancellationToken);
}
