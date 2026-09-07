using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Engagement.Recommendations.Application.Recommendations;
using HBA.Engagement.Recommendations.Domain.Recommendations;

using HBA.Engagement.Recommendations.Infrastructure.Persistence.Outbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Engagement.Recommendations.Infrastructure.Persistence;

/// <summary>DbContext du module Recommendations (schéma « recommendations »).</summary>
public sealed class RecommendationsDbContext : ModuleDbContext, IOutboxDbContext, IRecommendationsUnitOfWork
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

    public const string SchemaName = "recommendations";

    public RecommendationsDbContext(
        DbContextOptions<RecommendationsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Recommendation> Recommendations => Set<Recommendation>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RecommendationsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
