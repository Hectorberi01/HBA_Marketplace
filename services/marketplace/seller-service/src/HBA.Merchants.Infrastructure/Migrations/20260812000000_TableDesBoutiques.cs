using System;
using HBA.Merchants.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HBA.Merchants.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES TABLES DE LA BOUTIQUE — CELLES QUI N'AVAIENT JAMAIS ÉTÉ CRÉÉES.
    ///
    /// LE DOMAINE, LA CONFIGURATION ET LE DÉPÔT EXISTAIENT. PAS LA TABLE.
    ///
    /// `Store`, `StoreConfiguration`, `StoreRepository`, `StoreCommands`,
    /// `StoreQueries` : tout était écrit, et `SellersDbContext` exposait bien
    /// `DbSet&lt;Store&gt;`. Mais aucune migration ne créait `sellers.stores`, et
    /// l'instantané du modèle ne connaissait que `Seller`, `KybDocument` et
    /// l'outbox — preuve que `dotnet ef migrations add` n'a jamais été relancé
    /// après l'ajout de la configuration.
    ///
    /// Le code compilait. Les tests portant sur le domaine passaient. Rien ne
    /// pouvait le signaler avant qu'une requête n'atteigne PostgreSQL.
    ///
    /// ET C'EST LA REPRISE QUI A FINI PAR LE DIRE, DEUX MIGRATIONS PLUS LOIN.
    ///
    /// `20260813000000_RepriseStoresFromSellers` interroge `sellers.stores` pour
    /// savoir si la reprise a déjà eu lieu. Sur une base neuve :
    ///
    ///     42P01: relation "sellers.stores" does not exist
    ///
    /// L'erreur désignait la reprise, alors que la reprise était juste : c'est la
    /// table qui manquait. D'où la position de cette migration — AVANT elle.
    ///
    /// ÉCRITE À LA MAIN, DONC À CONFRONTER AU MODÈLE.
    ///
    /// Elle reproduit `StoreConfiguration` colonne par colonne. L'instantané a
    /// été complété en conséquence ; sans cela, le prochain
    /// `dotnet ef migrations add` recréerait ces mêmes tables — exactement le
    /// doublon qui a fait échouer food-service.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(SellersDbContext))]
    [Migration("20260812000000_TableDesBoutiques")]
    public partial class TableDesBoutiques : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stores",
                schema: "sellers",
                columns: table => new
                {
                    // ValueGeneratedNever : l'identifiant vient du domaine
                    // (`StoreId.New()`), et la reprise en dépend — elle donne à
                    // la boutique l'identifiant de son vendeur.
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),

                    // Simple uuid, sans clé étrangère : le lieu vit dans le schéma
                    // `inventory`, qui migre séparément. Une contrainte inter-schémas
                    // imposerait un ordre entre deux services indépendants.
                    FulfillmentLocationId = table.Column<Guid>(type: "uuid", nullable: true),

                    // `BusinessContact`, type possédé : deux colonnes dans CETTE
                    // table, pas une table à part.
                    ContactPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),

                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stores", x => x.Id);
                });

            // DES LIGNES, PAS DU JSON — voir `StoreConfiguration`.
            //
            // « Quelles boutiques sont ouvertes maintenant ? » se répond par un
            // index. En jsonb, la même question devient une lecture complète de
            // la table suivie d'un tri en mémoire.
            migrationBuilder.CreateTable(
                name: "store_opening_hours",
                schema: "sellers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_opening_hours", x => x.Id);

                    // Cascade : un créneau n'a aucun sens sans sa boutique.
                    table.ForeignKey(
                        name: "FK_store_opening_hours_stores_StoreId",
                        column: x => x.StoreId,
                        principalSchema: "sellers",
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Le multi-boutiques se lit par vendeur : c'est l'index qui porte
            // l'écran « mes boutiques » et toute la cascade de fermeture.
            migrationBuilder.CreateIndex(
                name: "IX_stores_SellerId",
                schema: "sellers",
                table: "stores",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_store_opening_hours_StoreId_Day",
                schema: "sellers",
                table: "store_opening_hours",
                columns: new[] { "StoreId", "Day" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "store_opening_hours", schema: "sellers");
            migrationBuilder.DropTable(name: "stores", schema: "sellers");
        }
    }
}
