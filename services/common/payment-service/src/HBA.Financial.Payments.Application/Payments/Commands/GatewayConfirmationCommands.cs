using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Application.Abstractions.Gateways;
using HBA.Financial.Payments.Domain.Payments;

namespace HBA.Financial.Payments.Application.Payments.Commands;

/// <summary>
/// Traite un webhook PSP : vérifie la signature, normalise l'événement et
/// applique le résultat au paiement corrélé (encaissement / échec / remboursement).
/// Idempotent : un événement déjà appliqué est acquitté sans erreur.
/// </summary>
public sealed record ProcessGatewayWebhookCommand(string Provider, string RawBody, string? Signature) : ICommand;

/// <summary>
/// Confirme un paiement au retour de la redirection : interroge le PSP pour
/// connaître le statut réel de la session/intention, puis l'applique. Sécurise
/// le cas où le webhook n'est pas (encore) arrivé.
/// </summary>
public sealed record ConfirmPaymentFromRedirectCommand(Guid PaymentId) : ICommand;

/// <summary>Applique un résultat PSP normalisé à un paiement, de façon idempotente.</summary>
internal static class GatewayOutcomeApplier
{
    public static Result Apply(Payment payment, GatewayEvent gatewayEvent, ILogger logger)
    {
        var providerReference = gatewayEvent.ProviderReference ?? payment.ProviderReference ?? string.Empty;

        return gatewayEvent.Outcome switch
        {
            GatewayOutcome.Captured => payment.Status == PaymentStatus.Captured ? Result.Success() : payment.Capture(providerReference),
            GatewayOutcome.Failed => payment.Status == PaymentStatus.Failed ? Result.Success() : payment.Fail(gatewayEvent.FailureReason ?? "Paiement refusé par le prestataire."),
            GatewayOutcome.Refunded => AppliquerRemboursement(payment, gatewayEvent, logger),
            _ => Result.Success()
        };
    }

