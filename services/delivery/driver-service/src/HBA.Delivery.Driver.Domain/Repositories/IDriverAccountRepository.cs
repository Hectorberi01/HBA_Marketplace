using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Delivery.Driver.Domain.Enums;

namespace HBA.Delivery.Driver.Domain.Repositories;

/// <summary>
/// Accès aux dossiers livreur. L'implémentation vit en Infrastructure.
///
/// `GetByUserIdAsync` EST LA LECTURE CENTRALE DE CE SERVICE, pas une commodité :
/// TOUTES les routes `/me` passent par elle. C'est elle qui remplace le
/// `DefaultDriverId` codé en dur de `DriverStore`, et c'est pourquoi elle prend un
/// `userId` — celui du jeton — et jamais un `driverId` fourni par l'appelant.
/// </summary>
public interface IDriverAccountRepository
{
    Task<DriverAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Le dossier rattaché à un compte HBA, pièces et véhicules compris.</summary>
    Task<DriverAccount?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Un compte a-t-il déjà un dossier ? Lecture sans matérialisation.</summary>
    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dossiers d'un état donné, les plus anciens d'abord.
    ///
    /// L'ORDRE N'EST PAS COSMÉTIQUE : sans lui, la file de vérification se lit
    /// dans l'ordre physique des lignes, et un livreur qui attend depuis trois
    /// jours passe après celui qui vient de s'inscrire. C'est le même raisonnement
    /// que `IDriverRepository.ListByAccountStatusAsync` chez delivery-service.
    /// </summary>
    Task<IReadOnlyList<DriverAccount>> ListByStatusAsync(
        DriverVerificationStatus status, int take = 100, CancellationToken cancellationToken = default);

    Task AddAsync(DriverAccount account, CancellationToken cancellationToken = default);
}
