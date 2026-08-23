using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Infrastructure.Persistence.Configurations;

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.OrderId).IsRequired();
        builder.Property(r => r.Quantity).IsRequired();
        builder.Property(r => r.ExpiresAtUtc).IsRequired();

        // ═════════════════════════════════════════════════════════════════════
        // LE STATUT (ISSUE-045).
        //
        // EN TEXTE, PAS EN ENTIER. C'est la convention du dépôt (voir
        // `ReturnRequestConfiguration`), et elle vaut surtout pour ce qu'elle
        // permet : l'index unique partiel ci-dessous filtre sur
        // `"Status" = 'Active'`. Un entier rendrait ce filtre illisible — et
        // réordonner l'énumération changerait silencieusement ce qu'il désigne.
        //
        // VALEUR PAR DÉFAUT `Active`, POUR LES LIGNES DÉJÀ EN BASE.
        //
        // `stock_reservations` contient des réservations écrites avant l'existence
        // du statut. Elles sont, par construction, EN COURS — les libérées et les
        // confirmées avaient été SUPPRIMÉES. `Active` est donc la seule valeur
        // juste, et un défaut en base la garantit même aux insertions qui ne
        // passeraient pas par EF. Même dispositif que `StockVersion`.
        // ═════════════════════════════════════════════════════════════════════
        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(ReservationStatus.Active);

        // `ConfirmedAtUtc`, `ReleasedAtUtc` et `ExpiredAtUtc` sont laissés à la
        // convention : `DateTime?` → colonne nullable, ce qui est exactement ce
        // qu'on veut. Une transition qui n'a pas eu lieu n'a pas d'heure, et les
        // lignes antérieures à cette migration n'en auront jamais — voir l'encadré
        // de `StockReservation` : un historique qui invente une date ment.

        // La relation et la FK ombre « InventoryItemId » sont définies par le
        // parent (InventoryItemConfiguration) ; EF crée l'index de FK.
        builder.HasIndex(r => r.OrderId);

        // ═════════════════════════════════════════════════════════════════════
        // UNE SEULE RÉSERVATION EN COURS PAR (ARTICLE, COMMANDE) — ISSUE-075.
        //
        // LA VÉRIFICATION APPLICATIVE NE SUFFIT PAS.
        //
        // `InventoryItem.Reserve` cherche une réservation `Active` pour la commande
        // et POSE la quantité au lieu d'en ajouter une seconde. Cela traite le cas
        // courant — un rejeu après échéance dépassée. Cela ne voit PAS deux rejeux
        // simultanés : les deux lisent « aucune réservation active » avant que l'un
        // ait écrit. Seul cet index ferme la course, et il la ferme du bon côté —
        // la seconde insertion échoue au lieu d'immobiliser le stock deux fois.
        //
        // PARTIEL, ET C'EST TOUTE LA DIFFICULTÉ.
        //
        // Un index unique SEC sur (InventoryItemId, OrderId) serait faux. Une même
        // commande peut légitimement laisser PLUSIEURS lignes historiques sur le
        // même article : réservée puis libérée (paiement refusé), puis réservée à
        // nouveau après une seconde tentative de paiement — trois lignes, dont une
        // seule est `Active`. Depuis qu'on ne supprime plus rien (ISSUE-045),
        // l'historique s'accumule et un index sec refuserait la reprise de
        // paiement la plus banale.
        //
        // L'unicité ne porte donc que sur ce qui doit vraiment être unique : ce qui
        // IMMOBILISE du stock à un instant donné.
        //
        // CE QU'IL NE COUVRE PAS : il n'empêche pas deux réservations actives de
        // la même commande sur DEUX articles différents (deux emplacements, ou deux
        // SKU) — c'est légitime, une commande se sert à plusieurs endroits.
        //
        // Le filtre nomme la colonne en PascalCase entre guillemets doubles : ce
        // dépôt n'applique aucune convention snake_case aux colonnes (seules
        // `sku` et les tables le sont, explicitement).
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex("InventoryItemId", nameof(StockReservation.OrderId))
            .IsUnique()
            .HasDatabaseName("ux_stock_reservations_active_order")
            .HasFilter("\"Status\" = 'Active'");

        // ═════════════════════════════════════════════════════════════════════
        // LE BALAYAGE DES RÉSERVATIONS EXPIRÉES N'AVAIT AUCUN INDEX (§4).
        //
        // `ExpireStockReservationsWorker` tourne en permanence et pose la même
        // question à chaque tour : « quelles réservations `Active` ont dépassé leur
        // échéance ? ». Sans index, c'est un balayage complet d'une table qui NE
        // DÉCROÎT JAMAIS — depuis ISSUE-045, on ne supprime plus les lignes, on les
        // marque. Le coût du balayage grandit donc indéfiniment, pour trouver à
        // chaque tour une poignée de lignes.
        //
        // INDEX PARTIEL, COMME SON VOISIN. Seules les réservations `Active`
        // peuvent expirer : une `Confirmed`, une `Released` ou une déjà `Expired`
        // ne sera jamais rendue par cette requête. Un index complet indexerait
        // toute l'histoire du stock pour servir la frange vivante — et c'est
        // précisément le reproche fait à l'ancien index d'outbox.
        //
        // Le filtre nomme la colonne en PascalCase entre guillemets doubles, comme
        // celui du dessus : ce dépôt n'applique aucune convention snake_case aux
        // colonnes.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(r => r.ExpiresAtUtc)
            .HasDatabaseName("ix_stock_reservations_expiring")
            .HasFilter("\"Status\" = 'Active'");
    }
}
