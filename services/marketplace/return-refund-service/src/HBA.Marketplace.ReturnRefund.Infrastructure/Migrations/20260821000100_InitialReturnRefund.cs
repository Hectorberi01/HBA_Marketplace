using System;
using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Migrations
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// MIGRATION INITIALE — LE SCHÉMA N'EXISTAIT NULLE PART.
    ///
    /// `ReturnRefundDbContext` déclarait onze tables — l'agrégat de retour, ses six
    /// collections, les remboursements, leurs tentatives, les clés d'idempotence —
    /// avec leurs longueurs, leurs précisions et leurs index. Et le dossier
    /// `Migrations` n'existait pas.
    ///
    /// CE N'ÉTAIT PAS UN OUBLI SANS CONSÉQUENCE. `Program.cs` appelle `Migrate()`
    /// au démarrage : sans migration, l'appel ne crée que `__EFMigrationsHistory` et
    /// rend la main. Le service démarrait donc NORMALEMENT, en paraissant sain, et
    /// c'est la première requête métier qui rendait « relation … does not exist ».
    /// Un démarrage réussi sur une base vide est le pire des deux mondes.
    ///
    /// le contrôle `migrations` listait les neuf tables manquantes depuis le
    /// début. Personne ne lisait sa sortie.
    ///
    /// ÉCRITE À LA MAIN : attributs `[DbContext]` + `[Migration]` (sans eux EF ne
    /// la charge pas), pas de `.Designer.cs`, snapshot tenu à jour dans le même geste.
    /// ═════════════════════════════════════════════════════════════════════════
    ///
    /// <para>
    /// <b>CRÉER CE SCHÉMA NE REND PAS LE SERVICE SÛR.</b> Aucune de ses routes ne
    /// vérifie l'appartenance : tout vendeur authentifié peut approuver, inspecter et
    /// chiffrer le remboursement du dossier d'un concurrent, et `sellerId` est lu dans
    /// la query string (ISSUE-017, ISSUE-018, ISSUE-019). Aucune route de la passerelle
    /// ne mène ici aujourd'hui — c'est la seule chose qui limite les dégâts.
    /// <b>Ne pas router ce service avant le lot 1.2 du plan de correction.</b>
    /// </para>
    ///
    /// <para>
    /// <b>`audit_entries` est créée ici</b> parce que ce contexte est l'un des trois
    /// du dépôt où `KeepsAuditTrail` vaut `true`. La table était donc déclarée, promise
    /// à l'exploitant, et absente de la base — l'audit d'un dossier de retour ne
    /// s'écrivait nulle part.
    /// </para>
    ///
    /// <para>
    /// <b>`refunds.IdempotencyKey` n'est PAS unique</b>, et c'est fidèle au modèle
    /// actuel : `RefundConfiguration` déclare la colonne sans index unique. C'est un
    /// défaut connu (deux remboursements possibles pour un même dossier) traité au
    /// lot 3.1, avec sa propre migration et son garde-fou anti-doublon. Le corriger
    /// ici ferait diverger la base du modèle.
    /// </para>
    /// </summary>
    [DbContext(typeof(ReturnRefundDbContext))]
    [Migration("20260821000100_InitialReturnRefund")]
    public partial class InitialReturnRefund : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "return_refund");

            // ─────────────────────────────────────────────────────────────────
            // Outbox du module, forme complète dès la création (réessai, lettres
            // mortes, `TraceParent`) : un module neuf n'a pas à rejouer la dette
            // des modules historiques.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "return_refund",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TraceParent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "return_refund",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                });

            // ─────────────────────────────────────────────────────────────────
            // L'agrégat. `PolicySnapshot` est un type possédé aplati dans la même
            // table sous des noms explicites (`policy_*`) : la politique de retour
            // en vigueur AU MOMENT de la demande, figée. Une politique qui change
            // ensuite ne doit pas réécrire l'histoire d'un dossier ouvert.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "return_requests",
                schema: "return_refund",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ResolutionRequested = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CustomerComment = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    EstimatedRefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ApprovedRefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ReturnShippingPayer = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    // Jeton de concurrence applicatif : un entier que le domaine
                    // incrémente lui-même. Ce n'est PAS un `rowVersion` — la
                    // configuration déclare `IsConcurrencyToken()` seul, sans
                    // génération par la base. Le marquer `rowVersion: true` ici
                    // ferait diverger la colonne du modèle.
                    Version = table.Column<int>(type: "integer", nullable: false),
                    policy_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    policy_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    policy_return_window_days = table.Column<int>(type: "integer", nullable: false),
                    policy_allow_return = table.Column<bool>(type: "boolean", nullable: false),
                    policy_allow_refund_only = table.Column<bool>(type: "boolean", nullable: false),
                    policy_require_evidence = table.Column<bool>(type: "boolean", nullable: false),
                    policy_require_inspection = table.Column<bool>(type: "boolean", nullable: false),
                    policy_restocking_fee_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "return_items",
                schema: "return_refund",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    SkuSnapshot = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NameSnapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OrderedQuantity = table.Column<int>(type: "integer", nullable: false),
                    DeliveredQuantity = table.Column<int>(type: "integer", nullable: false),
                    AlreadyReturnedQuantity = table.Column<int>(type: "integer", nullable: false),
                    RequestedQuantity = table.Column<int>(type: "integer", nullable: false),
                    ReceivedQuantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConditionDeclared = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ConditionInspected = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_return_items_return_requests_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "return_refund",
                        principalTable: "return_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_evidence",
                schema: "return_refund",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Caption = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_return_evidence_return_requests_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "return_refund",
                        principalTable: "return_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_shipments",
                schema: "return_refund",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveryId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Mode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_return_shipments_return_requests_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "return_refund",
                        principalTable: "return_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_inspections",
                schema: "return_refund",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    Condition = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Disposition = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    InspectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_inspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_return_inspections_return_requests_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "return_refund",
                        principalTable: "return_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_status_history",
                schema: "return_refund",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_status_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_return_status_history_return_requests_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "return_refund",
                        principalTable: "return_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ─────────────────────────────────────────────────────────────────
            // Remboursements. `Breakdown` est un type possédé qui possède lui-même
            // sept `Money` : chaque poste du calcul est écrit en toutes lettres
            // (`items_amount`, `restocking_fee_amount`…) plutôt que réduit à un
            // total. Un litige se tranche sur la décomposition, pas sur la somme.
            // ─────────────────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "refunds",
                schema: "return_refund",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderRefundId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    items_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    items_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    original_shipping_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    original_shipping_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    discount_allocation_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount_allocation_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    restocking_fee_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    restocking_fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    return_shipping_charge_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    return_shipping_charge_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    previous_refunds_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    previous_refunds_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refunds_return_requests_ReturnId",
                        column: x => x.ReturnId,
                        principalSchema: "return_refund",
                        principalTable: "return_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refund_attempts",
                schema: "return_refund",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RefundId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refund_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refund_attempts_refunds_RefundId",
                        column: x => x.RefundId,
                        principalSchema: "return_refund",
                        principalTable: "refunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "return_refund",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    ReturnRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => x.Key);
                });

            // ─── Index ────────────────────────────────────────────────────────

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "return_refund",
                table: "outbox_messages",
                columns: new[] { "NextAttemptAtUtc", "OccurredOnUtc" },
                filter: "\"ProcessedOnUtc\" IS NULL AND \"DeadLetteredOnUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_letters",
                schema: "return_refund",
                table: "outbox_messages",
                column: "DeadLetteredOnUtc",
                filter: "\"DeadLetteredOnUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_ActorUserId_OccurredOnUtc",
                schema: "return_refund",
                table: "audit_entries",
                columns: new[] { "ActorUserId", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_EntityType_EntityId_OccurredOnUtc",
                schema: "return_refund",
                table: "audit_entries",
                columns: new[] { "EntityType", "EntityId", "OccurredOnUtc" });

            // Le numéro de dossier est ce que le client cite au support : il doit
            // désigner un dossier et un seul.
            migrationBuilder.CreateIndex(
                name: "IX_return_requests_ReturnNumber",
                schema: "return_refund",
                table: "return_requests",
                column: "ReturnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_return_requests_OrderId",
                schema: "return_refund",
                table: "return_requests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_return_requests_CustomerId_CreatedAtUtc",
                schema: "return_refund",
                table: "return_requests",
                columns: new[] { "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_return_requests_SellerId_Status",
                schema: "return_refund",
                table: "return_requests",
                columns: new[] { "SellerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_return_items_ReturnId",
                schema: "return_refund",
                table: "return_items",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_return_items_OrderItemId",
                schema: "return_refund",
                table: "return_items",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_return_evidence_ReturnId",
                schema: "return_refund",
                table: "return_evidence",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_return_shipments_ReturnId",
                schema: "return_refund",
                table: "return_shipments",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_return_inspections_ReturnId",
                schema: "return_refund",
                table: "return_inspections",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_return_status_history_ReturnId_OccurredAtUtc",
                schema: "return_refund",
                table: "return_status_history",
                columns: new[] { "ReturnId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_refunds_ReturnId",
                schema: "return_refund",
                table: "refunds",
                column: "ReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_refund_attempts_RefundId",
                schema: "return_refund",
                table: "refund_attempts",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_keys_ReturnRequestId",
                schema: "return_refund",
                table: "idempotency_keys",
                column: "ReturnRequestId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "audit_entries", schema: "return_refund");
            migrationBuilder.DropTable(name: "idempotency_keys", schema: "return_refund");
            migrationBuilder.DropTable(name: "outbox_messages", schema: "return_refund");
            migrationBuilder.DropTable(name: "refund_attempts", schema: "return_refund");
            migrationBuilder.DropTable(name: "return_evidence", schema: "return_refund");
            migrationBuilder.DropTable(name: "return_inspections", schema: "return_refund");
            migrationBuilder.DropTable(name: "return_items", schema: "return_refund");
            migrationBuilder.DropTable(name: "return_shipments", schema: "return_refund");
            migrationBuilder.DropTable(name: "return_status_history", schema: "return_refund");
            migrationBuilder.DropTable(name: "refunds", schema: "return_refund");
            migrationBuilder.DropTable(name: "return_requests", schema: "return_refund");
        }
    }
}
