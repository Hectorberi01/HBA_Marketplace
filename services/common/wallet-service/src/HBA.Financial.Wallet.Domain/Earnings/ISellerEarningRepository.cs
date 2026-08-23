namespace HBA.Financial.Wallet.Domain.Earnings;

/// <summary>Agrégat des gains d'un vendeur sur une période (relevé).</summary>
/// <summary>Relevé agrégé d'un vendeur sur une période.</summary>
/// <param name="ProviderFees">
/// Frais du prestataire de paiement.
///
/// IL MANQUAIT AU RÉSUMÉ, ALORS QU'IL ÉTAIT SUR CHAQUE GAIN.
///
/// `SellerEarning.ProviderFeeAmount` existe depuis l'origine, et les LIGNES du
/// relevé le rendaient déjà. Seule l'agrégation l'omettait : l'application devait
/// donc soit sommer les lignes elle-même, soit afficher zéro. La première solution
/// aurait mis une règle d'agrégation financière dans trois applications ; la
/// seconde aurait dit au vendeur que le paiement ne lui coûte rien.
///
/// CONSÉQUENCE À CONNAÎTRE : `GrossSales - Commissions` NE DONNE PAS
/// `NetPayout`. Il faut retirer les deux. C'est précisément ce qu'un résumé sans
/// les frais rendait impossible à vérifier.
/// </param>
public sealed record SellerStatement(
    Guid SellerId, decimal GrossSales, decimal Commissions, decimal ProviderFees,
    decimal NetPayout, string Currency, int LineCount);

public interface ISellerEarningRepository
{
    Task AddAsync(SellerEarning earning, CancellationToken cancellationToken = default);

    /// <summary>Vrai si la commande a déjà été comptabilisée (idempotence des events).</summary>
    Task<bool> ExistsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Gains accumulés (non soldés) créés dans la période.</summary>
    Task<IReadOnlyList<SellerEarning>> ListAccruedInPeriodAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    /// <summary>Gains libérés (escrow levé à la livraison) et donc payables, dans la période.</summary>
    Task<IReadOnlyList<SellerEarning>> ListReleasedInPeriodAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    /// <summary>Gains rattachés à une commande (pour libération à la livraison).</summary>
    Task<IReadOnlyList<SellerEarning>> ListByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Gains rattachés à un lot de reversement (pour annuler le lot).</summary>
    Task<IReadOnlyList<SellerEarning>> ListByBatchAsync(Guid settlementBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gains PAYABLES d'un vendeur, DU PLUS ANCIEN AU PLUS RÉCENT.
    ///
    /// L'ordre n'est pas cosmétique : c'est la règle d'imputation d'un retrait
    /// (voir <c>SellerEarningImputation</c>). Le changer changerait quels gains un
    /// retrait consomme, donc ce qui reste à reverser au lot suivant.
    ///
    /// Sans borne de période, contrairement à <see cref="ListReleasedInPeriodAsync"/> :
    /// un retrait ne se rattache à aucune période de règlement, il consomme ce qui
    /// est payable au moment où le vendeur le demande.
    /// </summary>
    Task<IReadOnlyList<SellerEarning>> ListReleasedBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Gains soldés par un retrait donné (pour les rendre payables si le retrait échoue).</summary>
    Task<IReadOnlyList<SellerEarning>> ListByWithdrawalAsync(Guid withdrawalId, CancellationToken cancellationToken = default);

    Task<SellerStatement> GetSellerStatementAsync(Guid sellerId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lignes détaillées du relevé d'un vendeur : chaque gain créé sur la période,
    /// trié par date de création. Sert à produire les écritures détaillées
    /// (vente + commission) du relevé, sans agrégation.
    /// </summary>
    Task<IReadOnlyList<SellerEarning>> ListSellerEarningsAsync(Guid sellerId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
}
