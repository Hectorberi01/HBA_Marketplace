using HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Persistence.Configurations;

internal sealed class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> builder)
    {
        builder.ToTable("return_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReturnNumber).HasMaxLength(32).IsRequired();
        builder.HasIndex(r => r.ReturnNumber).IsUnique();
        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => new { r.CustomerId, r.CreatedAtUtc });
        builder.HasIndex(r => new { r.SellerId, r.Status });

        // L'INDEX DU BALAYAGE D'EXPIRATION.
        builder.HasIndex(r => new { r.Status, r.ExpiresAtUtc });

        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.EstimatedRefundAmount).HasPrecision(18, 2);
        builder.Property(r => r.ApprovedRefundAmount).HasPrecision(18, 2);
        builder.Property(r => r.ReturnShippingPayer).HasMaxLength(24);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.ResolutionRequested).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.ReasonCode).HasConversion<string>().HasMaxLength(64);
        builder.Property(r => r.Version).IsConcurrencyToken();

        builder.OwnsOne(r => r.PolicySnapshot, policy =>
        {
            policy.Property(p => p.PolicyId).HasColumnName("policy_id").HasMaxLength(80);
            policy.Property(p => p.Version).HasColumnName("policy_version").HasMaxLength(32);
            policy.Property(p => p.ReturnWindowDays).HasColumnName("policy_return_window_days");
            policy.Property(p => p.AllowReturn).HasColumnName("policy_allow_return");
            policy.Property(p => p.AllowRefundOnly).HasColumnName("policy_allow_refund_only");
            policy.Property(p => p.RequireEvidence).HasColumnName("policy_require_evidence");
            policy.Property(p => p.RequireInspection).HasColumnName("policy_require_inspection");
            policy.Property(p => p.RestockingFeePercent).HasColumnName("policy_restocking_fee_percent").HasPrecision(5, 2);
            policy.Ignore(p => p.CustomerPaysReturnShippingFor);
            policy.Ignore(p => p.AutoApproveReasons);
        });

        // `Restrict` SUR LES SIX — UN DOSSIER DE LITIGE EST UNE PREUVE.
        builder.HasMany(r => r.Items).WithOne().HasForeignKey(i => i.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Evidence).WithOne().HasForeignKey(e => e.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Shipments).WithOne().HasForeignKey(s => s.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Inspections).WithOne().HasForeignKey(i => i.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Refunds).WithOne().HasForeignKey(r => r.ReturnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.History).WithOne().HasForeignKey(h => h.ReturnId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReturnItemConfiguration : IEntityTypeConfiguration<ReturnItem>
{
    public void Configure(EntityTypeBuilder<ReturnItem> builder)
    {
        builder.ToTable("return_items");
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => i.OrderItemId);
        builder.Property(i => i.SkuSnapshot).HasMaxLength(128);
        builder.Property(i => i.NameSnapshot).HasMaxLength(512);
        builder.Property(i => i.UnitPaidAmount).HasPrecision(18, 2);
        builder.Property(i => i.Currency).HasMaxLength(3);
        builder.Property(i => i.ReasonCode).HasConversion<string>().HasMaxLength(64);
        builder.Property(i => i.ConditionDeclared).HasConversion<string>().HasMaxLength(64);
        builder.Property(i => i.ConditionInspected).HasConversion<string>().HasMaxLength(64);
    }
}

internal sealed class ReturnEvidenceConfiguration : IEntityTypeConfiguration<ReturnEvidence>
{
    public void Configure(EntityTypeBuilder<ReturnEvidence> builder)
    {
        builder.ToTable("return_evidence");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.MediaId).HasMaxLength(128);
        builder.Property(e => e.Kind).HasMaxLength(64);
        builder.Property(e => e.Caption).HasMaxLength(512);
    }
}

internal sealed class ReturnShipmentConfiguration : IEntityTypeConfiguration<ReturnShipment>
{
    public void Configure(EntityTypeBuilder<ReturnShipment> builder)
    {
        builder.ToTable("return_shipments");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.DeliveryId).HasMaxLength(128);
        builder.Property(s => s.Mode).HasMaxLength(64);
        builder.Property(s => s.TrackingNumber).HasMaxLength(128);
    }
}

internal sealed class ReturnInspectionConfiguration : IEntityTypeConfiguration<ReturnInspection>
{
    public void Configure(EntityTypeBuilder<ReturnInspection> builder)
    {
        builder.ToTable("return_inspections");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Condition).HasConversion<string>().HasMaxLength(64);
        builder.Property(i => i.Disposition).HasConversion<string>().HasMaxLength(64);
        builder.Property(i => i.Notes).HasMaxLength(2000);
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.ReturnId);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.Currency).HasMaxLength(3);
        builder.Property(r => r.IdempotencyKey).HasMaxLength(160);

        // UN REMBOURSEMENT PAR CLÉ — LA COURSE DE `DecideRefund` SE FERME ICI.
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();

        // L'INDEX DU BALAYAGE D'EXÉCUTION.
        builder.HasIndex(r => new { r.Status, r.CreatedAtUtc });

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(r => r.ProviderRefundId).HasMaxLength(128);
        builder.OwnsOne(r => r.Breakdown, b =>
        {
            b.OwnsOne(x => x.Items, m => MapMoney(m, "items"));
            b.OwnsOne(x => x.Tax, m => MapMoney(m, "tax"));
            b.OwnsOne(x => x.OriginalShipping, m => MapMoney(m, "original_shipping"));
            b.OwnsOne(x => x.DiscountAllocation, m => MapMoney(m, "discount_allocation"));
            b.OwnsOne(x => x.RestockingFee, m => MapMoney(m, "restocking_fee"));
            b.OwnsOne(x => x.ReturnShippingCharge, m => MapMoney(m, "return_shipping_charge"));
            b.OwnsOne(x => x.PreviousRefunds, m => MapMoney(m, "previous_refunds"));
        });
        // `Restrict` — LE SECOND NIVEAU DE LA CHAÎNE.
        builder.HasMany(r => r.Attempts).WithOne().HasForeignKey(a => a.RefundId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void MapMoney(
        OwnedNavigationBuilder<Domain.ValueObjects.RefundBreakdown, Domain.ValueObjects.Money> builder,
        string prefix)
    {
        builder.Property(m => m.Amount).HasColumnName($"{prefix}_amount").HasPrecision(18, 2);
        builder.Property(m => m.Currency).HasColumnName($"{prefix}_currency").HasMaxLength(3);
    }
}

internal sealed class RefundAttemptConfiguration : IEntityTypeConfiguration<RefundAttempt>
{
    public void Configure(EntityTypeBuilder<RefundAttempt> builder)
    {
        builder.ToTable("refund_attempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Provider).HasMaxLength(64);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(a => a.ProviderReference).HasMaxLength(256);
    }
}

internal sealed class ReturnStatusHistoryConfiguration : IEntityTypeConfiguration<ReturnStatusHistory>
{
    public void Configure(EntityTypeBuilder<ReturnStatusHistory> builder)
    {
        builder.ToTable("return_status_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(64);
        builder.Property(h => h.Reason).HasMaxLength(2000);
        builder.HasIndex(h => new { h.ReturnId, h.OccurredAtUtc });
    }
}

internal sealed class ReturnIdempotencyKeyConfiguration : IEntityTypeConfiguration<ReturnIdempotencyKey>
{
    public void Configure(EntityTypeBuilder<ReturnIdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys");
        builder.HasKey(k => k.Key);
        builder.Property(k => k.Key).HasMaxLength(180);
        builder.HasIndex(k => k.ReturnRequestId).IsUnique();
    }
}
