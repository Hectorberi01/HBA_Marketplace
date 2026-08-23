namespace HBA.Financial.Payments.Application.Abstractions.Gateways;

/// <summary>
/// Contexte d'une demande de paiement transmis au PSP. <see cref="PayerMsisdn"/>
/// (numéro du payeur) n'est renseigné que pour le Mobile Money (RequestToPay).
/// </summary>
public sealed record GatewayChargeContext(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string? ReturnUrl,
    string? CancelUrl,
    string? PayerMsisdn = null);

/// <summary>
/// Session créée côté PSP. Selon le flux :
/// - HostedCheckout : <see cref="RedirectUrl"/> renseigné (rediriger l'acheteur).
/// - PaymentIntent : <see cref="ClientSecret"/> renseigné (confirmer côté client).
/// <see cref="ProviderReference"/> est l'id de corrélation (session / intent).
/// </summary>
public sealed record GatewaySession(string ProviderReference, string? RedirectUrl, string? ClientSecret);

/// <summary>Résultat normalisé d'un événement PSP (webhook ou interrogation de statut).</summary>
public enum GatewayOutcome
{
    Pending = 0,
    Captured = 1,
    Failed = 2,
    Refunded = 3,
    Ignored = 4
}

/// <summary>Événement PSP normalisé, indépendant du fournisseur.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE RECORD NE PORTAIT AUCUN MONTANT : 5 000 F REMBOURSÉS CLÔTURAIENT UNE
/// COMMANDE DE 50 000 F.
///
/// Un webhook « remboursé » arrivait sans que rien ne dise COMBIEN. Faute de
/// mieux, `GatewayOutcomeApplier` appelait `payment.Refund()`, qui rembourse le
/// SOLDE ENTIER. Un geste commercial de 5 000 F sur une commande de 50 000 F
/// passait donc le paiement en « Refunded » : la commande était close comme
/// intégralement remboursée, le solde remboursable tombait à zéro, et les
/// 45 000 F restants devenaient irrécupérables par le système. Perte sèche pour
/// la plateforme, comptabilité fausse, et aucun message d'erreur nulle part.
///
/// DEUX MONTANTS, PARCE QUE LES PSP N'EN DISENT PAS LE MÊME.
///
/// Certains annoncent le montant de CE remboursement (<see cref="RefundAmount"/>),
/// d'autres le CUMUL remboursé sur la transaction (<see cref="TotalRefundedAmount"/> —
/// c'est le cas de `amount_refunded` chez Stripe). Les confondre reviendrait à
/// rembourser deux fois, ou à ne rien imputer du tout. Chaque adaptateur remplit
/// donc celui qu'il sait lire, et laisse l'autre nul.
///
/// LES DEUX NULS = ON NE SAIT PAS, ET ON REFUSE.
///
/// Aucun repli vers « montant total » : c'est exactement le défaut corrigé ici.
/// Voir l'encadré de `GatewayOutcomeApplier`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="RefundAmount">
/// Montant de CE remboursement, en unités majeures (2 500 F = 2500m). Nul si le
/// prestataire ne le dit pas.
/// </param>
/// <param name="TotalRefundedAmount">
/// CUMUL remboursé sur la transaction chez le prestataire, en unités majeures.
/// Nul si le prestataire ne le dit pas. L'imputation se fait par différence avec
/// ce que nous avons déjà enregistré — ce qui rend le rejeu du webhook inoffensif.
/// </param>
/// <param name="RefundCurrency">
/// Devise annoncée par le prestataire pour le remboursement. Nulle si absente ;
/// si elle est présente et ne correspond pas à celle du paiement, on refuse.
/// </param>
/// <param name="RefundReference">
/// Identifiant du remboursement CHEZ LE PRESTATAIRE. Sert de clé d'idempotence :
/// deux livraisons du même webhook produisent la même clé, donc une seule ligne
/// dans `payment_refunds` (index unique posé au lot 3.1).
/// </param>
public sealed record GatewayEvent(
    bool Verified,
    GatewayOutcome Outcome,
    string? ProviderReference,
    string? FailureReason,
    decimal? RefundAmount = null,
    decimal? TotalRefundedAmount = null,
    string? RefundCurrency = null,
    string? RefundReference = null);

