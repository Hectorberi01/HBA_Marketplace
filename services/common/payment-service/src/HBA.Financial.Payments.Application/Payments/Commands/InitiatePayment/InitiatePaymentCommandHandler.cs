using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Application.Abstractions.Gateways;
using HBA.Financial.Payments.Domain.Payments;
using Microsoft.Extensions.Logging;

namespace HBA.Financial.Payments.Application.Payments.Commands.InitiatePayment;

/// <summary>
/// Initie un paiement : lit le montant de la commande via Ordering (Contracts),
/// refuse si la commande n'attend pas de paiement ou si un paiement est déjà en
/// cours, crée le paiement (Pending), puis ouvre la session auprès du PSP
/// (checkout hébergé ou intention) et rattache sa référence pour la corrélation.
/// </summary>
internal sealed class InitiatePaymentCommandHandler : ICommandHandler<InitiatePaymentCommand, InitiatePaymentResult>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPayableOrderReader _commandes;
    private readonly IPaymentGatewayResolver _gatewayResolver;
    private readonly IPaymentsUnitOfWork _unitOfWork;
    private readonly ILogger<InitiatePaymentCommandHandler> _logger;

    public InitiatePaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IPayableOrderReader commandes,
        IPaymentGatewayResolver gatewayResolver,
        IPaymentsUnitOfWork unitOfWork,
        ILogger<InitiatePaymentCommandHandler> logger)
    {
        _paymentRepository = paymentRepository;
        _commandes = commandes;
        _gatewayResolver = gatewayResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<InitiatePaymentResult>> Handle(InitiatePaymentCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PaymentMethod>(command.Method, ignoreCase: true, out var method))
        {
            return Result.Failure<InitiatePaymentResult>(Error.Validation("payments.method_invalid", "Moyen de paiement inconnu."));
        }

        if (!Enum.TryParse<PaymentFlow>(command.Flow, ignoreCase: true, out var flow))
        {
            return Result.Failure<InitiatePaymentResult>(Error.Validation("payments.flow_invalid", "Flux de paiement inconnu (HostedCheckout / PaymentIntent)."));
        }

        // L'univers se lit comme le moyen et le flux : une chaîne du corps, analysée
        // tôt, refusée si elle ne désigne rien. On ne retombe PAS sur la valeur par
        // défaut de l'énumération en cas d'échec — `Marketplace` valant zéro, une
        // chaîne fantaisiste ferait alors payer une commande de repas comme une
        // commande marketplace, et le paiement deviendrait introuvable pour les deux.
        if (!Enum.TryParse<PaymentOrderType>(command.OrderType, ignoreCase: true, out var orderType))
        {
            return Result.Failure<InitiatePaymentResult>(
                Error.Validation("payments.order_type_invalid", "Univers de commande inconnu (Marketplace / Food)."));
        }

        var gatewayResult = _gatewayResolver.Resolve(command.Provider);
        if (gatewayResult.IsFailure)
        {
            return Result.Failure<InitiatePaymentResult>(gatewayResult.Error);
        }

        if (gatewayResult.Value.RequiresPayerPhone && string.IsNullOrWhiteSpace(command.PayerPhone))
        {
            return Result.Failure<InitiatePaymentResult>(
                Error.Validation("payments.payer_phone_required", "Le numéro du payeur est requis pour le Mobile Money."));
        }

        var order = await _commandes.ReadAsync(orderType, command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<InitiatePaymentResult>(Error.NotFound("payments.order.not_found", "Commande introuvable."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // SEUL L'ACHETEUR PAIE SA COMMANDE.
        //
        // `order.BuyerId` vient d'order-service : c'est l'acheteur réel. Le
        // comparer à l'appelant est la seule chose qui empêchait un tiers de
        // créer un paiement `Pending` sur une commande qui n'est pas la sienne —
        // et de la rendre impayable pour toujours.
        //
        // Le refus se présente comme une commande introuvable : répondre « ce
        // n'est pas la vôtre » confirmerait que cet identifiant existe et attend
        // un règlement.
        //
        // L'administration n'a pas de dérogation ici : initier un paiement AU NOM
        // d'un acheteur n'est pas un geste d'exploitation, c'est un débit.
        // ═════════════════════════════════════════════════════════════════════
        if (command.RequestedByUserId is not { } demandeur || order.BuyerId != demandeur)
        {
            return Result.Failure<InitiatePaymentResult>(
                Error.NotFound("payments.order.not_found", "Commande introuvable."));
        }

        if (!string.Equals(order.Status, "AwaitingPayment", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<InitiatePaymentResult>(Error.Conflict("payments.order.not_payable", "La commande n'attend pas de paiement."));
        }

        var existing = await _paymentRepository.GetByOrderAsync(orderType, command.OrderId, cancellationToken);

        // ─────────────────────────────────────────────────────────────────────────────
        // ON VÉRIFIE AUPRÈS DU PSP AVANT DE DÉCLARER UN PAIEMENT « EN COURS ».
        //
        // Un paiement reste `Pending` tant qu'un webhook ne l'a pas conclu. Or le
        // webhook peut ne jamais arriver : réseau coupé, endpoint momentanément
        // indisponible, signature rejetée, acheteur qui ferme la page. Le paiement
        // reste alors `Pending` POUR TOUJOURS — et cette garde, prise au mot, interdit
        // définitivement toute nouvelle tentative sur cette commande.
        //
        // Constaté en production : côté FedaPay la transaction était « Annulée », côté
        // plateforme elle était encore « en attente de paiement ». L'acheteur voyait une
        // commande bloquée, sans aucun moyen de payer, et rien dans l'interface ne
        // pouvait l'en sortir.
        //
        // On interroge donc le prestataire pour connaître l'état RÉEL avant de refuser.
        // Si la transaction est terminée (échouée, annulée), le paiement passe à `Failed`
        // et la tentative suivante est autorisée juste en dessous.
        //
        // Best-effort : si le PSP est injoignable, on retombe sur l'ancien comportement,
        // pessimiste mais sûr. Mieux vaut un refus temporaire qu'un double débit.
        // ─────────────────────────────────────────────────────────────────────────────
        if (existing is { Status: PaymentStatus.Pending } && !string.IsNullOrWhiteSpace(existing.ProviderReference))
        {
            await ReconcileWithProviderAsync(existing, cancellationToken);
        }

        if (existing is not null && existing.Status is PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.Captured)
        {
            return Result.Failure<InitiatePaymentResult>(Error.Conflict("payments.already_exists", "Un paiement est déjà en cours pour cette commande."));
        }

        var amountResult = Money.Create(order.GrandTotal, order.Currency);
        if (amountResult.IsFailure)
        {
            return Result.Failure<InitiatePaymentResult>(amountResult.Error);
        }

        // L'UNIVERS VIENT DE LA COMMANDE REÇUE, PLUS D'UNE CONSTANTE.
        //
        // Il valait `Marketplace` en dur, avec un commentaire qui annonçait
        // exactement ce lot : « le jour où food-order-service ouvrira son chemin de
        // paiement, il lui faudra SA propre commande d'initiation ou un paramètre
        // explicite ici ». C'est le paramètre explicite qui a été retenu — voir
        // `IPayableOrderReader` pour pourquoi un seul chemin plutôt que deux.
        //
        // Ce champ n'est pas décoratif : `PaymentCapturedIntegrationEvent.OrderType`
        // en découle, et c'est LUI que food-order-service et order-service filtrent
        // pour savoir si un paiement les concerne.
        var paymentResult = Payment.Create(
            order.OrderId, orderType, order.BuyerId, amountResult.Value,
            method, gatewayResult.Value.Provider, flow);
        if (paymentResult.IsFailure)
        {
            return Result.Failure<InitiatePaymentResult>(paymentResult.Error);
        }

        var payment = paymentResult.Value;
        var gateway = gatewayResult.Value;

        var context = new GatewayChargeContext(
            payment.Id.Value, payment.OrderId, payment.Amount.Amount, payment.Amount.Currency,
            command.ReturnUrl, command.CancelUrl, command.PayerPhone);

        var session = flow == PaymentFlow.HostedCheckout
            ? await gateway.CreateCheckoutAsync(context, cancellationToken)
            : await gateway.CreatePaymentIntentAsync(context, cancellationToken);

        var attach = payment.AttachGatewaySession(session.ProviderReference);
        if (attach.IsFailure)
        {
            return Result.Failure<InitiatePaymentResult>(attach.Error);
        }

        await _paymentRepository.AddAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new InitiatePaymentResult(
            payment.Id.Value, gateway.Provider, flow.ToString(), session.ProviderReference, session.RedirectUrl, session.ClientSecret);
    }

    /// <summary>
    /// Aligne un paiement resté <see cref="PaymentStatus.Pending"/> sur l'état réel
    /// connu du prestataire. Ne remonte jamais d'erreur : c'est une amélioration
    /// opportuniste du diagnostic, pas une étape dont dépend l'initiation.
    /// </summary>
    private async Task ReconcileWithProviderAsync(Payment existing, CancellationToken cancellationToken)
    {
        try
        {
            var previousGateway = _gatewayResolver.Resolve(existing.Provider);
            if (previousGateway.IsFailure)
            {
                // Prestataire retiré de la configuration depuis la tentative : on ne
                // peut plus rien lui demander. Le paiement restera Pending, et le refus
                // ci-dessous s'appliquera — cas assez rare pour ne pas être traité ici.
                return;
            }

            var current = await previousGateway.Value.GetStatusAsync(existing.ProviderReference!, cancellationToken);
            // Le journal de l'appelant, et non un `NullLogger` : c'est ici que
            // remontent les refus d'imputation d'un remboursement — devise
            // incohérente, montant absent (voir `GatewayOutcomeApplier`). Les
            // taire ferait disparaître, dans le silence d'une réconciliation
            // « opportuniste », la seule trace d'un argent rendu dont on ignore
            // le montant.
            if (GatewayOutcomeApplier.Apply(existing, current, _logger).IsSuccess)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Le PSP est injoignable ou répond n'importe quoi. On ne laisse PAS cette
            // panne faire échouer l'initiation d'un paiement tout neuf : on retombe
            // simplement sur la garde pessimiste. Le `when` laisse passer l'annulation,
            // qui n'est pas une erreur du prestataire.
            _logger.LogWarning(
                ex,
                "Réconciliation impossible auprès de {Provider} pour le paiement {PaymentId}. La garde « paiement déjà en cours » s'applique telle quelle.",
                existing.Provider,
                existing.Id.Value);
        }
    }
}
