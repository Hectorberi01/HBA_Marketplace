namespace HBA.Financial.Payments.Domain.Payments;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);

    Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken cancellationToken = default);

    /// <summary>Le paiement d'une commande, DANS SON UNIVERS.</summary>
    Task<Payment?> GetByOrderAsync(
        PaymentOrderType orderType, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Le paiement portant cet identifiant de commande, QUEL QUE SOIT l'univers.</summary>
    Task<Payment?> FindByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Retrouve un paiement par sa référence PSP (corrélation webhook / retour).</summary>
    Task<Payment?> GetByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>Liste tous les paiements de la plateforme (back-office admin).</summary>
    Task<IReadOnlyList<Payment>> ListAllAsync(int take = 200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Page de paiements pour la console admin : filtre par statut, recherche par
    /// identifiant (paiement ou commande, GUID exact), tri par date décroissante.
    /// </summary>
    Task<(IReadOnlyList<Payment> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, Guid? id, PaymentStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrégats (compteurs + montants encaissés/remboursés) sur l'ensemble filtré
    /// par recherche (identifiant paiement ou commande), pour les indicateurs de la
    /// console.
    /// </summary>
    Task<(int Total, int CapturedCount, decimal CapturedAmount, int PendingCount, int FailedCount, int RefundedCount, decimal RefundedAmount)> GetStatsAsync(
        Guid? id, CancellationToken cancellationToken = default);
}
