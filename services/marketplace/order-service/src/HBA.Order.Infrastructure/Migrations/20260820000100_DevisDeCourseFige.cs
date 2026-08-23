using HBA.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Orders.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE DEVIS DE COURSE QUI A FIXÉ LES FRAIS DE LIVRAISON.
    ///
    /// SANS LUI, LE CLIENT ET LA PLATEFORME N'ACHÈTENT PAS LA MÊME COURSE.
    ///
    /// Les frais d'un repas sont chiffrés au checkout, à la distance réelle. La
    /// course, elle, n'est créée que lorsque le sac est prêt — vingt à quarante
    /// minutes plus tard. Redemander un devis à ce moment produit un SECOND prix,
    /// qui peut différer : grille tarifaire éditée, zone redécoupée, version de
    /// tarification incrémentée.
    ///
    /// Le client aurait alors payé un montant et la plateforme en aurait acheté un
    /// autre, sans que l'écart soit mesuré nulle part. Figer l'identifiant du
    /// devis fait créer la course AU PRIX DÉJÀ PAYÉ.
    ///
    /// NULLABLE : la marchandise n'en a pas.
    ///
    /// Ses frais restent un forfait choisi parmi ceux que le serveur propose, sans
    /// devis. Et les commandes de repas antérieures à ce mécanisme n'en ont pas
    /// non plus : l'adaptateur redemande alors un devis, comme avant.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(OrderingDbContext))]
    [Migration("20260820000100_DevisDeCourseFige")]
    public partial class DevisDeCourseFige : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
            => migrationBuilder.AddColumn<string>(
                name: "DeliveryQuoteId",
                schema: "ordering",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

        protected override void Down(MigrationBuilder migrationBuilder)
            // Les commandes en cours perdent le lien vers leur devis : leur course
            // sera créée sur un devis neuf, au prix du moment.
            => migrationBuilder.DropColumn(
                name: "DeliveryQuoteId",
                schema: "ordering",
                table: "orders");
    }
}
