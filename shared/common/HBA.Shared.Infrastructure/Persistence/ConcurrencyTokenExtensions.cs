using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Shared.Infrastructure.Persistence;

/// <summary>Verrou optimiste PostgreSQL, adossé à la colonne système <c>xmin</c>.</summary>
public static class ConcurrencyTokenExtensions
{
    /// <summary>Déclare <c>xmin</c> comme jeton de concurrence de l'entité.</summary>
    public static EntityTypeBuilder<TEntity> UsePostgresRowVersion<TEntity>(
        this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        return builder;
    }
}
