using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Application.Abstractions;

/// <summary>Unit of Work propre au module Sellers (évite la collision DI inter-modules).</summary>
public interface ISellerUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Sérialise les mutations d'équipe d'un même vendeur, pour la durée de la
    /// transaction en cours.
    /// </summary>
    /// <param name="operation">
    /// Le travail à mener sous verrou : lectures, décision, et le <c>
    /// SaveChangesAsync</c> qui la persiste.
    /// </param>
    /// <returns>Le résultat de l'opération.</returns>
    Task<Result> ExecuteUnderSellerLockAsync(
        Guid sellerId,
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken cancellationToken = default);
}
