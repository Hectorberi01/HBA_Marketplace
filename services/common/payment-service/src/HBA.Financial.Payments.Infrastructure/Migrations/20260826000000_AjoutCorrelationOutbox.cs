using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Payments.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA CORRÉLATION MÉTIER TRAVERSE ENFIN L'OUTBOX (§11 gRPC).
    ///
    /// CE QUI ÉTAIT PERDU, ET OÙ EXACTEMENT.
    ///
    /// `x-correlation-id` était bien propagé sur HTTP et sur gRPC — l'intercepteur
    /// le recopie. Mais l'outbox est une frontière ASYNCHRONE : le message part
    /// plusieurs secondes plus tard, dans un service d'arrière-plan qui n'a plus
    /// rien de la requête d'origine. Tout ce qui n'est pas écrit en base à
    /// l'insertion est perdu là.
    ///
    /// `TraceParent` l'avait compris et voyage depuis le lot précédent. La
    /// corrélation, non : `KafkaIntegrationEventPublisher` retombait sur
    /// `Activity.Current.TraceId`. Une valeur cohérente, propagée — et qui n'a
    /// AUCUN rapport avec le `meta.requestId` que l'utilisateur a sous les yeux et
    /// recopie dans un signalement au support.
    ///
    /// Conséquence : un incident traversant trois services n'était pas
    /// reconstituable à partir de ce que la personne pouvait citer.
    ///
    /// CE N'EST PAS UN DOUBLON DE `TraceParent`. Les deux répondent à des
    /// questions différentes — l'un sert l'exploitant qui ouvre une trace, l'autre
    /// la personne qui écrit au support — et aucun ne remplace l'autre.
    ///
    /// NULLABLE, ET SANS REPRISE. Les messages déjà écrits n'ont pas de
    /// corrélation et ne peuvent pas en recevoir une après coup : l'appel qui les a
    /// produits est terminé. Une valeur par défaut leur en inventerait une qui ne
    /// mène nulle part.
    ///
    /// `IF NOT EXISTS` : la même prudence que pour `TraceParent`. Une base déjà
    /// alignée à la main ne doit pas faire échouer le démarrage.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(HBA.Financial.Payments.Infrastructure.Persistence.PaymentsDbContext))]
    [Migration("20260826000000_AjoutCorrelationOutbox")]
    public partial class AjoutCorrelationOutbox : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"ALTER TABLE payments.outbox_messages ADD COLUMN IF NOT EXISTS ""CorrelationId"" character varying(100);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"ALTER TABLE payments.outbox_messages DROP COLUMN IF EXISTS ""CorrelationId"";");
        }
    }
}
