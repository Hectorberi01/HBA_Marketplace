using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Domain.RollUps;
using HBA.Analytics.Infrastructure.Persistence.Inbox;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Events;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Analytics.Infrastructure.Persistence;

/// <summary>DbContext du module Analytics (schéma « analytics »).</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE INBOX, PAS D'OUTBOX — ET LES DEUX MOITIÉS SE JUSTIFIENT.
///
/// L'INBOX EST OBLIGATOIRE ICI. Ce service ne fait qu'INCRÉMENTER : « ajouter une
/// commande » rejouée compte deux fois, et rien dans la ligne de roll-up ne peut
/// s'en apercevoir — elle ne voit pas l'identifiant de l'événement. Là où
/// `CloseCartOnOrderPlacedHandler` survit à un rejeu par sa garde d'état, aucun
/// gestionnaire d'ici n'a d'équivalent. La table `consumer_inbox` est donc la
/// SEULE protection, et le service ne doit jamais tourner sans elle.
///
/// L'OUTBOX EST ABSENTE, ET CE N'EST PAS UN OUBLI. Ce service ne publie aucun
/// événement d'intégration : il n'implémente donc pas `IOutboxDbContext` et ne
/// surcharge pas `AjouterAuOutbox`, dont le défaut vide est exactement juste ici.
/// Poser une table `outbox_messages` qu'aucun code ne remplit ferait croire, à
/// qui la trouve, que quelque chose devrait en sortir.
///
/// CE QUE CETTE ABSENCE COÛTE LE JOUR OÙ ELLE CHANGERA : un service d'analytique
/// qui se mettrait à publier — une alerte de seuil, par exemple — a besoin des
/// TROIS gestes ensemble, dans le même commit : l'entité `OutboxMessage`, sa
/// migration, et l'enregistrement du processeur. Deux sur trois donnent une
/// panne silencieuse : la transaction réussit, l'appelant reçoit son 200, et le
/// message n'arrive nulle part.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
        // `ConsumerInboxConfiguration` VIT DANS CET ASSEMBLAGE, donc le balayage
        // la trouve. Les services antérieurs l'appliquent EN PLUS à la main :
        // c'est une trace de l'époque où elle venait du socle, et l'appel double
        // est sans effet. Ne pas le recopier ici évite de faire croire qu'il est
        // nécessaire.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnalyticsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
