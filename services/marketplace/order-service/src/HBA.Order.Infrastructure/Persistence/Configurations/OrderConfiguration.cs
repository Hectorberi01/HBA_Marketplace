using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Orders.Domain.Orders;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Orders.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {

        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — la commande est mutée par la saga (paiement, expédition, livraison) et
        // par l'acheteur (annulation). Deux transitions concurrentes s'écraseraient.
        //
        // (`UsePostgresRowVersion` — l'API Npgsql `UseXminAsConcurrencyToken` est dépréciée
        //  et casse la build en « warnings = errors » ; notre extension fait exactement
        //  ce qu'elle faisait. Voir ConcurrencyTokenExtensions.)
        //
        // `xmin` est une colonne SYSTÈME de PostgreSQL : elle existe déjà sur chaque
        // ligne et porte le numéro de la transaction qui l'a écrite en dernier. On ne
        // l'ajoute pas, on la LIT. Rien à changer dans le modèle de domaine.
        //
        // EF l'inclut désormais dans la clause WHERE de chaque UPDATE. Si une autre
        // transaction a modifié la ligne entre-temps, l'UPDATE touche 0 ligne et EF
        // lève `DbUpdateConcurrencyException` — traduite en 409 (voir
        // ServiceExceptionMiddleware).
        //
        // AUCUN RETRY AUTOMATIQUE, ET C'EST DÉLIBÉRÉ.
        //
        // ModuleDbContext dispatche les événements de domaine AVANT
        // base.SaveChangesAsync, et draine les événements d'intégration vers l'outbox.
        // Rejouer la commande dans le MÊME scope re-dispatcherait ces événements et
        // dupliquerait les messages d'outbox. On échoue donc franchement en 409 ; le
        // client rejoue avec une requête neuve (les PSP le font d'eux-mêmes sur leurs
        // webhooks).
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.HorodateLesModifications();

        // ═════════════════════════════════════════════════════════════════════
        // UNE COMMANDE PAYÉE PORTE SON PAIEMENT — EN BASE, PAS SEULEMENT EN MÉMOIRE.
        //
        // `Order.MarkPaid` pose `PaymentId` ET `Status = Paid` dans le même geste,
        // et rien ne remet jamais `PaymentId` à nul. La colonne est nullable — une
        // commande non payée n'a pas de paiement, et c'est correct — mais RIEN en
        // base ne liait les deux : une écriture directe, un correctif SQL, un
        // futur chemin de code pouvaient poser `Paid` sans paiement. La commande
        // apparaît alors payée à l'acheteur, au vendeur et au tableau de bord,
        // et il n'existe aucun identifiant pour la rapprocher d'un encaissement.
        //
        // QUATRE STATUTS, PAS UN SEUL.
        //
        // `Paid` est le premier, pas le dernier : `Confirmed`, `Delivered` et
        // `UnderReview` ne s'atteignent QUE depuis `Paid` (voir les gardes de
        // transition d'`Order`). Ne contraindre que `Paid` laisserait passer une
        // commande livrée sans paiement, ce qui est le même défaut un cran plus
        // loin.
        //
        // `Cancelled` et `Failed` en sont exclus À DESSEIN : on annule aussi bien
        // AVANT le paiement (panier abandonné, stock indisponible) qu'APRÈS
        // (arbitrage). Les y inclure rejetterait des annulations légitimes.
        //
        // AJOUTER UN ÉTAT POST-PAIEMENT À `OrderStatus` SANS L'AJOUTER ICI LE
        // LAISSE HORS CONTRAINTE — silencieusement. C'est le même piège que les
        // index partiels de `deliveries`, et il n'a pas de parade automatique :
        // la liste est écrite en toutes lettres pour qu'on la relise.
        //
        // Le statut est stocké en TEXTE (`HasConversion<string>` plus bas), donc
        // la contrainte se lit telle quelle en SQL.
        // ═════════════════════════════════════════════════════════════════════
        builder.ToTable("orders", t => t.HasCheckConstraint(
            "ck_orders_paid_requires_payment",
            "\"Status\" NOT IN ('Paid', 'Confirmed', 'Delivered', 'UnderReview') "
            + "OR \"PaymentId\" IS NOT NULL"));

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new OrderId(value))
            .ValueGeneratedNever();

        builder.Property(o => o.BuyerId).IsRequired();
        builder.Property(o => o.CartId).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();

        // Code promo figé au checkout (snapshot). Nullable : la plupart des commandes
        // n'en ont pas. C'est cette colonne qui permet, à la confirmation, de savoir
        // quel coupon décompter.
        builder.Property(o => o.PromotionCode).HasMaxLength(64);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.CreatedAtUtc).IsRequired();
        builder.Property(o => o.Subtotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.TotalSellerDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.TotalPlatformDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.ShippingFee).HasColumnType("numeric(18,2)").IsRequired().HasDefaultValue(0m);
        builder.Property(o => o.GrandTotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.PaymentId);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);

        // ═════════════════════════════════════════════════════════════════════
        // L'ARBITRAGE : POURQUOI, ET DEPUIS QUAND.
        //
        // COLONNE DISTINCTE DE `CancellationReason`, ET IL LE FAUT.
        //
        // Une commande en arbitrage n'est pas annulée : elle est payée, son stock
        // est décrémenté, et l'exploitation décidera de la relancer ou de la
        // retourner. Partager la colonne ferait afficher un motif d'annulation
        // sur une vente vivante — et, si l'arbitrage conclut au remboursement,
        // écraserait la CAUSE (« course annulée ») par la DÉCISION.
        //
        // Le statut, lui, n'a besoin d'aucune migration de type : il est stocké en
        // TEXTE (`HasConversion<string>` ci-dessus), et « UnderReview » tient
        // largement dans les 20 caractères.
        // ═════════════════════════════════════════════════════════════════════
        builder.Property(o => o.ReviewReason).HasMaxLength(500);
        builder.Property(o => o.UnderReviewSinceUtc);

        // Adresse de livraison figée (snapshot, toutes colonnes nullables).
        builder.Property(o => o.ShipToLabel).HasMaxLength(60);
        builder.Property(o => o.ShipToRecipient).HasMaxLength(120);
        builder.Property(o => o.ShipToPhone).HasMaxLength(20);
        builder.Property(o => o.ShipToCommuneCode).HasMaxLength(40);
        builder.Property(o => o.ShipToQuartier).HasMaxLength(120);
        builder.Property(o => o.ShipToLandmark).HasMaxLength(200);
        builder.Property(o => o.ShipToLine1).HasMaxLength(200);
        builder.Property(o => o.ShipToCountryCode).HasMaxLength(2);
        builder.Property(o => o.ShipToLatitude);
        builder.Property(o => o.ShipToLongitude);

        // Résolus depuis BeninGeography à l'affichage : rien à stocker.
        builder.Ignore(o => o.ShipToCommuneName);
        builder.Ignore(o => o.HasShipToCoordinates);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // Correction d'une affirmation antérieure (voir doc 22) : SANS IsRequired(), EF
        // ne sévrait PAS pour autant. Le `OnDelete(DeleteBehavior.Cascade)` ci-dessous
        // gouverne aussi le sort des ORPHELINS — avec Cascade, un enfant retiré de la
        // collection est SUPPRIMÉ, pas mis à NULL. Les données de production l'ont confirmé.
        //
        // Alors pourquoi IsRequired() ? Pour trois raisons plus modestes et plus sûres :
        //
        //   1. Cette clé étrangère est RÉELLEMENT obligatoire — un enfant sans parent n'a
        //      aucun sens métier. Le modèle le déclarait facultatif. Un modèle qui ment
        //      finit toujours par produire du code qui se trompe.
        //
        //   2. La colonne était NULL-able en base, donc RIEN ne l'interdisait. Une ligne
        //      orpheline a d'ailleurs été trouvée en production (message_reactions) : on
        //      ignore ce qui l'a créée, et c'est précisément le problème. NOT NULL l'aurait
        //      refusée, quelle que soit sa provenance.
        //
        //   3. Sans ça, le comportement dépend d'un réglage FRAGILE : retirer le
        //      `OnDelete(Cascade)` — geste anodin en apparence — ferait réellement basculer
        //      cette relation en sévérance. Avec IsRequired() ET NOT NULL, c'est impossible.
        //
        // ET C'EST DÉSORMAIS `Restrict`, PAS `Cascade` (§8).
        //
        // Supprimer une commande emportait ce qui a été VENDU — les lignes, et par
        // ricochet leurs options de repas. L'en-tête porte le total ; les lignes
        // portent le contenu. Une commande sans lignes est une somme sans objet :
        // rien ne permet plus de dire ce que le client a reçu, ni au vendeur ce
        // qu'il a expédié.
        //
        // Le point (3) ci-dessus est devenu le point principal : il annonçait que
        // retirer `Cascade` ferait basculer la relation en SÉVÉRANCE. Avec
        // `IsRequired()` et le NOT NULL en base, EF lève au lieu de sévrer.
        // Vérifié avant de toucher : `_lines` n'est jamais muté par retrait — une
        // commande ne perd pas de ligne, elle s'annule.
        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey("OrderId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // ═════════════════════════════════════════════════════════════════════
        // CE QUE LES RETOURS ONT RETIRÉ À CETTE COMMANDE (ISSUE-014).
        //
        // Enfants de l'agrégat, chargés avec lui : `GetOrderReturnContextAsync`
        // les lit dans la MÊME lecture que les lignes. Une projection à part
        // aurait rouvert l'écart qu'on ferme ici — un retour enregistré, une
        // commande qui l'ignore encore, et un second remboursement validé entre
        // les deux.
        // ═════════════════════════════════════════════════════════════════════
        // `Restrict` — C'EST DE L'ARGENT REPRIS AU VENDEUR (§8).
        //
        // Ces lignes disent ce que les retours ont retiré à la commande. Les
        // effacer avec la commande supprimerait la seule explication du delta entre
        // ce qui a été facturé et ce qui a été réglé.
        builder.HasMany(o => o.ReturnSettlements)
            .WithOne()
            .HasForeignKey("OrderId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(o => o.ReturnSettlements).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Somme des dossiers, recalculée à chaque lecture. Sans cet Ignore, EF
        // réclamerait une colonne pour une valeur qui n'en a pas — et une colonne
        // cumulative aurait exigé sa propre idempotence.
        builder.Ignore(o => o.RefundedAmount);

        builder.HasIndex(o => new { o.BuyerId, o.Status });

        // ═════════════════════════════════════════════════════════════════════
        // UN PANIER NE PRODUIT QU'UNE COMMANDE — ET C'EST LA BASE QUI LE DIT.
        //
        // `POST /api/orders` n'avait aucune idempotence, et `CartId` n'avait ni
        // contrainte d'unicité ni même un index : un double-clic créait DEUX
        // commandes sur le même panier, donc deux paiements à réclamer.
        //
        // La relecture préalable de `GetByCartAsync` traite le cas courant. Elle
        // ne voit PAS deux requêtes simultanées : les deux lisent « aucune
        // commande » avant que l'une ait écrit. Seul cet index ferme cette course
        // — et il la ferme du bon côté, la seconde insertion échouant au lieu
        // d'encaisser deux fois.
        //
        // Il sert aussi la lecture : sans lui, `GetByCartAsync` balaierait la
        // table à chaque passage en commande.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(o => o.CartId).IsUnique();

        // Propriétés CALCULÉES, dérivées des lignes. Le dépôt applique cette règle
        // partout (voir `Ignore(l => l.LineTotal)`) : sans elle, EF réclame une
        // colonne pour une valeur qui n'en a pas.
        // Le devis de course déjà payé. Longueur alignée sur l'identifiant public
        // rendu par le module Delivery.
        builder.Property(o => o.DeliveryQuoteId).HasMaxLength(64);

        builder.Ignore(o => o.Kind);
        builder.Ignore(o => o.RestaurantId);
        builder.Ignore(o => o.HasShippingAddress);

        builder.Ignore(o => o.DomainEvents);
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        // Le discriminant, stocké en texte : une commande se relit en base pendant
        // les incidents, et « Food » s'y comprend là où « 1 » demande de retrouver
        // l'énumération.
        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(10).IsRequired();

        // ── Marchandise : vides pour une ligne de repas ─────────────────────
        builder.Property(l => l.OfferId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();
        builder.Property(l => l.SellerId).IsRequired();

        // NON NULL MAIS POSSIBLEMENT VIDE. Une ligne de repas porte la chaîne
        // vide : la colonne garde sa contrainte, et c'est `Kind` qui dit s'il faut
        // la lire. Distinguer « pas de SKU » de « SKU inconnu » n'apporterait rien.
        builder.Property(l => l.Sku).HasMaxLength(64).IsRequired();
        builder.Property(l => l.ShipFromLocationId).IsRequired();

        // ── Restauration ────────────────────────────────────────────────────
        builder.Property(l => l.RestaurantId).IsRequired();
        builder.Property(l => l.MenuItemId).IsRequired();
        builder.Property(l => l.Notes).HasMaxLength(500);

        // `Restrict` — LE SECOND NIVEAU DE LA CHAÎNE
        // `orders → order_lines → order_line_options`.
        //
        // Les options sont ce qui distingue deux lignes identiques : « sans piment »,
        // « supplément fromage ». C'est précisément ce qu'un client conteste quand il
        // dit ne pas avoir reçu ce qu'il avait commandé.
        builder.HasMany(l => l.Options)
            .WithOne()
            .HasForeignKey("OrderLineId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(l => l.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(l => l.Quantity).IsRequired();
        builder.Property(l => l.UnitBasePrice).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(l => l.SellerDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(l => l.PlatformDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(l => l.FinalUnitPrice).HasColumnType("numeric(18,2)").IsRequired();

        // Propriétés CALCULÉES : sans ces Ignore, EF réclamerait des colonnes.
        builder.Ignore(l => l.LineTotal);
        builder.Ignore(l => l.RequiresStockReservation);

        builder.HasIndex("OrderId");
        builder.HasIndex(l => l.SellerId);

        // L'ADAPTATEUR VERS FOOD CHERCHE PAR RESTAURANT.
        //
        // Index partiel : les lignes de marchandise portent toutes
        // `RestaurantId = '00000000-…'` et n'ont rien à faire ici. Un index plein
        // les indexerait toutes sous la même clé — le pire cas pour un B-tree.
        builder.HasIndex(l => l.RestaurantId).HasFilter("\"Kind\" = 'Food'");
    }
}

internal sealed class OrderLineOptionConfiguration : IEntityTypeConfiguration<OrderLineOption>
{
    public void Configure(EntityTypeBuilder<OrderLineOption> builder)
    {
        builder.ToTable("order_line_options");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.OptionGroupId).IsRequired();
        builder.Property(o => o.OptionId).IsRequired();

        builder.HasIndex("OrderLineId");
    }
}

internal sealed class OrderReturnSettlementConfiguration : IEntityTypeConfiguration<OrderReturnSettlement>
{
    public void Configure(EntityTypeBuilder<OrderReturnSettlement> builder)
    {
        builder.ToTable("order_return_settlements");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.ReturnRequestId).IsRequired();
        builder.Property(s => s.RefundedAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(s => s.RecordedAtUtc).IsRequired();
        builder.Property(s => s.LastSeenAtUtc).IsRequired();

        builder.HasMany(s => s.Lines)
            .WithOne()
            .HasForeignKey("OrderReturnSettlementId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(s => s.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // UN DOSSIER, UNE LIGNE — ET C'EST LA BASE QUI LE DIT.
        //
        // Le rapprochement en mémoire (« ce dossier est-il déjà connu ? ») traite
        // le cas courant. Il ne voit pas deux messages du même dossier traités en
        // parallèle : les deux lisent « inconnu » avant que l'un ait écrit, et la
        // commande compterait deux fois la même marchandise rendue. Cet index
        // ferme la course du bon côté — la seconde insertion échoue, le message
        // est rejoué, et le second passage trouve le dossier.
        builder.HasIndex("OrderId", "ReturnRequestId").IsUnique();
    }
}

internal sealed class OrderReturnSettlementLineConfiguration : IEntityTypeConfiguration<OrderReturnSettlementLine>
{
    public void Configure(EntityTypeBuilder<OrderReturnSettlementLine> builder)
    {
        builder.ToTable("order_return_settlement_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.OrderItemId).IsRequired();
        builder.Property(l => l.Quantity).IsRequired();

        builder.HasIndex("OrderReturnSettlementId");
    }
}
