using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Financial.Billing.Application.Abstractions;
using HBA.Financial.Billing.Domain.Commissions;
using HBA.Financial.Billing.Domain.Invoices;

using HBA.Financial.Billing.Infrastructure.Persistence.Outbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Financial.Billing.Infrastructure.Persistence;

/// <summary>DbContext du module Billing (schéma « billing »).</summary>
public sealed class BillingDbContext : ModuleDbContext, IOutboxDbContext, IBillingUnitOfWork
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

    public const string SchemaName = "billing";

    public BillingDbContext(
        DbContextOptions<BillingDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
