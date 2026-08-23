using HBA.Communication.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Communication.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES PIÈCES JOINTES DE DISCUSSION DEVIENNENT PRIVÉES.
    ///
    /// CETTE MIGRATION ACCOMPAGNE UN CORRECTIF DE SÉCURITÉ.
    ///
    /// Les pièces jointes étaient déposées dans un bucket PUBLIC et leur adresse
    /// permanente recopiée dans le message : photo d'un colis, facture, capture de
    /// virement, parfois une pièce d'identité réclamée par un vendeur — tout cela
    /// se lisait SANS COMPTE, par quiconque connaissait l'URL.
    ///
    /// LES PIÈCES EXISTANTES RESTENT PUBLIQUES, ET IL FAUT LE SAVOIR.
    ///
    /// Leurs octets sont dans un bucket public et cette migration ne les déplace
    /// pas : leur URL survit sous `LegacyUrl` et continue de fonctionner — donc la
    /// fuite reste ouverte POUR ELLES. Fermer aussi ce cas suppose de recopier
    /// chaque fichier vers le stockage privé puis d'effacer l'original : un
    /// travail distinct, à décider, et le seul qui refermera complètement.
    ///
    /// Ce qui est acquis dès ce déploiement : plus aucune NOUVELLE pièce jointe
    /// n'obtient d'adresse publique.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [DbContext(typeof(MessagingDbContext))]
    [Migration("20260817000000_PiecesJointesPrivees")]
    public partial class PiecesJointesPrivees : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Renommage, pas suppression : cette URL est la SEULE chose qui
            // désigne encore les fichiers déjà envoyés. La perdre effacerait des
            // preuves de litige en cours.
            migrationBuilder.RenameColumn(
                name: "Url",
                schema: "messaging",
                table: "message_attachments",
                newName: "LegacyUrl");

            // Les pièces déposées après la bascule n'ont pas d'URL : la colonne
            // ne peut plus être obligatoire. Sans cet assouplissement, le premier
            // envoi échouerait après un téléversement réussi — au pire moment.
            migrationBuilder.AlterColumn<string>(
                name: "LegacyUrl",
                schema: "messaging",
                table: "message_attachments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaId",
                schema: "messaging",
                table: "message_attachments",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            // CET INDEX PORTE UN CONTRÔLE DE SÉCURITÉ, PAS UN AFFICHAGE.
            //
            // « Ce média est-il dans cette conversation ? » est posée à chaque
            // ouverture d'une pièce jointe. Sans index, elle balaie toutes les
            // pièces jointes de la plateforme.
            migrationBuilder.CreateIndex(
                name: "IX_message_attachments_MediaId",
                schema: "messaging",
                table: "message_attachments",
                column: "MediaId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_message_attachments_MediaId",
                schema: "messaging",
                table: "message_attachments");

            migrationBuilder.DropColumn(
                name: "MediaId",
                schema: "messaging",
                table: "message_attachments");

            // LES PIÈCES DÉPOSÉES APRÈS LA BASCULE N'ONT PAS D'URL : les mettre
            // à la chaîne vide pour rétablir la contrainte les rend définitivement
            // introuvables. Ce Down n'est utilisable qu'immédiatement après le
            // déploiement, avant tout nouvel envoi.
            migrationBuilder.Sql(
                "UPDATE messaging.message_attachments SET \"LegacyUrl\" = '' WHERE \"LegacyUrl\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "LegacyUrl",
                schema: "messaging",
                table: "message_attachments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "LegacyUrl",
                schema: "messaging",
                table: "message_attachments",
                newName: "Url");
        }
    }
}