    /// <summary>
    /// Impute un remboursement notifié par le prestataire, POUR LE MONTANT ANNONCÉ.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// 5 000 F REMBOURSÉS CLÔTURAIENT UNE COMMANDE DE 50 000 F.
    ///
    /// Cette branche s'écrivait `payment.Refund()` — le remboursement du SOLDE
    /// ENTIER — parce que `GatewayEvent` ne portait aucun montant. Un geste
    /// commercial de 5 000 F notifié par webhook passait donc le paiement en
    /// « Refunded » : solde remboursable à zéro, commande close comme
    /// intégralement remboursée, 45 000 F que le système croyait avoir rendus et
    /// qui étaient toujours chez nous. Perte sèche, comptabilité fausse, aucune
    /// alerte.
    ///
    /// Le domaine savait pourtant déjà faire : `payment_refunds` accepte
    /// PLUSIEURS lignes, `RefundedAmount` en fait la somme, et `MarkRefundSucceeded`
    /// ne bascule le paiement en « Refunded » QUE si `RefundableAmount` tombe à
    /// zéro. Il ne manquait que le montant.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// ET QUAND LE PRESTATAIRE NE DIT PAS LE MONTANT ? ON REFUSE.
    ///
    /// C'est le cas de FedaPay sur un simple `status: refunded` de transaction, et
    /// de tout PSP dont le payload ne porte ni montant de remboursement ni cumul.
    ///
    /// Décision : REFUS EXPLICITE. On ne touche pas au paiement, on journalise en
    /// Critical, et on rend une erreur métier — le webhook répond 422, ce que le
    /// tableau de bord du prestataire affiche comme un endpoint en échec. C'est
    /// un signal que quelqu'un voit.
    ///
    /// Le repli « alors c'est un remboursement total » est EXACTEMENT le défaut
    /// corrigé ici : il transforme une information manquante en écriture
    /// comptable fausse et irréversible. Un remboursement non enregistré se
    /// rattrape en lisant le relevé du prestataire ; une commande close à tort ne
    /// se rattrape plus.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// IDEMPOTENCE : LA CLÉ VIENT DU PRESTATAIRE, PAS DE NOUS.
    ///
    /// Kafka et les PSP livrent au moins une fois. La clé d'idempotence est donc
    /// dérivée de l'identifiant de remboursement du prestataire quand il existe,
    /// ou du CUMUL annoncé — deux formes stables d'une livraison à l'autre.
    /// L'index unique `(PaymentId, IdempotencyKey)` posé au lot 3.1 tranche les
    /// courses concurrentes en base (23505 → 409), et `BeginRefund` rend la ligne
    /// existante plutôt que d'en créer une seconde.
    ///
    /// LIMITE ASSUMÉE : si le prestataire annonce un MONTANT sans identifiant
    /// de remboursement, deux remboursements partiels du MÊME montant sur le même
    /// paiement se confondent — le second est ignoré. On sous-enregistre plutôt
    /// que de sur-enregistrer : le sens de l'erreur est celui qui ne clôt pas une
    /// commande à tort.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private static Result AppliquerRemboursement(Payment payment, GatewayEvent gatewayEvent, ILogger logger)
    {
        // Déjà intégralement remboursé : rejeu du webhook, rien à faire.
        if (payment.Status == PaymentStatus.Refunded)
        {
            return Result.Success();
        }

        if (payment.Status != PaymentStatus.Captured)
        {
            logger.LogWarning(
                "Remboursement annoncé par le prestataire pour le paiement {PaymentId} qui est "
                + "dans l'état {Statut} : rien n'est imputé.",
                payment.Id.Value, payment.Status);

            return Result.Failure(Error.Conflict(
                "payments.not_refundable", "Seul un paiement encaissé peut être remboursé."));
        }

        // Une devise annoncée qui n'est pas celle du paiement n'est pas une
        // conversion à faire : c'est un payload qu'on ne comprend pas.
        if (!string.IsNullOrWhiteSpace(gatewayEvent.RefundCurrency)
            && !string.Equals(gatewayEvent.RefundCurrency, payment.Amount.Currency, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogCritical(
                "Remboursement annoncé en {DeviseAnnoncee} pour le paiement {PaymentId} libellé en "
                + "{DevisePaiement} : rien n'est imputé, vérification manuelle requise.",
                gatewayEvent.RefundCurrency, payment.Id.Value, payment.Amount.Currency);

            return Result.Failure(Error.BusinessRule(
                "payments.refund_currency_mismatch",
                "La devise du remboursement annoncé ne correspond pas à celle du paiement."));
        }

        decimal montant;
        string cleIdempotence;

        if (gatewayEvent.RefundAmount is { } montantAnnonce)
        {
            montant = montantAnnonce;
            cleIdempotence = string.IsNullOrWhiteSpace(gatewayEvent.RefundReference)
                ? $"payment:{payment.Id.Value}:refund:psp-amount:{montant:0.####}"
                : $"payment:{payment.Id.Value}:refund:psp:{gatewayEvent.RefundReference}";
        }
        else if (gatewayEvent.TotalRefundedAmount is { } cumulPrestataire)
        {
            // Le prestataire annonce un CUMUL (Stripe : `amount_refunded`). Ce qui
            // reste à imputer est la différence avec ce que nous avons déjà
            // enregistré — ce qui rend le rejeu naturellement inoffensif.
            montant = cumulPrestataire - payment.RefundedAmount;

            if (montant <= 0m)
            {
                logger.LogDebug(
                    "Cumul remboursé annoncé ({Cumul}) déjà couvert pour le paiement {PaymentId} : rejeu ignoré.",
                    cumulPrestataire, payment.Id.Value);

                return Result.Success();
            }

            cleIdempotence = $"payment:{payment.Id.Value}:refund:psp-total:{cumulPrestataire:0.####}";
        }
        else
        {
            //Voir l'encadré : refus explicite, jamais de repli vers « total ».
            logger.LogCritical(
                "REMBOURSEMENT SANS MONTANT annoncé par {Prestataire} pour le paiement {PaymentId} "
                + "(référence {ProviderReference}, montant du paiement {Montant} {Devise}). "
                + "Le prestataire a rendu de l'argent et nous ne savons pas combien : RIEN n'est "
                + "imputé — imputer le solde entier clôturerait peut-être à tort une commande "
                + "partiellement remboursée. Rapprochement manuel requis.",
                payment.Provider, payment.Id.Value, gatewayEvent.ProviderReference,
                payment.Amount.Amount, payment.Amount.Currency);

            return Result.Failure(Error.BusinessRule(
                "payments.refund_amount_missing",
                "Le prestataire annonce un remboursement sans en préciser le montant : "
                + "aucune imputation n'est faite."));
        }

        var montantRembourse = Money.Create(montant, payment.Amount.Currency);
        if (montantRembourse.IsFailure)
        {
            return Result.Failure(montantRembourse.Error);
        }

        var demande = payment.BeginRefund(
            montantRembourse.Value,
            "Remboursement notifié par le prestataire.",
            cleIdempotence,
            returnId: null,
            externalRefundId: null,
            DateTime.UtcNow);

        if (demande.IsFailure)
        {
            logger.LogCritical(
                "Remboursement de {Montant} {Devise} annoncé par {Prestataire} REFUSÉ par le domaine "
                + "pour le paiement {PaymentId} — {Code} : {Message}. L'argent est parti chez le "
                + "prestataire et n'est PAS enregistré ici.",
                montant, payment.Amount.Currency, payment.Provider, payment.Id.Value,
                demande.Error.Code, demande.Error.Message);

            return Result.Failure(demande.Error);
        }

        // Rejeu exact d'un webhook déjà imputé : la ligne existe et est réussie.
        if (demande.Value.Status == PaymentRefundStatus.Succeeded)
        {
            return Result.Success();
        }

        var marquage = payment.MarkRefundSucceeded(
            demande.Value.Id,
            gatewayEvent.RefundReference ?? gatewayEvent.ProviderReference ?? demande.Value.Id.ToString(),
            DateTime.UtcNow);

        if (marquage.IsFailure)
        {
            return Result.Failure(marquage.Error);
        }

        if (payment.Status == PaymentStatus.Refunded)
        {
            logger.LogInformation(
                "Paiement {PaymentId} INTÉGRALEMENT remboursé ({Montant} {Devise} imputés par le "
                + "prestataire {Prestataire}).",
                payment.Id.Value, montant, payment.Amount.Currency, payment.Provider);
        }
        else
        {
            // Le cas que l'ancien code écrasait : un partiel reste un partiel.
            logger.LogInformation(
                "Remboursement PARTIEL de {Montant} {Devise} imputé au paiement {PaymentId} "
                + "(cumul {Cumul} sur {Total}). Le paiement reste encaissé pour le solde.",
                montant, payment.Amount.Currency, payment.Id.Value,
                payment.RefundedAmount, payment.Amount.Amount);
        }

        return Result.Success();
    }
}

internal sealed class ProcessGatewayWebhookCommandHandler : ICommandHandler<ProcessGatewayWebhookCommand>
{
    private readonly IPaymentGatewayResolver _gatewayResolver;
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;
    private readonly ILogger<ProcessGatewayWebhookCommandHandler> _logger;

