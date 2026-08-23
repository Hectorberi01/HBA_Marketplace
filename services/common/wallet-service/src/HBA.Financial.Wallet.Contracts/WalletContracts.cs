using HBA.Shared.Domain.Results;

namespace HBA.Financial.Wallet.Contracts;

/// <summary>Soldes du portefeuille d'un vendeur.</summary>
public sealed record SellerWalletView(
    Guid SellerId,
    decimal PendingBalance,
    decimal AvailableBalance,
    // Somme des retraits EN COURS DE ROUTE : demandés (attente de validation admin) ET
    // en cours de versement chez le PSP. Dans les deux cas les fonds sont déjà retenus,
    // retirés du solde principal. Ne compter que les premiers ferait « disparaître »
    // l'argent de l'écran du vendeur entre la validation et la confirmation du PSP.
    decimal PendingWithdrawal,
    string Currency);

/// <summary>
/// Soldes du portefeuille d'un livreur.
///
/// PAS DE « SOLDE À VENIR », CONTRAIREMENT AU VENDEUR : une course est faite ou ne
/// l'est pas, et aucun retour produit ne reprend le gain de celui qui a transporté
/// le colis. Voir l'encadré de DriverWallet.
/// </summary>
public sealed record DriverWalletView(
    Guid DriverId,
    decimal AvailableBalance,

    // Total gagné depuis l'inscription, retraits compris. Le solde seul ne dit rien
    // de ce qu'on a gagné — un retrait le remet à zéro — et c'est ce cumul que le
    // livreur regarde pour savoir si le métier le nourrit.
    decimal LifetimeEarned,
    string Currency);

/// <summary>Demande de retrait en attente de validation (vue admin, enrichie vendeur).</summary>
public sealed record PendingWithdrawalView(
    Guid Id,
    Guid SellerId,
    string SellerName,
    decimal Amount,
    string Currency,
    string? PayoutProvider,
    string? PayoutAccountNumber,
    DateTime CreatedAtUtc);

/// <summary>
/// Retrait dont le versement a été demandé au PSP mais N'EST PAS confirmé (vue admin).
///
/// C'est l'état qui mérite une surveillance humaine : tant qu'il dure, le vendeur est
/// débité sans avoir reçu son argent. <paramref name="Anomaly"/> porte l'incident
/// éventuel (timeout…), et un <paramref name="ProviderRef"/> ABSENT signale le cas le
/// plus grave : versement peut-être parti, mais impossible à réconcilier
/// automatiquement — il faut le retrouver dans le tableau de bord du PSP.
/// </summary>
public sealed record ProcessingWithdrawalView(
    Guid Id,
    Guid SellerId,
    string SellerName,
    decimal Amount,
    string Currency,
    string? ProviderRef,
    string? Anomaly,
    DateTime CreatedAtUtc,
    DateTime? SentToPspAtUtc);

/// <summary>Soldes du portefeuille de la plateforme (admin).</summary>
public sealed record PlatformWalletView(
    decimal CommissionBalance,
    decimal ProviderFeeBalance,
    decimal ShippingBalance,
    decimal RefundsBalance,
    string Currency);

