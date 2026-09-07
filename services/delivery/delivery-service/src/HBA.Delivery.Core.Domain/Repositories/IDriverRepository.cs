// DÉPLACÉ DEPUIS `driver-service/src/HBA.Delivery.Driver.Domain/Repositories` (lot
// 5.4, ISSUE-069).

using HBA.Deliveries.Domain.Deliveries;

namespace HBA.Deliveries.Domain.Drivers;

/// <summary>Accès aux livreurs. L'implémentation vit en Infrastructure.</summary>
public interface IDriverRepository
{
    Task<Driver?> GetByIdAsync(DriverId id, CancellationToken cancellationToken = default);

    /// <summary>Retrouve le livreur rattaché à un compte utilisateur.</summary>
    Task<Driver?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Charge plusieurs livreurs d'un coup, à partir des identifiants renvoyés par
    /// le cache de positions.
    /// </summary>
    Task<IReadOnlyList<Driver>> ListByIdsAsync(IReadOnlyCollection<DriverId> ids, CancellationToken cancellationToken = default);

    /// <summary>Livreurs d'un état de compte donné, les plus anciens d'abord.</summary>
    Task<IReadOnlyList<Driver>> ListByAccountStatusAsync(
        DriverAccountStatus status, int take = 100, CancellationToken cancellationToken = default);

    Task AddAsync(Driver driver, CancellationToken cancellationToken = default);
}
