# Audit Complet Reexecute - HBAExpress

Date: 2026-08-20
Mode: audit complet du repository courant, sans correction de code metier.

## Synthese Executable

Le repository contient maintenant les 31 services attendus dans `services/*/*`, les 3 BFF principaux et l'API Gateway. La structure par couches a nettement progresse sur Delivery et Food, avec des projets Domain/Application/Infrastructure/Api et des protos gRPC pour la plupart des services.

La production reste bloquee. P0.1 a partiellement corrige ReturnRefund -> Order: le RPC `GetOrderReturnContext` existe dans `shared/proto/order/v1/order.proto`, le serveur Order l'expose via `OrderingGrpcService`, et ReturnRefund appelle maintenant `IOrderingModuleApi`. P0.2 branche maintenant ReturnRefund -> Payment en gRPC avec `RefundPayment`, une table `payment_refunds` et une idempotence DB. Mais les lots restent PARTIAL: E2E CreateReturn non execute, `DeliveredAtUtc` et quantites deja retournees approximatifs, et reconciliation PSP apres timeout non generalisee.

## Inventaire

TOTAL SERVICES: 31

Common: billing, identity, media, notification, payment, promotion, recommendation, review, user, wallet, wishlist.

Marketplace: cart, catalog, inventory, order, return-refund, seller.

Food: availability, food-cart, food-order, kitchen-prep, menu, restaurant, review.

Delivery: delivery-pricing, delivery, dispatch, driver, proof-of-delivery, route, tracking.

Apps: api-gateway, client-bff, seller-bff, driver-bff.

Tests detectes: 18 projets `.csproj` sous `tests/` et `apps/*/tests`.

Migrations detectees: 26 dossiers `Migrations`.

## Changements depuis le premier audit

P0.1 - ReturnRefund <-> Order:

- `GetOrderReturnContext` ajoute au proto Order.
- `Order.PaymentId` ajoute et alimente depuis `PaymentCapturedIntegrationEvent`.
- Migration `20260824000000_AddOrderPaymentId`.
- `ReturnRefund.Infrastructure/Grpc/OrderClient/OrderGrpcClient.cs` n'est plus un placeholder.
- Builds Order API et ReturnRefund API verts.

Statut: PARTIAL, documente dans `docs/audit/IMPLEMENTATION_STATUS.md`.

## Findings Prioritaires

### HIGH - P0.2 Payment Refund gRPC partiel

Evidence: `shared/proto/financial/v1/financial.proto` expose le contexte refund, `FinancialGrpcService` appelle `RefundPaymentCommand`, `PaymentGrpcClient` ReturnRefund appelle Financial gRPC, et `20260824010000_AddPaymentRefunds` ajoute `payment_refunds`.

Impact: une demande de retour peut maintenant executer un remboursement via Payment gRPC avec idempotence DB. Le risque residuel est la reconciliation provider-level apres timeout et les tests E2E/contract manquants.

### CRITICAL - BFF persona encore squelettes

Evidence: `apps/client-bff/src/HBA.ClientBff.Api/Program.cs:3`, idem seller/driver.

Impact: les applications client, vendeur et livreur ne disposent pas encore de facade utilisable pour les parcours complets.

### CRITICAL - Driver non durable et identite hardcodee

Evidence: `DriverStore.cs:9` a des `ConcurrentDictionary`; `DriverStore.cs:13` expose `DefaultDriverId`.

Impact: profils, vehicules, disponibilite et isolation utilisateur non fiables.

### CRITICAL - Dispatch non durable et candidats hardcodes

Evidence: `DispatchStore.cs:9-11` stocke jobs/assignments/candidates en memoire; `DispatchStore.cs:138-142` construit deux candidats hardcodes.

Impact: affectation livreur non fiable, pas de concurrence transactionnelle, pas de recovery.

### HIGH - Payment PSP encore simule par defaut dans plusieurs chemins

Evidence: `SimulatedPaymentGateway.cs:41`, `:49`, `:80`, `:85`.

Impact: encaissement, statut et refund ne sont pas productionnels tant que les adapters reels/sandbox reels ne sont pas imposes.

### HIGH - Food Availability et Kitchen en memoire

Evidence: `AvailabilityStore.cs:8`, `KitchenStore.cs:8`.

Impact: saga Food non durable: disponibilites et tickets cuisine disparaissent au redemarrage.

### HIGH - Delivery endpoints a durcir

Evidence: endpoints Delivery maps via `app.MapGroup("/api/v1/...")` et `app.MapGroup("/internal/v1/...")`. Le socle `AddHbaService` protege par fallback, mais les policies par role/resource restent a verifier et completer, notamment driver assigne/client proprietaire/internal service audience.

### HIGH - Outbox/Inbox non uniforme

Le socle existe (`AddOutboxProcessor`, `ModuleDbContext`), mais les services encore en memoire publient des evenements sans transaction durable. Cela concerne surtout Driver, Dispatch, Tracking, Proof, Route et plusieurs services Food simples.

### MEDIUM - Tests incomplets pour les nouveaux domaines

Des tests racine existent et Order Authorization est vert, mais il n'y a pas encore de projets contract/integration/E2E pour la majorite des services Delivery/Food/ReturnRefund.

### MEDIUM - Worktree tres charge

