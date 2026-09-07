using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Delivery.Driver.Domain.Enums;

namespace HBA.Delivery.Driver.Domain.Repositories;

/// <summary>Accès aux dossiers livreur.</summary>
public interface IDriverAccountRepository
{
    Task<DriverAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Le dossier rattaché à un compte HBA, pièces et véhicules compris.</summary>
    Task<DriverAccount?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Un compte a-t-il déjà un dossier ? Lecture sans matérialisation.</summary>
    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Dossiers d'un état donné, les plus anciens d'abord.</summary>
    Task<IReadOnlyList<DriverAccount>> ListByStatusAsync(
        DriverVerificationStatus status, int take = 100, CancellationToken cancellationToken = default);

    Task AddAsync(DriverAccount account, CancellationToken cancellationToken = default);
}
