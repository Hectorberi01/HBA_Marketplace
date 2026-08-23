using HBA.Deliveries.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Deliveries.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX LIVREURS NE PEUVENT PLUS ACCEPTER LA MÊME COURSE — ISSUE-028.
    ///
    /// CE QUI ÉTAIT CASSÉ, ET CE QUE ÇA COÛTAIT SUR LE TERRAIN.
    ///
    /// `Delivery` n'avait AUCUN jeton de concurrence, et rien en base ne disait
    /// qu'un livreur ne porte qu'une course à la fois. Deux acceptations
    /// concurrentes lisaient toutes deux `Status = DriverAssigned`, passaient
    /// toutes deux la garde de `AcceptByDriver`, et la seconde écriture écrasait
    /// la première SANS BRUIT — pendant que `DeliveryAcceptedDomainEvent` était
    /// levé deux fois, donc deux rémunérations engagées.
    ///
    /// Ce que le client voyait : deux motos devant la boutique, un colis remis
    /// deux fois ou pas du tout, et un litige que rien dans les journaux ne
    /// permettait de trancher — la trace de la première acceptation avait été
    /// écrasée par la seconde.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX MÉCANISMES, ET ILS NE PROTÈGENT PAS LA MÊME CHOSE.
    ///
    ///   1. `xmin` — jeton PAR LIGNE. Il arbitre deux écritures sur la MÊME
    ///      course : la première gagne, la seconde touche 0 ligne et lève
    ///      `DbUpdateConcurrencyException`. Il ne voit RIEN de ce qui se passe
    ///      sur une autre ligne.
    ///
    ///   2. `ux_deliveries_engaged_driver` — index unique PARTIEL. Il arbitre
    ///      deux courses DIFFÉRENTES qui voudraient le même livreur engagé.
    ///      `xmin` en est structurellement incapable, et c'est la confusion
    ///      détaillée dans `MemberConfiguration` et `ISellerUnitOfWork` : « le
    ///      verrou optimiste couvre le quota » est faux, et coûteux.
    ///
    /// Il fallait les DEUX. Aucun ne remplace l'autre.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// `xmin` EST LU, PAS AJOUTÉ.
    ///
    /// C'est une colonne SYSTÈME de PostgreSQL, présente sur toute table depuis
    /// toujours. `UsePostgresRowVersion()` se contente de la DÉCLARER à EF pour
    /// qu'elle entre dans la clause `WHERE` des `UPDATE`. Aucun `AddColumn` n'est
    /// donc émis ici, et la montée ne réécrit pas la table.
    ///
    /// (order-service, lui, avait émis un `AddColumn<uint>("xmin")` dans
    /// `20260714135446_AddConcurrencyTokens` — un `AddColumn` sur une colonne
    /// système que PostgreSQL ignore. Inoffensif là-bas, inutile ; on ne le
    /// recopie pas.)
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// POURQUOI PAS L'INDEX UNIQUE SEC QUE DEMANDAIT L'AUDIT.
    ///
    /// `UNIQUE ("AssignedDriverId")` sans filtre interdirait à un livreur d'avoir
    /// deux courses DE TOUTE SON HISTOIRE : sa deuxième course serait refusée par
    /// la base, définitivement. Le filtre restreint la contrainte aux cinq états
    /// ENGAGÉS — de l'acceptation à l'arrivée chez le destinataire. Un livreur y
    /// figure au plus une fois ; ses courses terminées et annulées sont hors index.
    ///
    /// CE QUE CETTE CONTRAINTE NE COUVRE PAS :
    ///
    ///   • `DriverAssigned` est VOLONTAIREMENT hors du filtre. Le dispatch propose
    ///     à plusieurs candidats à la fois ; interdire une proposition à un livreur
    ///     qui termine sa course actuelle casserait l'enchaînement des courses.
    ///     Une proposition n'est pas un engagement.
    ///   • Le GROUPAGE. Le jour où un livreur portera deux colis du même quartier,
    ///     CET INDEX DEVRA TOMBER. Il encode une règle d'exploitation, pas une loi
    ///     de la nature.
    ///   • Rien ici n'empêche une course d'être proposée à un livreur qui n'existe
    ///     plus : il n'y a pas de clé étrangère vers `drivers`, conformément à la
    ///     règle du dépôt entre agrégats (on référence par identifiant).
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES DOUBLONS EXISTANTS SONT RÉVOQUÉS, PAS SUPPRIMÉS, ET IL Y EN AURA.
    ///
    /// C'est le défaut décrit par ISSUE-028 : la base porte forcément des livreurs
    /// engagés sur plusieurs courses. La création de l'index échouerait, au
    /// démarrage, après le déploiement — les migrations passent AVANT l'ouverture
    /// du port.
    ///
    /// La reprise garde LA PLUS ANCIENNE acceptation de chaque livreur — celle qui
    /// a réellement gagné la course, et dont le livreur est le plus probablement
    /// déjà en train de s'occuper — et RENVOIE LES AUTRES EN RECHERCHE
    /// (`SearchingDriver`, `AssignedDriverId` remis à nul, `AcceptedAtUtc` effacé).
    ///
    /// Pourquoi renvoyer en recherche plutôt qu'annuler : ces courses ont un
    /// client qui attend un colis. Les annuler serait décider à sa place. Le
    /// dispatch les reprendra au tour suivant, ce qui est exactement le
    /// comportement prévu pour une course dont l'affectation a été révoquée.
    ///
    /// CE QUE LA REPRISE NE FAIT PAS, ET C'EST À SAVOIR AVANT DE DÉPLOYER :
    /// elle n'écrit AUCUNE ligne dans `delivery_assignments`. La proposition
    /// acceptée y reste marquée `Accepted` alors que la course est repartie en
    /// recherche. C'est incohérent, et c'est délibéré : réécrire un historique de
    /// propositions serait falsifier la trace de ce qui s'est passé. La colonne
    /// `Reason` d'une révocation est prévue pour l'exploitation, pas pour une
    /// migration. Une requête de contrôle est donnée plus bas.
    ///
    /// Pour inspecter AVANT de migrer :
    ///
    ///     SELECT "AssignedDriverId", count(*)
    ///     FROM deliveries.deliveries
    ///     WHERE "AssignedDriverId" IS NOT NULL
    ///       AND "Status" IN ('DriverAccepted', 'ArrivedAtPickup', 'PickedUp',
    ///                        'InTransit', 'ArrivedAtDropoff')
    ///     GROUP BY "AssignedDriverId"
    ///     HAVING count(*) &gt; 1;
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// <para>
    /// Attributs `[DbContext]` + `[Migration]` sur la classe, pas de fichier
    /// `.Designer.cs` : convention du dépôt pour les migrations écrites à la main.
    /// S'il en manque un, EF ignore la migration EN SILENCE — l'index n'est jamais
    /// créé, la double affectation reste possible, et RIEN ne le signale.
    /// </para>
    /// </summary>
    [DbContext(typeof(DeliveriesDbContext))]
    [Migration("20260904000100_VerrouAffectationCourse")]
    public partial class VerrouAffectationCourse : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ─────────────────────────────────────────────────────────────────
            // Reprise des livreurs déjà engagés sur plusieurs courses.
            // Elle doit précéder la création de l'index, qui échouerait sinon.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
