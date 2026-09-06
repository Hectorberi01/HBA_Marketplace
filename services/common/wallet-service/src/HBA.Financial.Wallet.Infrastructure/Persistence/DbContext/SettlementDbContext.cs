using Microsoft.EntityFrameworkCore;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Domain.Batches;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Financial.Wallet.Domain.Wallets;

namespace HBA.Financial.Wallet.Infrastructure.Persistence;

/// <summary>DbContext du module Settlement (schéma « settlement »).</summary>
public sealed class WalletDbContext : ModuleDbContext, IWalletUnitOfWork
{
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

    /// <summary>Portefeuilles clients : l'argent rendu qu'aucun prestataire n'a su rembourser (D33).</summary>
    public DbSet<CustomerWallet> CustomerWallets => Set<CustomerWallet>();

    /// <summary>Demandes de virement des clients, tranchées à la main par un administrateur (D33).</summary>
    public DbSet<CustomerWithdrawal> CustomerWithdrawals => Set<CustomerWithdrawal>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

    /// <summary>
    /// Traces de consommation Kafka (§19.5).
    ///
    /// ICI, LA TRACE EST DU MÊME ORDRE QUE LE GRAND LIVRE.
    ///
    /// Elle est écrite par le MÊME `SaveChangesAsync` que le crédit qu'elle
    /// protège — c'est ce qui fait qu'un rejeu retrouve soit les deux, soit
    /// aucun, jamais un vendeur crédité sans trace.
    /// </summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    protected override string Schema => SchemaName;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).
    ///
    /// `KeepsAuditTrail` VALAIT `false` SUR VINGT ET UN CONTEXTES SUR VINGT-QUATRE.
    ///
    /// Ce qui n'y laissait AUCUNE trace : l'approbation et le refus d'un RETRAIT.
    ///
    /// C'est le geste par lequel l'argent quitte la plateforme pour le compte mobile
    /// d'un vendeur ou d'un livreur. `withdrawals.Status` retient qu'il est approuvé ;
    /// rien ne retenait par qui. Le journal comptable de `WalletTransaction` décrit
    /// les MOUVEMENTS, pas les DÉCISIONS — un virement approuvé à tort y apparaît
    /// comme un virement parfaitement normal.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `settlement.audit_entries` —
    /// l'inverse produirait une surcharge qui promet une table absente, et le défaut
    /// ne se verrait qu'au premier `SaveChanges` en production.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    protected override bool KeepsAuditTrail => true;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
        // Configuration du socle : elle vit dans un autre assembly, le balayage
        // ci-dessus ne la trouve pas. Sans elle, `consumer_inbox` n'existe pas dans
        // le modèle et `EfConsumerInbox` lèverait au premier message.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
