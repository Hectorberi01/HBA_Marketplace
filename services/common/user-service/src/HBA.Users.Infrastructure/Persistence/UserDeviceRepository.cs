using HBA.Users.Domain.Devices;
using Microsoft.EntityFrameworkCore;

namespace HBA.Users.Infrastructure.Persistence;

internal sealed class UserDeviceRepository : IUserDeviceRepository
{
    private readonly UsersDbContext _context;

    public UserDeviceRepository(UsersDbContext context) => _context = context;

    public async Task AddAsync(UserDevice device, CancellationToken cancellationToken = default)
        => await _context.Devices.AddAsync(device, cancellationToken);

    public Task<UserDevice?> FindAsync(
        Guid userId, string pushToken, CancellationToken cancellationToken = default)
        => _context.Devices.FirstOrDefaultAsync(
            d => d.UserId == userId && d.PushToken == pushToken, cancellationToken);

    public async Task<IReadOnlyList<UserDevice>> ListByUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await _context.Devices
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAtUtc)
            .ToListAsync(cancellationToken);
}
