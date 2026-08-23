using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Infrastructure.Persistence.Converters;

namespace HBA.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductRevisionConfiguration : IEntityTypeConfiguration<ProductRevision>
{
    public void Configure(EntityTypeBuilder<ProductRevision> builder)
    {
        builder.ToTable("product_revisions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // MÊME CONVERSION QUE LA CLÉ PRIMAIRE DE `Product`, ET MÊME TYPE CLR.
        //
        // EF exige le second : une clé étrangère `Guid` face à une clé primaire
        // `ProductId` fait échouer la construction du modèle. Voir l'encadré de
        // `ProductRevision.ProductId`. La colonne reste un `uuid`.
        builder.Property(r => r.ProductId)
            .HasConversion(id => id.Value, value => new ProductId(value))
            .IsRequired();
        builder.Property(r => r.Version).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ShortDescription).HasMaxLength(500);
        builder.Property(r => r.Description).HasMaxLength(4000);
        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.CategoryId).IsRequired();
        builder.Property(r => r.BrandId);

        builder.Property(r => r.Slug)
            .HasConversion(slug => slug.Value, value => Slug.Create(value).Value)
            .HasColumnName("slug")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.Attributes)
            .HasConversion(new AttributesJsonConverter(), new AttributesJsonComparer())
            .HasColumnType("jsonb")
            .HasColumnName("attributes")
            .IsRequired();

        builder.Property(r => r.Tags)
            .HasColumnType("text[]")
            .HasColumnName("tags")
            .IsRequired();

        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.SubmittedAtUtc);
        builder.Property(r => r.ReviewedAtUtc);
        builder.Property(r => r.PublishedAtUtc);

        // ═══════════════════════════════════════════════════════════════════════
        // TARIFICATION DE RÉFÉRENCE — COLONNES PLATES, PAS DE TABLE SÉPARÉE.
        //
        // ÉCART ASSUMÉ AU §20, QUI PRÉVOIT UNE TABLE `product_prices`.
        //
        // Cette table n'a de sens que le jour où une VARIANTE porte son propre prix
        // (§11) : il faut alors une ligne dont la clé est « révision ou variante »,
        // ce qu'aucune colonne d'une seule table ne sait représenter. Tant que le
        // prix ne vit que sur la révision, une table séparée ajouterait une
        // jointure à chaque lecture de fiche pour ne stocker que six colonnes.
        //
        // `OwnsOne` sans `ToTable` est aussi le mécanisme déjà utilisé et éprouvé
        // dans ce dépôt pour Money sur product_offers.
        // ═══════════════════════════════════════════════════════════════════════
        builder.OwnsOne(r => r.Pricing, pricing =>
        {
            // BIGINT, PAS NUMERIC (§21, décision D13).
            //
            // Des francs CFA entiers. Voir l'encadré de ProductPricing pour la
            // cohabitation avec product_offers, resté en numeric(18,2).
            pricing.Property(p => p.BasePrice)
                .HasColumnName("base_price")
                .HasColumnType("bigint")
                .IsRequired();

            pricing.Property(p => p.CompareAtPrice)
                .HasColumnName("compare_at_price")
                .HasColumnType("bigint");

            pricing.Property(p => p.CostPrice)
                .HasColumnName("cost_price")
                .HasColumnType("bigint");

            pricing.Property(p => p.Currency)
                .HasColumnName("currency")
                .HasMaxLength(3)
                .IsRequired();

            pricing.Property(p => p.TaxIncluded)
                .HasColumnName("tax_included")
                .IsRequired();

            pricing.Property(p => p.TaxRate)
                .HasColumnName("tax_rate")
                .IsRequired();
        });

        builder.Navigation(r => r.Pricing).IsRequired();

        builder.HasOne(r => r.Condition)
            .WithOne()
            .HasForeignKey<ProductCondition>(c => c.RevisionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // La fiche technique du §12, portée par la révision : la modifier est une
        // modification critique au sens du §6.
        builder.HasMany(r => r.Specifications)
            .WithOne()
            .HasForeignKey(g => g.RevisionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Specifications).UsePropertyAccessMode(PropertyAccessMode.Field);

        // UNE SEULE RÉVISION PAR NUMÉRO DE VERSION (§21).
        //
        // Sans cet index, deux écritures concurrentes sur le même produit créent
        // deux révisions « version 5 ». Rien ne se plaint ; on découvre le doublon
        // en cherchant pourquoi la file de validation affiche deux fois la même
        // fiche, et il est alors impossible de savoir laquelle a été approuvée.
        builder.HasIndex(r => new { r.ProductId, r.Version }).IsUnique();

        // ═══════════════════════════════════════════════════════════════════════
        // LE SLUG N'EST PAS UNIQUE — SAUF PARMI LES RÉVISIONS PUBLIÉES.
        //
        // C'est le piège de cette table, et il tombe dans les deux sens :
        //
        //   • un index unique simple casserait la deuxième révision d'un produit,
        //     qui porte le MÊME slug que la première. Le vendeur corrigerait une
        //     faute de frappe et recevrait une violation de contrainte ;
        //   • aucune contrainte du tout casserait l'URL publique
        //     `GET /api/v1/catalog/products/{slug}` (§17), qui suppose qu'un slug
        //     désigne un produit. Deux fiches publiées homonymes rendraient la
        //     route non déterministe — et la fiche servie changerait au gré du plan
        //     d'exécution PostgreSQL.
        //
        // L'unicité PARTIELLE dit exactement ce qui est vrai : parmi ce qui est
        // visible, un slug ne désigne qu'une chose. Même forme que
        // `ux_product_offers_store_variant` et `ux_coupon_usages_live_hold`.
        // ═══════════════════════════════════════════════════════════════════════
        builder.HasIndex(r => r.Slug)
            .IsUnique()
            .HasFilter("\"Status\" = 'Published'")
            .HasDatabaseName("ux_product_revisions_published_slug");

        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.CategoryId);
        builder.HasIndex(r => r.BrandId);
    }
}

