namespace HBA.Shared.Application.Abstractions;

/// <summary>Frontière transactionnelle d'un module.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
