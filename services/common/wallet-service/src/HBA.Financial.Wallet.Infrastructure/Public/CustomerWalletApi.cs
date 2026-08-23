using MediatR;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Financial.Wallet.Contracts;
using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Infrastructure.Public;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// IMPLÉMENTATION IN-PROCESS DE L'API PUBLIQUE DU PORTEFEUILLE CLIENT (D33).
///
/// C'est par ici que payment-service rend l'argent quand la passerelle ne sait
/// pas rembourser — FedaPay, MTN, Moov, PayPal. Le remboursement PSP reste la
/// voie normale là où elle existe (Stripe rend sur la carte, sans étape de
/// retrait pour le client) : le routage se fait sur `IPaymentGateway.SupportsRefund`,
/// pas ici.
///
/// DÉLÈGUE À MEDIATR, comme `OrderingModuleApi`. La règle métier — la clé
/// d'idempotence exigée, le portefeuille créé à la volée, le registre de rejeu —
/// vit dans le gestionnaire d'Application, un seul endroit. La court-circuiter en
/// appelant le repository directement dupliquerait ces garde-fous dans une classe
/// d'Infrastructure, et le premier correctif appliqué d'un seul côté les ferait
/// diverger sur un flux d'argent.
///
/// Le pipeline MediatR apporte au passage la validation FluentValidation et la
/// journalisation : sur un remboursement, ce n'est pas un coût à éviter.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
