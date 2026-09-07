using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Drivers.Infrastructure.Persistence.Configurations;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HBA.Drivers.Infrastructure.Auditing;
using HBA.Drivers.Infrastructure.Persistence.Outbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Drivers.Infrastructure.Persistence;

/// <summary>LE PREMIER `DbContext` DE driver-service.</summary>
public sealed class DriverDbContext : ModuleDbContext, IOutboxDbContext
{
    // L'OUTBOX ET L'INBOX DE CE SERVICE — LEURS TABLES LUI APPARTIENNENT.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void ConfigurerLesTablesTechniques(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxConfiguration());
        // PAS D'INBOX : ce service ne consomme aucun evenement d'integration.
        // La ligne `ApplyConfiguration(new ConsumerInboxConfiguration())` mappait
        // `consumer_inbox` dans le modele alors qu'aucune migration ne cree cette
        // table — et `InboxCleanupService` l'interrogeait a chaque tick.
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

    public const string SchemaName = "drivers";

    public DriverDbContext(
        DbContextOptions<DriverDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<DriverAccount> DriverAccounts => Set<DriverAccount>();

    protected override string Schema => SchemaName;

    /// <summary>LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).</summary>
    protected override bool KeepsAuditTrail => true;

    // LE JOURNAL D'AUDIT DE CE SERVICE — L'ENTITE ET SA TABLE LUI APPARTIENNENT.
    protected override void ConfigurerLeJournalDAudit(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfiguration(new AuditConfiguration());

    protected override void AjouterUneEntreeDAudit(
        string typeDEntite,
        string identifiant,
        AuditOperation operation,
        Guid? acteur,
        string typeDActeur,
        string? correlation,
        DateTime instantUtc)
        => Set<AuditEntry>().Add(new AuditEntry
        {
            EntityType = typeDEntite,
            EntityId = identifiant,
            Operation = operation,
            ActorUserId = acteur,
            ActorType = typeDActeur,
            CorrelationId = correlation,
            OccurredOnUtc = instantUtc
        });


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new DriverAccountConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
