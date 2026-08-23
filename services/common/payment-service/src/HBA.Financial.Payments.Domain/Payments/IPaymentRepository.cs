namespace HBA.Financial.Payments.Domain.Payments;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);

    Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Le paiement d'une commande, DANS SON UNIVERS.
    ///
    /// L'UNIVERS FAIT PARTIE DE LA CLÉ, ET CE N'EST PAS DE LA PRÉCAUTION.
    ///
    /// `ix_payments_order` porte `(OrderType, OrderId)` depuis le début, et sa
    /// configuration le dit : « deux commandes d'univers différents peuvent porter
    /// le même identifiant sans que ce soit une anomalie ». La lecture, elle,
    /// filtrait sur `OrderId` SEUL — donc ne pouvait pas se servir de cet index
    /// (sa colonne de tête est `OrderType`), et surtout n'avait aucun moyen de
    /// distinguer les deux univers le jour où le second existerait. Ce jour est
    /// celui du lot 6.1.
    /// </summary>
    Task<Payment?> GetByOrderAsync(
        PaymentOrderType orderType, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Le paiement portant cet identifiant de commande, QUEL QUE SOIT l'univers.
    ///
    /// RÉSERVÉ À LA CONSULTATION. Elle sert la route
    /// `GET /api/financial/payments/by-order/{orderId}`, dont les appelants n'ont
    /// jamais eu à dire de quel univers ils parlent. Aucun chemin qui TOUCHE à
    /// l'argent ne doit passer par là : pour agir sur un paiement, on sait
    /// toujours de quel univers vient la commande, et `GetByOrderAsync` est alors
    /// la bonne porte.
    /// </summary>
    Task<Payment?> FindByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Retrouve un paiement par sa référence PSP (corrélation webhook / retour).</summary>
    Task<Payment?> GetByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken = default);

    /// <summary>Liste tous les paiements de la plateforme (back-office admin).</summary>
    Task<IReadOnlyList<Payment>> ListAllAsync(int take = 200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Page de paiements pour la console admin : filtre par statut, recherche par
    /// identifiant (paiement ou commande, GUID exact), tri par date décroissante.
    /// Renvoie le total filtré + la répartition par statut (avant filtre statut).
    /// </summary>
    Task<(IReadOnlyList<Payment> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
        int page, int pageSize, Guid? id, PaymentStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrégats (compteurs + montants encaissés/remboursés) sur l'ensemble filtré par
    /// recherche (identifiant paiement ou commande), pour les indicateurs de la console.
    /// </summary>
    Task<(int Total, int CapturedCount, decimal CapturedAmount, int PendingCount, int FailedCount, int RefundedCount, decimal RefundedAmount)> GetStatsAsync(
        Guid? id, CancellationToken cancellationToken = default);
}
