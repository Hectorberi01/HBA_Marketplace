using HBA.Communication.Notifications.Domain.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Communication.Notifications.Infrastructure.Persistence.Configurations;

/// <summary>Mapping de la table <c>notification_templates</c> du §10.15.</summary>
public sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_templates");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Code).HasMaxLength(120).IsRequired();
        builder.Property(t => t.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(t => t.Locale).HasMaxLength(10).IsRequired();
        builder.Property(t => t.SubjectTemplate).HasMaxLength(300);
        builder.Property(t => t.BodyTemplate).IsRequired();
        builder.Property(t => t.Version).IsRequired();
        builder.Property(t => t.IsActive).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        // UNICITÉ SUR (code, canal, locale, version), ET LA VERSION EN FAIT PARTIE.
        builder.HasIndex(t => new { t.Code, t.Channel, t.Locale, t.Version })
            .IsUnique()
            .HasDatabaseName("ux_notification_templates_code_channel_locale_version");

        // La recherche du gabarit actif est le chemin chaud : elle a lieu à chaque
        // notification émise.
        builder.HasIndex(t => new { t.Code, t.Channel, t.Locale })
            .HasFilter("\"IsActive\" = TRUE")
            .HasDatabaseName("ix_notification_templates_active");

        builder.Ignore(t => t.DomainEvents);
    }
}