    public ProcessGatewayWebhookCommandHandler(
        IPaymentGatewayResolver gatewayResolver,
        IPaymentRepository repository,
        IPaymentsUnitOfWork unitOfWork,
        ILogger<ProcessGatewayWebhookCommandHandler> logger)
    {
        _gatewayResolver = gatewayResolver;
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ProcessGatewayWebhookCommand command, CancellationToken cancellationToken)
    {
        var gatewayResult = _gatewayResolver.Resolve(command.Provider);
        if (gatewayResult.IsFailure)
        {
            return Result.Failure(gatewayResult.Error);
        }

        var gatewayEvent = await gatewayResult.Value.ParseWebhookAsync(command.RawBody, command.Signature, cancellationToken);
        if (!gatewayEvent.Verified)
        {
            return Result.Failure(Error.Unauthorized("payments.webhook_invalid_signature", "Signature de webhook invalide."));
        }

        if (gatewayEvent.Outcome == GatewayOutcome.Ignored || string.IsNullOrWhiteSpace(gatewayEvent.ProviderReference))
        {
            return Result.Success();
        }

        var payment = await _repository.GetByProviderReferenceAsync(gatewayEvent.ProviderReference!, cancellationToken);
        if (payment is null)
        {
            // Paiement inconnu : on acquitte pour éviter les renvois en boucle du PSP.
            return Result.Success();
        }

        var apply = GatewayOutcomeApplier.Apply(payment, gatewayEvent, _logger);
        if (apply.IsFailure)
        {
            return apply;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ConfirmPaymentFromRedirectCommandHandler : ICommandHandler<ConfirmPaymentFromRedirectCommand>
{
    private readonly IPaymentGatewayResolver _gatewayResolver;
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;
    private readonly ILogger<ConfirmPaymentFromRedirectCommandHandler> _logger;

    public ConfirmPaymentFromRedirectCommandHandler(
        IPaymentGatewayResolver gatewayResolver,
        IPaymentRepository repository,
        IPaymentsUnitOfWork unitOfWork,
        ILogger<ConfirmPaymentFromRedirectCommandHandler> logger)
    {
        _gatewayResolver = gatewayResolver;
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ConfirmPaymentFromRedirectCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(command.PaymentId), cancellationToken);
        if (payment is null)
        {
            return Result.Failure(Error.NotFound("payments.not_found", "Paiement introuvable."));
        }

        if (payment.Status == PaymentStatus.Captured)
        {
            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(payment.ProviderReference))
        {
            return Result.Failure(Error.Conflict("payments.no_session", "Aucune session PSP rattachée à ce paiement."));
        }

        var gatewayResult = _gatewayResolver.Resolve(payment.Provider);
        if (gatewayResult.IsFailure)
        {
            return Result.Failure(gatewayResult.Error);
        }

        var gatewayEvent = await gatewayResult.Value.GetStatusAsync(payment.ProviderReference, cancellationToken);

        var apply = GatewayOutcomeApplier.Apply(payment, gatewayEvent, _logger);
        if (apply.IsFailure)
        {
            return apply;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
