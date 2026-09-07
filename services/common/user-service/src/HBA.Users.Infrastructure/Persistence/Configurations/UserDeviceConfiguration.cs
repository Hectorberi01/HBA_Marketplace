using HBA.Users.Domain.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Users.Infrastructure.Persistence.Configurations;

/// <summary>Mapping de la table <c>devices</c> du §10.2.</summary>
public sealed class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> builder)
    {
        builder.ToTable("devices");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.UserId).IsRequired();
        builder.Property(d => d.Platform).HasMaxLength(16).IsRequired();
        builder.Property(d => d.PushToken).HasMaxLength(UserDevice.MaxPushToken).IsRequired();
        builder.Property(d => d.RegisteredOnUtc).IsRequired();
        builder.Property(d => d.LastSeenAtUtc).IsRequired();

        // UNICITÉ SUR LE COUPLE, PAS SUR LE JETON SEUL.
        builder.HasIndex(d => new { d.UserId, d.PushToken })
            .IsUnique()
            .HasDatabaseName("ux_devices_user_push_token");

        // Purge des jetons dormants : les fournisseurs refusent les jetons périmés
        // sans le signaler, cette date est la seule base d'un nettoyage.
        builder.HasIndex(d => d.LastSeenAtUtc).HasDatabaseName("ix_devices_last_seen_at");

        builder.Ignore(d => d.DomainEvents);
    }
}
