using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Commerce.Application.Abstractions;
using CartAggregate = HBA.Commerce.Domain.Carts.Cart;

namespace HBA.Commerce.Infrastructure.Persistence;

/// <summary>DbContext du module Cart (schéma « cart »).</summary>
public sealed class CartDbContext : ModuleDbContext, ICartUnitOfWork
{
    public const string SchemaName = "cart";

    public CartDbContext(
        DbContextOptions<CartDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<CartAggregate> Carts => Set<CartAggregate>();

    /// <summary>
    /// Traces de consommation Kafka (§19.5).
    ///
    /// Elles n'appartiennent à aucun agrégat : c'est le dispatcher qui les pose,
    /// dans la MÊME transaction que l'effet métier du gestionnaire. Le DbSet
    /// existe pour que la table se voie depuis le contexte comme n'importe quelle
    /// autre — l'inbox y écrit par <c>Set&lt;ConsumerInboxEntry&gt;()</c>.
    /// </summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CartDbContext).Assembly);
        // Configuration du socle : autre assembly, le balayage ne la trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
