using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Financial.Payments.Domain.Payments;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Financial.Payments.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {

        // VERROU OPTIMISTE — DOUBLE CAPTURE = DOUBLE CRÉDIT AU VENDEUR.
        builder.UsePostgresRowVersion();
        builder.ToTable("payments");
        builder.HorodateLesModifications();

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.OrderId).IsRequired();

        // VALEUR PAR DÉFAUT « Marketplace » POUR LES LIGNES DÉJÀ EN BASE.
        builder.Property(p => p.OrderType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(PaymentOrderType.Marketplace)
            .IsRequired();

        // Un paiement se retrouve par sa commande, et l'univers fait partie de la
        // clé de recherche : deux commandes d'univers différents peuvent porter le
        // même identifiant sans que ce soit une anomalie.
        builder.HasIndex(p => new { p.OrderType, p.OrderId })
            .HasDatabaseName("ix_payments_order");
        builder.Property(p => p.BuyerId).IsRequired();
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Provider).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Flow).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.ProviderReference).HasMaxLength(200);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.CapturedAtUtc);
        builder.Property(p => p.EscrowReleasedAt);
        builder.Ignore(p => p.IsEscrowHeld);

        builder.OwnsOne(p => p.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });
        builder.Navigation(p => p.Amount).IsRequired();

        // `Restrict`, ET NON `Cascade` — UN PAIEMENT SUPPRIMÉ EMPORTAIT LA PREUVE
        // QUE LE CLIENT AVAIT ÉTÉ REMBOURSÉ.
        builder.HasMany(p => p.Refunds)
            .WithOne()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.Status);

        // UNE RÉFÉRENCE PSP NE DÉSIGNE QU'UN SEUL PAIEMENT.
        builder.HasIndex(p => p.ProviderReference)
            .IsUnique()
            .HasFilter("\"ProviderReference\" IS NOT NULL");

        builder.Ignore(p => p.DomainEvents);
    }
}

internal sealed class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("payment_refunds");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.PaymentId)
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .IsRequired();

        builder.Property(r => r.ReturnId);
        builder.Property(r => r.ExternalRefundId);
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasMaxLength(180).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.ProviderRefundId).HasMaxLength(200);
        builder.Property(r => r.FailureReason).HasMaxLength(500);
        builder.Property(r => r.RequestedAtUtc).IsRequired();
        builder.Property(r => r.CompletedAtUtc);
        builder.Property(r => r.LastAttemptAtUtc);
        builder.Property(r => r.AttemptCount).IsRequired();

        builder.OwnsOne(r => r.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });
        builder.Navigation(r => r.Amount).IsRequired();

        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey }).IsUnique();

        // Même panne que sur `payments.ProviderReference`, un cran plus bas : cet
        // identifiant est celui que le service de retours donne au remboursement
        // qu'il demande.
        builder.HasIndex(r => r.ExternalRefundId)
            .IsUnique()
            .HasFilter("\"ExternalRefundId\" IS NOT NULL");
        builder.HasIndex(r => r.ReturnId);
        builder.HasIndex(r => r.Status);
    }
}