/// <summary>Résultat d'un remboursement demandé au PSP.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// « ÉCHEC » RECOUVRAIT DEUX SITUATIONS OPPOSÉES, ET LA FILE S'EN SATURAIT.
///
/// « Le prestataire REFUSE » et « le prestataire NE RÉPOND PAS » rendaient le même
/// `Success:false`. L'appelant ne pouvait donc pas choisir : rejouer un refus est
/// inutile — il sera refusé à l'identique la fois d'après, indéfiniment — tandis
/// que ne PAS rejouer un timeout réseau abandonne un remboursement qui aurait
/// abouti à la seconde tentative.
///
/// <see cref="Transient"/> tranche : vrai = la panne est passagère (réseau, PSP
/// injoignable, 5xx), l'appel mérite d'être rejoué ; faux = décision métier du
/// prestataire, à enregistrer et à traiter à la main.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="Transient">
/// Vrai si l'échec vient du transport et non d'une décision du prestataire.
/// Faux par défaut : un adaptateur qui ne se prononce pas est réputé avoir été
/// REFUSÉ, ce qui est le côté sûr — on n'entretient pas une file de rejeux sur un
/// refus définitif.
/// </param>
public sealed record GatewayRefundResult(bool Success, string? ProviderReference, string? Error, bool Transient = false);

/// <summary>Contexte complet d'un remboursement, avec idempotence inter-service.</summary>
public sealed record GatewayRefundContext(
    string ProviderReference,
    decimal Amount,
    string Currency,
    string Reason,
    string IdempotencyKey);

/// <summary>
/// Port (hexagonal) d'un prestataire de paiement. Une implémentation par PSP
/// (Stripe, PayPal…) vit dans Infrastructure ; le métier ne dépend que de ce
/// contrat. Couvre les deux flux (checkout hébergé / intention serveur) et les
/// deux modes de confirmation (webhook signé / retour de redirection).
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Nom du prestataire, ex. « Stripe », « PayPal », « MtnMomo », « Moov » (insensible à la casse).</summary>
    string Provider { get; }

    /// <summary>Vrai si le PSP exige le numéro du payeur (Mobile Money : RequestToPay).</summary>
    bool RequiresPayerPhone { get; }

    /// <summary>
    /// Vrai si cet adaptateur sait RÉELLEMENT demander un remboursement au
    /// prestataire.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUATRE ADAPTATEURS RÉPONDAIENT « ÉCHEC » EN DUR, ET PERSONNE NE LE
    /// SAVAIT AU DÉMARRAGE.
    ///
    /// FedaPay, MTN MoMo, Moov et PayPal codent `RefundAsync` à `Success:false` :
    /// aucun appel n'est fait, aucun remboursement ne part. C'est HONNÊTE — mieux
    /// vaut cela qu'un faux « remboursé » — mais l'information n'existait qu'au
    /// fond d'une méthode, découverte le jour où un client réclame son argent.
    ///
    /// Cette propriété la remonte au démarrage : l'installeur refuse la production
    /// tant que l'exploitant n'a pas déclaré qu'il assume des remboursements
    /// manuels. Même règle que `SimulatedPayoutGateway`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    bool SupportsRefund => true;

    /// <summary>Crée une page de paiement hébergée et renvoie l'URL de redirection.</summary>
    Task<GatewaySession> CreateCheckoutAsync(GatewayChargeContext context, CancellationToken cancellationToken = default);

    /// <summary>Crée une intention de paiement à confirmer côté client (renvoie un client secret).</summary>
    Task<GatewaySession> CreatePaymentIntentAsync(GatewayChargeContext context, CancellationToken cancellationToken = default);

    /// <summary>Vérifie la signature et normalise le payload d'un webhook PSP.</summary>
    Task<GatewayEvent> ParseWebhookAsync(string rawBody, string? signatureHeader, CancellationToken cancellationToken = default);

    /// <summary>Interroge le statut courant d'une session/intention (retour de redirection, réconciliation).</summary>
    Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>Demande le remboursement d'un paiement encaissé.</summary>
    Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>Demande le remboursement d'un paiement encaissé avec montant et clé d'idempotence.</summary>
    Task<GatewayRefundResult> RefundAsync(GatewayRefundContext context, CancellationToken cancellationToken = default)
        => RefundAsync(context.ProviderReference, cancellationToken);
}
