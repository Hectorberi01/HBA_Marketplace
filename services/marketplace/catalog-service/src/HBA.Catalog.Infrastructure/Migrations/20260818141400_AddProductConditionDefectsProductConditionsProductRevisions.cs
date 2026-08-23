using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Catalog.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE CORPS DE CETTE MIGRATION A ÉTÉ RÉÉCRIT À LA MAIN. L'INSTANTANÉ, NON.
    ///
    /// `dotnet ef` a produit le schéma correct — c'est la partie qu'il faut lui
    /// laisser, et le `CatalogDbContextModelSnapshot` reste exactement le sien.
    /// Mais l'ORDRE et le CHOIX des opérations qu'il avait générés auraient détruit
    /// les données de production. Trois défauts, du moins grave au pire :
    ///
    ///   1. Les `DropColumn` venaient AVANT la création de `product_revisions`.
    ///      Nom, description, catégorie et marque disparaissaient avant qu'aucune
    ///      ligne n'ait pu être déménagée.
    ///
    ///   2. `CreatedOnUtc` était RENOMMÉ en `UpdatedAtUtc`. Passe encore.
    ///
    ///   3. `CategoryId` était RENOMMÉ en `CurrentRevisionId`, et `BrandId` en
    ///      `StoreId`.
    ///
    /// Le troisième point est celui qui compte. EF apparie par TYPE : une colonne
    /// `uuid?` qui disparaît et une autre `uuid?` qui apparaît lui ressemblent à un
    /// renommage. Après application, chaque produit aurait porté l'identifiant de
    /// sa CATÉGORIE dans `CurrentRevisionId` — pointant une révision qui n'existe
    /// pas — et celui de sa MARQUE dans `StoreId`.
    ///
    /// Ces deux valeurs sont des uuid parfaitement formés. Aucune contrainte ne les
    /// refuse, aucun journal ne s'en plaint. `CurrentRevision` aurait levé au
    /// premier chargement, et l'on aurait cherché le défaut dans le dépôt.
    ///
    /// D'où l'ordre ci-dessous : on AJOUTE, on CRÉE, on DÉMÉNAGE, on VERROUILLE,
    /// et seulement alors on SUPPRIME.
    ///
    /// `gen_random_uuid()` demande PostgreSQL 13 ou plus (fonction native
    /// depuis cette version, extension `pgcrypto` avant).
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public partial class AddProductConditionDefectsProductConditionsProductRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─────────────────────────────────────────────────────────────────
            // 1. LES NOUVELLES COLONNES DE `products`, TOUTES NULLABLES D'ABORD.
            //
            // `CurrentRevisionId`, `CreatedAtUtc` et `UpdatedAtUtc` finiront NOT
            // NULL, mais ne peuvent pas l'être avant d'être remplies. Les créer
            // NOT NULL avec une valeur par défaut — ce que fait EF — laisserait
            // des `0001-01-01` et des uuid nuls partout où la reprise échouerait,
            // sans que rien ne le signale.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "catalog",
                table: "products",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<Guid>(
                name: "StoreId",
                schema: "catalog",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentRevisionId",
                schema: "catalog",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublishedRevisionId",
                schema: "catalog",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "catalog",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                schema: "catalog",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmittedAtUtc",
                schema: "catalog",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAtUtc",
                schema: "catalog",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAtUtc",
                schema: "catalog",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAtUtc",
                schema: "catalog",
                table: "products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionReason",
                schema: "catalog",
                table: "products",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            // ─────────────────────────────────────────────────────────────────
            // 2. LES TROIS TABLES (générées par EF, reprises telles quelles).
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "product_revisions",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ShortDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: true),
                    base_price = table.Column<long>(type: "bigint", nullable: false),
                    compare_at_price = table.Column<long>(type: "bigint", nullable: true),
                    cost_price = table.Column<long>(type: "bigint", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tax_included = table.Column<bool>(type: "boolean", nullable: false),
                    tax_rate = table.Column<int>(type: "integer", nullable: false),
                    attributes = table.Column<string>(type: "jsonb", nullable: false),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_revisions_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_conditions",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Grade = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    IsRefurbished = table.Column<bool>(type: "boolean", nullable: false),
                    HasOriginalPackaging = table.Column<bool>(type: "boolean", nullable: false),
                    HasOriginalAccessories = table.Column<bool>(type: "boolean", nullable: false),
                    FunctionalStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RefurbishedByType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RefurbishedBySellerId = table.Column<Guid>(type: "uuid", nullable: true),
                    refurbishment_operations = table.Column<List<string>>(type: "text[]", nullable: false),
                    BatteryHealthPercentage = table.Column<int>(type: "integer", nullable: true),
                    BatteryReplaced = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_conditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_conditions_product_revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalSchema: "catalog",
                        principalTable: "product_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_condition_defects",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConditionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_condition_defects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_condition_defects_product_conditions_ConditionId",
                        column: x => x.ConditionId,
                        principalSchema: "catalog",
                        principalTable: "product_conditions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ─────────────────────────────────────────────────────────────────
            // 3. LA REPRISE DES DONNÉES. C'est ce qu'aucun outil ne devine.
            // ─────────────────────────────────────────────────────────────────

            // Une révision par produit, en version 1.
            //
            // Le statut de la révision SUIT celui du produit. Une fiche en vente
            // doit ressortir avec une révision 'Published' : sinon
            // `PublishedRevisionId` pointerait une révision en brouillon, et
            // `Product.Publish` refuserait de republier après une dépublication.
            //
            // PRIX DE RÉFÉRENCE À 1, PAS À 0. Le domaine exige `basePrice > 0`
            // (§23) : un 0 rendrait chaque fiche reprise impossible à modifier, la
            // validation refusant le contenu qu'on vient de relire. 1 F est faux
            // aussi, mais VISIBLEMENT faux, et il laisse la fiche corrigeable. Le
            // vrai prix n'est pas perdu : il vit dans `product_offers` (D12).
            migrationBuilder.Sql(@"
INSERT INTO catalog.product_revisions (
    ""Id"", ""ProductId"", ""Version"", ""Status"",
    ""Name"", slug, ""ShortDescription"", ""Description"", ""Type"",
    ""CategoryId"", ""BrandId"",
    base_price, compare_at_price, cost_price, currency, tax_included, tax_rate,
    attributes, tags,
    ""CreatedAtUtc"", ""SubmittedAtUtc"", ""ReviewedAtUtc"", ""PublishedAtUtc"")
SELECT
    gen_random_uuid(),
    p.""Id"",
    1,
    CASE WHEN p.""Status"" = 'Active' THEN 'Published' ELSE 'Draft' END,
    p.""Name"",
    p.slug,
    NULL,
    COALESCE(p.""Description"", ''),
    'Physical',
    p.""CategoryId"",
    p.""BrandId"",
    1, NULL, NULL, 'XOF', true, 0,
    COALESCE(p.attributes, '{}'::jsonb),
    COALESCE(p.tags, ARRAY[]::text[]),
    p.""CreatedOnUtc"",
    NULL, NULL,
    CASE WHEN p.""Status"" = 'Active' THEN p.""CreatedOnUtc"" ELSE NULL END
FROM catalog.products p;");

            // Une condition « Neuf » par révision. La colonne est NOT NULL côté
            // révision : sans cette insertion, la contrainte de clé étrangère
            // refuserait chaque ligne créée juste au-dessus.
            migrationBuilder.Sql(@"
INSERT INTO catalog.product_conditions (
    ""Id"", ""RevisionId"", ""Type"", ""Grade"", ""Description"",
    ""IsUsed"", ""IsRefurbished"", ""HasOriginalPackaging"", ""HasOriginalAccessories"",
    ""FunctionalStatus"", ""RefurbishedByType"", ""RefurbishedBySellerId"",
    refurbishment_operations, ""BatteryHealthPercentage"", ""BatteryReplaced"")
SELECT
    gen_random_uuid(), r.""Id"", 'New', NULL, NULL,
    false, false, false, false,
    'FullyFunctional', NULL, NULL,
    ARRAY[]::text[], NULL, NULL
FROM catalog.product_revisions r;");

            // Raccrocher chaque produit à sa révision.
            //
            // `StoreId` RESTE NULL, ET C'EST VOULU. Aucune valeur n'est
            // déductible : rattacher au hasard une fiche à l'une des boutiques du
            // vendeur serait une erreur qui survivrait à ce fichier. La garde est
            // dans le domaine — `SubmitForReview` refuse une fiche sans boutique —
            // donc ces lignes restent lisibles et modifiables, et ne peuvent plus
            // avancer tant que personne ne les rattache.
            migrationBuilder.Sql(@"
UPDATE catalog.products p
SET ""CurrentRevisionId""   = r.""Id"",
    ""PublishedRevisionId"" = CASE WHEN p.""Status"" = 'Active' THEN r.""Id"" ELSE NULL END,
    ""CreatedAtUtc""        = p.""CreatedOnUtc"",
    ""UpdatedAtUtc""        = p.""CreatedOnUtc"",
    ""PublishedAtUtc""      = CASE WHEN p.""Status"" = 'Active'   THEN p.""CreatedOnUtc"" ELSE NULL END,
    ""ArchivedAtUtc""       = CASE WHEN p.""Status"" = 'Archived' THEN p.""CreatedOnUtc"" ELSE NULL END
FROM catalog.product_revisions r
WHERE r.""ProductId"" = p.""Id"";");

            // LE RENOMMAGE DU STATUT — LA LIGNE QUI REND LES PRODUITS VISIBLES.
            //
            // « Active » n'existe plus dans l'énumération. Oubliée, cette mise à
            // jour rend chaque produit en vente illisible pour EF, et la
            // comparaison littérale de `HBA.Catalog.Contracts.Grpc/ProductsGrpc.cs`
            // renverrait `IsVisible = false` sans lever d'erreur.
            migrationBuilder.Sql(
                "UPDATE catalog.products SET \"Status\" = 'Published' WHERE \"Status\" = 'Active';");

            // ─────────────────────────────────────────────────────────────────
            // 4. VERROUILLER CE QUI EST DÉSORMAIS REMPLI.
            //
            // Si la reprise a laissé une ligne derrière elle, ces trois ALTER
            // échouent et la transaction de migration est annulée. C'est le bon
            // comportement : mieux vaut une migration qui refuse d'aboutir qu'une
            // base à moitié reprise que personne ne remarque.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.AlterColumn<Guid>(
                name: "CurrentRevisionId",
                schema: "catalog",
                table: "products",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                schema: "catalog",
                table: "products",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                schema: "catalog",
                table: "products",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            // ─────────────────────────────────────────────────────────────────
            // 5. SEULEMENT MAINTENANT : SUPPRIMER L'ANCIEN.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.DropIndex(
                name: "IX_products_CategoryId",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_BrandId",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_slug",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(name: "Name", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "slug", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "Description", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "attributes", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "tags", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "CategoryId", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "BrandId", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "CreatedOnUtc", schema: "catalog", table: "products");

            // ─────────────────────────────────────────────────────────────────
            // 6. LES INDEX (générés par EF, repris tels quels).
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_products_StoreId",
                schema: "catalog",
                table: "products",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_products_PublishedRevisionId",
                schema: "catalog",
                table: "products",
                column: "PublishedRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_products_SellerId_Status",
                schema: "catalog",
                table: "products",
                columns: new[] { "SellerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_products_Status",
                schema: "catalog",
                table: "products",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_product_condition_defects_ConditionId",
                schema: "catalog",
                table: "product_condition_defects",
                column: "ConditionId");

            migrationBuilder.CreateIndex(
                name: "IX_product_conditions_RevisionId",
                schema: "catalog",
                table: "product_conditions",
                column: "RevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_conditions_Type",
                schema: "catalog",
                table: "product_conditions",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_product_revisions_BrandId",
                schema: "catalog",
                table: "product_revisions",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_product_revisions_CategoryId",
                schema: "catalog",
                table: "product_revisions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_product_revisions_ProductId_Version",
                schema: "catalog",
                table: "product_revisions",
                columns: new[] { "ProductId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_revisions_Status",
                schema: "catalog",
                table: "product_revisions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "ux_product_revisions_published_slug",
                schema: "catalog",
                table: "product_revisions",
                column: "slug",
                unique: true,
                filter: "\"Status\" = 'Published'");
        }

        /// <summary>
        /// CE RETOUR EN ARRIÈRE RECOPIE CE QU'IL PEUT, ET PERD LE RESTE.
        ///
        /// Il rend à `products` le contenu de sa révision COURANTE — donc pas
        /// forcément celui qu'elle avait avant la migration, si un vendeur a édité
        /// entre-temps — et remappe les statuts vers les trois anciens. Sont perdus
        /// sans retour : les révisions antérieures, les conditions commerciales,
        /// les défauts déclarés, les prix de référence et l'historique de
        /// validation.
        ///
        /// Un `Down` qui ne dirait rien de cela serait pire qu'un `Down` absent :
        /// on le lancerait en croyant revenir en arrière.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "catalog",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "slug",
                schema: "catalog",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "catalog",
                table: "products",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "attributes",
                schema: "catalog",
                table: "products",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<List<string>>(
                name: "tags",
                schema: "catalog",
                table: "products",
                type: "text[]",
                nullable: false,
                defaultValue: new List<string>());

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                schema: "catalog",
                table: "products",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<Guid>(
                name: "BrandId",
                schema: "catalog",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedOnUtc",
                schema: "catalog",
                table: "products",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.Sql(@"
UPDATE catalog.products p
SET ""Name""        = r.""Name"",
    slug           = r.slug,
    ""Description"" = r.""Description"",
    attributes     = r.attributes,
    tags           = r.tags,
    ""CategoryId""  = r.""CategoryId"",
    ""BrandId""     = r.""BrandId"",
    ""CreatedOnUtc"" = r.""CreatedAtUtc""
FROM catalog.product_revisions r
WHERE r.""Id"" = p.""CurrentRevisionId"";");

            migrationBuilder.Sql(@"
UPDATE catalog.products
SET ""Status"" = CASE
    WHEN ""Status"" = 'Published' THEN 'Active'
    WHEN ""Status"" = 'Archived'  THEN 'Archived'
    ELSE 'Draft'
END;");

            migrationBuilder.DropTable(
                name: "product_condition_defects",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_conditions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "product_revisions",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "IX_products_PublishedRevisionId",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_SellerId_Status",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_Status",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_StoreId",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(name: "ApprovedAtUtc", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "ArchivedAtUtc", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "CreatedAtUtc", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "CurrentRevisionId", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "PublishedAtUtc", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "PublishedRevisionId", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "StoreId", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "SubmittedAtUtc", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "SuspensionReason", schema: "catalog", table: "products");
            migrationBuilder.DropColumn(name: "UpdatedAtUtc", schema: "catalog", table: "products");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "catalog",
                table: "products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.CreateIndex(
                name: "IX_products_CategoryId",
                schema: "catalog",
                table: "products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_products_BrandId",
                schema: "catalog",
                table: "products",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_products_slug",
                schema: "catalog",
                table: "products",
                column: "slug",
                unique: true);
        }
    }
}
