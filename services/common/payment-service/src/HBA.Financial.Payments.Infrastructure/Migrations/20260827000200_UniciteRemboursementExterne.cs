using HBA.Financial.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Payments.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN REMBOURSEMENT EXTERNE NE DONNE QU'UNE LIGNE `payment_refunds`.
    ///
    /// MÊME DÉFAUT QUE `payments.ProviderReference` : L'INDEX EXISTAIT, PAS
    /// L'UNICITÉ.
    ///
    /// `ExternalRefundId` est l'identifiant que le service de retours donne au
    /// remboursement qu'il DEMANDE ici. C'est sa clé pour se reconnaître dans notre
    /// réponse. Sans unicité, deux lignes peuvent le porter : l'issue du PSP est
    /// alors reportée sur une seule, l'autre reste `Processing` sans fin, et le
    /// dossier de retour resté en suspens n'est jamais soldé côté client — qui
    /// attend un argent que personne ne sait plus lui devoir.
    ///
    /// Pire : le second remboursement, lui, a bel et bien été DEMANDÉ au PSP. Deux
    /// lignes, deux appels sortants, un seul suivi. L'argent peut être parti deux
    /// fois pendant que la base n'en montre qu'un.
    ///
    /// LE PRÉCÉDENT EST JUSTE À CÔTÉ.
    ///
    /// `(PaymentId, IdempotencyKey)` est unique depuis `AddPaymentRefunds` : la
    /// table sait déjà refuser un doublon quand la clé est fournie par l'appelant.
    /// `ExternalRefundId` couvre l'autre entrée — celle où c'est un identifiant
    /// d'agrégat distant qui fait foi. Les deux sont nécessaires : elles ferment
    /// deux portes différentes sur la même table.
    ///
    /// INDEX PARTIEL. La colonne est nullable : un remboursement peut naître
    /// d'un webhook, sans dossier de retour en face. Comme sur `ProviderReference`,
    /// le filtre écrit l'intention plus qu'il n'ajoute une garantie — PostgreSQL
    /// tient déjà deux NULL pour distincts.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MIGRATION ÉCHOUERA SI DES DOUBLONS EXISTENT DÉJÀ.
    ///
    /// C'est voulu. Chaque ligne en double est un remboursement qui a peut-être
    /// été versé. Aucune règle automatique ne peut choisir laquelle garder : il
    /// faut savoir laquelle le PSP a réellement exécutée, et cela se lit chez lui,
    /// pas ici. Un `DELETE` écrit dans cette migration effacerait la trace d'un
    /// virement sans que personne ne l'ait décidé.
    ///
    /// Le contrôle préalable ci-dessous existe pour que l'échec soit LISIBLE : sans
    /// lui, PostgreSQL rend « could not create unique index », qui ne dit ni
    /// combien de dossiers sont concernés ni comment les retrouver — et le service
    /// refuse de démarrer sur un message qu'on met une heure à décoder.
    ///
    /// Pour inspecter avant de reprendre :
    ///
    ///     SELECT "ExternalRefundId", count(*), array_agg("Id"), array_agg("Status")
    ///     FROM payments.payment_refunds
    ///     WHERE "ExternalRefundId" IS NOT NULL
    ///     GROUP BY "ExternalRefundId"
    ///     HAVING count(*) &gt; 1;
    /// ═════════════════════════════════════════════════════════════════════════
    ///
    /// <para>
    /// Attributs `[DbContext]` + `[Migration]` sur la classe, pas de fichier
    /// `.Designer.cs` : convention du dépôt pour les migrations écrites à la main.
    /// S'il en manque un, EF ignore la migration EN SILENCE.
    /// </para>
    /// </summary>
    [DbContext(typeof(PaymentsDbContext))]
    [Migration("20260827000200_UniciteRemboursementExterne")]
    public partial class UniciteRemboursementExterne : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
DECLARE doublons int;
BEGIN
    SELECT count(*) INTO doublons
    FROM (
        SELECT ""ExternalRefundId""
        FROM payments.payment_refunds
        WHERE ""ExternalRefundId"" IS NOT NULL
        GROUP BY ""ExternalRefundId""
        HAVING count(*) > 1
    ) AS d;

    IF doublons > 0 THEN
        RAISE EXCEPTION
            'Impossible de rendre payments.payment_refunds.""ExternalRefundId"" unique : % identifiant(s) externe(s) portent plusieurs remboursements. Chacun est un virement peut-etre deja parti, aucune regle automatique ne peut choisir lequel garder. Pour les lister : SELECT ""ExternalRefundId"", count(*), array_agg(""Id""), array_agg(""Status"") FROM payments.payment_refunds WHERE ""ExternalRefundId"" IS NOT NULL GROUP BY ""ExternalRefundId"" HAVING count(*) > 1;',
            doublons;
    END IF;
END $$;");

            // PostgreSQL ne convertit pas un index existant en index unique : il faut
            // déposer le non-unique pour que l'unique reprenne son nom.
            migrationBuilder.DropIndex(
                name: "IX_payment_refunds_ExternalRefundId",
                schema: "payments",
                table: "payment_refunds");

            migrationBuilder.CreateIndex(
                name: "IX_payment_refunds_ExternalRefundId",
                schema: "payments",
                table: "payment_refunds",
                column: "ExternalRefundId",
                unique: true,
                filter: "\"ExternalRefundId\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_refunds_ExternalRefundId",
                schema: "payments",
                table: "payment_refunds");

            migrationBuilder.CreateIndex(
                name: "IX_payment_refunds_ExternalRefundId",
                schema: "payments",
                table: "payment_refunds",
                column: "ExternalRefundId");
        }
    }
}
