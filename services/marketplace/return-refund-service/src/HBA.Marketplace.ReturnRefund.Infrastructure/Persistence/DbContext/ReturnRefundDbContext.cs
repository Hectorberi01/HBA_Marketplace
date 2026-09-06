using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Persistence;

public sealed class ReturnRefundDbContext : ModuleDbContext, IReturnRefundUnitOfWork
{
    public const string SchemaName = "return_refund";

    public ReturnRefundDbContext(
        DbContextOptions<ReturnRefundDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<ReturnRequest> ReturnRequests => Set<ReturnRequest>();
    public DbSet<ReturnIdempotencyKey> IdempotencyKeys => Set<ReturnIdempotencyKey>();

    protected override string Schema => SchemaName;

    protected override bool KeepsAuditTrail => true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReturnRefundDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

public sealed class ReturnIdempotencyKey
{
    private ReturnIdempotencyKey()
    {
    }

    public ReturnIdempotencyKey(string key, Guid returnRequestId, DateTime createdAtUtc)
    {
        Key = key;
        ReturnRequestId = returnRequestId;
        CreatedAtUtc = createdAtUtc;
    }

    public string Key { get; private set; } = string.Empty;
    public Guid ReturnRequestId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
