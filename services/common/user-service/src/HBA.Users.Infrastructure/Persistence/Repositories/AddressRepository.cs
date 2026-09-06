using HBA.Users.Domain.Addresses;
using Microsoft.EntityFrameworkCore;

namespace HBA.Users.Infrastructure.Persistence;

internal sealed class AddressRepository : IAddressRepository
{
    private readonly UsersDbContext _dbContext;

    public AddressRepository(UsersDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Address address, CancellationToken cancellationToken = default)
        => await _dbContext.Addresses.AddAsync(address, cancellationToken);

    public void Remove(Address address)
        => _dbContext.Addresses.Remove(address);

    public async Task<Address?> GetByIdAsync(AddressId id, CancellationToken cancellationToken = default)
        => await _dbContext.Addresses.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Address>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.Addresses
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedOnUtc)
            .ToListAsync(cancellationToken);
}
