using HBA.Food.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Food.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE DOSSIER VENDEUR QUI ENCAISSE LES RECETTES D'UN ÉTABLISSEMENT.
    ///
    /// SANS CETTE COLONNE, LE RESTAURATEUR N'ÉTAIT PAYÉ PAR AUCUN CHEMIN.
    ///
    /// Toute la chaîne de reversement — gains, portefeuille, demande de retrait,
    /// payout Mobile Money — est indexée sur un identifiant de VENDEUR et résout
    /// le compte de destination par le dossier. Un restaurant n'en ayant aucun,
    /// ses ventes étaient encaissées par la plateforme et s'arrêtaient là.
    ///
    /// NULLABLE, ET LES ÉTABLISSEMENTS EXISTANTS RESTENT SANS DOSSIER.
    ///
    /// Poser une valeur par défaut reviendrait à désigner un compte au hasard —
    /// c'est-à-dire à choisir sur quel numéro Mobile Money partent des recettes.
    /// La colonne naît donc nulle, et chaque restaurateur rattache le sien.
    ///
    /// Conséquence assumée : les établissements DÉJÀ EN SERVICE continuent de
    /// fonctionner — `Submit` n'est pas rejoué sur eux — mais leurs recettes ne
    /// seront comptabilisées qu'une fois le dossier rattaché. L'accrual le
    /// journalise en erreur, nommément, pour que cela se voie.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(FoodDbContext))]
    [Migration("20260820000000_DossierDeReversementDuRestaurant")]
    public partial class DossierDeReversementDuRestaurant : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
            => migrationBuilder.AddColumn<Guid>(
                name: "PayoutSellerId",
                schema: "food",
                table: "restaurants",
                type: "uuid",
                nullable: true);

        protected override void Down(MigrationBuilder migrationBuilder)
            // CE RETOUR EN ARRIÈRE COUPE LE LIEN ENTRE UN RESTAURANT ET SON
            // COMPTE DE PAIEMENT. Les gains déjà écrits gardent leur bénéficiaire —
            // ils portent l'identifiant du vendeur, pas celui du restaurant — mais
            // aucune nouvelle recette ne sera plus attribuable.
            => migrationBuilder.DropColumn(
                name: "PayoutSellerId",
                schema: "food",
                table: "restaurants");
    }
}
