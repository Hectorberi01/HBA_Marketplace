using HBA.Media.Application.Abstractions;
using HBA.Media.Domain.Assets;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using HBA.Media.Infrastructure.Persistence.Outbox;
using HBA.Media.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Media.Infrastructure.Persistence;

/// <summary>LE SCHÉMA DU SERVICE MÉDIA.</summary>
public sealed class MediaDbContext : ModuleDbContext, IOutboxDbContext, IMediaUnitOfWork
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

    public const string SchemaName = "media";

    public MediaDbContext(
        DbContextOptions<MediaDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<MediaAsset> Assets => Set<MediaAsset>();

    /// <summary>Traces de consommation Kafka (§19.5).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediaDbContext).Assembly);
        // Configuration du socle : elle vit dans un autre assembly, le balayage
        // ci-dessus ne la trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_assets");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(id => id.Value, value => new MediaAssetId(value))
            .ValueGeneratedNever();

        builder.Property(a => a.OwnerType).HasConversion<int>().IsRequired();
        builder.Property(a => a.OwnerId).IsRequired();
        builder.Property(a => a.MediaType).HasConversion<int>().IsRequired();

        builder.Property(a => a.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(a => a.ObjectKey).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Bucket).HasMaxLength(120).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(a => a.Extension).HasMaxLength(10).IsRequired();
        builder.Property(a => a.SizeBytes).IsRequired();

        builder.Property(a => a.Visibility).HasConversion<int>().IsRequired();
        builder.Property(a => a.Status).HasConversion<int>().IsRequired();

        // SHA-256 en hexadécimal : 64 caractères, toujours.
        builder.Property(a => a.Checksum).HasMaxLength(64).IsRequired();

        builder.Property(a => a.Width);
        builder.Property(a => a.Height);
        builder.Property(a => a.DurationSeconds);
        builder.Property(a => a.CreatedByUserId).IsRequired();
        builder.Property(a => a.CreatedOnUtc).IsRequired();
        builder.Property(a => a.UpdatedOnUtc);
        builder.Property(a => a.DeletedOnUtc);
        builder.Property(a => a.FailureReason).HasMaxLength(500);

        // UNE CLÉ D'OBJET NE DÉSIGNE QU'UN MÉDIA.
        builder.HasIndex(a => a.ObjectKey)
            .IsUnique()
            .HasDatabaseName("ux_media_assets_object_key");

        // « Les médias de cette ressource » : la requête la plus fréquente, une
        // galerie produit ou les photos d'un restaurant.
        builder.HasIndex(a => new { a.OwnerType, a.OwnerId });

        // INDEX DE L'IDEMPOTENCE. Sans lui, chaque upload parcourt tous les médias
        // du propriétaire pour chercher une empreinte — sur une galerie de cent
        // images, à chaque envoi.
        builder.HasIndex(a => new { a.OwnerType, a.OwnerId, a.Checksum })
            .HasDatabaseName("ix_media_assets_checksum");

        // Le ménage de rétention balaie les supprimés par date.
        builder.HasIndex(a => a.DeletedOnUtc)
            .HasFilter("\"DeletedOnUtc\" IS NOT NULL")
            .HasDatabaseName("ix_media_assets_deleted");

        builder.OwnsMany<MediaVariant>("_variants", variantes =>
        {
            variantes.ToTable("media_variants");
            variantes.WithOwner().HasForeignKey("MediaAssetId");
            variantes.HasKey(v => v.Id);

            variantes.Property(v => v.Id).ValueGeneratedNever();
            variantes.Property(v => v.VariantType).HasConversion<int>().IsRequired();
            variantes.Property(v => v.ObjectKey).HasMaxLength(500).IsRequired();
            variantes.Property(v => v.ContentType).HasMaxLength(120).IsRequired();
            variantes.Property(v => v.Width).IsRequired();
            variantes.Property(v => v.Height).IsRequired();
            variantes.Property(v => v.SizeBytes).IsRequired();
            variantes.Property(v => v.CreatedOnUtc).IsRequired();
        });

        // CHAQUE COLLECTION POSSÉDÉE EXIGE DEUX GESTES : OwnsMany sur le CHAMP
        // PRIVÉ, et Ignore sur la propriété de LECTURE. Sans le second, EF refuse
        // de construire le modèle — au scaffold, pas à la compilation.
        builder.Ignore(a => a.DomainEvents);
        builder.Ignore(a => a.Variants);

        // Propriétés CALCULÉES : elles se dérivent du statut et n'ont rien en base.
        builder.Ignore(a => a.IsUsable);
        builder.Ignore(a => a.IsPubliclyReadable);
    }
}

/// <summary>Factory design-time pour `dotnet ef migrations add`.</summary>
public sealed class MediaDbContextFactory : IDesignTimeDbContextFactory<MediaDbContext>
{
    public MediaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MEDIA_DB")
            ?? "Host=localhost;Port=5432;Database=marketplace;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MediaDbContext.SchemaName))
            .Options;

        return new MediaDbContext(options, NoOpDomainEventDispatcher.Instance, new IntegrationEventQueue());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public static readonly NoOpDomainEventDispatcher Instance = new();

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

internal sealed class MediaAssetRepository : IMediaAssetRepository
{
    private readonly MediaDbContext _dbContext;

    public MediaAssetRepository(MediaDbContext dbContext) => _dbContext = dbContext;

    // Les variantes sont un type « owned » : EF les charge avec la racine, sans
    // Include.
    public async Task<MediaAsset?> GetByIdAsync(MediaAssetId id, CancellationToken cancellationToken = default)
        => await _dbContext.Assets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MediaAsset>> ListByOwnerAsync(
        MediaOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
        => await _dbContext.Assets
            .AsNoTracking()
            .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId && a.Status != MediaStatus.Deleted)
            .OrderBy(a => a.CreatedOnUtc)
            .ToListAsync(cancellationToken);

    public async Task<MediaAsset?> FindByChecksumAsync(
        MediaOwnerType ownerType, Guid ownerId, string checksum, CancellationToken cancellationToken = default)
        => await _dbContext.Assets
            .FirstOrDefaultAsync(
                a => a.OwnerType == ownerType && a.OwnerId == ownerId && a.Checksum == checksum,
                cancellationToken);

    /// <summary>LE FILTRE DE RÉTENTION SE FAIT EN MÉMOIRE, ET C'EST ASSUMÉ.</summary>
    public async Task<IReadOnlyList<MediaAsset>> ListPurgeableAsync(
        DateTime nowUtc, int take, CancellationToken cancellationToken = default)
    {
        var candidats = await _dbContext.Assets
            .Where(a => a.Status == MediaStatus.Deleted && a.DeletedOnUtc != null && a.DeletedOnUtc <= nowUtc)
            .OrderBy(a => a.DeletedOnUtc)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(cancellationToken);

        return candidats.Where(a => a.IsPurgeable(nowUtc)).ToList();
    }

    public async Task AddAsync(MediaAsset asset, CancellationToken cancellationToken = default)
        => await _dbContext.Assets.AddAsync(asset, cancellationToken);

    public void Remove(MediaAsset asset) => _dbContext.Assets.Remove(asset);
}
