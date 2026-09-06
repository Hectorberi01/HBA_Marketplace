using Microsoft.EntityFrameworkCore;
using HBA.Communication.Notifications.Domain.Preferences;

namespace HBA.Communication.Notifications.Infrastructure.Persistence;

internal sealed class NotificationPreferenceRepository : INotificationPreferenceRepository
{
    private readonly NotificationsDbContext _dbContext;

    public NotificationPreferenceRepository(NotificationsDbContext dbContext) => _dbContext = dbContext;

    public Task<NotificationPreference?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => _dbContext.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public async Task AddAsync(NotificationPreference preference, CancellationToken cancellationToken = default)
        => await _dbContext.NotificationPreferences.AddAsync(preference, cancellationToken);
}
