using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// `refunds.IdempotencyKey` DEVIENT UNIQUE.
    ///
    /// La dette est annoncée dans `InitialReturnRefund` : la migration initiale a
    /// créé la colonne SANS unicité, fidèlement au modèle de l'époque, en renvoyant
    /// la correction à ce lot. C'est cette migration-là.
    ///
    /// CE QUE ÇA COÛTE AUJOURD'HUI.
    ///
    /// Deux appels SIMULTANÉS à `POST /seller/returns/{id}/refund-decision`
    /// chargent tous deux l'agrégat, lisent tous deux `_refunds.Count == 0`,
    /// fabriquent tous deux la clé `return:{ReturnId}:refund:1` et écrivent DEUX
    /// remboursements sur le même dossier de retour.
    ///
    /// `RefundCalculationPolicy` ne les arrête pas : elle plafonne le montant sur
    /// un total déjà remboursé que ni l'une ni l'autre n'a encore écrit. Les deux
    /// passent, chacune lève un `RefundRequestedDomainEvent`, et le client est
    /// remboursé deux fois pour un seul retour.
    ///
    /// La fenêtre n'est pas théorique : un double-clic du vendeur suffit, et le
    /// chemin entre la lecture et l'écriture traverse une validation de montant
    /// puis une transition d'état.
    ///
    /// POURQUOI LA SEULE COLONNE, ET NON `(ReturnId, IdempotencyKey)`.
    ///
    /// Le précédent voisin — `payments.payment_refunds` — est bien un couple
    /// `(PaymentId, IdempotencyKey)`, parce que là-bas la clé peut venir de
    /// l'appelant et n'a donc aucune unicité propre.
    ///
    /// Ici c'est l'inverse : `ReturnRequest.DecideRefund` est le SEUL producteur de
    /// `Refund`, et il écrit `return:{Id}:refund:{n}` — le Guid du dossier est déjà
    /// DANS la clé. Ajouter `ReturnId` à l'index n'y ajouterait aucun pouvoir de
    /// discrimination, et affaiblirait le contrôle : une clé qui se répéterait d'un
    /// dossier à l'autre passerait sans bruit, alors que c'est précisément le
    /// symptôme d'un générateur de clé cassé qu'on veut voir échouer tôt.
    ///
    /// Pas de filtre partiel : la colonne est NOT NULL depuis sa création.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MIGRATION ÉCHOUERA SI DES DOUBLONS EXISTENT DÉJÀ.
    ///
    /// C'est voulu, et c'est le seul comportement honnête : chaque ligne en double
    /// est un remboursement qui a peut-être ÉTÉ VERSÉ. Aucune règle automatique ne
    /// peut décider laquelle garder — cela dépend de ce que le prestataire a
    /// réellement exécuté, et cela se lit chez lui, pas dans cette table. Un
    /// `DELETE` écrit ici effacerait la trace d'un virement sans que personne ne
    /// l'ait décidé.
    ///
    /// Le contrôle préalable ci-dessous existe pour que l'échec soit LISIBLE. Sans
    /// lui, PostgreSQL rend « could not create unique index », qui ne dit ni
    /// combien de dossiers sont concernés ni comment les retrouver — et le service
    /// refuse de démarrer sur un message qu'on met une heure à décoder.
    ///
    /// Pour inspecter avant de reprendre :
    ///
    ///     SELECT "IdempotencyKey", count(*), array_agg("Id"), array_agg("Status")
    ///     FROM return_refund.refunds
    ///     GROUP BY "IdempotencyKey"
    ///     HAVING count(*) &gt; 1;
    ///
    /// En développement, repartir d'une base neuve est plus rapide que d'arbitrer.
    /// ═════════════════════════════════════════════════════════════════════════
    ///
    /// <para>
    /// Attributs `[DbContext]` + `[Migration]` sur la classe, pas de fichier
    /// `.Designer.cs` : convention du dépôt pour les migrations écrites à la main.
    /// S'il en manque un, EF ignore la migration EN SILENCE — l'index n'est jamais
    /// créé, et le premier double remboursement passe comme si de rien n'était.
    /// </para>
    /// </summary>
    [DbContext(typeof(ReturnRefundDbContext))]
    [Migration("20260827000300_UniciteCleRemboursementRetour")]
    public partial class UniciteCleRemboursementRetour : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
DECLARE doublons int;
BEGIN
    SELECT count(*) INTO doublons
    FROM (
        SELECT ""IdempotencyKey""
        FROM return_refund.refunds
        GROUP BY ""IdempotencyKey""
        HAVING count(*) > 1
    ) AS d;

    IF doublons > 0 THEN
        RAISE EXCEPTION
            'Impossible de rendre return_refund.refunds.""IdempotencyKey"" unique : % cle(s) portent plusieurs remboursements. Chacun est un versement peut-etre deja parti, aucune regle automatique ne peut choisir lequel garder. Pour les lister : SELECT ""IdempotencyKey"", count(*), array_agg(""Id""), array_agg(""Status"") FROM return_refund.refunds GROUP BY ""IdempotencyKey"" HAVING count(*) > 1;',
            doublons;
    END IF;
END $$;");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_IdempotencyKey",
                schema: "return_refund",
                table: "refunds",
                column: "IdempotencyKey",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refunds_IdempotencyKey",
                schema: "return_refund",
                table: "refunds");
        }
    }
}
