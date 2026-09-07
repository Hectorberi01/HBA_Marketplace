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
/// cours, crée le paiement (Pending), puis ouvre la session auprès du PSP (checkout
/// hébergé ou intention) et rattache sa référence pour la corrélation.
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

        // L'univers se lit comme le moyen et le flux : une chaîne du corps,
        // analysée tôt, refusée si elle ne désigne rien.
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

        // SEUL L'ACHETEUR PAIE SA COMMANDE.
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

        // ON VÉRIFIE AUPRÈS DU PSP AVANT DE DÉCLARER UN PAIEMENT « EN COURS ».
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
    /// connu du prestataire.
    /// </summary>
    private async Task ReconcileWithProviderAsync(Payment existing, CancellationToken cancellationToken)
    {
        try
        {
            var previousGateway = _gatewayResolver.Resolve(existing.Provider);
            if (previousGateway.IsFailure)
            {
                // Prestataire retiré de la configuration depuis la tentative : on
                // ne peut plus rien lui demander.
                return;
            }

            var current = await previousGateway.Value.GetStatusAsync(existing.ProviderReference!, cancellationToken);
            // Le journal de l'appelant, et non un `NullLogger` : c'est ici que
            // remontent les refus d'imputation d'un remboursement — devise
            // incohérente, montant absent (voir `GatewayOutcomeApplier`).
            if (GatewayOutcomeApplier.Apply(existing, current, _logger).IsSuccess)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Le PSP est injoignable ou répond n'importe quoi.
            _logger.LogWarning(
                ex,
                "Réconciliation impossible auprès de {Provider} pour le paiement {PaymentId}. La garde « paiement déjà en cours » s'applique telle quelle.",
                existing.Provider,
                existing.Id.Value);
        }
    }
}
