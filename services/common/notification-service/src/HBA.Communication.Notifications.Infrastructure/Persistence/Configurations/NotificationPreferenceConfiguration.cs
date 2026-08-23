using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Communication.Notifications.Domain.Preferences;

namespace HBA.Communication.Notifications.Infrastructure.Persistence.Configurations;

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences");

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).ValueGeneratedNever();

        // Catégories coupées -> text[] natif PostgreSQL.
        builder.Property(p => p.MutedCategories)
            .HasColumnType("text[]")
            .HasColumnName("muted_categories")
            .IsRequired();

        builder.Property(p => p.UpdatedAtUtc).IsRequired();
    }
}
