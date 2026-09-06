using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HBA.Marketplace.ReturnRefund.Infrastructure.Auditing;
using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence.Outbox;
using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Marketplace.ReturnRefund.Infrastructure.Persistence;

public sealed class ReturnRefundDbContext : ModuleDbContext, IOutboxDbContext, IReturnRefundUnitOfWork
{
    // ═════════════════════════════════════════════════════════════════════════
    // L'OUTBOX ET L'INBOX DE CE SERVICE — LEURS TABLES LUI APPARTIENNENT.
    //
    // Le socle draine la file d'evenements et exclut ces deux tables du journal
    // d'audit ; il ne connait plus ni l'une ni l'autre. Ces trois membres sont ce
    // qu'il appelle, et ils repondent avec les entites de `Persistence/`.
    // ═════════════════════════════════════════════════════════════════════════
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

    // ═════════════════════════════════════════════════════════════════════════
    // LE JOURNAL D'AUDIT DE CE SERVICE — L'ENTITE ET SA TABLE LUI APPARTIENNENT.
    //
    // Le socle collecte les mutations, resout l'acteur et fixe l'instant unique de
    // la transaction ; il ne connait plus aucune table d'audit. Ces deux methodes
    // sont ce qu'il appelle, et elles repondent avec l'entite de `Auditing/`.
    // ═════════════════════════════════════════════════════════════════════════
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
