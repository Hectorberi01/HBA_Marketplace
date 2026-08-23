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

// ============================================================================
// Remboursement DIRECT d'un client, initié par l'admin sur une commande (hors flux
// « retour »). FedaPay ne remboursant pas une transaction via l'API, l'argent part
// par un PAYOUT Mobile Money vers le numéro du client, et le coût est débité du
// portefeuille plateforme (solde « refunds »). Statuts et gestion des issues
// indéterminées calqués sur les retraits vendeur : on ne rembourse jamais à
// l'aveugle un versement dont on ignore l'issue (risque de double versement).
// ============================================================================

/// <summary>
/// Rembourse un client sur une commande : débite la plateforme et déclenche un payout
/// FedaPay MoMo vers son numéro. Le montant est plafonné au total payé de la commande,
/// diminué des remboursements directs déjà effectués.
///
/// <para>
/// `IdempotencyKey` est laissée à `null` par un appelant HTTP : le gestionnaire la
/// reprend alors de <see cref="HbaRequestContext"/>, donc de l'en-tête
/// `Idempotency-Key`. Le paramètre existe pour les émetteurs qui n'ont pas de
/// requête entrante — un consommateur Kafka, une reprise — et qui doivent fournir
/// la leur explicitement. Absente des deux côtés, le versement est REFUSÉ.
/// </para>
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
        //
        // Le validateur ne voit que la commande ; il ne sait pas si une requête
        // HTTP porte l'en-tête. Une règle `NotEmpty` rejetterait donc le cas
        // NORMAL — un appelant HTTP qui laisse le gestionnaire lire l'en-tête.
        // Le contrôle est dans `Handle`, où les deux sources sont visibles.
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
        // ═════════════════════════════════════════════════════════════════════
        // LA CLÉ D'IDEMPOTENCE EST EXIGÉE AVANT TOUT LE RESTE.
        //
        // CE QU'ELLE EMPÊCHE : QUE L'ARGENT PARTE DEUX FOIS.
        //
        // Ce gestionnaire déclenche un PAYOUT Mobile Money vers le numéro d'un
        // client. Un appel HTTP réessayé — réseau lent, double-clic, rejeu d'une
        // file — repasserait ici, créerait un second `CustomerRefund` et enverrait
        // un SECOND VIREMENT. Rien ne le rattrape : un payout exécuté chez FedaPay
        // ne s'annule pas, et le client n'a aucune raison de signaler qu'il a reçu
        // trop d'argent.
        //
        // Le §5 rend l'en-tête obligatoire sur les POST de création et de paiement ;
        // un versement en est un. On refuse donc franchement plutôt que de laisser
        // passer.
        //
        // ET ON N'INVENTE PAS DE CLÉ DE REPLI.
        //
        // Une clé dérivée de la commande et du montant paraîtrait protéger et ferait
        // pire : elle interdirait un second remboursement PARTIEL légitime sur la
        // même commande, tout en laissant passer le rejeu dès que le montant change
        // d'un franc. Mieux vaut un 400 explicite qu'une garantie qui ment.
        //
        // Le contrôle est ici, en tête, AVANT le débit du portefeuille plateforme et
        // avant l'appel au PSP : rien ne doit être écrit pour une requête qu'on va
        // refuser.
        // ═════════════════════════════════════════════════════════════════════
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

        // On ne rembourse qu'une commande RÉELLEMENT ENCAISSÉE (Confirmed / Delivered).
        if (order.Status is not ("Confirmed" or "Delivered"))
        {
            return Result.Failure<CustomerRefundView>(Error.Validation(
                "settlement.order_not_refundable", "Seule une commande encaissée (confirmée ou livrée) peut être remboursée ici."));
        }

        // L'opérateur doit être un Mobile Money routable, sinon le payout échouera —
        // autant refuser avant de débiter la plateforme.
        if (!WalletPayout.IsMobileMoney(command.Provider))
        {
            return Result.Failure<CustomerRefundView>(Error.Validation(
                "settlement.provider_unsupported", "Opérateur Mobile Money non pris en charge (mtnmomo, moovmoney, celtis)."));
        }

        // Plafond : total payé (commande + livraison) moins les remboursements DIRECTS
        // déjà effectués sur cette commande.
        // Ne nette PAS encore les remboursements issus du flux « retour ». À suivre si
        // les deux flux doivent partager un plafond commun.
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

        // Débit plateforme AVANT le versement (contre-passé si le PSP refuse), pour ne
        // jamais laisser un payout parti sans écriture comptable en regard.
        await _wallets.AccrueCustomerRefundAsync(refund.Amount, refund.Currency, refund.Id.Value, cancellationToken);

        // ═════════════════════════════════════════════════════════════════════
        // ON PERSISTE L'INTENTION AVANT D'APPELER LE PRESTATAIRE (ISSUE-074).
        //
        // Ce `SaveChanges` n'existait pas. Le PSP était appelé alors que RIEN
        // n'était encore en base : ni la ligne `customer_refunds`, ni l'écriture
        // comptable. Un incident entre l'appel et le premier enregistrement — le
        // processus qui tombe, le conteneur qu'on remplace, la base qui refuse —
        // laissait l'argent PARTI et aucune trace de son départ.
        //
        // Ce n'est pas un cas d'école : `ListProcessingAsync` est la seule entrée
        // de la réconciliation, et elle lit cette table. Sans ligne, la
        // réconciliation ne voit rien, ne cherche rien, et le versement n'existe
        // que sur le relevé du prestataire. Il faut alors rapprocher les deux à la
        // main, en devinant lequel des remboursements demandés correspond.
        //
        // CE QUE CE PREMIER ENREGISTREMENT INSCRIT EXACTEMENT.
        //
        // Un remboursement en `Processing` SANS référence de prestataire. C'est
        // exactement ce que veut dire cet état ailleurs dans le dépôt : « le
        // versement est peut-être parti, on ne sait pas ». Le débit plateforme est
        // déjà posé et ne sera contre-passé que sur un refus DÉFINITIF — jamais sur
        // une issue indéterminée, sous peine de double versement.
        //
        // ET SI L'INCIDENT SURVIENT MAINTENANT, ENTRE LES DEUX ?
        //
        // La ligne existe, sans `ProviderRef`. `ReconcileCustomerRefundsCommand` la
        // rencontre, ne peut rien interroger sans référence, et la SAUTE — c'est
        // écrit dans son code. Elle reste donc en attente d'un arbitrage humain,
        // mais elle est VISIBLE, chiffrée, rattachée à une commande et à un client.
        // C'est toute la différence avec l'état d'avant.
        //
        // Le même motif est déjà appliqué correctement par
        // `RefundPaymentCommandHandler` côté payments : la demande est persistée
        // avant l'appel, précisément pour ce cas.
        // ═════════════════════════════════════════════════════════════════════
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
            // Rejet DÉFINITIF : rien n'est parti → on contre-passe le débit plateforme.
            case PayoutOutcomeStatus.Failed:
                refund.Fail(outcome.Error ?? "Versement refusé par le prestataire.");
                await _wallets.ReverseCustomerRefundAsync(refund.Amount, refund.Currency, refund.Id.Value, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result.Failure<CustomerRefundView>(Error.Failure("settlement.refund_failed", refund.FailureReason!));

            // Issue INDÉTERMINÉE (timeout/5xx) : le versement est peut-être parti. On NE
            // contre-passe PAS — la réconciliation tranchera.
            case PayoutOutcomeStatus.Unknown:
                refund.MarkProcessing(outcome.ProviderReference, outcome.Error);
                break;

            // Accepté = créé + démarré chez FedaPay (« started », pas encore « sent ») :
            // la réconciliation clôturera en Completed sur le statut « sent ».
            default:
                refund.MarkProcessing(outcome.ProviderReference);
                break;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletMapper.ToView(refund);
    }
}

/// <summary>
/// Réconcilie les remboursements client « en cours » avec le statut RÉEL du dépôt chez
/// le PSP. Même logique que la réconciliation des retraits : Sent → Completed, Failed →
/// contre-passation du débit plateforme, sinon on ne touche à rien. Idempotent.
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
            // Sans référence PSP (timeout avant l'identifiant), on ne peut rien interroger
            // ni rembourser à l'aveugle : arbitrage humain.
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

                // pending / started / processing / unknown : encore en vol → on ne touche à rien.
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
