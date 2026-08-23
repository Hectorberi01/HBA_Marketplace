using System;
using HBA.Merchants.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Merchants.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// L'ÉQUIPE D'UN VENDEUR — SIX TABLES, ET UNE REPRISE.
    ///
    /// ÉCRITE À LA MAIN, DONC À CONFRONTER AU MODÈLE.
    ///
    /// Comme `TableDesBoutiques` et `RepriseKybVersMedia` avant elle. L'instantané
    /// `SellersDbContextModelSnapshot` doit être complété en conséquence : sans
    /// cela, le prochain `dotnet ef migrations add` recréerait ces mêmes tables.
    ///
    /// LA REPRISE EST LA PARTIE QUI COMPTE, ET ELLE N'EST PAS COSMÉTIQUE.
    ///
    /// Chaque vendeur existant devient membre de son propre dossier, avec le rôle
    /// OWNER. Sans elle, `CountActiveOwnersAsync` répondrait ZÉRO pour tout le
    /// monde — et la garde du « dernier propriétaire », qui protège justement
    /// contre un dossier orphelin, laisserait passer le premier retrait venu faute
    /// de trouver un propriétaire à protéger.
    ///
    /// L'identifiant du rôle OWNER est écrit en dur ici. C'est le même que celui de
    /// `SystemSellerRoles.OwnerId`, et c'est précisément à cela que sert un
    /// identifiant fixe : une reprise SQL n'a pas à deviner ce que le code sèmera
    /// au démarrage.
    ///
    /// ORDRE : LE RÔLE SYSTÈME EST INSÉRÉ ICI AUSSI, ET DÉLIBÉRÉMENT EN DOUBLE.
    ///
    /// L'amorçage C# le créera au démarrage — mais la migration s'exécute AVANT,
    /// et la clé étrangère de `seller_member_roles` exigerait sinon un rôle qui
    /// n'existe pas encore. L'insertion est conditionnelle (`ON CONFLICT DO
    /// NOTHING`) et l'amorçage recalera ensuite ses permissions : les deux chemins
    /// convergent, aucun ne dépend de l'autre.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(SellersDbContext))]
    [Migration("20260819120000_TableDesMembresVendeur")]
    public partial class TableDesMembresVendeur : Migration
    {
        private const string RoleProprietaire = "a5100001-0000-4000-8000-000000000001";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "seller_roles",
                schema: "sellers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),

                    // NUL = rôle système, partagé par tous les vendeurs.
                    SellerId = table.Column<Guid>(type: "uuid", nullable: true),

                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),

                    // La VOCATION du rôle (0 = vendeur, 1 = boutique), en entier
                    // pour que la comparaison tienne en SQL.
                    Scope = table.Column<int>(type: "integer", nullable: false),

                    IsSystemRole = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "sellers",
                columns: table => new
                {
                    SellerRoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permission = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.SellerRoleId, x.Permission });

                    table.ForeignKey(
                        name: "FK_role_permissions_seller_roles_SellerRoleId",
                        column: x => x.SellerRoleId,
                        principalSchema: "sellers",
                        principalTable: "seller_roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seller_members",
                schema: "sellers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),

                    // Vient d'Identity. Pas de clé étrangère : l'autre schéma
                    // appartient à un autre service, qui migre séparément.
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),

                    Status = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    JobTitle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    JoinedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "seller_member_roles",
                schema: "sellers",
                columns: table => new
                {
                    SellerMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerRoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_member_roles", x => new { x.SellerMemberId, x.SellerRoleId });

                    table.ForeignKey(
                        name: "FK_seller_member_roles_seller_members_SellerMemberId",
                        column: x => x.SellerMemberId,
                        principalSchema: "sellers",
                        principalTable: "seller_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "store_memberships",
                schema: "sellers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),

                    // 0 = Prepared : l'affectation est écrite, la règle ne
                    // s'applique pas encore. Voir StoreMembershipConfiguration.
                    Enforcement = table.Column<int>(type: "integer", nullable: false),

                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_store_memberships", x => x.Id);

                    table.ForeignKey(
                        name: "FK_store_memberships_seller_members_SellerMemberId",
                        column: x => x.SellerMemberId,
                        principalSchema: "sellers",
                        principalTable: "seller_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "store_membership_roles",
                schema: "sellers",
                columns: table => new
                {
                    StoreMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerRoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_store_membership_roles", x => new { x.StoreMembershipId, x.SellerRoleId });

                    table.ForeignKey(
                        name: "FK_store_membership_roles_store_memberships_StoreMembershipId",
                        column: x => x.StoreMembershipId,
                        principalSchema: "sellers",
                        principalTable: "store_memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── Index ────────────────────────────────────────────────────────

            migrationBuilder.CreateIndex(
                name: "IX_seller_roles_SellerId",
                schema: "sellers",
                table: "seller_roles",
                column: "SellerId");

            // Un nom par vendeur…
            migrationBuilder.CreateIndex(
                name: "UX_seller_roles_SellerId_Name",
                schema: "sellers",
                table: "seller_roles",
                columns: new[] { "SellerId", "Name" },
                unique: true,
                filter: "\"SellerId\" IS NOT NULL");

            // …et un nom système unique globalement. Deux index et non un seul :
            // PostgreSQL considère deux NULL comme distincts, si bien qu'un index
            // unique nu sur (SellerId, Name) laisserait passer plusieurs rôles
            // système homonymes.
            migrationBuilder.CreateIndex(
                name: "UX_seller_roles_SystemName",
                schema: "sellers",
                table: "seller_roles",
                column: "Name",
                unique: true,
                filter: "\"SellerId\" IS NULL");

            // UN COMPTE NE FIGURE QU'UNE FOIS DANS UN VENDEUR.
            migrationBuilder.CreateIndex(
                name: "UX_seller_members_SellerId_UserId",
                schema: "sellers",
                table: "seller_members",
                columns: new[] { "SellerId", "UserId" },
                unique: true);

            // L'INDEX DU CHEMIN CHAUD : toute requête vendeur, sur cinq
            // services, part d'un identifiant d'utilisateur.
            migrationBuilder.CreateIndex(
                name: "IX_seller_members_UserId",
                schema: "sellers",
                table: "seller_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_store_memberships_SellerMemberId_StoreId",
                schema: "sellers",
                table: "store_memberships",
                columns: new[] { "SellerMemberId", "StoreId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_store_memberships_StoreId",
                schema: "sellers",
                table: "store_memberships",
                column: "StoreId");

            // ── La reprise ───────────────────────────────────────────────────

            migrationBuilder.Sql($"""
                -- Le rôle OWNER, posé avant les membres qui le référencent.
                -- L'amorçage C# recalera ses permissions au démarrage ; ici seule
                -- l'existence de la ligne compte, pour la clé étrangère.
                INSERT INTO sellers.seller_roles
                    ("Id", "SellerId", "Name", "Description", "Scope", "IsSystemRole", "CreatedOnUtc")
                VALUES
                    ('{RoleProprietaire}', NULL, 'OWNER',
                     'Propriétaire du dossier vendeur. Contrôle complet.',
                     0, TRUE, NOW() AT TIME ZONE 'UTC')
                ON CONFLICT ("Id") DO NOTHING;

                -- CHAQUE VENDEUR EXISTANT DEVIENT MEMBRE DE SON PROPRE DOSSIER.
                --
                -- `gen_random_uuid()` vient de pgcrypto, présent en standard depuis
                -- PostgreSQL 13. `CreatedOnUtc` reprend la date d'inscription du
                -- vendeur plutôt que celle de la migration : l'ancienneté d'un
                -- propriétaire dans son équipe est le jour où il l'a fondée, pas
                -- celui où l'on a créé la table.
                INSERT INTO sellers.seller_members
                    ("Id", "SellerId", "UserId", "Status", "DisplayName", "JobTitle",
                     "InvitedByUserId", "JoinedOnUtc", "CreatedOnUtc")
                SELECT
                    gen_random_uuid(), s."Id", s."UserId", 1, NULL, NULL,
                    NULL, s."CreatedOnUtc", s."CreatedOnUtc"
                FROM sellers.sellers s
                WHERE NOT EXISTS (
                    SELECT 1 FROM sellers.seller_members m
                    WHERE m."SellerId" = s."Id" AND m."UserId" = s."UserId");

                INSERT INTO sellers.seller_member_roles ("SellerMemberId", "SellerRoleId")
                SELECT m."Id", '{RoleProprietaire}'
                FROM sellers.seller_members m
                WHERE NOT EXISTS (
                    SELECT 1 FROM sellers.seller_member_roles r
                    WHERE r."SellerMemberId" = m."Id"
                      AND r."SellerRoleId" = '{RoleProprietaire}');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "store_membership_roles", schema: "sellers");
            migrationBuilder.DropTable(name: "store_memberships", schema: "sellers");
            migrationBuilder.DropTable(name: "seller_member_roles", schema: "sellers");
            migrationBuilder.DropTable(name: "seller_members", schema: "sellers");
            migrationBuilder.DropTable(name: "role_permissions", schema: "sellers");
            migrationBuilder.DropTable(name: "seller_roles", schema: "sellers");
        }
    }
}
