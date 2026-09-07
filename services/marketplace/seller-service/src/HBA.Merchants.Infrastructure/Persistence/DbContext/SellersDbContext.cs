using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Results;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Merchants.Application;
using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Stores;

using HBA.Merchants.Infrastructure.Auditing;
using HBA.Merchants.Infrastructure.Persistence.Outbox;
using HBA.Merchants.Infrastructure.Persistence.Inbox;
using HBA.Merchants.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Merchants.Infrastructure.Persistence;

/// <summary>DbContext du module Sellers (schéma « sellers »).</summary>
public sealed class SellersDbContext : ModuleDbContext, IOutboxDbContext, ISellerUnitOfWork
{
    // L'OUTBOX ET L'INBOX DE CE SERVICE — LEURS TABLES LUI APPARTIENNENT.
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

    public const string SchemaName = "sellers";

    private readonly ICacheService _cache;

    public SellersDbContext(
        DbContextOptions<SellersDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue,
        ICacheService cache)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
        _cache = cache;
    }

    public DbSet<Seller> Sellers => Set<Seller>();

    public DbSet<Store> Stores => Set<Store>();

    /// <summary>Traces de consommation Kafka (§19.5) et requêtes idempotentes (§25).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    public DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    /// <summary>L'équipe d'un vendeur (§21 du cahier des membres).</summary>
    public DbSet<SellerMember> SellerMembers => Set<SellerMember>();

    /// <summary>Les rôles, système et personnalisés confondus.</summary>
    public DbSet<SellerRole> SellerRoles => Set<SellerRole>();

    /// <summary>Les invitations en cours et closes (§7).</summary>
    public DbSet<SellerInvitation> SellerInvitations => Set<SellerInvitation>();

    protected override string Schema => SchemaName;

    /// <summary>LE JOURNAL D'AUDIT EST ACTIF ICI.</summary>
    protected override bool KeepsAuditTrail => true;

    // LE JOURNAL D'AUDIT DE CE SERVICE — L'ENTITE ET SA TABLE LUI APPARTIENNENT.
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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SellersDbContext).Assembly);

        // LES CONFIGS DU SOCLE VIVENT DANS UN AUTRE ASSEMBLAGE.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Mène l'opération sous verrou consultatif, dans une transaction que cette
    /// méthode ouvre et referme elle-même.
    /// </summary>
    public async Task<Result> ExecuteUnderSellerLockAsync(
        Guid sellerId,
        Func<CancellationToken, Task<Result>> operation,
        CancellationToken cancellationToken = default)
    {
        if (!Database.IsNpgsql())
        {
            return await operation(cancellationToken);
        }

        var cle = BitConverter.ToInt64(sellerId.ToByteArray(), 0);

        if (Database.CurrentTransaction is not null)
        {
            await Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0});", [cle], cancellationToken);

            return await operation(cancellationToken);
        }

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);

        await Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0});", [cle], cancellationToken);

        var resultat = await operation(cancellationToken);

        // UN ÉCHEC ANNULE. Les appelants rendent leur refus AVANT d'écrire, donc
        // l'annulation ne leur retire rien — mais elle rend le contrat net : une
        // opération refusée ne laisse aucune trace, et le verrou tombe avec elle.
        if (resultat.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return resultat;
        }

        await transaction.CommitAsync(cancellationToken);
        return resultat;
    }

    /// <summary>Invalidation du cache — point de passage unique, comme dans Catalog.</summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var keysToEvict = await CollectCacheKeysToEvictAsync(cancellationToken);

        var affected = await base.SaveChangesAsync(cancellationToken);

        if (keysToEvict.Count > 0)
        {
            await _cache.RemoveManyAsync(keysToEvict, cancellationToken);
        }

        return affected;
    }

    /// <summary>ON ÉVINCE PAR AGRÉGAT, PAS PAR ENTITÉ MODIFIÉE.</summary>
    private async Task<List<string>> CollectCacheKeysToEvictAsync(CancellationToken cancellationToken)
    {
        // Rien à écrire : rien à évincer.
        if (!ChangeTracker.HasChanges())
        {
            return [];
        }

        var keys = new HashSet<string>();

        // Les vendeurs dont l'équipe doit voir son contexte d'autorisation
        // recalculé.
        var sellersTouches = new HashSet<Guid>();

        // Tout vendeur SUIVI, quel que soit son état — y compris « Unchanged ».
        foreach (var entree in ChangeTracker.Entries<Seller>())
        {
            var seller = entree.Entity;

            keys.Add(SellersCacheKeys.Seller(seller.Id.Value));
            // Les deux clés tombent ensemble : n'en évincer qu'une laisserait
            // l'autre servir un résumé périmé, sur l'un seulement des deux chemins
            // de lecture.
            keys.Add(SellersCacheKeys.SellerByUser(seller.UserId));

            // ET LA CLÉ DE L'ANCIEN PROPRIÉTAIRE, QUAND `UserId` CHANGE.
            if (entree.State == EntityState.Modified)
            {
                var ancien = entree.OriginalValues.GetValue<Guid>(nameof(Seller.UserId));

                if (ancien != seller.UserId)
                {
                    keys.Add(SellersCacheKeys.SellerByUser(ancien));
                }
            }

            // ET L'ÉTAT OPÉRATIONNEL, QUI DÉCIDE SI L'ÉQUIPE PEUT ENCORE VENDRE.
            keys.Add(SellersCacheKeys.SellerCanOperate(seller.Id.Value));

            sellersTouches.Add(seller.Id.Value);
        }

        // TOUTE MUTATION D'APPARTENANCE ÉVINCE LA RÉSOLUTION PAR UTILISATEUR.
        foreach (var entry in ChangeTracker.Entries<SellerMember>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            keys.Add(SellersCacheKeys.SellerByUser(entry.Entity.UserId));

            // ET LE CONTEXTE D'AUTORISATION, QUI EST LA CLÉ QUI COMPTE.
            keys.Add(SellersCacheKeys.MemberAccess(entry.Entity.UserId));
        }

        // Filet : une pièce touchée sans que son parent soit chargé (cas qui ne
        // devrait pas se produire — on passe toujours par l'agrégat — mais qui,
        // s'il survenait, laisserait un résumé périmé sans aucun signe).
        foreach (var entry in ChangeTracker.Entries<KybDocument>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var fk = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "SellerId");
            if (fk is null)
            {
                continue;
            }

            foreach (var candidate in new[] { fk.OriginalValue, fk.CurrentValue })
            {
                if (candidate is Guid sellerId && sellerId != Guid.Empty)
                {
                    keys.Add(SellersCacheKeys.Seller(sellerId));
                }
            }
        }

        // UN RÔLE MODIFIÉ PÉRIME LE CONTEXTE DE TOUS CEUX QUI LE PORTENT.
        foreach (var entry in ChangeTracker.Entries<SellerRole>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var sellerId = entry.Entity.SellerId
                ?? entry.Property(nameof(SellerRole.SellerId)).OriginalValue as Guid?;

            if (sellerId is { } id && id != Guid.Empty)
            {
                sellersTouches.Add(id);
            }
        }

        if (sellersTouches.Count > 0)
        {
            // `AsNoTracking` : on ne veut que des identifiants, et charger des
            // membres dans le contexte pendant un enregistrement ajouterait des
            // entités suivies au moment le plus mal choisi.
            var membres = await SellerMembers
                .AsNoTracking()
                .Where(m => sellersTouches.Contains(m.SellerId))
                .Select(m => m.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var userId in membres)
            {
                keys.Add(SellersCacheKeys.MemberAccess(userId));
                keys.Add(SellersCacheKeys.SellerByUser(userId));
            }
        }

        return [.. keys];
    }
}
