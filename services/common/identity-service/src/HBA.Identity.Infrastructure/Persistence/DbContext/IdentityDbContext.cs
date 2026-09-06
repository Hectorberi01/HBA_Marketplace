using HBA.Identity.Domain.Mfa;
using HBA.Shared.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Roles;
using HBA.Identity.Domain.Users;

using HBA.Identity.Infrastructure.Auditing;
using HBA.Identity.Infrastructure.Persistence.Outbox;
using HBA.Identity.Infrastructure.Persistence.Inbox;
using HBA.Identity.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Identity.Infrastructure.Persistence;

/// <summary>
/// DbContext du module Identity. Vit dans le schéma « identity » : pas de JOIN ni
/// de foreign key vers un autre schéma. Hérite de ModuleDbContext pour l'Unit of
/// Work (dispatch des domain events) et l'outbox.
/// </summary>
public sealed class IdentityDbContext : ModuleDbContext, IOutboxDbContext, IIdentityUnitOfWork
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

    public const string SchemaName = "identity";

    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Défis à usage unique du §10.1, table <c>mfa_challenges</c>.</summary>
    public DbSet<MfaChallenge> MfaChallenges => Set<MfaChallenge>();

    /// <summary>Traces de consommation Kafka (§19.5) et requêtes idempotentes (§5).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    // PAS DE « DbSet<Address> » ICI. Le carnet d'adresses a été déplacé dans le
    // module User (schéma « users ») : il répond à « qui est la personne ? », pas à
    // « qui peut se connecter ? ». La table identity.addresses est supprimée par la
    // migration MoveAddressesToUsers, APRÈS la reprise des données côté Users.

    protected override string Schema => SchemaName;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).
    ///
    /// `KeepsAuditTrail` VALAIT `false` SUR VINGT ET UN CONTEXTES SUR VINGT-QUATRE.
    ///
    /// Ce qui n'y laissait AUCUNE trace : l'attribution et le retrait d'un rôle
    /// PLATEFORME (`POST`/`DELETE /api/identity/users/{id}/roles`), la modification
    /// des permissions d'un rôle, et la suspension d'un compte.
    ///
    /// C'est le journal le plus important du dépôt. Un rôle plateforme ouvre tout le
    /// back-office ; se l'attribuer à soi-même était, jusqu'à cette migration, un
    /// geste que rien ne consignait nulle part. La seule question qu'on pose après
    /// une compromission — « qui a donné ce droit, et quand » — n'avait pas de
    /// réponse.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `identity.audit_entries` —
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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        // Configurations du socle : elles vivent dans un autre assembly, le balayage
        // ci-dessus ne les trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
