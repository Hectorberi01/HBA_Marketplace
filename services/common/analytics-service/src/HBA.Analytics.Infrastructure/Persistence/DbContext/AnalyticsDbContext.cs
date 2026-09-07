using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Domain.RollUps;
using HBA.Analytics.Infrastructure.Persistence.Inbox;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Events;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Analytics.Infrastructure.Persistence;

/// <summary>DbContext du module Analytics (schéma « analytics »).</summary>
public sealed class AnalyticsDbContext : ModuleDbContext, IAnalyticsUnitOfWork
{
    public const string SchemaName = "analytics";

    public AnalyticsDbContext(
        DbContextOptions<AnalyticsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<VenteJournaliereVendeur> SellerDaily => Set<VenteJournaliereVendeur>();

    public DbSet<ActiviteJournalierePlateforme> PlatformDaily => Set<ActiviteJournalierePlateforme>();

    public DbSet<InscriptionJournaliere> SignupDaily => Set<InscriptionJournaliere>();

    /// <summary>Lot 2 — ce qu'un vendeur perd en annulations.</summary>
    public DbSet<AnnulationJournaliereVendeur> SellerCancellationDaily
        => Set<AnnulationJournaliereVendeur>();

    /// <summary>Lot 2 — les tentatives de paiement, par prestataire et par issue.</summary>
    public DbSet<PaiementJournalier> PaymentDaily => Set<PaiementJournalier>();

    /// <summary>Traces de consommation Kafka (§19.5).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // `ConsumerInboxConfiguration` VIT DANS CET ASSEMBLAGE, donc le balayage la
        // trouve.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnalyticsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
