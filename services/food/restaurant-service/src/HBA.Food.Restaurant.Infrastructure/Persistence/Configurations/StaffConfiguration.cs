using HBA.Food.Domain.Staff;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Food.Infrastructure.Persistence.Configurations;

/// <summary>
/// Le personnel des restaurants (cahier des charges §8).
/// </summary>
internal sealed class RestaurantStaffConfiguration : IEntityTypeConfiguration<RestaurantStaff>
{
    public void Configure(EntityTypeBuilder<RestaurantStaff> builder)
    {
        builder.ToTable("restaurant_staff");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new RestaurantStaffId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.RestaurantId).IsRequired();
        builder.Property(s => s.UserId).IsRequired();

        // Le rôle est stocké en ENTIER, et sa valeur EST la hiérarchie.
        //
        // Le stocker en chaîne rendrait la comparaison de rang impossible en SQL
        // — or « combien reste-t-il de propriétaires actifs ? » est une requête,
        // pas un parcours en mémoire.
        builder.Property(s => s.Role).HasConversion<int>().IsRequired();

        builder.Property(s => s.IsFounder).IsRequired();
        builder.Property(s => s.IsActive).IsRequired();
        builder.Property(s => s.CreatedOnUtc).IsRequired();
        builder.Property(s => s.UpdatedOnUtc);

        // UN COMPTE NE FIGURE QU'UNE FOIS DANS UN RESTAURANT.
        //
        // Deux lignes pour la même personne donneraient deux jeux de droits, et
        // la réponse à « que peut-il faire ? » dépendrait de l'ordre de lecture.
        // L'unicité porte sur le couple, actifs et partis confondus : un ancien
        // employé se RÉACTIVE, il ne se recrée pas.
        builder.HasIndex(s => new { s.RestaurantId, s.UserId })
            .IsUnique()
            .HasDatabaseName("ux_restaurant_staff_restaurant_user");

        // Résolution de l'établissement depuis le jeton, à chaque requête de
        // l'espace restaurateur : c'est l'index le plus sollicité du module.
        builder.HasIndex(s => s.UserId).HasDatabaseName("ix_restaurant_staff_user");

        // VERROU OPTIMISTE (§20 du cahier : « garantir une seule transition de
        // statut valide en cas d'actions concurrentes »).
        //
        // Deux propriétaires qui retirent chacun l'autre au même instant liraient
        // tous deux « il reste 2 propriétaires », et l'établissement se
        // retrouverait sans personne. Le compte des propriétaires actifs est une
        // lecture-puis-décision : sans verrou, la garde du dernier propriétaire ne
        // vaut que tant que personne ne va vite.
        builder.UsePostgresRowVersion();

        builder.OwnsMany<StaffPermissionOverride>("_overrides", derogations =>
        {
            derogations.ToTable("restaurant_staff_permissions");
            derogations.WithOwner().HasForeignKey("RestaurantStaffId");

            derogations.Property(o => o.Permission).HasConversion<int>();
            derogations.Property(o => o.IsGranted).IsRequired();

            // La clé est le COUPLE membre + permission : une permission ne peut
            // pas être à la fois accordée et retirée pour la même personne.
            derogations.HasKey("RestaurantStaffId", nameof(StaffPermissionOverride.Permission));
        });

        builder.Ignore(s => s.DomainEvents);
        builder.Ignore(s => s.Overrides);
        builder.Ignore(s => s.EffectivePermissions);
    }
}
