using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Financial.Wallet.Domain.Batches;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Financial.Wallet.Infrastructure.Persistence.Configurations;

internal sealed class SellerEarningConfiguration : IEntityTypeConfiguration<SellerEarning>
{
    public void Configure(EntityTypeBuilder<SellerEarning> builder)
    {
        builder.ToTable("seller_earnings");
        builder.HorodateLesModifications();

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new SellerEarningId(value))
            .ValueGeneratedNever();

        builder.Property(e => e.OrderId).IsRequired();
        builder.Property(e => e.OfferId).IsRequired();
        builder.Property(e => e.SellerId).IsRequired();
        builder.Property(e => e.ProductId).IsRequired();
        builder.Property(e => e.GrossAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.CommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.ProviderFeeAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.NetAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.CreatedAtUtc).IsRequired();
        builder.Property(e => e.ReleasedAtUtc);
        builder.Property(e => e.SettlementBatchId);
        builder.Property(e => e.SettledByWithdrawalId);

        // LES CUMULS DE REPRISE (voir `SellerEarning.ReversedGrossAmount`).
        builder.Property(e => e.ReversedGrossAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.ReversedCommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.ReversedProviderFeeAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(e => e.ReversedNetAmount).HasColumnType("numeric(18,2)").IsRequired();

        // LES QUATRE « RESTANT » SONT CALCULÉS — IGNORER EST OBLIGATOIRE.
        builder.Ignore(e => e.RemainingGrossAmount);
        builder.Ignore(e => e.RemainingCommissionAmount);
        builder.Ignore(e => e.RemainingProviderFeeAmount);
        builder.Ignore(e => e.RemainingNetAmount);

        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.SellerId);
        builder.HasIndex(e => new { e.Status, e.CreatedAtUtc });

        // La règle d'imputation lit les gains payables d'UN vendeur, du plus ancien
        // au plus récent.
        builder.HasIndex(e => new { e.SellerId, e.Status, e.ReleasedAtUtc });

        // Remonter les gains d'un retrait à rembourser (refus, échec PSP).
        builder.HasIndex(e => e.SettledByWithdrawalId)
            .HasFilter("\"SettledByWithdrawalId\" IS NOT NULL")
            .HasDatabaseName("ix_seller_earnings_withdrawal");

        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class SettlementBatchConfiguration : IEntityTypeConfiguration<SettlementBatch>
{
    public void Configure(EntityTypeBuilder<SettlementBatch> builder)
    {
        builder.ToTable("settlement_batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id)
            .HasConversion(id => id.Value, value => new SettlementBatchId(value))
            .ValueGeneratedNever();

        builder.Property(b => b.PeriodStartUtc).IsRequired();
        builder.Property(b => b.PeriodEndUtc).IsRequired();
        builder.Property(b => b.Currency).HasMaxLength(3).IsRequired();
        builder.Property(b => b.TotalNet).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(b => b.CreatedAtUtc).IsRequired();

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE
        // EF.
        builder.HasMany(b => b.Payouts)
            .WithOne()
            .HasForeignKey("SettlementBatchId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // LES DEUX SEULES LECTURES DE LISTE TRIENT SUR `CreatedAtUtc`, SUR TOUTE LA
        // TABLE ET SANS BORNE (`SettlementRepositories.cs:135` et `:143`).
        builder.HasIndex(b => b.CreatedAtUtc);

        builder.Navigation(b => b.Payouts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(b => b.DomainEvents);
    }
}

internal sealed class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.ToTable("payouts");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.SellerId).IsRequired();
        builder.Property(p => p.GrossAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.CommissionAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.NetAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.ProviderRef).HasMaxLength(200);
        builder.Property(p => p.PaidAtUtc);

        // L'index sur la FK « SettlementBatchId » est créé automatiquement par la
        // relation (HasMany dans SettlementBatchConfiguration) ; on ne le déclare
        // pas ici pour ne pas dépendre de l'ordre d'application des configurations.
        builder.HasIndex(p => p.SellerId);
    }
}
