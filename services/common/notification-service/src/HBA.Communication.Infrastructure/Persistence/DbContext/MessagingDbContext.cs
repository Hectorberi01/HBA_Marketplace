using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Communication.Application.Abstractions;
using HBA.Communication.Domain.Conversations;

using HBA.Communication.Infrastructure.Persistence.Outbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Communication.Infrastructure.Persistence;

/// <summary>DbContext du module Messaging (schéma « messaging »).</summary>
public sealed class MessagingDbContext : ModuleDbContext, IOutboxDbContext, IMessagingUnitOfWork
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
        // PAS D'INBOX : ce module ne consomme aucun evenement d'integration.
        // La ligne `ApplyConfiguration(new ConsumerInboxConfiguration())` mappait
        // `consumer_inbox` dans le schema `messaging` alors qu'aucune migration ne
        // cree cette table — le module Notifications, lui, la cree dans le sien.
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

    public const string SchemaName = "messaging";

    public MessagingDbContext(
        DbContextOptions<MessagingDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Conversation> Conversations => Set<Conversation>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MessagingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
