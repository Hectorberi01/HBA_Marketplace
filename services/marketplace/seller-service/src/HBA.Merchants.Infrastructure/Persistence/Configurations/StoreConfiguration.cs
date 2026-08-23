using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Merchants.Domain.Stores;

namespace HBA.Merchants.Infrastructure.Persistence.Configurations;

internal sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("stores");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new StoreId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.SellerId).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(150).IsRequired();
        builder.Property(s => s.LogoUrl).HasMaxLength(500);
        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.StatusReason).HasMaxLength(500);

        // Simple Guid : Sellers ne référence pas Inventory. L'existence du lieu est
        // vérifiée par l'Application, qui a le droit d'appeler les deux modules.
        builder.Property(s => s.FulfillmentLocationId);

        builder.Property(s => s.CreatedOnUtc).IsRequired();
        builder.Property(s => s.UpdatedOnUtc);

        builder.OwnsOne(s => s.Contact, contact =>
        {
            contact.Property(c => c.Phone).HasColumnName("ContactPhone").HasMaxLength(20).IsRequired();
            contact.Property(c => c.Email).HasColumnName("ContactEmail").HasMaxLength(200);
        });

        // ─────────────────────────────────────────────────────────────────────
        // LES HORAIRES SONT DES LIGNES, PAS UN JSON.
        //
        // Une grille horaire se filtre (« quelles boutiques sont ouvertes
        // maintenant ? ») et se trie. En jsonb, chacune de ces questions devient
        // une lecture complète de la table suivie d'un tri en mémoire.
        //
        // OwnsMany PAR NOM DE CHAMP : la collection est privée et exposée en
        // lecture seule. EF doit écrire dedans, pas dans la vue.
        // ─────────────────────────────────────────────────────────────────────
        builder.OwnsMany<StoreOpeningHour>("_openingHours", hours =>
        {
            hours.ToTable("store_opening_hours");
            hours.WithOwner().HasForeignKey("StoreId");
            hours.Property<int>("Id").ValueGeneratedOnAdd();
            hours.HasKey("Id");

            hours.Property(h => h.Day).HasConversion<string>().HasMaxLength(10).IsRequired();
            hours.Property(h => h.OpensAt).IsRequired();
            hours.Property(h => h.ClosesAt).IsRequired();

            hours.HasIndex("StoreId", "Day");
        });

        // Le multi-boutiques se lit par vendeur : c'est l'index qui porte l'écran
        // « mes boutiques » et toute la cascade de fermeture.
        builder.HasIndex(s => s.SellerId);

        builder.Ignore(s => s.DomainEvents);

        // IGNORÉES, PAS MAPPÉES. `OpeningHours` et `IsSelling` sont des
        // propriétés CALCULÉES — une vue en lecture seule et un booléen dérivé du
        // statut. EF mappe le champ privé `_openingHours` (ci-dessus) ; tenter de
        // mapper la vue par-dessus créerait une seconde navigation vers la même
        // table. Même arrangement que Product.Variants.
        builder.Ignore(s => s.OpeningHours);
        builder.Ignore(s => s.IsSelling);
    }
}
