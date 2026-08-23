using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Financial.Billing.Application.Abstractions;
using HBA.Financial.Billing.Domain.Commissions;
using HBA.Financial.Billing.Domain.Invoices;

namespace HBA.Financial.Billing.Infrastructure.Persistence;

/// <summary>DbContext du module Billing (schéma « billing »).</summary>
public sealed class BillingDbContext : ModuleDbContext, IBillingUnitOfWork
{
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
