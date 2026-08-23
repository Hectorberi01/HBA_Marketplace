using HBA.Shared.Domain.Primitives;

namespace HBA.Financial.Wallet.Domain.Wallets;

/// <summary>
/// Demande de retrait d'un vendeur depuis son solde principal. Créée à l'état
/// Requested (fonds retenus) et en attente de validation par l'admin. À la
/// validation, un payout FedaPay Mobile Money est déclenché : Completed en cas
/// de succès (avec la référence PSP) ou Failed (avec le motif). L'admin peut
/// aussi la refuser (Rejected), auquel cas les fonds sont recrédités.
/// </summary>
public sealed class Withdrawal : AggregateRoot<WithdrawalId>
{
    private Withdrawal()
    {
    }

    private Withdrawal(
        WithdrawalId id, Guid sellerId, decimal amount, string currency,
        string? payoutProvider, string? payoutAccountNumber, string? payoutAccountName)
        : base(id)
    {
        SellerId = sellerId;
        Amount = amount;
        Currency = currency;
        PayoutProvider = payoutProvider;
        PayoutAccountNumber = payoutAccountNumber;
        PayoutAccountName = payoutAccountName;
        Status = WithdrawalStatus.Requested;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Vrai tant que la demande attend la validation de l'admin.</summary>
    public bool IsPendingApproval => Status == WithdrawalStatus.Requested;

    /// <summary>Versement demandé au PSP, en attente de confirmation (réconciliation).</summary>
    public bool IsProcessing => Status == WithdrawalStatus.Processing;

    /// <summary>Date de la demande de versement au PSP (sert au délai d'alerte).</summary>
    public DateTime? SentToPspAtUtc { get; private set; }

    public Guid SellerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public WithdrawalStatus Status { get; private set; }
    public string? ProviderRef { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA DESTINATION DU VIREMENT, FIGÉE À LA DEMANDE.
    ///
    /// ELLE NE L'ÉTAIT PAS, ET LA VALIDATION ADMIN NE PROTÉGEAIT DONC RIEN.
    ///
    /// La demande ne portait que le montant. À l'approbation, le handler relisait
    /// le compte de versement COURANT du vendeur. Séquence :
    ///
    ///   1. le vendeur demande un retrait — les fonds sont retenus ;
    ///   2. le compte de versement est modifié ;
    ///   3. l'admin approuve — l'argent part vers le NOUVEAU compte.
    ///
    /// L'étape de validation existe précisément pour contrôler les sorties
    /// d'argent. Elle était contournée par le fait que la destination était
    /// résolue APRÈS la validation : l'admin ne pouvait pas voir ce qu'il
    /// approuvait, puisque sa file d'attente affichait elle aussi le compte lu à
    /// l'instant présent.
    ///
    /// En Mobile Money, un versement parti ne revient pas.
    ///
    /// NULLABLE — ET C'EST UNE DETTE, PAS UN CHOIX.
    /// Les demandes créées AVANT cette colonne n'ont pas de destination figée.
    /// Voir ApproveWithdrawalCommandHandler : ces demandes-là retombent sur le
    /// compte courant, avec une trace explicite.
    /// </summary>
    public string? PayoutProvider { get; private set; }

    /// <summary>Numéro Mobile Money visé, figé à la demande. Voir <see cref="PayoutProvider"/>.</summary>
    public string? PayoutAccountNumber { get; private set; }

    /// <summary>Nom du bénéficiaire, figé à la demande.</summary>
    public string? PayoutAccountName { get; private set; }

    /// <summary>Cette demande porte-t-elle une destination figée ?</summary>
    public bool HasFrozenDestination
        => !string.IsNullOrWhiteSpace(PayoutProvider) && !string.IsNullOrWhiteSpace(PayoutAccountNumber);

    /// <summary>
    /// La destination figée correspond-elle encore au compte du vendeur ?
    ///
    /// Un écart n'est pas forcément une fraude — un vendeur corrige un numéro mal
    /// saisi. Mais c'est toujours une demande PÉRIMÉE : elle ne vise plus ce que
    /// le vendeur veut, et plus ce que l'admin croit valider.
    /// </summary>
    public bool MatchesDestination(string? provider, string? accountNumber)
        => string.Equals(PayoutProvider, provider, StringComparison.OrdinalIgnoreCase)
           && string.Equals(PayoutAccountNumber, accountNumber, StringComparison.Ordinal);

    public static Withdrawal Create(
        Guid sellerId, decimal amount, string currency,
        string payoutProvider, string payoutAccountNumber, string? payoutAccountName)
        => new(WithdrawalId.New(), sellerId, amount,
            string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant(),
            payoutProvider, payoutAccountNumber, payoutAccountName);

    /// <summary>
    /// Le versement a été DEMANDÉ au PSP (créé + démarré), ou son issue est indéterminée.
    /// Les fonds restent débités : on ne rembourse pas tant qu'on ne sait pas.
    /// <paramref name="providerRef"/> peut être nul (cas d'un timeout avant d'obtenir
    /// l'identifiant) — le retrait devra alors être tranché manuellement.
    /// </summary>
    public void MarkProcessing(string? providerRef, string? note = null)
    {
        Status = WithdrawalStatus.Processing;
        ProviderRef = providerRef ?? ProviderRef;
        FailureReason = note; // trace de l'anomalie éventuelle (timeout…)
        SentToPspAtUtc ??= DateTime.UtcNow;
    }

    /// <summary>
    /// Versement CONFIRMÉ par le PSP (statut « sent »). C'est le seul chemin vers
    /// Completed : on ne clôture jamais sur une simple acceptation de la demande.
    /// </summary>
    public void Complete(string? providerRef)
    {
        Status = WithdrawalStatus.Completed;
        ProviderRef = providerRef ?? ProviderRef;
        FailureReason = null;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Fail(string reason)
    {
        Status = WithdrawalStatus.Failed;
        FailureReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Refus par l'admin (les fonds sont recrédités par le handler).</summary>
    public void Reject(string reason)
    {
        Status = WithdrawalStatus.Rejected;
        FailureReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }
}
