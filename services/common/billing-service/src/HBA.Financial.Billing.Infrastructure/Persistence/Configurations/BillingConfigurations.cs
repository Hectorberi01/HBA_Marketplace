using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Financial.Billing.Domain.Commissions;
using HBA.Financial.Billing.Domain.Invoices;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Financial.Billing.Infrastructure.Persistence.Configurations;

internal sealed class CommissionRuleConfiguration : IEntityTypeConfiguration<CommissionRule>
{
    public void Configure(EntityTypeBuilder<CommissionRule> builder)
    {
        builder.ToTable("commission_rules");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new CommissionRuleId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.Scope).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.TargetId);
        builder.Property(r => r.Rate).HasColumnType("numeric(6,4)").IsRequired();
        builder.Property(r => r.FixedFee).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.MinFee).HasColumnType("numeric(18,2)");
        builder.Property(r => r.MaxFee).HasColumnType("numeric(18,2)");
        builder.Property(r => r.EffectiveFromUtc).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();

        builder.HasIndex(r => new { r.Scope, r.TargetId });
        builder.HasIndex(r => r.IsActive);

        builder.Ignore(r => r.DomainEvents);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.HorodateLesModifications();

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasConversion(id => id.Value, value => new InvoiceId(value))
            .ValueGeneratedNever();

        builder.Property(i => i.SellerId).IsRequired();
        builder.Property(i => i.PeriodStartUtc).IsRequired();
        builder.Property(i => i.PeriodEndUtc).IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.TotalAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.IssuedAtUtc);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE
        // EF.
        builder.HasMany(i => i.Lines)
            .WithOne()
            .HasForeignKey("InvoiceId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => i.SellerId);

        // JETON DE CONCURRENCE (§6).
        builder.UsePostgresRowVersion();

        builder.Ignore(i => i.DomainEvents);
    }
}

internal sealed class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine>
{
    public void Configure(EntityTypeBuilder<InvoiceLine> builder)
    {
        builder.ToTable("invoice_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.Description).HasMaxLength(300).IsRequired();
        builder.Property(l => l.Amount).HasColumnType("numeric(18,2)").IsRequired();

        builder.HasIndex("InvoiceId");
    }
}
