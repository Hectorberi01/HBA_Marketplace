using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Financial.Payments.Domain.PaymentMethods;

namespace HBA.Financial.Payments.Infrastructure.Persistence.Configurations;

internal sealed class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("payment_methods");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new SavedPaymentMethodId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Label).HasMaxLength(60).IsRequired();
        builder.Property(p => p.Provider).HasMaxLength(40).IsRequired();
        builder.Property(p => p.AccountRef).HasMaxLength(40).IsRequired();
        builder.Property(p => p.ExpiryMonth);
        builder.Property(p => p.ExpiryYear);
        builder.Property(p => p.HolderName).HasMaxLength(120);
        builder.Property(p => p.IsDefault).IsRequired();
        builder.Property(p => p.CreatedOnUtc).IsRequired();

        builder.HasIndex(p => p.UserId);
        builder.Ignore(p => p.DomainEvents);
    }
}