/// <summary>Un remboursement client direct (versement MoMo initié par l'admin).</summary>
public sealed record CustomerRefundView(
    Guid Id,
    Guid OrderId,
    Guid BuyerId,
    decimal Amount,
    string Currency,
    string Reason,
    string Msisdn,
    string Provider,
    string Status,
    string? ProviderRef,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

/// <summary>Une ligne du grand livre wallet (relevé vendeur / plateforme).</summary>
public sealed record WalletTransactionView(
    Guid Id,
    string Account,
    string Direction,
    decimal Amount,
    string Currency,
    string Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTime CreatedAtUtc);

/// <summary>Une demande de retrait vendeur.</summary>
public sealed record WithdrawalView(
    Guid Id,
    Guid SellerId,
    decimal Amount,
    string Currency,
    string Status,
    string? ProviderRef,
    string? FailureReason,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);


// ════════════════════════════════════════════════════════════════════════════
// LE PORTEFEUILLE CLIENT (D33).
//
// FedaPay n'expose aucune API de remboursement. Un remboursement client crédite
// donc SON portefeuille — l'argent lui est rendu immédiatement, à l'intérieur de
// la plateforme — et le virement vers son Mobile Money est une DEMANDE distincte,
// exécutée et marquée payée à la main par un administrateur.
// ════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Solde du portefeuille d'un client.
///
/// PAS DE « SOLDE À VENIR » : un remboursement est acquis à la seconde où il est
/// décidé, l'argent est déjà celui du client. Voir l'encadré de `CustomerWallet`.
/// </summary>
public sealed record CustomerWalletView(
    Guid CustomerId,
    decimal AvailableBalance,

    // Total remboursé depuis toujours, virements sortis compris. Le solde seul ne
    // dit rien de ce qui a été rendu — un virement le remet à zéro — et c'est ce
    // cumul que le client oppose au support quand il conteste un remboursement.
    decimal LifetimeRefunded,

    // Somme des demandes de virement EN ATTENTE de décision : les fonds ont déjà
    // quitté le solde disponible. Les omettre ferait « disparaître » l'argent de
    // l'écran du client entre sa demande et la décision de l'administrateur — le
    // même défaut que celui corrigé sur `SellerWalletView.PendingWithdrawal`.
    decimal PendingWithdrawal,
    string Currency);

/// <summary>Une demande de virement d'un client vers son Mobile Money.</summary>
public sealed record CustomerWithdrawalView(
    Guid Id,
    Guid CustomerId,
    decimal Amount,
    string Currency,

    // Destination FIGÉE à la demande : c'est elle, et rien d'autre, que
    // l'administrateur recopie chez le prestataire. Voir `CustomerWithdrawal.Msisdn`.
    string Msisdn,
    string Provider,
    string Status,

    // Référence du virement saisie par l'administrateur : la SEULE preuve que
    // l'argent est parti — aucun webhook ne confirmera ce versement.
    string? ExternalReference,
    string? AdminNote,
    DateTime RequestedAtUtc,
    DateTime? DecidedAtUtc,
    Guid? DecidedByUserId);

/// <summary>
/// Ce que rend un crédit de remboursement au portefeuille d'un client.
///
/// <para>
/// <paramref name="TransactionId"/> est l'identifiant de l'OPÉRATION au grand livre
/// (`WalletTransaction.TransactionId`) : c'est lui que l'appelant inscrit dans son
/// propre dossier pour pouvoir, plus tard, montrer par quelle écriture l'argent a
/// été rendu.
/// </para>
///
/// <para>
/// <paramref name="AlreadyApplied"/> distingue un crédit RÉEL d'un rejeu reconnu.
/// Les deux sont des succès et portent le MÊME <paramref name="TransactionId"/> —
/// c'est tout l'intérêt de la clé d'idempotence. Mais un appelant qui journalise
/// « client remboursé » sur un rejeu raconte deux remboursements pour un seul, et
/// c'est ce qui rend un rapprochement impossible à relire.
/// </para>
/// </summary>
public sealed record CustomerWalletCreditResult(
    Guid TransactionId,
    decimal NewBalance,
    string Currency,
    bool AlreadyApplied);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// PAR OÙ L'ARGENT REVIENT AU CLIENT QUAND LE PRESTATAIRE NE SAIT PAS LE RENDRE.
///
/// C'est le point d'entrée que payment-service appelle lorsque
/// `IPaymentGateway.SupportsRefund` est faux. Le remboursement PSP reste la voie
/// NORMALE quand la passerelle sait rembourser — Stripe rend l'argent sur la
/// carte, ce qui évite au client toute étape de retrait. Ce sont les prestataires
/// qui ne remboursent pas (FedaPay, MTN, Moov, PayPal dans ce dépôt) qui basculent
/// ici (D33).
///
/// IN-PROCESS, ET C'EST UNE CONTRAINTE DE DÉPLOIEMENT.
///
/// `HBA.Financial.Api` héberge payments, wallet et billing dans le même
/// processus : l'implémentation est locale, sans réseau. Séparer un jour ces
/// modules exigerait un transport ici — et c'est ce fichier qui le dira.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface ICustomerWalletApi
{
    /// <summary>
    /// Rend un montant au client sur son portefeuille.
    ///
    /// <para>
    /// <paramref name="idempotencyKey"/> sert de RÉFÉRENCE D'IDEMPOTENCE au grand
    /// livre : un rejeu ne crédite pas deux fois, il rend le même résultat (avec
    /// `AlreadyApplied = true`). Elle est OBLIGATOIRE — un appel avec une clé vide
    /// est refusé, il n'existe aucune clé de repli fabriquée ici. Voir
    /// `WalletReference` pour la portée exacte (elle inclut le client).
    /// </para>
    ///
    /// <para>
    /// Le portefeuille est créé à la volée si le client n'en a pas encore.
    /// </para>
    /// </summary>
    Task<Result<CustomerWalletCreditResult>> CreditRefundAsync(
        Guid customerId, decimal amount, string currency, string reason,
        string idempotencyKey, CancellationToken cancellationToken = default);
}
