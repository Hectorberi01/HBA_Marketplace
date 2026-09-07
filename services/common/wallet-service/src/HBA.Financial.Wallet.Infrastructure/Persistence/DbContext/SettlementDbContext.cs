using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Domain.Batches;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Financial.Wallet.Domain.Wallets;

using HBA.Financial.Wallet.Infrastructure.Auditing;
using HBA.Financial.Wallet.Infrastructure.Persistence.Outbox;
using HBA.Financial.Wallet.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Financial.Wallet.Infrastructure.Persistence;

/// <summary>DbContext du module Settlement (schéma « settlement »).</summary>
public sealed class WalletDbContext : ModuleDbContext, IOutboxDbContext, IWalletUnitOfWork
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

    public const string SchemaName = "settlement";

    public WalletDbContext(
        DbContextOptions<WalletDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<SellerEarning> Earnings => Set<SellerEarning>();
    public DbSet<SettlementBatch> Batches => Set<SettlementBatch>();
    public DbSet<SellerWallet> SellerWallets => Set<SellerWallet>();
    public DbSet<PlatformWallet> PlatformWallets => Set<PlatformWallet>();
    public DbSet<DriverWallet> DriverWallets => Set<DriverWallet>();
    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();
    public DbSet<CustomerRefund> CustomerRefunds => Set<CustomerRefund>();

    /// <summary>
    /// Portefeuilles clients : l'argent rendu qu'aucun prestataire n'a su
    /// rembourser (D33).
    /// </summary>
    public DbSet<CustomerWallet> CustomerWallets => Set<CustomerWallet>();

    /// <summary>
    /// Demandes de virement des clients, tranchées à la main par un administrateur
    /// (D33).
    /// </summary>
    public DbSet<CustomerWithdrawal> CustomerWithdrawals => Set<CustomerWithdrawal>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

    /// <summary>Traces de consommation Kafka (§19.5).</summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    protected override string Schema => SchemaName;

    /// <summary>LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).</summary>
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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
        // Configuration du socle : elle vit dans un autre assembly, le balayage
        // ci-dessus ne la trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
