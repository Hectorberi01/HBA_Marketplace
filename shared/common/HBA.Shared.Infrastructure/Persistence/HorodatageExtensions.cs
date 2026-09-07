using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Shared.Infrastructure.Persistence;

/// <summary>Horodatage de la DERNIÈRE modification d'une ligne.</summary>
public static class HorodatageExtensions
{
    /// <summary>Nom de la colonne, et seul endroit où il est écrit.</summary>
    public const string ColonneModification = "UpdatedAtUtc";

    /// <summary>Déclare <c>UpdatedAtUtc</c> sur l'entité, estampillée à chaque écriture.</summary>
    public static EntityTypeBuilder<TEntity> HorodateLesModifications<TEntity>(
        this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        // REFUS EXPLICITE PLUTÔT QUE DOUBLON SILENCIEUX.
        if (typeof(TEntity).GetProperty(ColonneModification) is not null)
        {
            throw new InvalidOperationException(
                $"« {typeof(TEntity).Name} » déclare déjà une propriété « {ColonneModification} » : "
                + "n'appelez pas HorodateLesModifications() dessus, l'entité tient déjà son horodatage.");
        }

        builder.Property<DateTime?>(ColonneModification)
            .HasColumnName(ColonneModification);

        return builder;
    }
}
