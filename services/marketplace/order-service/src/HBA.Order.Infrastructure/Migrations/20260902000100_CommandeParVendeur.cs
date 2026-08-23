using HBA.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Orders.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA COMMANDE VENDEUR (ISSUE-027, ISSUE-026, décision D29).
    ///
    /// L'AGRÉGAT N'EXISTAIT PAS, ET CINQ PERMISSIONS L'ATTENDAIENT.
    ///
    /// `OrderingModuleApi.GetOrderReturnContextAsync` rendait `SellerOrderId:
    /// null` EN DUR — la trace la plus visible du manque. Conséquence en chaîne :
    /// `ORDER_CONFIRM`, `ORDER_REJECT`, `ORDER_MARK_PREPARING`,
    /// `ORDER_MARK_READY` et `ORDER_CANCEL` étaient déclarées et distribuées au
    /// rôle `ORDER_MANAGER` sans garder AUCUNE route, parce qu'il n'y avait rien
    /// à faire changer d'état. Le parcours vendeur s'arrêtait à la RÉCEPTION de
    /// la commande.
    ///
    /// CETTE TABLE NE REMPLACE PAS `orders.Status`, ELLE S'Y AJOUTE.
    ///
    /// La saga de la commande — paiement, stock, course, règlement — reste seule
    /// maîtresse du cycle GLOBAL. Ici on stocke ce que CHAQUE vendeur a à faire
    /// de SA part. « Confirmée » n'a pas le même sujet dans les deux tables, et
    /// les fondre ferait tromper le paiement et le calcul des gains en silence.
    ///
    /// L'INDEX UNIQUE (OrderId, SellerId) N'EST PAS DÉCORATIF.
    ///
    /// La confirmation arrive par Kafka, qui livre AU MOINS une fois. La
    /// relecture applicative de `ConfirmOrderPaymentCommandHandler` traite le
    /// rejeu ordinaire ; elle ne voit pas deux messages traités EN PARALLÈLE, qui
    /// répondent tous deux « cette commande n'est pas découpée » avant que l'un
    /// ait écrit. Sans l'index, le vendeur verrait la même commande DEUX FOIS
    /// dans son carnet, pour un montant doublé — une erreur sur ce qu'il croit
    /// avoir vendu, découverte au moment d'être payé. Même construction que
    /// `order_return_settlements` et `UnicitePanierParCommande`.
    ///
    /// AUCUNE CLÉ ÉTRANGÈRE VERS `orders`, ET C'EST VOULU.
    ///
    /// `SellerOrder` est un AGRÉGAT, pas un enfant de la commande : une relation
    /// EF le ferait charger sous elle et salir `orders` à chaque geste d'un
    /// vendeur, donc mettrait deux vendeurs d'une même commande en concurrence
    /// sur la MÊME ligne parente. Le prix est qu'aucune contrainte de base
    /// n'empêche une part orpheline ; aucune commande n'est jamais supprimée
    /// (elles s'annulent), et la règle inter-agrégats du dépôt est de référencer
    /// par identifiant.
    ///
    /// `xmin` EST LU, PAS AJOUTÉ, ET ICI IL SERT VRAIMENT.
    ///
    /// L'encadré d'`InventoryItem.StockVersion` décrit un jeton de concurrence
    /// posé et pourtant INERTE : une mutation qui n'écrit que des lignes enfants
    /// n'émet aucun `UPDATE` sur le parent. Ce n'est pas le cas ici — les six
    /// transitions écrivent toutes le statut et un horodatage sur
    /// `seller_orders`, et les lignes sont figées à la création. Ce qu'il
    /// protège : deux membres d'une même équipe vendeur sur deux écrans, l'un qui
    /// confirme pendant que l'autre refuse.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA TABLE NAÎT VIDE, ET LES COMMANDES DÉJÀ CONFIRMÉES N'AURONT PAS DE PART.
    ///
    /// C'est la conséquence la plus importante de cette migration, et elle ne se
    /// voit nulle part dans le code :
    ///
    ///   • un vendeur ouvrant son carnet ne verra AUCUN état vendeur sur ses
    ///     commandes antérieures. `OrderSummary.SellerOrderStatus` y est nul, et
    ///     les cinq routes y répondent 404 « commande vendeur introuvable » ;
    ///   • `GetOrderReturnContextAsync` continuera donc de rendre
    ///     `SellerOrderId: null` sur exactement ces commandes-là — c'est-à-dire
    ///     les seules déjà LIVRÉES, donc les seules retournables aujourd'hui. Le
    ///     champ mettra un cycle de vente complet à devenir majoritairement
    ///     renseigné. Ce n'est pas une régression : il était nul pour TOUT le
    ///     monde avant.
    ///
    /// UN RATTRAPAGE `INSERT … SELECT` EST-IL DÉFENDABLE ? OUI POUR LES
    /// DONNÉES, NON POUR L'ÉTAT — ET C'EST POURQUOI IL N'EST PAS ÉCRIT ICI.
    ///
    /// La matière est là : `orders` jointe à `order_lines` donne exactement le
    /// découpage, avec le même filtre `Kind = 'Goods'` et le même regroupement
    /// par `SellerId`. Techniquement, la reprise tient en deux `INSERT …
    /// SELECT`.
    ///
    /// Le problème n'est pas la donnée, c'est le STATUT à écrire, et aucune
    /// valeur n'est vraie :
    ///
    ///   • `AwaitingConfirmation` demanderait à des vendeurs de confirmer des
    ///     colis qu'ils ont expédiés il y a trois semaines, et ferait apparaître
    ///     des centaines de « commandes à traiter » sur un carnet à jour. Sur une
    ///     commande DÉJÀ LIVRÉE, cela crée en plus un geste qui n'a plus aucun
    ///     sens — et un bouton « refuser » sur une vente conclue ;
    ///   • `HandedOver` affirmerait un fait que nous n'avons pas constaté : rien
    ///     dans order-service ne dit qu'un vendeur a bien remis SON colis, et sur
    ///     une commande multi-vendeurs `Delivered` ne prouve pas que les deux
    ///     l'ont fait ;
    ///   • un statut différent selon `orders.Status` — `HandedOver` si livrée,
    ///     `Confirmed` sinon — fabriquerait un historique plausible et faux, et
    ///     c'est la pire des trois : personne ne saurait, dans six mois, quelles
    ///     lignes ont été DÉDUITES et lesquelles ont été VÉCUES. Les
    ///     horodatages, eux, seraient tous ceux de la migration.
    ///
    /// La décision est donc : PAS DE RATTRAPAGE. On ne fabrique pas de l'histoire
    /// qui n'a pas eu lieu — c'est la même règle qui a fait renoncer au
    /// rattrapage des retours antérieurs dans `RetoursImputesALaCommande`. Le
    /// dispositif se remplit à partir de la première confirmation, et les
    /// commandes antérieures restent lisibles sans état vendeur, exactement comme
    /// avant.
    ///
    /// Si l'exploitation en veut un plus tard, il devra être écrit comme un
    /// script d'exploitation daté, PAS comme une migration : il exige une
    /// décision produit sur le statut à poser, et cette décision-là ne
    /// s'improvise pas dans un `Up()`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(OrderingDbContext))]
    [Migration("20260902000100_CommandeParVendeur")]
    public partial class CommandeParVendeur : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "seller_orders",
                schema: "ordering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),

                    // Recopié depuis la commande : l'événement de refus doit dire QUI
                    // prévenir sans relire la commande. Un message asynchrone qui
                    // déclenche un appel synchrone est ce que le découplage évite.
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),

                    // En TEXTE, comme `orders.Status` : une commande vendeur se relit en
                    // base pendant les incidents, et « ReadyForPickup » s'y comprend là
                    // où « 3 » demande de retrouver l'énumération. C'est aussi ce qui la
                    // protège d'un renumérotage.
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),

                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),

                    // Un horodatage PAR ÉTAPE, et non un seul « dernière transition ».
                    // Un champ unique répond « quand a-t-elle bougé » ; il ne répond pas
                    // « depuis combien de temps ce vendeur laisse-t-il traîner une
                    // commande qu'il a acceptée », qui est la question que
                    // l'exploitation pose. Même raisonnement que
                    // `orders.UnderReviewSinceUtc`.
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreparingAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadyForPickupAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HandedOverAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefusedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),

                    // La seule trace de pourquoi une commande PAYÉE ne sera pas honorée.
                    // 500 comme `CancellationReason` et `ReviewReason` : c'est la même
                    // sorte de texte, écrit par un humain et relu par un humain.
                    RefusalReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),

                    // Verrou optimiste. `xmin` est une colonne SYSTÈME de PostgreSQL :
                    // on ne l'ajoute pas, on la LIT. Voir l'encadré ci-dessus — ici,
                    // contrairement à `InventoryItem`, toutes les transitions écrivent
                    // sur cette ligne, donc le jeton est réellement évalué.
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "seller_order_lines",
                schema: "ordering",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerOrderId = table.Column<Guid>(type: "uuid", nullable: false),

                    // La ligne d'origine dans `order_lines`. C'est par elle qu'un retour
                    // se rapproche — `order_return_settlement_lines` désigne la LIGNE et
                    // non le produit, parce qu'une même référence peut figurer deux fois.
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),

                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),

                    // NON NULL mais possiblement VIDE, comme `order_lines.Sku` dont elle
                    // est la copie.
                    Sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),

                    // Sans l'emplacement, aucun consommateur de l'événement de refus ne
                    // peut rendre le stock : Inventory travaille par
                    // (SKU, emplacement, commande).
                    ShipFromLocationId = table.Column<Guid>(type: "uuid", nullable: false),

                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPaidAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seller_order_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seller_order_lines_seller_orders_SellerOrderId",
                        column: x => x.SellerOrderId,
                        principalSchema: "ordering",
                        principalTable: "seller_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_seller_orders_OrderId_SellerId",
                schema: "ordering",
                table: "seller_orders",
                columns: new[] { "OrderId", "SellerId" },
                unique: true);

            // Le carnet du vendeur, filtré par état : c'est l'écran de travail
            // d'`ORDER_MANAGER`, lu à chaque ouverture de la console.
            migrationBuilder.CreateIndex(
                name: "IX_seller_orders_SellerId_Status",
                schema: "ordering",
                table: "seller_orders",
                columns: new[] { "SellerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_seller_order_lines_SellerOrderId",
                schema: "ordering",
                table: "seller_order_lines",
                column: "SellerOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_seller_order_lines_OrderLineId",
                schema: "ordering",
                table: "seller_order_lines",
                column: "OrderLineId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "seller_order_lines",
                schema: "ordering");

            migrationBuilder.DropTable(
                name: "seller_orders",
                schema: "ordering");
        }
    }
}
