using System;
using HBA.Deliveries.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Deliveries.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUAND CETTE COURSE A-T-ELLE BOUGÉ, ET UN GAIN LIVREUR EST-IL RECALCULABLE ?
    /// (§9.1, §9.2)
    ///
    /// ── 1. UpdatedAtUtc
    ///
    /// Une course traverse onze états. Sans horodatage de modification, une course
    /// restée en SearchingDriver ne dit pas si le dispatch vient d'échouer ou si
    /// elle attend depuis la veille — la seule question qui décide s'il faut
    /// relancer ou rembourser.
    /// NULLABLE, SANS VALEUR PAR DÉFAUT.
    ///
    /// Les lignes antérieures à cette migration restent à NULL, ce qui se lit « on
    /// ne sait pas ». Un DEFAULT now() leur ferait toutes dire qu'elles ont été
    /// touchées à la seconde du déploiement : faux, et faux d'une manière qui ne se
    /// remarque pas — c'est-à-dire pire que l'absence de colonne.
    ///
    /// LA COLONNE N'EXISTE QUE DANS LE MODÈLE EF (propriété fantôme).
    ///
    /// Aucune propriété C# ne lui correspond : c'est une donnée d'EXPLOITATION, pas
    /// une donnée métier, et le domaine ne doit pas pouvoir fonder une règle sur
    /// l'heure d'un UPDATE. Elle est posée par ModuleDbContext à chaque écriture —
    /// INSERT compris, pour que NULL garde un sens unique. Voir HorodatageExtensions.
    ///
    /// CE QUE CETTE COLONNE NE VERRA PAS.
    ///
    /// Une écriture qui ne touche QUE des lignes enfants ne met pas la ligne parente
    /// en Modified : EF n'émet aucun UPDATE dessus, et l'estampille ne bouge pas.
    /// Même angle mort que le jeton de concurrence xmin, mêmes causes.
    ///
    /// ── 2. Deux contraintes de cohérence des montants
    ///
    /// CE QUE L'AUDIT DEMANDAIT ICI AURAIT REJETÉ DES COURSES LÉGITIMES.
    ///
    /// Il réclamait « une course livrée a un prix et un gain ». Le domaine dit
    /// explicitement le contraire dans Delivery.MarkDelivered : sans devis, on
    /// laisse NUL plutôt que zéro, parce que « aucun gain calculé » se cherche là
    /// où « zéro franc » se paie. La contrainte demandée aurait fait échouer la
    /// remise d'un colis pour imposer une règle que le code refuse.
    ///
    /// Ce qui est vrai, et ce qui est posé ici :
    ///
    ///   • un prix sans devise n'est ni facturable ni versable — AttachQuote pose
    ///     toujours les deux ensemble ;
    ///   • un gain livreur sans le prix ni le taux dont il dérive est un montant
    ///     que personne ne peut recalculer ni contester. Et c'est de l'argent dû.
    ///
    /// Le raisonnement complet est dans DeliveryConfiguration, à côté de la
    /// déclaration ; il n'est pas recopié ici, deux exemplaires finissant toujours
    /// par diverger.
    ///
    /// CES CONTRAINTES PEUVENT FAIRE ÉCHOUER LA MIGRATION. Repérage :
    ///
    ///     SELECT "Id" FROM deliveries.deliveries
    ///     WHERE ("Price" IS NOT NULL AND "Currency" IS NULL)
    ///        OR ("DriverEarning" IS NOT NULL
    ///            AND ("Price" IS NULL OR "DriverShareRate" IS NULL));
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(DeliveriesDbContext))]
    [Migration("20260905000100_HorodatageEtCoherenceDesMontants")]
    public partial class HorodatageEtCoherenceDesMontants : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                schema: "deliveries",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_deliveries_price_has_currency",
                schema: "deliveries",
                table: "deliveries",
                sql: "\"Price\" IS NULL OR \"Currency\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_deliveries_earning_has_basis",
                schema: "deliveries",
                table: "deliveries",
                sql: "\"DriverEarning\" IS NULL OR (\"Price\" IS NOT NULL AND \"DriverShareRate\" IS NOT NULL)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CE RETOUR EN ARRIÈRE EST DESTRUCTIF : les dates effacées ne se
            // reconstruisent pas. Rejouer la migration recrée la colonne vide, et
            // toutes les lignes redeviennent « on ne sait pas ».
            migrationBuilder.DropCheckConstraint(
                name: "ck_deliveries_earning_has_basis",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_deliveries_price_has_currency",
                schema: "deliveries",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                schema: "deliveries",
                table: "deliveries");
        }
    }
}