internal sealed class ProductConditionConfiguration : IEntityTypeConfiguration<ProductCondition>
{
    public void Configure(EntityTypeBuilder<ProductCondition> builder)
    {
        builder.ToTable("product_conditions");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.RevisionId).IsRequired();

        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Grade).HasMaxLength(1);
        builder.Property(c => c.Description).HasMaxLength(2000);

        // CES DEUX BOOLÉENS SONT DÉDUITS DU TYPE, ILS NE SONT PAS SAISIS.
        //
        // Ils sont stockés quand même : les requêtes « tous les reconditionnés »
        // et « toutes les occasions » les lisent directement, et recalculer le
        // type en SQL obligerait à recopier la règle dans une clause WHERE — donc
        // à la maintenir à deux endroits. Voir ProductCondition.Create.
        builder.Property(c => c.IsUsed).IsRequired();
        builder.Property(c => c.IsRefurbished).IsRequired();

        builder.Property(c => c.HasOriginalPackaging).IsRequired();
        builder.Property(c => c.HasOriginalAccessories).IsRequired();
        builder.Property(c => c.FunctionalStatus).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(c => c.RefurbishedByType).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.RefurbishedBySellerId);
        builder.Property(c => c.RefurbishmentOperations)
            .HasColumnType("text[]")
            .HasColumnName("refurbishment_operations")
            .IsRequired();
        builder.Property(c => c.BatteryHealthPercentage);
        builder.Property(c => c.BatteryReplaced);

        builder.HasMany(c => c.Defects)
            .WithOne()
            .HasForeignKey(d => d.ConditionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Defects).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => c.RevisionId).IsUnique();
        builder.HasIndex(c => c.Type);
    }
}

internal sealed class ProductDefectConfiguration : IEntityTypeConfiguration<ProductDefect>
{
    public void Configure(EntityTypeBuilder<ProductDefect> builder)
    {
        builder.ToTable("product_condition_defects");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.ConditionId).IsRequired();
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Location).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.Severity).HasConversion<string>().HasMaxLength(20).IsRequired();
    }
}
