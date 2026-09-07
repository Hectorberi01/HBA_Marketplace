using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Delivery.Driver.Domain.Enums;
using HBA.Delivery.Driver.Domain.Repositories;
using HBA.Drivers.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HBA.Drivers.Infrastructure.Persistence.Repositories;

/// <summary>LECTURES DU DOSSIER LIVREUR.</summary>
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
/// comme `IDeliveryUnitOfWork` et `IReturnRefundUnitOfWork`.
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
