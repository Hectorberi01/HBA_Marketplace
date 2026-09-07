using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Financial.Wallet.Domain.Wallets;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Financial.Wallet.Infrastructure.Persistence.Configurations;

internal sealed class SellerWalletConfiguration : IEntityTypeConfiguration<SellerWallet>
{
    public void Configure(EntityTypeBuilder<SellerWallet> builder)
    {

        // VERROU OPTIMISTE — SANS LUI, UN VENDEUR PEUT SE FAIRE VERSER 5× SON
        // SOLDE.
        builder.UsePostgresRowVersion();
        builder.ToTable("seller_wallets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new SellerWalletId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.SellerId).IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.PendingBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.AvailableBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        builder.HasIndex(w => w.SellerId).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class DriverWalletConfiguration : IEntityTypeConfiguration<DriverWallet>
{
    public void Configure(EntityTypeBuilder<DriverWallet> builder)
    {
        // VERROU OPTIMISTE — même raison que pour le vendeur, et le risque est ici
        // PLUS élevé, pas moins.
        builder.UsePostgresRowVersion();
        builder.ToTable("driver_wallets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new DriverWalletId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.DriverId).IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.AvailableBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.LifetimeEarned).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        // UN SEUL portefeuille par livreur.
        builder.HasIndex(w => w.DriverId).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class CustomerWalletConfiguration : IEntityTypeConfiguration<CustomerWallet>
{
    public void Configure(EntityTypeBuilder<CustomerWallet> builder)
    {
        // VERROU OPTIMISTE — SANS LUI, UN REMBOURSEMENT SUR DEUX DISPARAÎT.
        builder.UsePostgresRowVersion();
        builder.ToTable("customer_wallets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new CustomerWalletId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.CustomerId).IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.AvailableBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.LifetimeRefunded).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        // UN CLIENT, UN PORTEFEUILLE — ET CET INDEX EST LA SEULE CHOSE QUI LE
        // TIENT.
        builder.HasIndex(w => w.CustomerId).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class CustomerWithdrawalConfiguration : IEntityTypeConfiguration<CustomerWithdrawal>
{
    public void Configure(EntityTypeBuilder<CustomerWithdrawal> builder)
    {
        // VERROU OPTIMISTE — IL MANQUAIT ICI, ET NULLE PART AILLEURS (audit 2.6).
        builder.UsePostgresRowVersion();
        builder.ToTable("customer_withdrawals");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new CustomerWithdrawalId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.CustomerId).IsRequired();
        builder.Property(w => w.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();

        // DESTINATION FIGÉE À LA DEMANDE, ET NON NULLABLE.
        builder.Property(w => w.Msisdn).HasMaxLength(30).IsRequired();
        builder.Property(w => w.Provider).HasMaxLength(30).IsRequired();

        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(w => w.RequestedAtUtc).IsRequired();
        builder.Property(w => w.DecidedAtUtc);
        builder.Property(w => w.DecidedByUserId);

        // Référence du virement saisie par l'administrateur : la seule preuve que
        // l'argent est parti.
        builder.Property(w => w.ExternalReference).HasMaxLength(200);
        builder.Property(w => w.AdminNote).HasMaxLength(500);

        // 180 caractères : la même borne que `customer_refunds` et
        // `payments.payment_refunds`.
        builder.Property(w => w.IdempotencyKey).HasMaxLength(180).IsRequired();

        builder.HasIndex(w => w.CustomerId);

        // La file d'administration lit par statut, tous clients confondus.
        builder.HasIndex(w => w.Status);

        // CLÉ D'IDEMPOTENCE — CE QUI EMPÊCHE UN DOUBLE-CLIC DE VIDER LE SOLDE.
        builder.HasIndex(w => w.IdempotencyKey).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class PlatformWalletConfiguration : IEntityTypeConfiguration<PlatformWallet>
{
    public void Configure(EntityTypeBuilder<PlatformWallet> builder)
    {

        // VERROU OPTIMISTE — le portefeuille plateforme est écrit par tous les flux
        // (commission, frais, contre-passations).
        builder.UsePostgresRowVersion();
        builder.ToTable("platform_wallet");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.CommissionBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.ProviderFeeBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.ShippingBalance).HasColumnType("numeric(18,2)").IsRequired();
        // Total reversé aux clients en remboursements directs.
        builder.Property(w => w.RefundsBalance).HasColumnType("numeric(18,2)").IsRequired().HasDefaultValue(0m);
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class CustomerRefundConfiguration : IEntityTypeConfiguration<CustomerRefund>
{
    public void Configure(EntityTypeBuilder<CustomerRefund> builder)
    {
        builder.ToTable("customer_refunds");
        builder.HorodateLesModifications();
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new CustomerRefundId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.OrderId).IsRequired();
        builder.Property(r => r.BuyerId).IsRequired();
        builder.Property(r => r.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.Msisdn).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Provider).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.ProviderRef).HasMaxLength(200);
        builder.Property(r => r.FailureReason).HasMaxLength(500);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.SentToPspAtUtc);
        builder.Property(r => r.CompletedAtUtc);

        // 180 caractères : la même borne que `payments.payment_refunds`.
        builder.Property(r => r.IdempotencyKey).HasMaxLength(180).IsRequired();

        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => r.Status);

        // LA SEULE CHOSE QUI EMPÊCHE UN SECOND VIREMENT.
        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();

        builder.Ignore(r => r.DomainEvents);
    }
}

internal sealed class WithdrawalConfiguration : IEntityTypeConfiguration<Withdrawal>
{
    public void Configure(EntityTypeBuilder<Withdrawal> builder)
    {
        builder.ToTable("withdrawals");
        builder.HorodateLesModifications();
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new WithdrawalId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.SellerId).IsRequired();
        builder.Property(w => w.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(w => w.ProviderRef).HasMaxLength(200);
        builder.Property(w => w.FailureReason).HasMaxLength(500);
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.CompletedAtUtc);

        // LA DESTINATION DU VIREMENT, FIGÉE À LA DEMANDE.
        builder.Property(w => w.PayoutProvider).HasMaxLength(30);
        builder.Property(w => w.PayoutAccountNumber).HasMaxLength(40);
        builder.Property(w => w.PayoutAccountName).HasMaxLength(120);

        builder.HasIndex(w => w.SellerId);

        // TROIS REQUÊTES FILTRENT SUR `Status`, ET AUCUN INDEX NE LES SERVAIT.
        builder.HasIndex(w => w.Status);

        // JETON DE CONCURRENCE — L'ARGENT SORT D'ICI, ET RIEN NE SÉRIALISAIT.
        builder.UsePostgresRowVersion();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("wallet_transactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.OwnerId).IsRequired();
        builder.Property(t => t.OwnerType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Account).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(t => t.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        builder.Property(t => t.Reason).HasMaxLength(50).IsRequired();
        builder.Property(t => t.ReferenceType).HasMaxLength(30);
        builder.Property(t => t.ReferenceId);
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        // §10.13 : regroupement des écritures d'une opération, et solde résultant.
        builder.Property(t => t.TransactionId).IsRequired().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.BalanceAfter).HasColumnType("numeric(18,2)");

        builder.HasIndex(t => new { t.OwnerId, t.CreatedAtUtc });

        // Toutes les écritures d'une opération se lisent ensemble : c'est la
        // requête du rapprochement comptable, et sans index elle balaie toute la
        // table.
        builder.HasIndex(t => t.TransactionId).HasDatabaseName("ix_wallet_transactions_transaction");

        // INDEX D'IDEMPOTENCE — la seule garantie qui tienne sous concurrence.
        builder.HasIndex(t => new { t.ReferenceType, t.ReferenceId, t.OwnerId, t.Account })
            .IsUnique()
            .HasFilter("\"ReferenceType\" = 'refund'")
            .HasDatabaseName("ux_wallet_transactions_refund_reversal");

        // IDEMPOTENCE DES GAINS DE COURSE — le second verrou du crédit livreur.
        builder.HasIndex(t => new { t.ReferenceType, t.ReferenceId })
            .IsUnique()
            .HasFilter("\"ReferenceType\" = 'driver_earning'")
            .HasDatabaseName("ux_wallet_transactions_driver_earning");

        // IDEMPOTENCE DU CRÉDIT DE REMBOURSEMENT CLIENT (D33).
        builder.HasIndex(
                t => new { t.ReferenceType, t.ReferenceId },
                "ux_wallet_transactions_customer_refund_credit")
            .IsUnique()
            .HasFilter("\"ReferenceType\" = 'customer_refund_credit'");

        builder.Ignore(t => t.DomainEvents);
    }
}
