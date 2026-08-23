using HBA.Financial.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Payments.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE RÉFÉRENCE PSP NE DÉSIGNE QU'UN SEUL PAIEMENT.
    ///
    /// L'INDEX EXISTAIT DÉJÀ — IL N'ÉTAIT PAS UNIQUE.
    ///
    /// C'est pire qu'une absence d'index : on croit la colonne protégée parce
    /// qu'on la voit dans la liste des index. Elle ne l'était que pour la lecture.
    ///
    /// CE QUE ÇA COÛTE.
    ///
    /// Le webhook du prestataire ne connaît pas nos identifiants : il retrouve le
    /// paiement PAR CETTE RÉFÉRENCE. Si deux lignes la portent, il en encaisse UNE
    /// AU HASARD — celle que PostgreSQL rend en premier, ce qui dépend du plan
    /// d'exécution et peut changer d'un jour à l'autre. L'autre reste `Pending`
    /// POUR TOUJOURS.
    ///
    /// Et rien ne le signale. Aucune erreur n'est levée, aucun journal n'est écrit :
    /// du point de vue du service, un paiement encore en attente est un état
    /// parfaitement normal. On l'apprend par le client, des semaines plus tard,
    /// quand il réclame la commande qu'il a payée.
    ///
    /// POURQUOI UN INDEX PARTIEL.
    ///
    /// La colonne est nullable PAR CONSTRUCTION : un paiement n'a pas de référence
    /// tant que le PSP n'a pas répondu. Le filtre `IS NOT NULL` sort ces lignes de
    /// l'index.
    ///
    /// Il ne faut pas se tromper sur ce qu'il apporte : PostgreSQL tient déjà deux
    /// NULL pour distincts, un index unique nu aurait accepté toute la file
    /// d'attente. Le filtre écrit l'intention en clair et évite d'indexer des
    /// lignes dont la clé n'existe pas encore. La garantie utile, elle, est
    /// inconditionnelle : DÈS QU'une référence est écrite, elle est unique.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MIGRATION ÉCHOUERA SI DES DOUBLONS EXISTENT DÉJÀ.
    ///
    /// C'est voulu. Les doublons éventuels sont des PAIEMENTS RÉELS, dont l'un a
    /// peut-être été encaissé. Aucune règle automatique ne peut décider lequel
    /// garder — cela dépend de ce que le PSP a réellement débité, de ce qui a été
    /// livré, de ce qui a été remboursé. Un `DELETE` écrit ici effacerait la trace
    /// d'un mouvement d'argent sans que personne ne l'ait décidé.
    ///
    /// Le contrôle préalable ci-dessous existe pour que l'échec soit LISIBLE. Sans
    /// lui, PostgreSQL rend « could not create unique index », qui ne dit ni
    /// combien de dossiers sont concernés ni comment les retrouver — et le service
    /// refuse de démarrer sur un message qu'on met une heure à décoder.
    ///
    /// Pour inspecter avant de reprendre :
    ///
    ///     SELECT "ProviderReference", count(*), array_agg("Id"), array_agg("Status")
    ///     FROM payments.payments
    ///     WHERE "ProviderReference" IS NOT NULL
    ///     GROUP BY "ProviderReference"
    ///     HAVING count(*) &gt; 1;
    ///
    /// En développement, repartir d'une base neuve est plus rapide que d'arbitrer.
    /// ═════════════════════════════════════════════════════════════════════════
    ///
    /// <para>
    /// Attributs `[DbContext]` + `[Migration]` sur la classe, pas de fichier
    /// `.Designer.cs` : convention du dépôt pour les migrations écrites à la main.
    /// S'il en manque un, EF ignore la migration EN SILENCE — l'index n'est jamais
    /// créé et rien ne le dit.
    /// </para>
    /// </summary>
    [DbContext(typeof(PaymentsDbContext))]
    [Migration("20260827000100_UniciteReferencePsp")]
    public partial class UniciteReferencePsp : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
DECLARE doublons int;
BEGIN
    SELECT count(*) INTO doublons
    FROM (
        SELECT ""ProviderReference""
        FROM payments.payments
        WHERE ""ProviderReference"" IS NOT NULL
        GROUP BY ""ProviderReference""
        HAVING count(*) > 1
    ) AS d;

    IF doublons > 0 THEN
        RAISE EXCEPTION
            'Impossible de rendre payments.payments.""ProviderReference"" unique : % reference(s) PSP portent plusieurs paiements. Ce sont des mouvements d''argent reels, aucune regle automatique ne peut choisir lequel garder. Pour les lister : SELECT ""ProviderReference"", count(*), array_agg(""Id""), array_agg(""Status"") FROM payments.payments WHERE ""ProviderReference"" IS NOT NULL GROUP BY ""ProviderReference"" HAVING count(*) > 1;',
            doublons;
    END IF;
END $$;");

            // L'index non unique doit disparaître avant que l'unique prenne son nom :
            // remplacer sur place est impossible, PostgreSQL ne convertit pas un index
            // existant en index unique.
            migrationBuilder.DropIndex(
                name: "IX_payments_ProviderReference",
                schema: "payments",
                table: "payments");

            migrationBuilder.CreateIndex(
                name: "IX_payments_ProviderReference",
                schema: "payments",
                table: "payments",
                column: "ProviderReference",
                unique: true,
                filter: "\"ProviderReference\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_ProviderReference",
                schema: "payments",
                table: "payments");

            migrationBuilder.CreateIndex(
                name: "IX_payments_ProviderReference",
                schema: "payments",
                table: "payments",
                column: "ProviderReference");
        }
    }
}
