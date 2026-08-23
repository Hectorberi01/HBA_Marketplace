using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Orders.Domain.Orders.SellerOrders;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Orders.Infrastructure.Persistence.Configurations;

internal sealed class SellerOrderConfiguration : IEntityTypeConfiguration<SellerOrder>
{
    public void Configure(EntityTypeBuilder<SellerOrder> builder)
    {
        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — ET ICI IL EST RÉELLEMENT ÉVALUÉ.
        //
        // L'ENCADRÉ DE `InventoryItem.StockVersion` DÉCRIT LE PIÈGE : une
        // mutation qui n'écrit que des lignes ENFANTS n'émet aucun
        // `UPDATE` sur le parent, donc le jeton `xmin` n'entre dans la clause
        // `WHERE` d'aucune requête, donc le verrou est TOTALEMENT INERTE — tout
        // en restant parfaitement visible dans la configuration, ce qui est le
        // pire cas pour une relecture.
        //
        // CET AGRÉGAT N'EST PAS CONCERNÉ, ET POUR UNE RAISON STRUCTURELLE.
        //
        // Ses lignes sont figées à la création : `SellerOrderLine` n'expose aucun
        // mutateur, et aucune des six transitions n'y touche. Chacune écrit le
        // statut ET un horodatage sur la ligne parente — donc chacune émet un
        // `UPDATE seller_orders … WHERE "Id" = … AND xmin = …`. Aucun compteur à
        // la `StockVersion` n'est nécessaire.
        //
        // RÈGLE À TENIR : le jour où quelqu'un ajoute une transition qui ne
        // mute qu'une ligne enfant — un statut par ligne, une quantité honorée
        // partielle — il faudra ce compteur, ou le verrou redeviendra décoratif.
        //
        // Ce qu'il protège concrètement : deux membres d'une même équipe vendeur
        // sur deux écrans, l'un qui confirme pendant que l'autre refuse. Sans
        // lui, la seconde écriture écraserait la première en silence, et une
        // commande refusée redeviendrait confirmée sans que rien ne le dise.
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.ToTable("seller_orders");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new SellerOrderId(value))
            .ValueGeneratedNever();

        // PAS DE CLÉ ÉTRANGÈRE VERS `orders`, ET C'EST DÉLIBÉRÉ.
        //
        // `SellerOrder` est un AGRÉGAT, pas un enfant de `Order` : une relation EF
        // le ferait charger sous la commande et salir `orders` à chaque geste d'un
        // vendeur — voir l'encadré du verrou ci-dessus, et `ISellerOrderRepository`.
        // La règle du dépôt est la même entre agrégats : on référence par
        // identifiant, on ne navigue pas.
        //
        // Le prix est réel et assumé : rien en base n'empêche une part orpheline si
        // une commande était supprimée. Aucune ne l'est jamais — les commandes
        // s'annulent, elles ne se suppriment pas.
        builder.Property(s => s.OrderId).IsRequired();
        builder.Property(s => s.SellerId).IsRequired();
        builder.Property(s => s.BuyerId).IsRequired();
        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();

        // Stocké en TEXTE, comme `Order.Status` : une commande vendeur se relit en
        // base pendant les incidents, et « ReadyForPickup » s'y comprend là où
        // « 3 » demande de retrouver l'énumération. C'est aussi ce qui protège
        // d'un renumérotage de l'énumération.
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.ConfirmedAtUtc);
        builder.Property(s => s.PreparingAtUtc);
        builder.Property(s => s.ReadyForPickupAtUtc);
        builder.Property(s => s.HandedOverAtUtc);
        builder.Property(s => s.RefusedAtUtc);

        // 500 comme `CancellationReason` et `ReviewReason` : c'est la même sorte
        // de texte, écrit par un humain et relu par un humain.
        builder.Property(s => s.RefusalReason).HasMaxLength(500);

        // `Restrict` (§8) — c'est ce que le vendeur a reçu l'ordre d'expédier.
        // Une commande vendeur sans lignes ne dit plus rien de ce qui a été demandé,
        // donc rien de ce qui aurait dû partir.
        builder.HasMany(s => s.Lines)
            .WithOne()
            .HasForeignKey("SellerOrderId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(s => s.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Propriétés CALCULÉES, dérivées des lignes : sans ces Ignore, EF
        // réclamerait des colonnes pour des valeurs qui n'en ont pas.
        builder.Ignore(s => s.ItemCount);
        builder.Ignore(s => s.Amount);
        builder.Ignore(s => s.IsOpen);
        builder.Ignore(s => s.DomainEvents);

        // ═════════════════════════════════════════════════════════════════════
        // UN VENDEUR, UNE PART PAR COMMANDE — ET C'EST LA BASE QUI LE DIT.
        //
        // La confirmation arrive par Kafka, qui livre AU MOINS une fois. La
        // relecture applicative de `ConfirmOrderPaymentCommandHandler` traite le
        // rejeu ORDINAIRE ; elle ne voit pas deux messages traités EN PARALLÈLE,
        // qui répondent tous deux « cette commande n'est pas découpée » avant que
        // l'un ait écrit.
        //
        // Sans cet index, le vendeur verrait la même commande deux fois dans son
        // carnet, pour un montant doublé — c'est-à-dire une erreur sur ce qu'il
        // croit avoir vendu, découverte au moment d'être payé. L'index ferme la
        // course du bon côté : la seconde insertion échoue, le message est
        // rejoué, et le second passage trouve les parts.
        //
        // C'est exactement la construction de `order_return_settlements` et de
        // `UnicitePanierParCommande`.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(s => new { s.OrderId, s.SellerId }).IsUnique();

        // Le carnet du vendeur, filtré par état : c'est l'écran de travail
        // d'`ORDER_MANAGER`, et il se lit à chaque ouverture de la console.
        builder.HasIndex(s => new { s.SellerId, s.Status });
    }
}

internal sealed class SellerOrderLineConfiguration : IEntityTypeConfiguration<SellerOrderLine>
{
    public void Configure(EntityTypeBuilder<SellerOrderLine> builder)
    {
        builder.ToTable("seller_order_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.OrderLineId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();

        // NON NULL MAIS POSSIBLEMENT VIDE, comme `OrderLine.Sku` dont elle est
        // la copie. Distinguer « pas de SKU » de « SKU inconnu » n'apporterait rien.
        builder.Property(l => l.Sku).HasMaxLength(64).IsRequired();

        builder.Property(l => l.ShipFromLocationId).IsRequired();
        builder.Property(l => l.Quantity).IsRequired();
        builder.Property(l => l.UnitPaidAmount).HasColumnType("numeric(18,2)").IsRequired();

        builder.Ignore(l => l.LineTotal);

        builder.HasIndex("SellerOrderId");

        // LA LIGNE D'ORIGINE EST INDEXÉE, ET CE N'EST PAS DÉCORATIF.
        //
        // C'est par `OrderLineId` qu'un retour se rapproche d'une part vendeur —
        // `OrderReturnSettlementLine` désigne la LIGNE et non le produit, parce
        // qu'une même référence peut figurer deux fois sur une commande. Sans cet
        // index, ce rapprochement balaierait la table.
        builder.HasIndex(l => l.OrderLineId);
    }
}
