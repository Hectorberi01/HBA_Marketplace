using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Engagement.Wishlist.Application.Abstractions;
using WishlistAggregate = HBA.Engagement.Wishlist.Domain.Wishlists.Wishlist;

using HBA.Engagement.Wishlist.Infrastructure.Persistence.Outbox;
using HBA.Engagement.Wishlist.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Engagement.Wishlist.Infrastructure.Persistence;

/// <summary>DbContext du module Wishlist (schéma « wishlist »).</summary>
public sealed class WishlistDbContext : ModuleDbContext, IOutboxDbContext, IWishlistUnitOfWork
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

    public const string SchemaName = "wishlist";

    public WishlistDbContext(
        DbContextOptions<WishlistDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<WishlistAggregate> Wishlists => Set<WishlistAggregate>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WishlistDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
