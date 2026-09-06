using HBA.Communication.Notifications.Domain.Notifications;
using HBA.Communication.Notifications.Domain.Templates;
using Microsoft.EntityFrameworkCore;

namespace HBA.Communication.Notifications.Infrastructure.Persistence;

internal sealed class NotificationTemplateRepository : INotificationTemplateRepository
{
    /// <summary>Locale de repli, cohérente avec `HbaRequestContext`.</summary>
    private const string LocaleParDefaut = "fr-BJ";

    private readonly NotificationsDbContext _context;

    public NotificationTemplateRepository(NotificationsDbContext context) => _context = context;

    public async Task<NotificationTemplate?> FindAsync(
        string code, NotificationChannel channel, string locale, CancellationToken cancellationToken = default)
    {
        var actifs = _context.Set<NotificationTemplate>().AsNoTracking()
            .Where(t => t.Code == code && t.Channel == channel && t.IsActive);

        // La locale demandée d'abord, la version la plus haute.
        var exact = await actifs
            .Where(t => t.Locale == locale)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (exact is not null || locale == LocaleParDefaut)
        {
            return exact;
        }

        // REPLI SUR LA LOCALE, JAMAIS SUR LE CANAL.
        //
        // Un texte dans la mauvaise langue reste lisible et se corrige. Un corps
        // d'e-mail envoyé par SMS coûte dix messages et arrive tronqué au milieu
        // d'une phrase.
        return await actifs
            .Where(t => t.Locale == LocaleParDefaut)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(NotificationTemplate template, CancellationToken cancellationToken = default)
        => await _context.Set<NotificationTemplate>().AddAsync(template, cancellationToken);
}