Le worktree contient de nombreux renames/suppressions/ajouts non stages des travaux precedents. Risque: un audit ou build global melange des changements non lies.

## gRPC Matrix Actualisee

| Consumer | Provider | RPC | Etat | Priorite |
|---|---|---|---|---|
| ReturnRefund | Order | GetOrderReturnContext | PARTIAL, build green | P0.1 |
| ReturnRefund | Payment | RefundPayment | Missing/unconfigured | P0.2 |
| ReturnRefund | Inventory | ProcessReturnedStock | Factice/no-op a verifier | P1 |
| ReturnRefund | Delivery | CreateReturnDelivery | Factice | P1 |
| Delivery | Pricing | ValidateQuote/CreateQuote | Partiel | P0/P1 |
| Delivery | Dispatch | RequestDispatch | Partiel | P1 |
| Dispatch | Driver | CheckDriverEligibility | Contrat present, usage non durable | P0/P1 |
| Dispatch | Route | EstimateRoute | Contrat present, usage non durable | P0/P1 |
| FoodOrder | Payment | Create/Capture | Partiel, payment simule | P0 |
| FoodOrder | Delivery | CreateDelivery | Partiel | P1 |

## Kafka Matrix Actualisee

Les contrats et producteurs existent dans plusieurs domaines, mais la fiabilite depend de la persistance locale:

- Stable/plus mature: Order, Catalog, Cart, Inventory, Seller, Payment, User, Media avec DbContext/outbox.
- Partiel: ReturnRefund avec outbox mais integrations externes encore incompletes.
- Non durable: Driver, Dispatch, Tracking, Proof, Route, Availability, Kitchen, Menu, Food Review selon les stores memoire detectes.

## Database Audit

Le schema Order a ete etendu par `PaymentId`. Ce point est necessaire pour P0.2.

Defauts persistants:

- Driver/Dispatch: aucune persistance productionnelle.
- Food Availability/Kitchen/Menu/Review: stores memoire.
- Tracking/Proof/Route: stores memoire a confirmer/remplacer.
- ReturnRefund: persistance presente, mais E2E non valide et clients Payment/Inventory/Delivery non termines.

## Security Audit

Points positifs:

- `AddHbaService` pose un fallback auth.
- Les health checks et gRPC internes passent par conventions partagees.
- Order Authorization tests verts: 8/8.

Risques restants:

- BFF squelettes, donc pas de controles persona applicatifs.
- Driver `/me` base sur `DefaultDriverId`, pas claims JWT.
- `/internal/v1/*` doit etre audite endpoint par endpoint pour audience/scope service.
- Payment webhooks sont `AllowAnonymous`, ce qui est normal pour PSP, mais exige secrets/signatures reels et interdiction de simulation prod.

## Sagas

Client Marketplace: PARTIAL. P0.1 avance le retour, mais P0.2/P0.3 bloquent refund reel.

Client Food: PARTIAL. Availability/Kitchen/Delivery chain non durable.

Seller: PARTIAL. Seller core existe, BFF absent, returns/refunds partiels.

Seller Member: PARTIAL. RBAC transversal/store membership a formaliser et tester.

Driver: BROKEN pour production. Driver/Dispatch en memoire et identite hardcodee.

Admin: PARTIAL. Pas d'admin-bff clair; operations sensibles dispersees.

## Tests Executes Lors De La Reexecution

- `dotnet build services/marketplace/order-service/src/HBA.Order.Api/HBA.Order.Api.csproj --no-restore --disable-build-servers /m:1`: green.
- `dotnet build services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Api/HBA.Marketplace.ReturnRefund.Api.csproj --no-restore --disable-build-servers /m:1`: green.
- `dotnet test tests/HBA.Order.AuthorizationTests/HBA.Order.AuthorizationTests.csproj --no-restore --disable-build-servers /m:1`: green, 8 tests.
- `dotnet test tests/HBA.Merchants.IntegrationTests/HBA.Merchants.IntegrationTests.csproj`: failed before reaching the Order fake because migration existing issue `column "TraceParent" of relation "outbox_messages" already exists`.

## Totaux

TOTAL ISSUES: 13

CRITICAL: 5

HIGH: 6

MEDIUM: 2

LOW: 0

BROKEN SAGAS: 1

PARTIAL SAGAS: 5

COHERENT SAGAS: 0

## Top 10 Production Blockers

1. Payment Refund gRPC partiel: E2E/contract/reconciliation PSP manquants.
2. Gateways PSP encore simulees ou non imposees en prod.
3. Driver-service en memoire et `DefaultDriverId`.
4. Dispatch-service en memoire et candidats hardcodes.
5. BFF persona squelettes.
6. Food Availability/Kitchen non durables.
7. Tracking/Proof/Route non durables.
8. Outbox/Inbox non uniforme sur services a etat.
9. Resource authorization Delivery/SellerMember a finaliser.
10. Tests contract/integration/E2E insuffisants et migration `TraceParent` bloquante dans Merchants tests.

## Prochaine Action Recommandee

Ne pas marquer P0.1/P0.2 DONE tant que l'acceptance fonctionnelle n'est pas verte. Le plus pragmatique:

1. Fermer l'E2E CreateReturn avec contexte Order reel.
2. Corriger le blocage de migration `TraceParent` dans les tests Merchants.
3. Ajouter les contract tests ReturnRefund -> Payment et la reconciliation provider-level.
