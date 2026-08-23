using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Wallet.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE DÉTAIL D'UN LOT DE REVERSEMENT, ET LE BALAYAGE DES RETRAITS (§4, §6, §8).
    ///
    /// Trois gestes, une seule migration : ils portent tous sur les mêmes deux
    /// tables, et écrire trois migrations à la main — avec trois éditions du même
    /// instantané — serait trois fois le risque pour aucun bénéfice.
    ///
    /// 1 · `FK_payouts_settlement_batches_SettlementBatchId` PASSE EN `Restrict`.
    ///
    /// Supprimer un lot effaçait le DÉTAIL de ce qui a été versé à chaque vendeur.
    /// Le lot porte le total ; les `payouts` portent qui a reçu combien. Sans eux,
    /// un vendeur qui conteste son versement n'a plus rien en face de son relevé.
    ///
    /// Vérifié avant de toucher : la collection `_payouts` n'est jamais mutée par
    /// retrait — seulement lue et alimentée. Le basculement est donc sans effet sur
    /// le code existant. La configuration porte `IsRequired()`, donc EF lève au lieu
    /// de sévrer si quelqu'un l'essayait un jour.
    ///
    /// 2 · `withdrawals.Status` GAGNE SON INDEX.
    ///
    /// Trois requêtes filtrent dessus, dont la reprise périodique des retraits en
    /// cours : un balayage complet à chaque tour pour trouver une poignée de lignes,
    /// sur une table qui ne décroît jamais. `customer_withdrawals` avait déjà cet
    /// index — il y a DEUX tables de retrait dans ce schéma, et seule celle des
    /// CLIENTS était servie. Celle des VENDEURS, l'argent qui part vers un compte
    /// Mobile Money, ne l'avait pas.
    ///
    /// 3 · `settlement_batches.CreatedAtUtc` GAGNE LE SIEN.
    ///
    /// Les deux seules lectures de liste trient dessus, sur toute la table et sans
    /// borne. L'index ne borne pas la requête — c'est le lot 8.4 — il rend le tri
    /// gratuit.
    ///
    /// CE QUI N'EST PAS ICI, ET QUI FAIT PARTIE DU MÊME LOT : le jeton de
    /// concurrence posé sur `withdrawals`. Il s'adosse à `xmin`, colonne SYSTÈME que
    /// chaque ligne PostgreSQL porte déjà — aucune colonne n'est créée, donc il n'y
    /// a aucune DDL à écrire. Le changement vit dans la configuration et dans
    /// l'instantané, et nulle part ailleurs. Le chercher ici serait le chercher là
    /// où il ne peut pas être.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Financial.Wallet.Infrastructure.Persistence.WalletDbContext))]
    [Migration("20260902000200_DetailDesVersementsEtRetraits")]
    public partial class DetailDesVersementsEtRetraits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL ne sait pas modifier le `ON DELETE` d'une contrainte
            // existante : il faut la retirer et la reposer.
            migrationBuilder.DropForeignKey(
                name: "FK_payouts_settlement_batches_SettlementBatchId",
                schema: "settlement",
                table: "payouts");

            migrationBuilder.AddForeignKey(
                name: "FK_payouts_settlement_batches_SettlementBatchId",
                schema: "settlement",
                table: "payouts",
                column: "SettlementBatchId",
                principalSchema: "settlement",
                principalTable: "settlement_batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "IX_withdrawals_Status",
                schema: "settlement",
                table: "withdrawals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_settlement_batches_CreatedAtUtc",
                schema: "settlement",
                table: "settlement_batches",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_settlement_batches_CreatedAtUtc",
                schema: "settlement",
                table: "settlement_batches");

            migrationBuilder.DropIndex(
                name: "IX_withdrawals_Status",
                schema: "settlement",
                table: "withdrawals");

            migrationBuilder.DropForeignKey(
                name: "FK_payouts_settlement_batches_SettlementBatchId",
                schema: "settlement",
                table: "payouts");

            migrationBuilder.AddForeignKey(
                name: "FK_payouts_settlement_batches_SettlementBatchId",
                schema: "settlement",
                table: "payouts",
                column: "SettlementBatchId",
                principalSchema: "settlement",
                principalTable: "settlement_batches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
