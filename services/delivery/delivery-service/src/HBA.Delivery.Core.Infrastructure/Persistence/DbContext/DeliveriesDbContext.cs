using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Partners;
using HBA.Deliveries.Domain.Webhooks;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HBA.Deliveries.Infrastructure.Auditing;
using HBA.Deliveries.Infrastructure.Persistence.Outbox;
using HBA.Deliveries.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Deliveries.Infrastructure.Persistence;

/// <summary>DbContext du module Deliveries (schéma « deliveries »).</summary>
public sealed class DeliveriesDbContext : ModuleDbContext, IOutboxDbContext, IDeliveryUnitOfWork
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

    public const string SchemaName = "deliveries";

    public DeliveriesDbContext(
        DbContextOptions<DeliveriesDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    /// <summary>
    /// Persiste, et rend <c> false</c> au lieu de lever sur un conflit de
    /// concurrence.
    /// </summary>
    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public DbSet<Domain.Deliveries.Delivery> Deliveries => Set<Domain.Deliveries.Delivery>();

    public DbSet<Driver> Drivers => Set<Driver>();

    // ── Partenaires (API publique) ──────────────────────────────────────────
    public DbSet<Partner> Partners => Set<Partner>();

    // ── Webhooks sortants ───────────────────────────────────────────────────
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    /// <summary>Traces de consommation Kafka (§19.5).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeliveriesDbContext).Assembly);
        // Configuration du socle : autre assembly, le balayage ne la trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
