using System;
using HBA.Promotions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Promotions.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUI PAIE LA REMISE, ET À QUI APPARTIENT LA CAMPAGNE (ISSUE-052, D28).
    ///
    /// CE QUI ÉTAIT CASSÉ.
    ///
    /// La table `promotions` portait un périmètre, un type, une valeur, un budget —
    /// et RIEN qui dise qui supporte le coût. Le reste de la plateforme suppose
    /// pourtant la distinction depuis l'origine : `PriceBreakdownDto` porte
    /// `SellerDiscount` **et** `PlatformDiscount`, `OrderLineDraft` les deux aussi,
    /// et wallet calcule le gain vendeur sur `UnitBasePrice - SellerDiscount`.
    ///
    /// Brancher promotion-service sans ces deux colonnes aurait fait supporter aux
    /// VENDEURS les coupons de la PLATEFORME — silencieusement, par le calcul des
    /// gains, et découvert au premier relevé contesté. C'est la raison pour
    /// laquelle ISSUE-052 se traite AVANT ISSUE-033, et non l'inverse.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE PART EN POINTS DE BASE, ET NON UN `funded_by` À DEUX VALEURS.
    ///
    /// D28 est explicite : « le champ doit permettre d'exprimer plus tard une
    /// remise COFINANCÉE sans migration supplémentaire ». Une colonne texte
    /// « PLATFORM | SELLER » ne le permet pas — le jour où le commerce demande
    /// moitié-moitié, il faut une seconde colonne, donc une seconde migration, donc
    /// une période où deux colonnes disent la même chose à moitié.
    ///
    /// `SellerFundedShareBps` est un entier en dix-millièmes : 0 = la plateforme
    /// paie tout, 10 000 = le vendeur paie tout, 5 000 = moitié-moitié. Le cas
    /// cofinancé est donc exprimable dès aujourd'hui, en base comme dans le
    /// domaine ; seul le COMMERCE reste à trancher, ce que D28 laisse ouvert.
    ///
    /// Le coût assumé : la valeur lisible d'un `SELECT` est « 10000 » et non
    /// « SELLER ». C'est le contraire du choix fait pour `scope` et `status`, qui
    /// sont stockés en TEXTE précisément pour rester lisibles à minuit. La
    /// différence est qu'une part n'est pas une nomenclature : il n'existe pas de
    /// liste finie de valeurs à nommer, et « SELLER_60_PLATFORM_40 » serait une
    /// nomenclature inventée à chaque campagne.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES LIGNES EXISTANTES DEVIENNENT « PLATEFORME », ET CE CHOIX N'EST PAS
    /// NEUTRE.
    ///
    /// Le défaut inverse — part vendeur — ferait payer rétroactivement des
    /// marchands pour des campagnes qu'ils n'ont ni décidées ni signées, par le
    /// chemin (le calcul des gains) où le prélèvement ne laisse aucune trace
    /// lisible sur un relevé.
    ///
    /// Et ici ce n'est même pas un arbitrage prudent : c'est la valeur VRAIE.
    /// Les trois routes de `/api/v1/merchant/promotions` étaient fermées à
    /// `RequireAdmin` — aucun vendeur n'a jamais eu le moyen de créer une
    /// campagne. Toutes les lignes présentes sont donc, par construction, des
    /// campagnes de la plateforme. `defaultValue: 0` n'invente rien : il nomme ce
    /// qu'elles sont déjà.
    ///
    /// Le corollaire vaut pour `OwnerSellerId`, laissée à NULL : il n'existe aucun
    /// vendeur à qui rattacher ces campagnes, et lui en inventer un rendrait
    /// annulable par un marchand une campagne décidée par la plateforme.
    ///
    /// LE DÉFAUT RESTE POSÉ SUR LA COLONNE APRÈS LA MIGRATION.
    ///
    /// Voir `PromotionConfiguration` : une insertion qui ne passerait pas par EF —
    /// un jeu de données, une reprise SQL — recevrait sinon 0 par hasard plutôt que
    /// par règle, et le jour où le défaut du domaine changerait, les deux
    /// diveraient sans que rien ne le dise.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// AUCUNE CONTRAINTE `CHECK` SUR LA COHÉRENCE PART / PROPRIÉTAIRE.
    ///
    /// L'invariant « une part vendeur non nulle exige un propriétaire » est tenu
    /// par `Promotion.Create`, pas par la base. C'est un écart assumé : une
    /// contrainte `CHECK` aurait été le bon endroit, mais elle ferait échouer la
    /// migration sur toute ligne existante incohérente — et il ne peut pas y en
    /// avoir, puisque les deux colonnes naissent ici. La poser plus tard, quand
    /// des campagnes vendeur existeront, coûtera une migration de plus mais se
    /// fera sur des données qu'on saura inspecter.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// <para>
    /// Attributs `[DbContext]` + `[Migration]` sur la classe, pas de fichier
    /// `.Designer.cs` : convention du dépôt pour les migrations écrites à la main.
    /// S'il en manque un, EF ignore la migration EN SILENCE — les colonnes
    /// n'existent jamais, et le premier `SELECT` de promotion-service tombe sur
    /// « column p.SellerFundedShareBps does not exist », au démarrage, après le
    /// déploiement.
    /// </para>
    /// </summary>
    [DbContext(typeof(PromotionsDbContext))]
    [Migration("20260901000100_FinanceurDePromotion")]
    public partial class FinanceurDePromotion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SellerFundedShareBps",
                schema: "promotions",
                table: "promotions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerSellerId",
                schema: "promotions",
                table: "promotions",
                type: "uuid",
                nullable: true);

            // « Mes campagnes » devient une requête de production : la route
            // marchand filtre sur le propriétaire depuis D28. L'index est PARTIEL —
            // les campagnes de la plateforme portent NULL et ne se cherchent jamais
            // par ce chemin.
            migrationBuilder.CreateIndex(
                name: "ix_promotions_owner_seller",
                schema: "promotions",
                table: "promotions",
                column: "OwnerSellerId",
                filter: "\"OwnerSellerId\" IS NOT NULL");
        }

        /// <summary>
        /// LA DESCENTE PERD DE L'INFORMATION, ET NE PEUT PAS FAIRE AUTREMENT.
        ///
        /// Retirer ces deux colonnes ramène le modèle où aucune campagne ne dit qui
        /// la paie. Toute campagne vendeur créée depuis la montée redeviendrait
        /// indiscernable d'une campagne plateforme — et le producteur de
        /// `PriceBreakdownDto` réécrirait `SellerDiscount: 0`, c'est-à-dire ferait
        /// porter à la plateforme des remises que des marchands avaient acceptées
        /// de financer.
        ///
        /// Une descente n'est donc sûre que sur une base où aucune campagne vendeur
        /// n'a encore été créée. Ailleurs, il faut d'abord relever
        /// `SELECT "Id", "OwnerSellerId", "SellerFundedShareBps" FROM
        /// promotions.promotions WHERE "SellerFundedShareBps" &gt; 0` et savoir ce
        /// qu'on en fait.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_promotions_owner_seller",
                schema: "promotions",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "OwnerSellerId",
                schema: "promotions",
                table: "promotions");

            migrationBuilder.DropColumn(
                name: "SellerFundedShareBps",
                schema: "promotions",
                table: "promotions");
        }
    }
}
