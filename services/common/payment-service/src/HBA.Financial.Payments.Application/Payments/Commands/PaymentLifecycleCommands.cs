using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Application.Abstractions.Gateways;
using HBA.Financial.Payments.Domain.Payments;
using HBA.Financial.Wallet.Contracts;

namespace HBA.Financial.Payments.Application.Payments.Commands;

/// <summary>Encaisse un paiement (simule la confirmation du PSP). Émet PaymentCaptured.</summary>
public sealed record CapturePaymentCommand(Guid PaymentId, string ProviderReference) : ICommand;

/// <summary>Marque un paiement en échec. Émet PaymentFailed.</summary>
public sealed record FailPaymentCommand(Guid PaymentId, string Reason) : ICommand;

/// <summary>Rembourse un paiement encaissé. Émet PaymentRefunded.</summary>
public sealed record RefundPaymentCommand(
    Guid PaymentId,
    decimal? Amount = null,
    string? Currency = null,
    string? Reason = null,
    string? IdempotencyKey = null,
    Guid? ReturnId = null,
    Guid? ExternalRefundId = null) : ICommand<RefundPaymentResult>;

public sealed record RefundPaymentResult(
    Guid PaymentId,
    Guid RefundId,
    string ProviderRefundId,
    string Status,
    decimal Amount,
    string Currency);

