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
    /// LES INVITATIONS — DEUX TABLES, ET DEUX INDEX QUI PORTENT DES RÈGLES.
    ///
    /// ÉCRITE À LA MAIN, DONC À CONFRONTER AU MODÈLE. L'instantané est complété
    /// en conséquence.
    ///
    /// `UX_seller_invitations_TokenHash` sert la recherche à l'acceptation — le
    /// seul chemin de lecture du parcours de l'invité — et interdit deux
    /// invitations de même empreinte, qui en rendraient une inatteignable.
    ///
    /// `UX_seller_invitations_Pending` est un index PARTIEL : il n'interdit les
    /// doublons que parmi les invitations en attente. Une même personne peut donc
    /// être invitée, révoquée, puis réinvitée — sans quoi une erreur de saisie
    /// fermerait définitivement une adresse.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(SellersDbContext))]
    [Migration("20260819130000_TableDesInvitations")]
    public partial class TableDesInvitations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "seller_invitations",
                schema: "sellers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    JobTitle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),

                    // L'EMPREINTE, JAMAIS LE JETON. 64 caractères : un SHA-256
                    // en hexadécimal.
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),

                    ExpiresOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_invitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "seller_invitation_assignments",
                schema: "sellers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy",
                                    NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SellerInvitationId = table.Column<Guid>(type: "uuid", nullable: false),

                    // Nul = rôle de niveau vendeur. Une clé composée (invitation,
                    // boutique, rôle) serait impossible : PostgreSQL refuse un NULL
                    // dans une clé primaire.
                    StoreId = table.Column<Guid>(type: "uuid", nullable: true),

                    SellerRoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_invitation_assignments", x => x.Id);

                    table.ForeignKey(
                        name: "FK_seller_invitation_assignments_seller_invitations_SellerInvi~",
                        column: x => x.SellerInvitationId,
                        principalSchema: "sellers",
                        principalTable: "seller_invitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_seller_invitations_TokenHash",
                schema: "sellers",
                table: "seller_invitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seller_invitations_SellerId",
                schema: "sellers",
                table: "seller_invitations",
                column: "SellerId");

            // 0 = Pending. La valeur littérale est ici parce qu'un filtre d'index
            // est du SQL : il ne connaît pas l'énumération.
            migrationBuilder.CreateIndex(
                name: "UX_seller_invitations_Pending",
                schema: "sellers",
                table: "seller_invitations",
                columns: new[] { "SellerId", "Email" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_seller_invitation_assignments_SellerInvitationId",
                schema: "sellers",
                table: "seller_invitation_assignments",
                column: "SellerInvitationId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "seller_invitation_assignments", schema: "sellers");
            migrationBuilder.DropTable(name: "seller_invitations", schema: "sellers");
        }
    }
}
