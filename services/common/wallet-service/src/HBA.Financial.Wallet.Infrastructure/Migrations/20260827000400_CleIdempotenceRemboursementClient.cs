using HBA.Financial.Wallet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Wallet.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE CLÉ D'IDEMPOTENCE SUR `settlement.customer_refunds`.
    ///
    /// LA TABLE N'EN AVAIT AUCUNE. L'ARGENT POUVAIT PARTIR DEUX FOIS.
    ///
    /// Une ligne de cette table est un VERSEMENT MOBILE MONEY VERS UN CLIENT. Elle
    /// n'avait que deux index — `OrderId` et `Status` — et aucune contrainte
    /// d'unicité d'aucune sorte. Un appel HTTP réessayé, un double-clic de l'admin,
    /// un rejeu de file : une seconde ligne, un second payout FedaPay, et l'argent
    /// est parti deux fois.
    ///
    /// Rien ne le rattrape. Un payout exécuté ne s'annule pas — c'est d'ailleurs
    /// pour cela que tout ce module refuse la contre-passation sur issue
    /// indéterminée. Et le client n'a aucune raison de signaler qu'il a trop reçu.
    /// On ne l'apprendrait qu'au rapprochement bancaire, si quelqu'un le fait.
    ///
    /// AUCUN ÉMETTEUR N'ÉCRIT ENCORE ICI — C'EST LE BON ORDRE.
    ///
    /// `InitiateCustomerRefundCommand` n'a aujourd'hui AUCUN appelant : aucun
    /// remboursement client direct n'est jamais versé (ISSUE-009). Le lot 3.2
    /// branchera les émetteurs. Cette migration passe donc AVANT eux, délibérément :
    /// on rend la table sûre pendant qu'elle est encore vide, plutôt que d'aller
    /// chercher la contrainte après le premier double virement — moment où elle ne
    /// pourra plus être posée sans arbitrer des mouvements d'argent réels.
    ///
    /// PORTÉE `(OrderId, IdempotencyKey)`.
    ///
    /// La clé vient VERBATIM de l'en-tête `Idempotency-Key` : un jeton opaque choisi
    /// par le client, qui n'embarque ni la commande ni l'acheteur. Rien ne la rend
    /// globalement unique — c'est la situation de `payments.payment_refunds`, dont
    /// la clé peut aussi venir de l'appelant et qui est pour cette raison indexée
    /// `(PaymentId, IdempotencyKey)`. On imite ce précédent.
    ///
    /// Une portée globale coûterait un faux refus : deux commandes DIFFÉRENTES dont
    /// les jetons se recoupent verraient le second remboursement — pourtant dû —
    /// rejeté par la contrainte. Le danger qu'on ferme est « le MÊME versement
    /// exécuté deux fois » ; deux versements sur deux commandes distinctes n'en
    /// sont pas un.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES LIGNES EXISTANTES REÇOIVENT `legacy:&lt;Id&gt;`, ET CE N'EST PAS UNE CLÉ.
    ///
    /// La colonne est obligatoire, il faut donc bien écrire quelque chose dans les
    /// lignes antérieures. `'legacy:' || "Id"` est distinct par construction (l'`Id`
    /// est la clé primaire), donc l'index les accepte toutes sans en fusionner deux.
    ///
    /// Ce n'est PAS une clé d'idempotence : c'est un marqueur qui dit « cette ligne
    /// est née avant la contrainte ». Il ne peut jamais entrer en collision avec un
    /// en-tête réel, et il ne prétend protéger aucun versement passé — ceux-là ont
    /// eu lieu sans garde-fou, et rien d'écrit ici ne peut le changer.
    ///
    /// En pratique la table est vide (ISSUE-009). La reprise existe pour que la
    /// migration reste honnête sur une base où quelqu'un aurait inséré à la main.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MIGRATION ÉCHOUERA SI DES DOUBLONS EXISTENT DÉJÀ.
    ///
    /// C'est voulu. Un doublon ici est un virement qui est peut-être PARTI DEUX
    /// FOIS. Aucune règle automatique ne peut décider laquelle des deux lignes
    /// garder — il faut savoir ce que le PSP a réellement exécuté, et cela se lit
    /// chez lui, pas dans cette table. Un `DELETE` écrit dans cette migration
    /// effacerait la trace d'un virement sans que personne ne l'ait décidé, et
    /// effacerait justement celle qu'il faudrait aller réclamer.
    ///
    /// Le contrôle préalable ci-dessous existe pour que l'échec soit LISIBLE. Sans
    /// lui, PostgreSQL rend « could not create unique index », qui ne dit ni combien
    /// de dossiers sont concernés ni comment les retrouver — et le service refuse de
    /// démarrer sur un message qu'on met une heure à décoder.
    ///
    /// Pour inspecter avant de reprendre :
    ///
    ///     SELECT "OrderId", "IdempotencyKey", count(*), array_agg("Id"), array_agg("Status")
    ///     FROM settlement.customer_refunds
    ///     GROUP BY "OrderId", "IdempotencyKey"
    ///     HAVING count(*) &gt; 1;
    ///
    /// En développement, repartir d'une base neuve est plus rapide que d'arbitrer.
    /// ═════════════════════════════════════════════════════════════════════════
    ///
    /// <para>
    /// Attributs `[DbContext]` + `[Migration]` sur la classe, pas de fichier
    /// `.Designer.cs` : convention du dépôt pour les migrations écrites à la main.
    /// S'il en manque un, EF ignore la migration EN SILENCE — la colonne n'existe
    /// jamais, `SaveChanges` tombe sur « column does not exist », et le versement
    /// échoue au pire endroit : après le débit du portefeuille plateforme.
    /// </para>
    /// </summary>
    [DbContext(typeof(WalletDbContext))]
    [Migration("20260827000400_CleIdempotenceRemboursementClient")]
    public partial class CleIdempotenceRemboursementClient : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ajoutée NULLABLE d'abord : une colonne obligatoire posée d'emblée
            // exigerait une valeur par défaut, et une valeur par défaut est la même
            // pour toutes les lignes — donc un doublon garanti dès qu'une commande
            // en porte deux. On remplit ligne à ligne, puis on resserre.
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                schema: "settlement",
                table: "customer_refunds",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            // Marqueur d'antériorité, distinct par construction (`Id` est la clé
            // primaire). Voir l'encadré : ce n'est pas une clé d'idempotence.
            migrationBuilder.Sql(@"
UPDATE settlement.customer_refunds
SET ""IdempotencyKey"" = 'legacy:' || ""Id""::text
WHERE ""IdempotencyKey"" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                schema: "settlement",
                table: "customer_refunds",
                type: "character varying(180)",
                maxLength: 180,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(180)",
                oldMaxLength: 180,
                oldNullable: true);

            migrationBuilder.Sql(@"
DO $$
DECLARE doublons int;
BEGIN
    SELECT count(*) INTO doublons
    FROM (
        SELECT ""OrderId"", ""IdempotencyKey""
        FROM settlement.customer_refunds
        GROUP BY ""OrderId"", ""IdempotencyKey""
        HAVING count(*) > 1
    ) AS d;

    IF doublons > 0 THEN
        RAISE EXCEPTION
            'Impossible de rendre settlement.customer_refunds (""OrderId"", ""IdempotencyKey"") unique : % couple(s) portent plusieurs remboursements. Chacun est un virement Mobile Money peut-etre deja parti DEUX FOIS, aucune regle automatique ne peut choisir lequel garder. Pour les lister : SELECT ""OrderId"", ""IdempotencyKey"", count(*), array_agg(""Id""), array_agg(""Status"") FROM settlement.customer_refunds GROUP BY ""OrderId"", ""IdempotencyKey"" HAVING count(*) > 1;',
            doublons;
    END IF;
END $$;");

            migrationBuilder.CreateIndex(
                name: "IX_customer_refunds_OrderId_IdempotencyKey",
                schema: "settlement",
                table: "customer_refunds",
                columns: new[] { "OrderId", "IdempotencyKey" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_customer_refunds_OrderId_IdempotencyKey",
                schema: "settlement",
                table: "customer_refunds");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                schema: "settlement",
                table: "customer_refunds");
        }
    }
}
