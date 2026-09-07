using HBA.Users.Application.Abstractions;
using HBA.Users.Domain.Addresses;
using HBA.Users.Domain.Devices;
using HBA.Users.Domain.Preferences;
using HBA.Users.Domain.Profiles;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HBA.Users.Infrastructure.Persistence.Outbox;
using HBA.Users.Infrastructure.Persistence.Inbox;
using HBA.Users.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Users.Infrastructure.Persistence;

/// <summary>LE MODULE USER — « QUI EST LA PERSONNE ? »</summary>
public sealed class UsersDbContext : ModuleDbContext, IOutboxDbContext, IUsersUnitOfWork
{
    // L'OUTBOX ET L'INBOX DE CE SERVICE — LEURS TABLES LUI APPARTIENNENT.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void ConfigurerLesTablesTechniques(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxConfiguration());
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
    }

    protected override void AjouterAuOutbox(
        string type, string contenu, DateTime survenuLeUtc, string? traceParent, string? correlation)
        => OutboxMessages.Add(new OutboxMessage
        {
            Type = type,
            Content = contenu,
            OccurredOnUtc = survenuLeUtc,
            TraceParent = traceParent,
            CorrelationId = correlation
        });

    public const string SchemaName = "users";

    public UsersDbContext(
        DbContextOptions<UsersDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Address> Addresses => Set<Address>();

    /// <summary>
    /// Les profils. Leur clé primaire est le UserId émis par Identity — voir
    /// l'encadré sur UserProfile : c'est ce qui rend « deux profils pour un compte
    /// » impossible par construction.
    /// </summary>
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    /// <summary>Préférences (§10.2). Une ligne par utilisateur, clé = UserId.</summary>
    public DbSet<UserPreferences> Preferences => Set<UserPreferences>();

    /// <summary>Appareils enregistrés pour les notifications push (§10.2).</summary>
    public DbSet<UserDevice> Devices => Set<UserDevice>();

    /// <summary>Traces de consommation Kafka (§19.5) et requêtes idempotentes (§5).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UsersDbContext).Assembly);

        // Les configurations du socle vivent dans un AUTRE assembly : le balayage
        // ci-dessus ne les trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());
        base.OnModelCreating(modelBuilder);
    }
}
