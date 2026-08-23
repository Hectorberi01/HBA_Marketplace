using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Communication.Notifications.Domain.Templates;
using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Communication.Notifications.Application.Abstractions;
using HBA.Communication.Notifications.Domain.Devices;
using HBA.Communication.Notifications.Domain.Notifications;
using HBA.Communication.Notifications.Domain.Preferences;

namespace HBA.Communication.Notifications.Infrastructure.Persistence;

/// <summary>DbContext du module Notifications (schéma « notifications »).</summary>
public sealed class NotificationsDbContext : ModuleDbContext, INotificationsUnitOfWork
{
    public const string SchemaName = "notifications";

    public NotificationsDbContext(
        DbContextOptions<NotificationsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    /// <summary>Gabarits transactionnels du §10.15, versionnés par (code, canal, locale).</summary>
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();

    /// <summary>Traces de consommation Kafka (§19.5) et requêtes idempotentes (§5).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
        // Configurations du socle : autre assembly, le balayage ne les trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());
        
        base.OnModelCreating(modelBuilder);
    }
}
