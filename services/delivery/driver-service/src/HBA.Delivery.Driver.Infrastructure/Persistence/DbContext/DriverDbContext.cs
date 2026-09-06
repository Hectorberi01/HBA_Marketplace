using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Drivers.Infrastructure.Persistence.Configurations;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HBA.Drivers.Infrastructure.Auditing;
using HBA.Drivers.Infrastructure.Persistence.Outbox;
using HBA.Drivers.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Drivers.Infrastructure.Persistence;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE PREMIER `DbContext` DE driver-service.
///
/// CE SERVICE N'AVAIT AUCUNE PERSISTANCE (ISSUE-029, ISSUE-030). Son état
/// entier tenait dans `DriverStore`, un `ConcurrentDictionary` de processus :
/// perdu au redémarrage, non partagé entre réplicas, et peuplé au démarrage d'un
/// unique livreur « VERIFIED » dont l'identifiant était codé en dur. Une
/// vérification faite sur un conteneur était invisible depuis l'autre.
///
/// SCHÉMA `drivers`, DANS LA BASE `hba_delivery`.
///
/// La base physique est celle que `docker-compose.dev.yml` donne déjà à ce
/// service, et c'est aussi celle de delivery-service — qui y tient le schéma
/// `deliveries`. Ce n'est PAS un partage de données : la règle d'or de
/// `ModuleDbContext` tient, un module ne lit et n'écrit que son propre schéma, et
/// il n'existe aucune clé étrangère entre les deux. C'est ce qui rend l'extraction
/// vers une base séparée mécanique le jour où elle sera décidée.
///
/// CE QU'IL NE FAUT PAS FAIRE ICI : AJOUTER UNE TABLE DE POSITIONS.
///
/// Un livreur en ligne émet sa position toutes les cinq à quinze secondes. Le
/// raisonnement est écrit en toutes lettres dans `IDriverLocationCache` chez
/// delivery-service — les positions vivent dans Redis, et nulle part ailleurs.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DriverDbContext : ModuleDbContext, IOutboxDbContext
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

    public const string SchemaName = "drivers";

    public DriverDbContext(
        DbContextOptions<DriverDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<DriverAccount> DriverAccounts => Set<DriverAccount>();

    protected override string Schema => SchemaName;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).
    ///
    /// `KeepsAuditTrail` VALAIT `false` SUR VINGT ET UN CONTEXTES SUR VINGT-QUATRE.
    ///
    /// Ce qui n'y laissait AUCUNE trace : la vérification d'un dossier de livreur et
    /// sa SUSPENSION.
    ///
    /// Suspendre un livreur le prive de son revenu du jour et, depuis le lot 5.2, le
    /// retire du dispatch. Vérifier un dossier ouvre l'inverse : le droit de porter
    /// les colis d'autrui. Les deux décisions se contestent, et aucune n'était
    /// consignée.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `drivers.audit_entries` —
    /// l'inverse produirait une surcharge qui promet une table absente, et le défaut
    /// ne se verrait qu'au premier `SaveChanges` en production.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
        modelBuilder.ApplyConfiguration(new DriverAccountConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
