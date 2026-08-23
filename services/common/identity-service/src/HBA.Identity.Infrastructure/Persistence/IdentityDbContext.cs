using HBA.Identity.Domain.Mfa;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Inbox;
using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Roles;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Infrastructure.Persistence;

/// <summary>
/// DbContext du module Identity. Vit dans le schéma « identity » : pas de JOIN ni
/// de foreign key vers un autre schéma. Hérite de ModuleDbContext pour l'Unit of
/// Work (dispatch des domain events) et l'outbox.
/// </summary>
public sealed class IdentityDbContext : ModuleDbContext, IIdentityUnitOfWork
{
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
