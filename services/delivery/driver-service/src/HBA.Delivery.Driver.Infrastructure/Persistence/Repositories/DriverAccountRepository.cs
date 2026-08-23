using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Delivery.Driver.Domain.Enums;
using HBA.Delivery.Driver.Domain.Repositories;
using HBA.Drivers.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HBA.Drivers.Infrastructure.Persistence.Repositories;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LECTURES DU DOSSIER LIVREUR.
///
/// AUCUNE N'EST EN `AsNoTracking`, ET C'EST VOULU : les quatre commandes de ce
/// service mutent l'agrégat qu'elles viennent de lire. Une lecture non suivie
/// rendrait `SaveChanges` silencieusement sans effet — le pire cas, puisque
/// l'appelant reçoit un succès.
///
/// Le port de LECTURE PURE, lui, n'existe pas encore : les requêtes passent par le
/// même chemin et paient donc le suivi pour rien. C'est un coût mesurable
/// seulement sur la file de vérification, qui charge jusqu'à deux cents dossiers
/// avec leurs pièces. À revoir le jour où cette file sera réellement utilisée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class DriverAccountRepository : IDriverAccountRepository
{
    private readonly DriverDbContext _dbContext;

    public DriverAccountRepository(DriverDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<DriverAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _dbContext.DriverAccounts.FirstOrDefaultAsync(account => account.Id == id, cancellationToken);

    public Task<DriverAccount?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => _dbContext.DriverAccounts.FirstOrDefaultAsync(account => account.UserId == userId, cancellationToken);

    public Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => _dbContext.DriverAccounts.AnyAsync(account => account.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<DriverAccount>> ListByStatusAsync(
        DriverVerificationStatus status, int take = 100, CancellationToken cancellationToken = default)
        => await _dbContext.DriverAccounts
            .Where(account => account.VerificationStatus == status)
            .OrderBy(account => account.SubmittedAtUtc ?? account.RegisteredAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(DriverAccount account, CancellationToken cancellationToken = default)
        => await _dbContext.DriverAccounts.AddAsync(account, cancellationToken);
}

/// <summary>
/// La frontière transactionnelle du module, servie par le `DbContext` lui-même —
/// comme `IDeliveryUnitOfWork` et `IReturnRefundUnitOfWork`. C'est ce qui garantit
/// que l'effet métier et son événement d'intégration partent ensemble.
/// </summary>
internal sealed class DriverUnitOfWork : IDriverUnitOfWork
{
    private readonly DriverDbContext _dbContext;

    public DriverUnitOfWork(DriverDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _dbContext.SaveChangesAsync(cancellationToken);
}
