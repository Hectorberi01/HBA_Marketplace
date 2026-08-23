using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Engagement.Reviews.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA NOTE D'UN VENDEUR SE CALCULAIT PAR BALAYAGE COMPLET (§4).
    ///
    /// `reviews` portait un index sur `ProductId` et aucun sur `SellerId`. Or deux
    /// requêtes filtrent dessus, et la plus chaude de loin est
    /// `GetSellerRatingAsync` — la note moyenne, affichée sur chaque fiche produit
    /// et chaque liste d'offres. Elle balayait TOUS les avis de la plateforme pour
    /// en garder ceux d'un vendeur.
    ///
    /// `(SellerId, Status)` ET NON `SellerId` SEUL.
    ///
    /// La note ne compte que les avis `Published`. Avec la seule colonne,
    /// PostgreSQL remonte aussi les avis en modération et les rejette ensuite —
    /// sur un vendeur populaire, la moitié du travail est faite pour rien. Le
    /// préfixe `SellerId` sert de toute façon la seconde requête, le carnet d'avis.
    ///
    /// CE QUE CET INDEX NE CORRIGE PAS : la requête charge encore TOUTES les
    /// notes en mémoire pour en faire la moyenne, au lieu d'un `AVG()` exécuté par
    /// la base. L'index rend la lecture ciblée ; il ne la rend pas bornée. C'est le
    /// lot 8.4 — et l'index y restera utile, puisqu'un `AVG()` filtré s'appuiera
    /// exactement dessus.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Engagement.Reviews.Infrastructure.Persistence.ReviewsDbContext))]
    [Migration("20260902000500_NoteVendeurIndexee")]
    public partial class NoteVendeurIndexee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_reviews_SellerId_Status",
                schema: "reviews",
                table: "reviews",
                columns: new[] { "SellerId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reviews_SellerId_Status",
                schema: "reviews",
                table: "reviews");
        }
    }
}
