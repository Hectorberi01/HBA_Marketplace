using HBA.Users.Domain.Preferences;
using Microsoft.EntityFrameworkCore;

namespace HBA.Users.Infrastructure.Persistence;

internal sealed class UserPreferencesRepository : IUserPreferencesRepository
{
    private readonly UsersDbContext _context;

    public UserPreferencesRepository(UsersDbContext context) => _context = context;

    public Task<UserPreferences?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
        => _context.Preferences.FirstOrDefaultAsync(p => p.Id == userId, cancellationToken);

    public async Task AddAsync(UserPreferences preferences, CancellationToken cancellationToken = default)
        => await _context.Preferences.AddAsync(preferences, cancellationToken);
}
