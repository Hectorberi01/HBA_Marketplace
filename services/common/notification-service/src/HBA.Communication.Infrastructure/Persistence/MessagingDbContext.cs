using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Communication.Application.Abstractions;
using HBA.Communication.Domain.Conversations;

namespace HBA.Communication.Infrastructure.Persistence;

/// <summary>DbContext du module Messaging (schéma « messaging »).</summary>
public sealed class MessagingDbContext : ModuleDbContext, IMessagingUnitOfWork
{
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
