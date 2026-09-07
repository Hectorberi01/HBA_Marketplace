using HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;
using HBA.Marketplace.ReturnRefund.Domain.Enums;

namespace HBA.Marketplace.ReturnRefund.Domain.Repositories;

/// <summary>
/// Un remboursement DÉCIDÉ dont le versement n'est pas encore acquis : de quoi
/// recharger l'agrégat et relancer l'exécution.
/// </summary>
public sealed record RefundExecutionTicket(Guid ReturnId, Guid RefundId);

public interface IReturnRequestRepository
{
    Task<ReturnRequest?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<ReturnRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);
    Task AddAsync(ReturnRequest request, string idempotencyKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReturnRequest>> ListCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReturnRequest>> ListSellerAsync(Guid sellerId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Une page de dossiers, toutes boutiques confondues, pour l'administration.</summary>
    Task<(IReadOnlyList<ReturnRequest> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)>
        ListForAdminAsync(int page, int pageSize, ReturnStatus? status, CancellationToken cancellationToken);

    /// <summary>Le nombre de dossiers d'un client — pour un total de page exact.</summary>
    Task<int> CountCustomerAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>Le nombre de dossiers d'une boutique — pour un total de page exact.</summary>
    Task<int> CountSellerAsync(Guid sellerId, CancellationToken cancellationToken);

    /// <summary>
    /// Les remboursements en attente de versement, du plus ancien au plus récent,
    /// et seulement ceux dont le DOSSIER attend encore son versement
    /// (<c>ReturnStatus.RefundPending</c>).
    /// </summary>
    Task<IReadOnlyList<RefundExecutionTicket>> ListRefundsAwaitingExecutionAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>Les dossiers dont le délai est dépassé et qui attendent encore quelqu'un.</summary>
    Task<IReadOnlyList<ReturnRequest>> ListExpirableAsync(DateTime nowUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Ce que les dossiers ENCORE OUVERTS de cette commande ont déjà engagé, ligne
    /// de commande par ligne de commande.
    /// </summary>
    /// <param name="exceptReturnId">
    /// Dossier à ne pas compter — le sien, quand l'appelant est en train de décider
    /// sur ce dossier-là et compte déjà ses propres engagements.
    /// </param>
    Task<IReadOnlyDictionary<Guid, int>> ListOpenQuantitiesByOrderAsync(
        Guid orderId, Guid? exceptReturnId, CancellationToken cancellationToken);

    /// <summary>
    /// Le résumé des retours d'une commande : ce qui a été REMBOURSÉ, et combien de
    /// dossiers sont ENCORE OUVERTS.
    /// </summary>
    Task<(decimal MontantRembourse, string Devise, int DossiersActifs)> GetOrderSummaryAsync(
        Guid orderId, CancellationToken cancellationToken);
}
