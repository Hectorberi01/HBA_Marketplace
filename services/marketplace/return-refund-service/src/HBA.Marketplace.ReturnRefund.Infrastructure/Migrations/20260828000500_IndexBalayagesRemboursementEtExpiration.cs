using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX INDEX POUR LES DEUX BALAYAGES QUI VIENNENT D'ÊTRE BRANCHÉS.
    ///
    /// Le lot 3.2 fait exister deux travailleurs qui n'existaient que de nom
    /// (`ReturnRefundWorkers` : les trois coquilles journalisaient « active » et
    /// rendaient la main). Chacun pose désormais une requête PÉRIODIQUE sur une
    /// table qui ne fait que grandir, et aucune des deux n'avait d'index.
    ///
    /// `refunds (Status, CreatedAtUtc)` — LA REQUÊTE LA PLUS FRÉQUENTE DU MODULE.
    ///
    /// `RefundRetryWorker` demande toutes les VINGT SECONDES les remboursements
    /// `Pending`, `Processing` ou `Failed`, du plus ancien au plus récent. Sans
    /// index, chaque tour est un parcours complet de `refunds` suivi d'un tri —
    /// pour n'en retenir, en régime normal, aucune ligne. Or l'écrasante majorité
    /// des lignes est `Succeeded` : c'est précisément ce qu'on ne veut pas lire.
    ///
    /// `Status` en tête parce que c'est lui qui élimine ; `CreatedAtUtc` ensuite
    /// sert le `ORDER BY` sans tri supplémentaire.
    ///
    /// `return_requests (Status, ExpiresAtUtc)` — MÊME RAISONNEMENT.
    ///
    /// `ExpireReturnsWorker` cherche toutes les dix minutes les dossiers
    /// `AwaitingApproval` ou `AwaitingReturn` dont la date est passée. L'index
    /// existant `(SellerId, Status)` ne sert pas cette requête : elle ne porte
    /// aucun vendeur.
    ///
    /// AUCUNE UNICITÉ ICI, ET IL NE FAUT PAS EN AJOUTER.
    ///
    /// Ce sont des index de LECTURE. La garde contre le double remboursement est
    /// ailleurs et elle est déjà posée : `IX_refunds_IdempotencyKey`, unique,
    /// migration `20260827000300` du lot 3.1. La clé y est fabriquée
    /// `return:{ReturnId}:refund:{n}` — le dossier est DANS la clé — donc une
    /// seconde contrainte sur `(ReturnId, IdempotencyKey)` n'apporterait aucun
    /// pouvoir de discrimination supplémentaire, et en retirerait : une clé qui se
    /// répéterait d'un dossier à l'autre passerait sans bruit, alors que c'est le
    /// symptôme d'un générateur cassé qu'on veut voir échouer tôt. Le raisonnement
    /// est écrit en toutes lettres dans `20260827000300_UniciteCleRemboursementRetour`.
    ///
    /// AUCUNE COLONNE N'EST AJOUTÉE NI MODIFIÉE. Les quatre colonnes indexées
    /// existent depuis `20260821000100_InitialReturnRefund` ; cette migration ne
    /// touche à aucune donnée et se rejoue sur une base vide comme sur une base
    /// pleine.
    /// </summary>
    /// <remarks>
    /// Attributs `[DbContext]` + `[Migration]` sur la classe, pas de fichier
    /// `.Designer.cs` : convention du dépôt pour les migrations écrites à la main.
    /// S'il en manque un, EF ignore la migration EN SILENCE — les index ne sont
    /// jamais créés, et les deux balayages parcourent leurs tables entières toutes
    /// les vingt secondes sans que rien ne le signale.
    /// </remarks>
    [DbContext(typeof(ReturnRefundDbContext))]
    [Migration("20260828000500_IndexBalayagesRemboursementEtExpiration")]
    public partial class IndexBalayagesRemboursementEtExpiration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_refunds_Status_CreatedAtUtc",
                schema: "return_refund",
                table: "refunds",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_return_requests_Status_ExpiresAtUtc",
                schema: "return_refund",
                table: "return_requests",
                columns: new[] { "Status", "ExpiresAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_return_requests_Status_ExpiresAtUtc",
                schema: "return_refund",
                table: "return_requests");

            migrationBuilder.DropIndex(
                name: "IX_refunds_Status_CreatedAtUtc",
                schema: "return_refund",
                table: "refunds");
        }
    }
}
