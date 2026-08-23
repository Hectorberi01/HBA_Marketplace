using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Commerce.Domain.Carts;
using HBA.Shared.Infrastructure.Persistence;
using CartAggregate = HBA.Commerce.Domain.Carts.Cart;

namespace HBA.Commerce.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<CartAggregate>
{
    public void Configure(EntityTypeBuilder<CartAggregate> builder)
    {
        builder.ToTable("carts");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CartId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.BuyerId).IsRequired();
        builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Code promo du panier. Nullable : la plupart des paniers n'en ont pas.
        builder.Property(c => c.PromotionCode).HasMaxLength(64);

        // IsRequired() est indispensable : sans lui, EF considère la relation
        // optionnelle et, à la suppression d'une ligne, tente un
        // « UPDATE cart_items SET CartId = NULL » (sévérance) au lieu d'un DELETE.
        // Comme la colonne CartId est NOT NULL, Postgres rejette → 500. Avec
        // IsRequired(), EF supprime l'orphelin (DELETE), ce qui est le comportement
        // attendu. Aligné sur le snapshot/migration (pas de nouvelle migration).
        //
        // NOTE DE 2026-07-14 (voir doc 22) : ce commentaire est ANTÉRIEUR et je n'ai pas
        // pu le vérifier. En EF Core, le `OnDelete(DeleteBehavior.Cascade)` ci-dessous
        // gouverne AUSSI le sort des orphelins — avec Cascade, un enfant retiré d'une
        // collection est SUPPRIMÉ, pas mis à NULL. Les données de production le confirment
        // sur les autres agrégats (10 commandes confirmées, zéro réservation orpheline).
        //
        // Le 500 décrit ci-dessus a donc probablement une autre cause, ou s'est produit
        // AVANT que `OnDelete(Cascade)` ne soit configuré. Je ne le supprime pas — il
        // consigne peut-être un incident réel — mais ne le prenez pas pour une explication
        // établie.
        builder.HasMany(c => c.Items)
            .WithOne()
            .HasForeignKey("CartId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.BuyerId, c.Status });

        // ═════════════════════════════════════════════════════════════════════
        // UN SEUL PANIER ACTIF PAR ACHETEUR — LE CODE LE SUPPOSAIT DÉJÀ (§5).
        //
        // `CartRepository.GetActiveCartAsync` fait un `FirstOrDefault` SANS TRI.
        // Avec deux paniers actifs, l'acheteur en voyait donc un AU HASARD : ses
        // articles apparaissaient et disparaissaient d'une requête à l'autre. La
        // création est un « récupérer-ou-créer » non atomique, et deux ajouts
        // simultanés produisaient deux paniers.
        //
        // INDEX PARTIEL. Un acheteur a un seul panier ACTIF, mais autant de
        // paniers `CheckedOut` et `Abandoned` que d'achats passés — c'est son
        // historique. Un index unique sans filtre les refuserait tous.
        //
        // LA MIGRATION QUI L'ACCOMPAGNE FUSIONNE LES DOUBLONS EXISTANTS, elle ne
        // les écarte pas. Voir `20260903000000_UnSeulPanierActifParAcheteur` : c'est
        // la première migration du chantier qui écrit des lignes métier, et elle
        // dit ce qu'elle ne sait pas fusionner.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(c => c.BuyerId)
            .IsUnique()
            .HasDatabaseName("ux_carts_active_buyer")
            .HasFilter("\"Status\" = 'Active'");

        // ═════════════════════════════════════════════════════════════════════
        // JETON DE CONCURRENCE — ET IL NE PROTÈGE PAS CE QU'ON CROIT (§6).
        //
        // L'encadré d'`UsePostgresRowVersion` prévient qu'un jeton n'est évalué
        // que dans un `UPDATE`, donc qu'il est INERTE sur un chemin qui n'écrit
        // que des lignes ENFANTS. C'est exactement le cas d'`AddItem` : il ajoute
        // à `_items` ou incrémente une ligne existante, et n'écrit RIEN sur
        // l'en-tête du panier. Vérifié avant de poser ce jeton, pas après.
        //
        // Ce que ce jeton protège réellement — et c'est le geste qui compte :
        //
        //   • `MarkCheckedOut` écrit `Status`. Deux validations simultanées du même
        //     panier — un double-clic, un client qui relance — produisaient DEUX
        //     commandes à partir d'un seul panier. Le second reçoit désormais 409.
        //   • `ApplyPromotionCode` / `RemovePromotionCode` écrivent `PromotionCode`.
        //
        // POURQUOI L'AJOUT DE LIGNES N'A PAS BESOIN DE CE JETON : l'unicité est
        // déjà tenue par la base. `ux (CartId, OfferId) WHERE Kind = 'Goods'`
        // refuse deux lignes pour la même offre, quelle que soit la concurrence.
        //
        // CE QUI RESTE OUVERT, ET IL FAUT LE SAVOIR : les lignes FOOD. Leur
        // unicité tient à la combinaison plat + options, vérifiée EN MÉMOIRE par
        // `CartItem.MatchesFood` — elle ne s'exprime pas en une colonne, donc
        // aucun index ne la porte. Deux ajouts concurrents du même plat avec les
        // mêmes options peuvent encore produire deux lignes. Ni ce jeton ni cet
        // index ne le couvrent ; il faudrait une empreinte des options stockée en
        // colonne, ce qui est un changement de modèle, pas un réglage.
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();

        // Propriété CALCULÉE : la nature se dérive des lignes.
        builder.Ignore(c => c.Kind);

        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        // Le discriminant. Stocké en texte : un panier se lit en base pendant les
        // incidents, et « Food » s'y comprend là où « 1 » demande de retrouver
        // l'énumération.
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(10).IsRequired();

        // ── Marchandise : vides pour une ligne food, donc plus obligatoires ──
        builder.Property(i => i.OfferId).IsRequired();
        builder.Property(i => i.ProductId).IsRequired();
        builder.Property(i => i.CategoryId).IsRequired();
        builder.Property(i => i.SellerId).IsRequired();
        builder.Property(i => i.ShipFromLocationId).IsRequired();

        // LE SKU N'EST PLUS OBLIGATOIRE EN CONTENU, MAIS RESTE NON NUL.
        //
        // Une ligne food porte la chaîne vide plutôt que NULL : la colonne garde sa
        // contrainte, et le code n'a pas à distinguer « pas de SKU » de « SKU
        // inconnu ». C'est `Kind` qui dit s'il faut la lire.
        builder.Property(i => i.Sku).HasMaxLength(64).IsRequired();

        // ── Restauration ────────────────────────────────────────────────────
        builder.Property(i => i.RestaurantId).IsRequired();
        builder.Property(i => i.MenuItemId).IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(500);

        builder.Property(i => i.UnitBaseAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.Quantity).IsRequired();

        builder.HasMany(i => i.Options)
            .WithOne()
            .HasForeignKey("CartItemId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Options).UsePropertyAccessMode(PropertyAccessMode.Field);

        // ═════════════════════════════════════════════════════════════════════
        // INDEX UNIQUE FILTRÉ SUR LA SEULE MARCHANDISE.
        //
        // Il était `("CartId","OfferId") UNIQUE` sans filtre. Toutes les lignes
        // food portent `OfferId = Guid.Empty` : le SECOND plat ajouté à un panier
        // aurait violé cette contrainte, et le client aurait reçu une erreur de
        // base de données en cliquant sur « ajouter ».
        //
        // Le filtre conserve la garantie là où elle a un sens — une offre ne
        // figure qu'une fois, ses ajouts successifs augmentent la quantité — et la
        // retire là où elle n'en a pas. L'unicité d'une ligne food repose sur la
        // combinaison plat + options, que `CartItem.MatchesFood` vérifie en
        // mémoire : elle ne s'exprime pas en une colonne.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex("CartId", "OfferId")
            .IsUnique()
            .HasFilter("\"Kind\" = 'Goods'");
    }
}

internal sealed class CartItemOptionConfiguration : IEntityTypeConfiguration<CartItemOption>
{
    public void Configure(EntityTypeBuilder<CartItemOption> builder)
    {
        builder.ToTable("cart_item_options");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.OptionGroupId).IsRequired();
        builder.Property(o => o.OptionId).IsRequired();

        builder.HasIndex("CartItemId");
    }
}
