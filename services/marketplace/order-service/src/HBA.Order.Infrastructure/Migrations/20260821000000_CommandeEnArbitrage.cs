using HBA.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Orders.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA SORTIE DE SECOURS DE LA SAGA : « EN ARBITRAGE ».
    ///
    /// UNE COMMANDE DEVENUE INEXÉCUTABLE RESTAIT « Confirmed » POUR TOUJOURS.
    ///
    /// Ni livraison, ni annulation, ni remboursement : escrow gelé, stock déjà
    /// décrémenté, argent encaissé, et un acheteur qui attend un colis que
    /// personne n'apportera. Deux chemins y menaient — la course annulée, dont
    /// l'événement ne remontait à aucun donneur d'ordre, et l'expédition
    /// multi-lieux, refusée à juste titre mais sans issue.
    ///
    /// LE STATUT LUI-MÊME NE DEMANDE AUCUN DDL.
    ///
    /// La colonne `Status` est un `character varying(20)` — l'énumération est
    /// convertie en TEXTE, pas en entier (voir `OrderConfiguration`). Ajouter
    /// « UnderReview » à `OrderStatus` n'impose donc ni type PostgreSQL à faire
    /// évoluer, ni contrainte à relâcher. C'est précisément la raison pour
    /// laquelle ce dépôt stocke ses états en clair : une commande se relit en base
    /// pendant les incidents, et un état ajouté n'y casse rien.
    ///
    /// Restent les deux colonnes qui portent le DOSSIER d'arbitrage.
    ///
    /// POURQUOI PAS `CancellationReason`, QUI EXISTE DÉJÀ.
    ///
    /// Une commande en arbitrage n'est PAS annulée : elle est payée et la vente
    /// est encore récupérable — une course annulée se réattribue le plus souvent.
    /// Partager la colonne afficherait un motif d'annulation sur une vente
    /// vivante, et le jour où l'arbitrage conclut au remboursement, la DÉCISION
    /// écraserait la CAUSE. On veut pouvoir lire les deux.
    ///
    /// `UnderReviewSinceUtc` N'EST PAS DÉCORATIVE : c'est le tri de la file.
    /// Sans elle, la console ne peut ordonner que par date de commande, et un
    /// dossier bloqué depuis trois jours passe derrière un bloqué depuis dix
    /// minutes.
    ///
    /// Les deux sont NULLABLES : aucune commande existante n'a jamais été
    /// arbitrée, et un défaut mentirait sur elles.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(OrderingDbContext))]
    [Migration("20260821000000_CommandeEnArbitrage")]
    public partial class CommandeEnArbitrage : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                schema: "ordering",
                table: "orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnderReviewSinceUtc",
                schema: "ordering",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // CE RETOUR EN ARRIÈRE PERD DES DOSSIERS OUVERTS.
            //
            // Les commandes restées en « UnderReview » gardent leur statut — la
            // colonne est du texte, rien ne l'efface — mais plus personne ne sait
            // POURQUOI elles y sont ni depuis quand. Le code redéployé refusera
            // par ailleurs de lire « UnderReview » comme une valeur d'énumération
            // connue, et `ListAllOrdersQuery` les rendra sans filtre possible.
            //
            // Utilisable immédiatement après le déploiement, avant le premier
            // arbitrage. Passé ce point, il faut d'abord vider la file.
            migrationBuilder.DropColumn(
                name: "UnderReviewSinceUtc",
                schema: "ordering",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ReviewReason",
                schema: "ordering",
                table: "orders");
        }
    }
}