internal sealed class CapturePaymentCommandHandler : ICommandHandler<CapturePaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public CapturePaymentCommandHandler(IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CapturePaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(command.PaymentId), cancellationToken);
        if (payment is null)
        {
            return Result.Failure(Error.NotFound("payments.not_found", "Paiement introuvable."));
        }

        var result = payment.Capture(command.ProviderReference);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class FailPaymentCommandHandler : ICommandHandler<FailPaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public FailPaymentCommandHandler(IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(FailPaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(command.PaymentId), cancellationToken);
        if (payment is null)
        {
            return Result.Failure(Error.NotFound("payments.not_found", "Paiement introuvable."));
        }

        var result = payment.Fail(string.IsNullOrWhiteSpace(command.Reason) ? "Paiement refusé." : command.Reason);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Rembourse un paiement encaissé — chez le prestataire, puis en base.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE HANDLER NE RENDAIT PAS L'ARGENT. IL SE CONTENTAIT DE L'ÉCRIRE.
///
/// Il s'écrivait ainsi, et rien de plus :
///
///     var result = payment.Refund();
///     await _unitOfWork.SaveChangesAsync(cancellationToken);
///
/// `IPaymentGateway.RefundAsync` est pourtant implémenté par les six
/// adaptateurs — FedaPay, MTN MoMo, Moov, Stripe, PayPal, et le simulateur — et
/// n'était appelé de NULLE PART dans le dépôt. Le paiement passait « Refunded »
/// dans notre base, la console d'administration l'affichait remboursé, le
/// vendeur était contre-passé… et l'argent restait chez l'opérateur. L'acheteur
/// ne revoyait jamais son versement Mobile Money.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// L'ORDRE DES TROIS ÉTAPES EST DÉLIBÉRÉ
///
/// 1. Vérifier l'état AVANT d'appeler le PSP. `Refund()` refuse un paiement non
///    encaissé ; l'appeler après le remboursement rendrait l'argent tout en
///    répondant « échec », et personne ne saurait que le virement est parti.
/// 2. Appeler le PSP. Tant qu'il n'a pas confirmé, rien ne bouge chez nous :
///    un échec réseau laisse le paiement « Captured », donc rejouable.
/// 3. Écrire, et seulement alors.
///
/// FENÊTRE RÉSIDUELLE ASSUMÉE, ET ELLE DOIT ÊTRE REFERMÉE.
///
/// Entre 2 et 3, si l'enregistrement échoue, l'argent est parti et le paiement
/// reste « Captured » : un second appel rembourserait une seconde fois. La
/// vraie réponse est un état intermédiaire persisté avant l'appel — un
/// « RefundPending » avec la référence du PSP — qui rendrait l'opération
/// idempotente. Cela demande une transition de domaine et une migration ; ce
/// n'est pas fait ici. En attendant, la référence de remboursement rendue par
/// le prestataire est journalisée : c'est elle qui permet de trancher à la main.
///
/// (Le même défaut existe à l'initiation : `InitiatePaymentCommandHandler`
/// ouvre la session PSP avant de persister. Une seule correction devrait
/// couvrir les deux.)
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class RefundPaymentCommandHandler : ICommandHandler<RefundPaymentCommand, RefundPaymentResult>
{
    /// <summary>
    /// Motif inscrit au grand livre du portefeuille. C'est un CODE, pas une phrase :
    /// la colonne est courte et le dépôt y range des jetons interrogeables
    /// (`refund_reversal`, `customer_refund`). Le motif humain du remboursement vit
    /// dans le dossier `PaymentRefund`, qui est fait pour lui.
    /// </summary>
    private const string MotifPortefeuille = "payment_refund";

    private readonly IPaymentRepository _repository;
    private readonly IPaymentGatewayResolver _gatewayResolver;
    private readonly ICustomerWalletApi _customerWallet;
    private readonly IPaymentsUnitOfWork _unitOfWork;
    private readonly ILogger<RefundPaymentCommandHandler> _logger;

    public RefundPaymentCommandHandler(
        IPaymentRepository repository,
        IPaymentGatewayResolver gatewayResolver,
        ICustomerWalletApi customerWallet,
        IPaymentsUnitOfWork unitOfWork,
        ILogger<RefundPaymentCommandHandler> logger)
    {
        _repository = repository;
        _gatewayResolver = gatewayResolver;
        _customerWallet = customerWallet;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<RefundPaymentResult>> Handle(RefundPaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(command.PaymentId), cancellationToken);
        if (payment is null)
        {
            return Error.NotFound("payments.not_found", "Paiement introuvable.");
        }

        // ── 1. L'état permet-il un remboursement ? ────────────────────────────
        if (payment.Status != PaymentStatus.Captured)
        {
            var existing = payment.Refunds.FirstOrDefault(r =>
                r.IdempotencyKey == (command.IdempotencyKey ?? $"payment:{command.PaymentId}:refund:full"));

            if (existing?.Status == PaymentRefundStatus.Succeeded)
            {
                return ToResult(payment, existing);
            }

            return Error.Conflict("payments.not_refundable", "Seul un paiement encaissé peut être remboursé.");
        }

        // Sans référence du prestataire, il n'y a rien à rembourser CHEZ LUI :
        // le paiement n'a jamais abouti de son côté. Refuser plutôt que d'écrire
        // « Refunded » sur une opération qui n'a pas d'existence externe.
        if (string.IsNullOrWhiteSpace(payment.ProviderReference))
        {
            return Error.Conflict(
                "payments.no_provider_reference",
                "Ce paiement ne porte aucune référence de prestataire : il ne peut pas être remboursé.");
        }

        var amount = Money.Create(command.Amount ?? payment.RefundableAmount, command.Currency ?? payment.Amount.Currency);
        if (amount.IsFailure)
        {
            return amount.Error;
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? $"payment:{payment.Id.Value}:refund:full"
            : command.IdempotencyKey.Trim();

        var existingRefund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existingRefund?.Status is PaymentRefundStatus.Succeeded or PaymentRefundStatus.Processing)
        {
            return ToResult(payment, existingRefund);
        }

        var gateway = _gatewayResolver.Resolve(payment.Provider);
        if (gateway.IsFailure)
        {
            return gateway.Error;
        }

        var begin = payment.BeginRefund(
            amount.Value,
            command.Reason ?? "Remboursement.",
            idempotencyKey,
            command.ReturnId,
            command.ExternalRefundId,
            DateTime.UtcNow);

        if (begin.IsFailure)
        {
            return begin.Error;
        }

        var refundRequest = begin.Value;
        if (refundRequest.Status == PaymentRefundStatus.Succeeded)
        {
            return ToResult(payment, refundRequest);
        }

        if (refundRequest.Status == PaymentRefundStatus.Processing)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 2. L'ARGENT REPART — PAR LE PRESTATAIRE, OU PAR LE PORTEFEUILLE (D33).
        //
        // CE BRANCHEMENT EST LA RAISON POUR LAQUELLE UN CLIENT EST REMBOURSÉ.
        //
        // FedaPay n'expose AUCUNE API de remboursement, et MTN, Moov et PayPal
        // non plus dans ce dépôt : leurs adaptateurs répondent « refusé » en dur.
        // Jusqu'ici, cette branche menait donc invariablement au refus métier
        // ci-dessous — dossier de retour escaladé en arbitrage humain, et client
        // qui attend son argent indéfiniment.
        //
        // La décision D33 tranche : quand le prestataire ne sait pas rendre
        // l'argent, on le rend sur le PORTEFEUILLE du client. Il l'a
        // immédiatement, il peut le dépenser sur une commande suivante, et il
        // demande un virement Mobile Money quand il le veut — c'est une seconde
        // étape, validée à la main par un administrateur.
        //
        // ON N'IMPOSE PAS LE PORTEFEUILLE À TOUT LE MONDE.
        //
        // Le routage se fait sur `SupportsRefund`, capacité déclarée par
        // l'adaptateur. Stripe SAIT rembourser : l'argent retourne sur la carte,
        // ce qui est meilleur pour le client — il n'a aucune démarche à faire.
        // Forcer le portefeuille pour tous ajouterait une étape de retrait à des
        // clients qui n'en ont pas besoin.
        //
        // ET C'EST ICI QUE ÇA SE JOUE, PAS DANS RETURN-REFUND.
        //
        // Trois flux remboursent un client — le retour, l'annulation de commande,
        // le geste administratif direct — et les trois passent par cette commande.
        // La règle posée ici vaut pour les trois ; posée chez l'un d'eux, elle
        // aurait manqué aux deux autres.
        // ═════════════════════════════════════════════════════════════════════
        var refund = gateway.Value.SupportsRefund
            ? await gateway.Value.RefundAsync(
                new GatewayRefundContext(
                    payment.ProviderReference,
                    refundRequest.Amount.Amount,
                    refundRequest.Amount.Currency,
                    refundRequest.Reason,
                    refundRequest.IdempotencyKey),
                cancellationToken)
            : await CrediterLePortefeuilleAsync(payment, refundRequest, cancellationToken);

        // ═════════════════════════════════════════════════════════════════════
        // PANNE PASSAGÈRE : ON NE DÉCIDE RIEN, ON LAISSE REJOUER.
        //
        // Un PSP injoignable n'a rien refusé — il n'a pas répondu. Écrire
        // « remboursement échoué » ici figerait en base une décision que personne
        // n'a prise, et le domaine émettrait un `PaymentRefundFailed` mensonger.
        //
        // La demande reste donc en « Processing » (elle a été persistée juste
        // avant l'appel, précisément pour ce cas), et l'erreur rendue est de type
        // `DependencyUnavailable` — 503 au bord HTTP, et signal de REJEU pour les
        // consommateurs d'événements.
        //
        // CE QUE CELA NE FERME PAS, ET IL FAUT LE SAVOIR : la demande restera
        // « Processing » indéfiniment tant qu'aucune tâche de réconciliation ne
        // la reprendra. Il n'en existe aucune dans le dépôt. La ligne porte
        // `AttemptCount` et `LastAttemptAtUtc` : c'est ce qu'une telle tâche
        // devra balayer.
        // ═════════════════════════════════════════════════════════════════════
        if (!refund.Success && refund.Transient)
        {
            _logger.LogCritical(
                "Remboursement INTERROMPU chez {Provider} pour le paiement {PaymentId} "
                + "(référence {ProviderReference}) : {Erreur}. Le prestataire n'a pas répondu — "
                + "on ignore si l'argent est parti. La demande {RefundId} reste « en cours ».",
                payment.Provider, payment.Id.Value, payment.ProviderReference, refund.Error, refundRequest.Id);

            return Error.DependencyUnavailable(
                "payments.refund_gateway_unavailable",
                refund.Error ?? "Le prestataire de paiement n'a pas répondu à la demande de remboursement.");
        }

        // ═════════════════════════════════════════════════════════════════════
        // REFUS MÉTIER : C'EST DÉFINITIF, ON L'ENREGISTRE ET ON REND LA MAIN.
        //
        // CE N'EST PLUS LE CAS ORDINAIRE DEPUIS D33, ET C'EST TOUT L'ÉCART.
        //
        // Cette branche était le sort NORMAL de quatre adaptateurs sur six, qui
        // répondent « refusé » en dur faute d'API de remboursement. Ceux-là sont
        // désormais routés vers le portefeuille du client (voir plus haut) et ne
        // passent plus ici. Ce qui reste, c'est un prestataire qui SAIT
        // rembourser et qui a refusé CETTE demande-là : montant déjà rendu,
        // transaction trop ancienne, compte clos chez lui.
        //
        // Rejouer un refus ne le transforme jamais en accord : cela n'obtient
        // qu'un consommateur qui se sature d'un message qui ne passera pas.
        //
        // On écrit donc l'échec sur le paiement — `MarkRefundFailed` émet
        // `PaymentRefundFailedDomainEvent`, seule trace durable —, on journalise
        // en Critical (un client attend son argent, et personne ne le saura
        // autrement), et on rend une erreur MÉTIER que l'appelant peut distinguer
        // d'une panne.
        //
        // LE `SaveChanges` CI-DESSOUS N'EST PAS QUE COSMÉTIQUE. C'est lui qui
        // committe la trace d'inbox posée par `IntegrationEventDispatcher` avant
        // l'appel du gestionnaire : sans écriture, la trace resterait en attente
        // et le message resterait rejouable indéfiniment.
        // ═════════════════════════════════════════════════════════════════════
        if (!refund.Success)
        {
            payment.MarkRefundFailed(refundRequest.Id, refund.Error ?? "Remboursement refuse par le prestataire.", DateTime.UtcNow);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogCritical(
                "Remboursement REFUSÉ par {Provider} pour le paiement {PaymentId} "
                + "(référence {ProviderReference}) : {Erreur}. Ce prestataire SAIT rembourser — "
                + "le refus porte donc sur cette demande précise, pas sur sa capacité. Le paiement "
                + "reste encaissé : le client N'EST PAS remboursé, arbitrage manuel requis.",
                payment.Provider, payment.Id.Value, payment.ProviderReference, refund.Error);

            return Error.BusinessRule(
                "payments.refund_rejected",
                refund.Error ?? "Le prestataire a refusé le remboursement.");
        }

        // ── 3. Et seulement maintenant, la base ───────────────────────────────
        var result = payment.MarkRefundSucceeded(
            refundRequest.Id,
            refund.ProviderReference ?? refundRequest.Id.ToString(),
            DateTime.UtcNow);
        if (result.IsFailure)
        {
            // Ne devrait pas arriver : l'état a été vérifié plus haut. Si cela
            // se produit, l'argent est DÉJÀ parti — d'où le niveau critique et
            // la référence, seule prise pour rattraper à la main.
            _logger.LogCritical(
                "ARGENT REMBOURSÉ CHEZ {Provider} MAIS PAS ENREGISTRÉ — paiement {PaymentId}, "
                + "référence de remboursement {RefundReference}. {Code} : {Message}.",
                payment.Provider, payment.Id.Value, refund.ProviderReference,
                result.Error.Code, result.Error.Message);

            return result.Error;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // La référence porte le chemin : « wallet:… » si l'argent est reparti sur le
        // portefeuille du client, l'identifiant du prestataire sinon. C'est la seule
        // chose qui distingue les deux dans le journal, et elle compte au
        // rapprochement.
        _logger.LogInformation(
            "Paiement {PaymentId} remboursé ({Provider}) — référence de remboursement {RefundReference}.",
            payment.Id.Value, payment.Provider, refund.ProviderReference);

        return ToResult(payment, refundRequest);
    }

    /// <summary>
    /// Rend l'argent sur le portefeuille du client, faute de pouvoir le rendre
    /// chez le prestataire (D33).
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE RÉSULTAT EST HABILLÉ EN `GatewayRefundResult`, ET C'EST DÉLIBÉRÉ.
    ///
    /// Tout ce qui suit — panne passagère, refus définitif, enregistrement du
    /// succès avec sa référence — est déjà écrit, éprouvé, et n'a aucune raison
    /// d'exister en double. Le portefeuille est un CHEMIN de remboursement de
    /// plus, pas un cas particulier : il rend donc la même forme de réponse.
    ///
    /// LA CLÉ D'IDEMPOTENCE TRANSMISE EST CELLE DU DOSSIER.
    ///
    /// `PaymentRefund.IdempotencyKey` est déterministe et déjà unique en base
    /// (index posé au lot 3.1). Le portefeuille s'en sert comme référence de son
    /// écriture : un rejeu de cette commande retrouve la même écriture et rend le
    /// même identifiant, au lieu de créditer une seconde fois. C'est ce qui rend
    /// sûr le rejeu d'un message Kafka sur un flux qui donne de l'argent.
    ///
    /// UN REFUS DU PORTEFEUILLE EST TRAITÉ COMME UNE PANNE PASSAGÈRE.
    ///
    /// Et il faut le justifier, parce que ce n'est pas anodin : un refus métier
    /// du portefeuille — devise incompatible, montant invalide — ne s'arrangera
    /// pas tout seul, et sera rejoué en vain. Mais l'inverse est pire. Écrire
    /// « remboursement échoué » ferme le dossier : plus personne ne reprend, et
    /// le client n'est jamais remboursé, alors que le portefeuille est le chemin
    /// dont on a décidé qu'il aboutirait TOUJOURS. On laisse donc rejouer, et le
    /// journal `Critical` ci-dessous est ce qui met un humain devant le cas.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    private async Task<GatewayRefundResult> CrediterLePortefeuilleAsync(
        Payment payment, PaymentRefund refundRequest, CancellationToken cancellationToken)
    {
        var credit = await _customerWallet.CreditRefundAsync(
            payment.BuyerId,
            refundRequest.Amount.Amount,
            refundRequest.Amount.Currency,
            MotifPortefeuille,
            refundRequest.IdempotencyKey,
            cancellationToken);

        if (credit.IsFailure)
        {
            _logger.LogCritical(
                "REMBOURSEMENT IMPOSSIBLE : {Provider} ne sait pas rembourser et le portefeuille du "
                + "client {BuyerId} a refusé le crédit de {Montant} {Devise} pour le paiement "
                + "{PaymentId} — {Code} : {Message}. Le client N'EST PAS remboursé ; la demande "
                + "{RefundId} reste « en cours » et sera rejouée.",
                payment.Provider, payment.BuyerId, refundRequest.Amount.Amount,
                refundRequest.Amount.Currency, payment.Id.Value,
                credit.Error.Code, credit.Error.Message, refundRequest.Id);

            return new GatewayRefundResult(
                Success: false,
                ProviderReference: null,
                Error: $"{credit.Error.Code} : {credit.Error.Message}",
                Transient: true);
        }

        // Un rejeu reconnu est un succès, mais il n'a rien crédité de nouveau. Le
        // dire autrement raconterait deux remboursements pour un seul, et rendrait
        // le rapprochement illisible.
        if (credit.Value.AlreadyApplied)
        {
            _logger.LogInformation(
                "Crédit de remboursement DÉJÀ APPLIQUÉ au portefeuille du client {BuyerId} "
                + "(écriture {TransactionId}) : rejeu reconnu, rien n'a été crédité de plus.",
                payment.BuyerId, credit.Value.TransactionId);
        }
        else
        {
            _logger.LogInformation(
                "Le prestataire {Provider} ne rembourse pas : {Montant} {Devise} rendus sur le "
                + "portefeuille du client {BuyerId} (écriture {TransactionId}, nouveau solde "
                + "{Solde}). Le virement Mobile Money se fera sur sa demande.",
                payment.Provider, refundRequest.Amount.Amount, refundRequest.Amount.Currency,
                payment.BuyerId, credit.Value.TransactionId, credit.Value.NewBalance);
        }

        // La référence dit PAR QUEL CHEMIN l'argent est reparti. Elle finit dans
        // `PaymentRefund.ProviderRefundId`, puis dans `ReturnRefundedIntegrationEvent
        // .RefundReference` — c'est-à-dire sous les yeux de qui rapproche les
        // comptes. « wallet: » y est plus utile qu'un GUID nu.
        return new GatewayRefundResult(
            Success: true,
            ProviderReference: $"wallet:{credit.Value.TransactionId}",
            Error: null);
    }

    private static RefundPaymentResult ToResult(Payment payment, PaymentRefund refund)
        => new(
            payment.Id.Value,
            refund.Id,
            refund.ProviderRefundId ?? refund.Id.ToString(),
            refund.Status.ToString(),
            refund.Amount.Amount,
            refund.Amount.Currency);
}
