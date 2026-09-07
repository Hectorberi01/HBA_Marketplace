using FluentValidation;
using HBA.Shared.Application.Context;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Orders.Contracts;
using HBA.Financial.Payments.Contracts;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Contracts;
using HBA.Financial.Wallet.Domain.Wallets;

namespace HBA.Financial.Wallet.Application.Wallets;

// Remboursement DIRECT d'un client, initié par l'admin sur une commande (hors flux
// « retour »).

/// <summary>
/// Rembourse un client sur une commande : débite la plateforme et déclenche un
/// payout FedaPay MoMo vers son numéro.
/// </summary>
public sealed record InitiateCustomerRefundCommand(
    Guid OrderId, decimal Amount, string Msisdn, string Provider, string Reason,
    string? IdempotencyKey = null) : ICommand<CustomerRefundView>;

public sealed class InitiateCustomerRefundCommandValidator : AbstractValidator<InitiateCustomerRefundCommand>
{
    public InitiateCustomerRefundCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m).WithMessage("Le montant doit être positif.");
        RuleFor(c => c.Msisdn).NotEmpty().WithMessage("Le numéro Mobile Money du client est requis.");
        RuleFor(c => c.Provider).NotEmpty().WithMessage("L'opérateur est requis.");
        RuleFor(c => c.Reason).NotEmpty().WithMessage("Un motif est requis.");

        // AUCUNE RÈGLE SUR `IdempotencyKey` ICI, ET CE N'EST PAS UN OUBLI.
    }
}

internal sealed class InitiateCustomerRefundCommandHandler : ICommandHandler<InitiateCustomerRefundCommand, CustomerRefundView>
{
    private readonly ICustomerRefundRepository _refunds;
    private readonly WalletMutations _wallets;
    private readonly IOrderingModuleApi _orders;
    private readonly IPayoutModuleApi _payouts;
    private readonly IWalletUnitOfWork _unitOfWork;

    public InitiateCustomerRefundCommandHandler(
        ICustomerRefundRepository refunds,
        WalletMutations wallets,
        IOrderingModuleApi orders,
        IPayoutModuleApi payouts,
        IWalletUnitOfWork unitOfWork)
    {
        _refunds = refunds;
        _wallets = wallets;
        _orders = orders;
        _payouts = payouts;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CustomerRefundView>> Handle(InitiateCustomerRefundCommand command, CancellationToken cancellationToken)
    {
        // LA CLÉ D'IDEMPOTENCE EST EXIGÉE AVANT TOUT LE RESTE.
        var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? HbaRequestContext.Current.IdempotencyKey
            : command.IdempotencyKey;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result.Failure<CustomerRefundView>(Error.Validation(
                "settlement.idempotency_key_required",
                "L'en-tête Idempotency-Key est obligatoire pour rembourser un client : sans lui, un appel réessayé enverrait un second virement."));
        }

        var order = await _orders.GetOrderAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<CustomerRefundView>(Error.NotFound("settlement.order_not_found", "Commande introuvable."));
        }

        // On ne rembourse qu'une commande RÉELLEMENT ENCAISSÉE (Confirmed /
        // Delivered).
        if (order.Status is not ("Confirmed" or "Delivered"))
        {
            return Result.Failure<CustomerRefundView>(Error.Validation(
                "settlement.order_not_refundable", "Seule une commande encaissée (confirmée ou livrée) peut être remboursée ici."));
        }

        // L'opérateur doit être un Mobile Money routable, sinon le payout échouera
        // — autant refuser avant de débiter la plateforme.
        if (!WalletPayout.IsMobileMoney(command.Provider))
        {
            return Result.Failure<CustomerRefundView>(Error.Validation(
                "settlement.provider_unsupported", "Opérateur Mobile Money non pris en charge (mtnmomo, moovmoney, celtis)."));
        }

        // Plafond : total payé (commande + livraison) moins les remboursements
        // DIRECTS déjà effectués sur cette commande.
        var paidTotal = order.GrandTotal + order.ShippingFee;
        var alreadyRefunded = await _refunds.SumActiveForOrderAsync(command.OrderId, cancellationToken);
        var remaining = paidTotal - alreadyRefunded;
        if (command.Amount > remaining)
        {
            return Result.Failure<CustomerRefundView>(Error.Validation(
                "settlement.refund_exceeds_paid",
                $"Le remboursement dépasse le montant remboursable ({remaining} {order.Currency})."));
        }

