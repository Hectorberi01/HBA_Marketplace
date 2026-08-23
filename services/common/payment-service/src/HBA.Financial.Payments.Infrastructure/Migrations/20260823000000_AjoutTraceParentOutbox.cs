using HBA.Financial.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Payments.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA COLONNE `TraceParent` DE L'OUTBOX — DÉCLARÉE PARTOUT, CRÉÉE NULLE PART.
    ///
    /// LE MODÈLE LA CONNAISSAIT, LES INSTANTANÉS AUSSI, AUCUNE MIGRATION NE
    ///    LA POSAIT.
    ///
    /// `OutboxMessage.TraceParent` existe, `OutboxConfiguration` la mappe, et les
    /// `*ModelSnapshot.cs` la portent. Mais l'instantané n'est pas du DDL : il
    /// décrit l'état ATTENDU, il ne le construit pas. Entre les deux il manquait
    /// le seul fichier qui écrit vraiment dans la base.
    ///
    /// Rien ne l'a signalé, et c'est ce qui rend le défaut intéressant :
    ///
    ///   • la compilation réussit — la propriété est en C#, la colonne en SQL ;
    ///   • `dotnet ef migrations add` n'aurait rien produit, l'instantané étant
    ///     déjà d'accord avec le modèle : c'est LUI qui sert de référence, et il
    ///     avait été mis à jour à la main ;
    ///   • `check-migrations.py` ne l'a pas vu non plus — il vérifie que chaque
    ///     TABLE configurée a une migration qui la crée, pas chaque COLONNE ;
    ///   • le service démarre, sert ses routes, et écrit en base sans broncher.
    ///
    /// Le premier symptôme arrive cinq secondes après le démarrage, dans le
    /// processeur d'outbox — un service de fond dont l'échec ne coupe aucune
    /// requête :
    ///
    ///     42703: column o.TraceParent does not exist
    ///
    /// Autrement dit : AUCUN ÉVÉNEMENT D'INTÉGRATION NE SORT. Le paiement
    /// n'atteint pas la commande, la commande n'atteint pas la cuisine, et les
    /// journaux répètent la même erreur toutes les cinq secondes sur vingt et une
    /// bases à la fois.
    ///
    /// NULLABLE, ET SANS REPRISE.
    ///
    /// Les messages déjà écrits n'ont pas de contexte de trace et ne peuvent pas
    /// en recevoir un après coup : l'appel qui les a produits est terminé. Une
    /// valeur par défaut leur inventerait une trace qui ne mène nulle part.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(PaymentsDbContext))]
    [Migration("20260823000000_AjoutTraceParentOutbox")]
    public partial class AjoutTraceParentOutbox : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE payments.outbox_messages ADD COLUMN IF NOT EXISTS ""TraceParent"" character varying(64);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE payments.outbox_messages DROP COLUMN IF EXISTS ""TraceParent"";");
        }
    }
}
