using HBA.Users.Domain.Preferences;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Users.Infrastructure.Persistence.Configurations;

/// <summary>Mapping de la table <c>preferences</c> du §10.2.</summary>
public sealed class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> builder)
    {
        builder.ToTable("preferences");

        // La clé est le UserId — voir l'encadré de UserPreferences. `ValueGeneratedNever`
        // est indispensable : sans lui, EF traite un Guid en clé comme généré par la
        // base et écrase l'identifiant fourni par un autre, tiré au hasard.
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Language).HasMaxLength(10).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.PushEnabled).IsRequired();
        builder.Property(p => p.MarketingOptIn).IsRequired();
        builder.Property(p => p.CreatedOnUtc).IsRequired();
        builder.Property(p => p.UpdatedOnUtc);

        builder.Ignore(p => p.DomainEvents);
    }
}