        var refund = CustomerRefund.Create(
            command.OrderId, order.BuyerId, command.Amount, order.Currency, command.Reason, command.Msisdn,
            command.Provider, idempotencyKey!);
        await _refunds.AddAsync(refund, cancellationToken);

        // Débit plateforme AVANT le versement (contre-passé si le PSP refuse), pour
        // ne jamais laisser un payout parti sans écriture comptable en regard.
        await _wallets.AccrueCustomerRefundAsync(refund.Amount, refund.Currency, refund.Id.Value, cancellationToken);

        // ON PERSISTE L'INTENTION AVANT D'APPELER LE PRESTATAIRE (ISSUE-074).
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var outcome = await _payouts.SendMobileMoneyPayoutAsync(
            new PayoutInstructionContract(
                Amount: refund.Amount,
                Currency: refund.Currency,
                BeneficiaryName: "Client",
                Msisdn: refund.Msisdn,
                Provider: refund.Provider,
                Reference: $"refund:{refund.Id}"),
            cancellationToken);

        switch (outcome.Status)
        {
            // Rejet DÉFINITIF : rien n'est parti → on contre-passe le débit
            // plateforme.
            case PayoutOutcomeStatus.Failed:
                refund.Fail(outcome.Error ?? "Versement refusé par le prestataire.");
                await _wallets.ReverseCustomerRefundAsync(refund.Amount, refund.Currency, refund.Id.Value, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result.Failure<CustomerRefundView>(Error.Failure("settlement.refund_failed", refund.FailureReason!));

            // Issue INDÉTERMINÉE (timeout/5xx) : le versement est peut-être parti.
            case PayoutOutcomeStatus.Unknown:
                refund.MarkProcessing(outcome.ProviderReference, outcome.Error);
                break;

            // Accepté = créé + démarré chez FedaPay (« started », pas encore « sent
            // ») : la réconciliation clôturera en Completed sur le statut « sent ».
            default:
                refund.MarkProcessing(outcome.ProviderReference);
                break;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletMapper.ToView(refund);
    }
}

/// <summary>
/// Réconcilie les remboursements client « en cours » avec le statut RÉEL du dépôt
/// chez le PSP. Même logique que la réconciliation des retraits : Sent → Completed,
/// Failed → contre-passation du débit plateforme, sinon on ne touche à rien.
/// </summary>
public sealed record ReconcileCustomerRefundsCommand(int BatchSize = 50) : ICommand<int>;

internal sealed class ReconcileCustomerRefundsCommandHandler : ICommandHandler<ReconcileCustomerRefundsCommand, int>
{
    private readonly ICustomerRefundRepository _refunds;
    private readonly IPayoutModuleApi _payouts;
    private readonly WalletMutations _wallets;
    private readonly IWalletUnitOfWork _unitOfWork;

    public ReconcileCustomerRefundsCommandHandler(
        ICustomerRefundRepository refunds,
        IPayoutModuleApi payouts,
        WalletMutations wallets,
        IWalletUnitOfWork unitOfWork)
    {
        _refunds = refunds;
        _payouts = payouts;
        _wallets = wallets;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(ReconcileCustomerRefundsCommand command, CancellationToken cancellationToken)
    {
        var processing = await _refunds.ListProcessingAsync(cancellationToken: cancellationToken);
        var settled = 0;

        foreach (var refund in processing.Take(command.BatchSize))
        {
            // Sans référence PSP (timeout avant l'identifiant), on ne peut rien
            // interroger ni rembourser à l'aveugle : arbitrage humain.
            if (string.IsNullOrWhiteSpace(refund.ProviderRef))
            {
                continue;
            }

            var progress = await _payouts.GetPayoutProgressAsync(refund.ProviderRef!, cancellationToken);

            switch (progress)
            {
                case PayoutProgress.Sent:
                    refund.Complete(refund.ProviderRef);
                    settled++;
                    break;

                case PayoutProgress.Failed:
                    refund.Fail("Versement refusé par le prestataire (statut « failed »).");
                    await _wallets.ReverseCustomerRefundAsync(refund.Amount, refund.Currency, refund.Id.Value, cancellationToken);
                    settled++;
                    break;

                // pending / started / processing / unknown : encore en vol → on ne
                // touche à rien.
                default:
                    break;
            }
        }

        if (settled > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return settled;
    }
}
