using HBA.Shared.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Domain.Payments;
using HBA.Financial.Payments.Domain.PaymentMethods;

using HBA.Financial.Payments.Infrastructure.Auditing;
using HBA.Financial.Payments.Infrastructure.Persistence.Outbox;
using HBA.Financial.Payments.Infrastructure.Persistence.Inbox;
using HBA.Financial.Payments.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Financial.Payments.Infrastructure.Persistence;

/// <summary>DbContext du module Payments (schéma « payments »).</summary>
public sealed class PaymentsDbContext : ModuleDbContext, IOutboxDbContext, IPaymentsUnitOfWork
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

    public const string SchemaName = "payments";

    public PaymentsDbContext(
        DbContextOptions<PaymentsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>Les moyens de paiement enregistrés par les acheteurs.</summary>
    public DbSet<SavedPaymentMethod> SavedPaymentMethods => Set<SavedPaymentMethod>();

    /// <summary>Traces de consommation Kafka (§19.5) et requêtes idempotentes (§5).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    /// <summary>SUR CE SERVICE, L'IDEMPOTENCE N'EST PAS UN CONFORT.</summary>
    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
        // Configurations du socle : autre assembly, le balayage ne les trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());
        
        base.OnModelCreating(modelBuilder);
    }
}