DO $$
DECLARE
    livreurs int;
    courses_liberees int;
BEGIN
    CREATE TEMP TABLE _doublons_affectation ON COMMIT DROP AS
    SELECT d.""Id"" AS course
    FROM (
        SELECT ""Id"",
               row_number() OVER (
                   PARTITION BY ""AssignedDriverId""
                   ORDER BY ""AcceptedAtUtc"" NULLS LAST, ""CreatedAtUtc"", ""Id""
               ) AS rang
        FROM deliveries.deliveries
        WHERE ""AssignedDriverId"" IS NOT NULL
          AND ""Status"" IN ('DriverAccepted', 'ArrivedAtPickup', 'PickedUp',
                            'InTransit', 'ArrivedAtDropoff')
    ) d
    WHERE d.rang > 1;

    -- Le nom de la variable differe de celui de la colonne : en plpgsql, une
    -- variable qui porte le nom d'une colonne rend la reference AMBIGUE et le
    -- bloc echoue a l'execution.
    SELECT count(*)::int INTO courses_liberees FROM _doublons_affectation;

    IF courses_liberees > 0 THEN
        SELECT count(DISTINCT ""AssignedDriverId"")::int
          INTO livreurs
          FROM deliveries.deliveries
         WHERE ""Id"" IN (SELECT course FROM _doublons_affectation);

        UPDATE deliveries.deliveries
        SET ""Status"" = 'SearchingDriver',
            ""AssignedDriverId"" = NULL,
            ""AcceptedAtUtc"" = NULL
        WHERE ""Id"" IN (SELECT course FROM _doublons_affectation);

        RAISE NOTICE 'deliveries : % course(s) renvoyee(s) en recherche, tenues par % livreur(s) deja engage(s) ailleurs. La course la PLUS ANCIENNE de chaque livreur est conservee. Les lignes de delivery_assignments ne sont PAS reecrites : elles gardent la trace de l''acceptation.', courses_liberees, livreurs;
    END IF;
END $$;");

            // L'index d'origine portait sur la seule colonne `AssignedDriverId`.
            // Il est remplacé par sa forme composite : EF ne sait pas déclarer
            // deux index distincts sur exactement le même jeu de colonnes, et
            // `(AssignedDriverId, CreatedAtUtc)` sert mieux la requête réelle
            // — « les courses de ce livreur, la plus récente d'abord ».
            migrationBuilder.DropIndex(
                name: "ix_deliveries_driver",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_driver",
                schema: "deliveries",
                table: "deliveries",
                columns: new[] { "AssignedDriverId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_deliveries_engaged_driver",
                schema: "deliveries",
                table: "deliveries",
                column: "AssignedDriverId",
                unique: true,
                filter: "\"AssignedDriverId\" IS NOT NULL AND \"Status\" IN "
                    + "('DriverAccepted', 'ArrivedAtPickup', 'PickedUp', 'InTransit', 'ArrivedAtDropoff')");
        }

        /// <summary>
        /// LA DESCENTE ROUVRE LA FAILLE, ET NE PEUT PAS FAIRE AUTREMENT.
        ///
        /// Retirer l'index rend possible qu'un livreur soit de nouveau engagé sur
        /// deux courses. Les courses renvoyées en recherche par la montée, elles,
        /// ne reviennent pas : leur affectation d'origine n'est plus connue une
        /// fois `AssignedDriverId` remis à nul. La reprise n'est PAS réversible.
        ///
        /// Le jeton `xmin` n'est pas « retiré » : il n'a jamais été ajouté. C'est
        /// la configuration EF qui cesse de le lire, pas la base qui change.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_deliveries_engaged_driver",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.DropIndex(
                name: "ix_deliveries_driver",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_driver",
                schema: "deliveries",
                table: "deliveries",
                column: "AssignedDriverId");
        }
    }
}
