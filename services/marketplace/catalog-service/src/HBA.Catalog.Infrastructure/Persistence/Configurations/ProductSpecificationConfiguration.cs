using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductSpecificationGroupConfiguration
    : IEntityTypeConfiguration<ProductSpecificationGroup>
{
    public void Configure(EntityTypeBuilder<ProductSpecificationGroup> builder)
    {
        builder.ToTable("product_specification_groups");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.RevisionId).IsRequired();
        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.Property(g => g.DisplayOrder).IsRequired();

        builder.HasMany(g => g.Items)
            .WithOne()
            .HasForeignKey(i => i.GroupId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(g => g.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(g => g.RevisionId);
    }
}

internal sealed class ProductSpecificationConfiguration : IEntityTypeConfiguration<ProductSpecification>
{
    public void Configure(EntityTypeBuilder<ProductSpecification> builder)
    {
        builder.ToTable("product_specifications");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.GroupId).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(500).IsRequired();
        builder.Property(s => s.DisplayOrder).IsRequired();

        builder.HasIndex(s => s.GroupId);
    }
}
