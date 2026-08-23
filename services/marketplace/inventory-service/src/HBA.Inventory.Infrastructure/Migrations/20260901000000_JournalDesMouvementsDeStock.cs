using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Inventory.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL DES MOUVEMENTS — <c>inventory.stock_movements</c> (ISSUE-044).
    ///
    /// RIEN NE GARDAIT TRACE D'UN AJUSTEMENT DE STOCK.
    ///
    /// <c>AdjustOnHand(int delta)</c> ne prenait ni acteur ni motif ; sa commande
    /// portait deux champs ; <c>InventoryItem</c> n'a même pas de
    /// <c>UpdatedOnUtc</c>. Un stock passant de 400 à 12 ne laissait aucune trace
    /// de qui, quand, ni pourquoi.
    ///
    /// Deux permissions le promettaient pourtant : <c>STOCK_MOVEMENT_VIEW</c> et
    /// <c>INVENTORY_TRANSFER</c>, toutes deux attribuées au rôle
    /// <c>INVENTORY_MANAGER</c>, dont la description dit « Stocks, ajustements,
    /// transferts ». Aucune des deux ne gardait la moindre route.
    ///
    /// AUCUNE CLÉ ÉTRANGÈRE VERS <c>inventory_items</c>.
    ///
    /// Le journal doit survivre à la disparition de l'article : c'est justement
    /// quand une ligne disparaît qu'on veut savoir ce qui lui est arrivé.
    ///
    /// AUCUNE REPRISE DE DONNÉES.
    ///
    /// Le journal commence ici, sur des articles qui ont déjà un stock. La somme
    /// des deltas ne vaudra donc jamais <c>OnHand</c> — c'est pour cela que chaque
    /// ligne porte <c>OnHandAfter</c>, le solde d'après le mouvement, qui rend la
    /// lecture utilisable dès la première ligne.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Inventory.Infrastructure.Persistence.InventoryDbContext))]
    [Migration("20260901000000_JournalDesMouvementsDeStock")]
    public partial class JournalDesMouvementsDeStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `CreateTable` ET NON `migrationBuilder.Sql`, ET CE N'EST PAS UN
            // DÉTAIL DE STYLE.
            //
            // `check-migrations.py` rejoue les migrations à sec et vérifie que
            // chaque table CONFIGURÉE est créée par l'une d'elles. Il ignore
            // délibérément le SQL brut — « analyser du SQL arbitraire dépasse ce
            // que ce script prétend faire ». Une table posée en `Sql(...)` passe
            // donc sous son radar : le contrôle a d'ailleurs refusé la première
            // version de cette migration, écrite ainsi.
            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false),
                    OnHandAfter = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                });

            // « Qu'est-il arrivé à CET article » — la lecture du vendeur.
            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_item",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "InventoryItemId", "OccurredOnUtc" });

            // « Qu'est-il arrivé à CETTE référence, tous lieux confondus » — quand
            // un SKU ne tombe pas juste et qu'on ne sait pas encore où chercher.
            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_sku",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "Sku", "OccurredOnUtc" });

            // Les deux moitiés d'un transfert se retrouvent par leur référence
            // commune. PARTIEL : la colonne est nulle sur la majorité des lignes,
            // et un index plein paierait un pointeur pour chaque vente.
            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_reference",
                schema: "inventory",
                table: "stock_movements",
                column: "Reference",
                filter: "\"Reference\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // IRRÉVERSIBLE EN PRATIQUE : le retour arrière efface le journal, et
            // rien ne le reconstitue.
            migrationBuilder.DropTable(name: "stock_movements", schema: "inventory");
        }
    }
}
