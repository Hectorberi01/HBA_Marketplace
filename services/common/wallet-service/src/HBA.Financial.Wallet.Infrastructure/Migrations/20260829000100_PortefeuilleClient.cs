using System;
using HBA.Financial.Wallet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Financial.Wallet.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE PORTEFEUILLE CLIENT ET SES DEMANDES DE VIREMENT (D33).
    ///
    /// CES DEUX TABLES N'EXISTAIENT PAS, ET LE CLIENT N'ÉTAIT DONC JAMAIS
    /// REMBOURSÉ.
    ///
    /// FedaPay n'expose AUCUNE API de remboursement — pas plus que MTN, Moov ou
    /// PayPal dans ce dépôt. Un retour validé, un remboursement décidé, et l'appel
    /// répondait `Success: false` : le dossier escaladait en `ManualReview` et
    /// l'argent ne revenait au client que si quelqu'un y pensait. Désormais le
    /// remboursement CRÉDITE `customer_wallets` — l'argent est rendu tout de
    /// suite, à l'intérieur de la plateforme — et le virement Mobile Money est une
    /// DEMANDE distincte (`customer_withdrawals`), exécutée et marquée payée à la
    /// main par un administrateur.
    ///
    /// CE QUE CES TABLES INTRODUISENT ET QU'IL FAUT DIRE : UNE DETTE.
    ///
    /// La somme de `customer_wallets."AvailableBalance"` est de l'argent DÛ. Il
    /// n'existe aucun rapprochement entre ce total et la trésorerie réelle, et
    /// aucune règle de péremption n'est posée — un solde de portefeuille est une
    /// créance, et y toucher sans avis juridique n'est pas une décision
    /// d'ingénierie. Les deux points sont ouverts et nommés en D33.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES TROIS INDEX D'UNICITÉ NE SONT PAS DÉCORATIFS.
    ///
    /// `IX_customer_wallets_CustomerId` — UN client, UN portefeuille. Le service
    /// de mutation crée le portefeuille quand il n'en trouve pas ; deux
    /// remboursements simultanés sur un client qui n'en a pas encore en créeraient
    /// DEUX, et l'un des deux soldes deviendrait invisible. C'est de l'argent dû
    /// au client qui disparaît sans erreur nulle part.
    ///
    /// `IX_customer_withdrawals_IdempotencyKey` — un double-clic retiendrait DEUX
    /// fois le solde du client et poserait deux lignes identiques dans la file
    /// d'administration, que rien ne permettrait de distinguer.
    ///
    /// `ux_wallet_transactions_customer_refund_credit` — le registre de rejeu du
    /// crédit. Le gestionnaire consulte le grand livre avant d'écrire, mais entre
    /// sa lecture et son écriture deux rejeux simultanés peuvent tous deux se
    /// croire premiers : seule la base ferme cette fenêtre. Il est PARTIEL, et son
    /// filtre nomme la colonne en PascalCase entre guillemets doubles — ce projet
    /// n'applique AUCUNE convention snake_case (vérifié dans le ModelSnapshot).
    ///
    /// LE TYPE DE RÉFÉRENCE EST `customer_refund_credit`, PAS `customer_refund`.
    ///
    /// `customer_refund` désigne déjà le COÛT plateforme d'un versement MoMo direct
    /// (`AccrueCustomerRefundAsync`). Les confondre ferait entrer deux flux dans la
    /// même contrainte — c'est exactement ce qui avait fait sauter
    /// `ux_wallet_transactions_driver_earning` au PREMIER paiement de livreur.
    /// ═════════════════════════════════════════════════════════════════════════
    ///
    /// AUCUNE REPRISE DE DONNÉES, ET IL FAUT LE SAVOIR.
    ///
    /// Les deux tables naissent vides. Les remboursements ANTÉRIEURS à cette
    /// migration — ceux qui ont escaladé en `ManualReview` faute de canal — ne s'y
    /// trouvent pas et ne s'y trouveront pas : rien dans wallet-service ne les
    /// connaît, et une migration ne peut pas lire la base de return-refund. Sur une
    /// base déjà exploitée, ces dossiers-là restent à traiter à la main.
    ///
    /// <para>
    /// Attributs `[DbContext]` + `[Migration]` sur la classe, pas de fichier
    /// `.Designer.cs` : convention du dépôt pour les migrations écrites à la main.
    /// S'il en manque un, EF ignore la migration EN SILENCE — les tables n'existent
    /// jamais, et le premier remboursement tombe sur « relation does not exist »
    /// APRÈS que le paiement a été encaissé.
    /// </para>
    ///
    /// <para>
    /// Le nom de la classe de contexte est `WalletDbContext` (le FICHIER
    /// s'appelle `SettlementDbContext.cs`, la classe non). Écrire
    /// `[DbContext(typeof(SettlementDbContext))]` ne compilerait pas.
    /// </para>
    /// </summary>
    [DbContext(typeof(WalletDbContext))]
    [Migration("20260829000100_PortefeuilleClient")]
    public partial class PortefeuilleClient : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_wallets",
                schema: "settlement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    AvailableBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    LifetimeRefunded = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),

                    // Verrou optimiste. `xmin` est une colonne SYSTÈME de PostgreSQL : on
                    // ne l'ajoute pas, on la LIT. Sans elle, deux crédits simultanés
                    // lisent le même solde et le second écrase le premier — un
                    // remboursement que le client ne reçoit jamais.
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_wallets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customer_withdrawals",
                schema: "settlement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),

                    // Destination FIGÉE à la demande, NON NULLABLE : c'est elle, et rien
                    // d'autre, que l'administrateur recopie chez le prestataire. Les
                    // colonnes équivalentes de `withdrawals` sont nullables par dette —
                    // des demandes créées avant leur existence — et c'est ce repli sur
                    // « le compte courant » qui rouvrait la faille côté vendeur.
                    Msisdn = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),

                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),

                    // La référence du virement saisie par l'administrateur : la SEULE
                    // preuve que l'argent est parti. Nullable, car elle n'existe qu'une
                    // fois la demande payée.
                    ExternalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AdminNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),

                    // 180 caractères : la même borne que `customer_refunds` et
                    // `payments.payment_refunds`.
                    IdempotencyKey = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_withdrawals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_wallets_CustomerId",
                schema: "settlement",
                table: "customer_wallets",
                column: "CustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_withdrawals_CustomerId",
                schema: "settlement",
                table: "customer_withdrawals",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_withdrawals_Status",
                schema: "settlement",
                table: "customer_withdrawals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_customer_withdrawals_IdempotencyKey",
                schema: "settlement",
                table: "customer_withdrawals",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_wallet_transactions_customer_refund_credit",
                schema: "settlement",
                table: "wallet_transactions",
                columns: new[] { "ReferenceType", "ReferenceId" },
                unique: true,
                filter: "\"ReferenceType\" = 'customer_refund_credit'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_wallet_transactions_customer_refund_credit",
                schema: "settlement",
                table: "wallet_transactions");

            migrationBuilder.DropTable(
                name: "customer_withdrawals",
                schema: "settlement");

            migrationBuilder.DropTable(
                name: "customer_wallets",
                schema: "settlement");
        }
    }
}
