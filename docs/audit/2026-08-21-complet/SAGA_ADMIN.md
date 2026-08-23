# Saga — Parcours d'administration de la plateforme

Analyse statique. Chaque constat cite un chemin relatif, une classe/méthode et un numéro
de ligne. Ce rapport couvre : connexion admin et RBAC, validation des vendeurs (KYB),
modération des produits, des restaurants et des avis, arbitrage des retours, validation
des livreurs, règles de tarification de livraison.

---

## 0. Résumé exécutable

| Parcours d'administration | Route + policy | Écrit en base | Déclenche en aval | Trace d'audit |
|---|---|---|---|---|
| Connexion admin / RBAC | `/api/identity/*`, `RequireRole("Admin")` | oui | jeton JWT | **non** (`IdentityDbContext` sans audit) |
| Validation vendeur (KYB) | `/api/v1/merchants/{id}/kyb/*`, Admin/Moderator | oui | événements consommés (rôle, catalogue, notification) | **oui** — seul cas du dépôt |
| Modération produits | `/api/v1/catalog/admin/products/*`, Admin/Moderator | oui | événements publiés et consommés | **non** |
| Modération restaurants | `/api/food/admin/restaurants/*`, Admin/Moderator | oui | `RestaurantApproved` → rôle `FoodPartner` | **non** |
| Modération avis | `/api/engagement/reviews/{id}/*`, Admin/Moderator | oui | `ReviewRejected`/`ReviewPublished` → notes | **non** |
| Arbitrage des retours | `/api/v1/admin/returns/*`, Admin/Moderator | **non — le schéma n'existe pas** | rien | déclarée, table jamais créée |
| Validation des livreurs | **AUCUNE ROUTE** | — | — | — |
| Tarification de livraison | `/api/v1/admin/delivery-pricing/*`, **AUCUNE POLICY** | oui | événements publiés | **non** |

---

## 1. Connexion admin et RBAC

**Point d'entrée** : `services/common/identity-service/src/HBA.Identity.Api/Endpoints/IdentityEndpoints.cs:65`
`POST /api/v1/auth/login` (`AllowAnonymous`), suivi de `POST /api/v1/auth/verify-otp` (`:104`)
pour la MFA. Le jeton est produit par
`HBA.Identity.Infrastructure/Security/JwtTokenGenerator.cs`.

**Qui peut appeler quoi.** Deux groupes administrateurs :
- `IdentityEndpoints.cs:455-465` — `/api/identity/users` : `MapAdminGroup` **puis**
  `.RequireAuthorization(policy => policy.RequireRole("Admin"))`. Les politiques
  s'additionnent (`shared/common/HBA.Shared.Hosting/Http/ApiAuthorization.cs:68-74`), donc
  l'effet est bien `Admin` seul — les modérateurs sont exclus. Correct.
- `IdentityEndpoints.cs:505-515` — `/api/identity/roles` : idem.

**Défaut structurel : le catalogue de permissions est décoratif.**
`IdentityDataSeeder.SeedDefaultRolesAsync` (`.../Persistence/IdentityDataSeeder.cs:33-64`)
attribue des permissions aux rôles (`users.manage`, `catalog.moderate`,
`deliveries.accept`…), et `JwtTokenGenerator.cs:80` les place dans le jeton sous le type
de revendication `permission` (`:17`).
**Aucun code du dépôt ne lit jamais cette revendication** : recherche exhaustive de
`PermissionClaimType` et de la chaîne `"permission"` hors du générateur → **0 résultat**.
Toute l'autorisation plateforme repose sur trois noms de rôle (`Admin`, `Moderator`,
`Seller`, `ApiAuthorization.cs:28-30`). Un rôle personnalisé créé par
`POST /api/identity/roles` avec des permissions fines ne changera **rien** aux droits
effectifs de son porteur. **Sévérité : HIGH.**

**Défaut : aucune ré-authentification n'est exigée pour une action de plateforme.**
`RequiresStepUp` (`services/marketplace/seller-service/src/HBA.Merchants.Contracts/MerchantCapabilities.cs:200`)
n'est consulté que sur des capacités **vendeur** (4 sites :
`CatalogEndpoints.cs:501` et `:547`, `InventoryEndpoints.cs:289`,
`FinancialEndpoints.cs:738`). Approuver un KYB, suspendre un vendeur, rejeter un produit,
lancer un règlement : aucune de ces actions ne demande de ré-authentification récente.
**Sévérité : MEDIUM.**

**Trois rôles semés et jamais exigés** : `Driver`, `Dispatcher`, `FoodPartner`
(`ApiAuthorization.cs:46-48`, `IdentityDataSeeder.cs:54-63`). `Dispatcher` est le seul
mentionné dans une policy (`MapOperationsGroup`, `ApiAuthorization.cs:118`) mais n'est
attribué par aucun chemin automatique.

---

## 2. Validation des vendeurs (KYB → approbation / rejet / suspension)

**Point d'entrée** : `services/marketplace/seller-service/src/HBA.Merchants.Api/Endpoints/MerchantEndpoints.cs:164-172`
```
var governance = app.MapAdminGroup("/api/v1/merchants");
governance.MapPost("/{sellerId:guid}/kyb/approve", ApproveKybAsync);
governance.MapPost("/{sellerId:guid}/kyb/reject", RejectKybAsync);
governance.MapPost("/{sellerId:guid}/activate", ActivateSellerAsync);
governance.MapPost("/{sellerId:guid}/suspend", SuspendSellerAsync);
governance.MapPost("/{sellerId:guid}/lift-suspension", LiftSuspensionAsync);
governance.MapPost("/{sellerId:guid}/reactivation/approve", ApproveReactivationAsync);
governance.MapDelete("/{sellerId:guid}", DeleteSellerAsync);
```
**Qui peut appeler** : `Admin` **ou** `Moderator` (`ApiAuthorization.cs:60-62`). Un
modérateur peut donc valider un dossier d'identité d'entreprise et supprimer un vendeur —
choix discutable, non signalé dans le code.

**États avant/après** (`HBA.Merchants.Domain/Sellers/Seller.cs`) :

| Action | Méthode | Avant → Après |
|---|---|---|
| approuver KYB | `ApproveKyb:344` | `KybStatus.InReview` → `Verified` (+ toutes les pièces `MarkVerified`) |
| rejeter KYB | `RejectKyb:382` | `InReview` → `Rejected`, **et** si `SellerStatus == Active` → `Suspended` (`:399-410`) |
| activer | `Activate:432` | `Pending`/`Suspended` → `Active` (exige `KybStatus == Verified`) |
| suspendre | `Suspend:473` | `Active` → `Suspended` |
| lever | `LiftSuspension:515` | `Suspended` → `Active` |
| approuver réactivation | `ApproveReactivation:624` | `PendingReactivation` → `Active` |

**Effet réel en aval** — c'est le seul parcours d'administration réellement chaîné :
`SellerKybVerifiedDomainEvent` (`Seller.cs:359`), `SellerKybRejectedDomainEvent` (`:404`),
`SellerSuspendedDomainEvent`. Consommateurs confirmés :
`services/marketplace/catalog-service/src/HBA.Catalog.Infrastructure/Integration/SellerLifecycleCatalogHandlers.cs:117`
(dépublication des produits d'un vendeur suspendu) et les handlers d'attribution de rôle
côté identity (`BusinessRoleGrantHandlers.cs`).

**Trace d'audit : OUI.** `SellersDbContext.cs:84` pose `KeepsAuditTrail => true`, la
migration existe (`HBA.Merchants.Infrastructure/Migrations/20260819160000_JournalDAudit.cs`)
et le journal est **lisible** — seul cas du dépôt :
`HBA.Merchants.Application/Members/AuditQueries.cs` +
`HBA.Merchants.Infrastructure/Persistence/AuditTrailReader.cs`.

**Défaut** : `ApproveKyb()` et `RejectKyb(reason)` ne prennent **pas** l'identifiant du
modérateur. L'acteur n'est connu que par le contexte de requête
(`ModuleDbContext.RecordAuditTrail`, `:187`), donc uniquement dans la table `audit_entries` ;
il n'est pas porté par l'événement `SellerKybVerifiedDomainEvent(SellerId, UserId)` — le
`UserId` y est celui du **vendeur**, pas du décideur. Un consommateur aval ne peut donc
jamais savoir qui a validé. **Sévérité : MEDIUM.**

---

## 3. Modération des produits

**Point d'entrée** : `services/marketplace/catalog-service/src/HBA.Catalog.Api/Endpoints/CatalogEndpoints.cs:124`
`MapAdminGroup("/api/v1/catalog/admin")`, puis `:157-162` :
```
admin.MapGet("/products/reviews", ListPendingReviewsAsync);
admin.MapGet("/products/{id:guid}/review", GetProductReviewsAsync);
admin.MapPost("/products/{id:guid}/approve", ApproveProductAsync);
admin.MapPost("/products/{id:guid}/reject", RejectProductAsync);
admin.MapPost("/products/{id:guid}/suspend", SuspendProductAsync);
admin.MapPost("/products/{id:guid}/restore", RestoreProductAsync);
```
**Qui peut appeler** : `Admin` ou `Moderator`.

**États** : `HBA.Catalog.Domain/Products/Product.cs` — `Approve:452`, `Reject:484`,
`Suspend:634`, `Restore:651`, toutes passant par `ChangerStatut` (`:681-689`), lui-même
gardé par la liste blanche `ProductStatusTransitions.IsAllowed`
(`Products/ProductStatus.cs:68-118`). La machine est correcte et exhaustive.

**Effet en aval** : `ProductApprovedDomainEvent` et `ProductSuspendedDomainEvent` sont
publiés en événements d'intégration
(`HBA.Catalog.Application/Products/EventHandlers/ProductLifecycleDomainEventHandlers.cs:47`, `:117`)
et le contexte draine son outbox. **Effet réel.**

**Trace d'audit : NON.** `CatalogDbContext` n'active pas `KeepsAuditTrail` (§5). Rejeter
ou suspendre la fiche d'un vendeur — c'est-à-dire lui retirer son chiffre d'affaires — ne
laisse aucune ligne nommant le modérateur. Les motifs vivent dans `ProductReview`
(entité métier), mais l'acteur n'y est pas rattaché à une identité vérifiée par
l'infrastructure. **Sévérité : HIGH.**

**Défaut secondaire** : `admin.MapDelete("/brands/{id:guid}")` et
`admin.MapDelete("/categories/{id:guid}")` (`CatalogEndpoints.cs:129`, `:134`) suppriment
un élément du référentiel partagé par tous les vendeurs, sans audit et sans
ré-authentification.

---

## 4. Modération des restaurants

**Point d'entrée** : `services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Endpoints/FoodEndpoints.cs:220-226`
```
var moderation = app.MapAdminGroup("/api/food/admin");
moderation.MapGet("/restaurants/pending", ListPendingRestaurantsAsync);
moderation.MapPost("/restaurants/{id:guid}/approve", ApproveRestaurantAsync);
moderation.MapPost("/restaurants/{id:guid}/reject", RejectRestaurantAsync);
moderation.MapPost("/restaurants/{id:guid}/suspend", SuspendRestaurantAsync);
moderation.MapPost("/restaurants/{id:guid}/lift-suspension", LiftRestaurantSuspensionAsync);
```
**Qui peut appeler** : `Admin` ou `Moderator`.

**États** (`HBA.Food.Restaurant.Domain/Aggregates/Restaurants/Restaurant.cs`) :
`SubmitForApproval:546` (`Draft` → `PendingApproval`), `Approve:597`
(`PendingApproval` → `Active`), `Reject:617` (`PendingApproval` → `Draft`),
`Suspend:670` (→ `Suspended`), `LiftSuspension:715` (→ `Active`), `Close:732` (→ `Closed`).

**Effet en aval : réel.** `RestaurantApprovedDomainEvent`
(`Domain/Aggregates/Restaurants/Events/RestaurantDomainEvents.cs:22`) →
`RestaurantApprovedDomainEventHandler`
(`Application/Restaurants/RestaurantDomainEventHandlers.cs:18`, enregistré
`Infrastructure/FoodModuleInstaller.cs:79`) → `RestaurantApprovedIntegrationEvent` →
attribution du rôle `FoodPartner`.

**Trace d'audit : NON.** `FoodDbContext` n'active pas `KeepsAuditTrail`.

---

## 5. Modération des avis

**Point d'entrée** : `services/common/review-service/src/HBA.Engagement.Api/Endpoints/EngagementEndpoints.cs:64-67`
```
var moderation = app.MapAdminGroup("/api/engagement/reviews");
moderation.MapPost("/{id:guid}/flag", FlagReviewAsync);
moderation.MapPost("/{id:guid}/reject", RejectReviewAsync);
moderation.MapPost("/{id:guid}/restore", RestoreReviewAsync);
```
**Qui peut appeler** : `Admin` ou `Moderator`.

**États** (`HBA.Engagement.Reviews.Domain/Reviews/Review.cs`) : `Flag:71`
(→ `Flagged`), `Reject:83` (→ `Rejected` + `ReviewRejectedDomainEvent:90`),
`Restore:98` (→ `Published` + `ReviewPublishedDomainEvent:105`).

**Défaut** : `Flag()` (`:71-81`) ne lève **aucun événement**. Un avis « signalé » reste
donc compté dans la note publique jusqu'à ce qu'un second geste (`Reject`) soit posé —
l'état intermédiaire ne protège rien. **Sévérité : MEDIUM.**

**Trace d'audit : NON.** `ReviewsDbContext` n'active pas `KeepsAuditTrail`. Retirer l'avis
d'un acheteur — donc modifier la réputation d'un vendeur — ne nomme personne.

---

## 6. Arbitrage des retours

**Point d'entrée** :
`services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Api/Endpoints/AdminReturnsEndpoints.cs:14-18`
```
var group = app.MapAdminGroup("/api/v1/admin/returns");
group.MapGet("/{id:guid}", GetAsync);
group.MapPost("/{id:guid}/override", OverrideAsync);
group.MapPost("/{id:guid}/close", CloseAsync);
```
**Qui peut appeler** : `Admin` ou `Moderator`. C'est le **seul** parcours d'administration
du dépôt qui transmet explicitement l'acteur à la commande
(`AdminReturnsEndpoints.cs:27`, `:30` → `CurrentUserId(user)`), qui l'écrit dans
l'historique métier (`ReturnRequest.AddHistory`, `.../Aggregates/ReturnRequest/ReturnRequest.cs:338`).
Bon point isolé.

**Trois défauts, dont deux graves.**

**(a) CRITICAL — le service ne peut pas fonctionner : aucune migration.**
`Api/Program.cs:21` appelle `await app.MigrateHbaDatabaseAsync<ReturnRefundDbContext>()`
alors que `services/marketplace/return-refund-service/` **ne contient aucun dossier
`Migrations` ni snapshot** (vérifié par `find`). Le schéma `returns` (tables
`return_requests`, `refunds`, `refund_attempts`, `outbox_messages`, et `audit_entries`
puisque `ReturnRefundDbContext.cs:28` déclare `KeepsAuditTrail => true`) n'est jamais créé.
Toute route de ce service échoue au premier accès à la base. Le journal d'audit **déclaré**
est donc, en pratique, un journal qui n'existe pas. (Constat cohérent avec
`DATABASE_AUDIT.md` §« DbContext sans aucune migration ».)

**(b) CRITICAL — le remboursement décidé n'est jamais exécuté.**
`ExecuteRefundCommand` est déclaré
(`Application/Commands/ReturnLifecycleCommands.cs:17`) et son handler est complet
(`Application/Commands/ExecuteRefund/ExecuteRefundCommandHandler.cs:32-87`, y compris
l'appel gRPC `RefundPaymentAsync` et la garde d'idempotence `:46-49`).
**Aucun émetteur n'existe** : recherche exhaustive de `new ExecuteRefundCommand(` hors de
sa propre déclaration → 0 résultat. Ni route, ni consumer Kafka, ni tâche de fond.
Conséquence : un dossier arbitré reste bloqué en `RefundPending`, le `Refund` reste
`Pending`, `ReturnStatus.Refunded` n'est **jamais** atteint, et l'argent n'est jamais rendu
à l'acheteur.

**(c) CRITICAL — aucun contrôle d'appartenance sur la surface vendeur du même service.**
`Api/Endpoints/SellerReturnsEndpoints.cs:14` utilise `MapSellerGroup` (rôles `Seller`,
`Admin`, `Moderator`) et expose `approve`, `reject`, `inspection`, `refund-decision`,
`shipment`, `receive` (`:17-22`). Les six handlers correspondants
(`Application/Commands/ReturnLifecycleCommands.cs:69-198`) chargent le dossier par son
seul identifiant et **ne comparent jamais `request.SellerId` à l'appelant**. Tout compte
portant le rôle `Seller` peut donc arbitrer le retour d'un autre vendeur — et, via
`refund-decision` (`:161-181`), fixer le montant d'un remboursement sur la vente d'autrui.
Même remarque pour `CustomerReturnsEndpoints.cs:19` (`GET /{id}` sans contrôle du
demandeur) et `:25` (`CreateAsync` : le client n'est pas comparé à l'acheteur de la
commande, il vient du contexte gRPC de la commande).

**États** : `Domain/Policies/ReturnStateMachine.cs:7-26` — table de transitions explicite,
16 états. Voir `STATE_MACHINE_AUDIT.md` pour les 4 états inatteignables.

---

## 7. Validation des livreurs

**ABSENTE — aucune route, aucune commande, aucun écran.**

- Recherche exhaustive de `MapAdminGroup(` : 15 occurrences, **aucune** ne concerne les
  livreurs.
- `Driver.Verify()`
  (`services/delivery/driver-service/src/HBA.Delivery.Driver.Domain/Aggregates/Driver/DeliveryDriver.cs:176`),
  `Driver.Suspend(reason)` (`:212`), `Driver.Block(reason)` (`:226`) :
  **0 appelant** dans tout le dépôt.
- `IDriverRepository.ListByAccountStatusAsync`
  (`driver-service/.../Repositories/IDriverRepository.cs:157`) — la file d'attente de
  vérification — est implémentée
  (`delivery-service/.../Persistence/Repositories/DeliveryRepositories.cs:153`) et
  **jamais appelée**.
- Conséquence : `DriverVerifiedIntegrationEvent` n'est jamais publié, donc
  `GrantDriverRoleHandler`
  (`services/common/identity-service/.../BusinessRoleGrantHandlers.cs:256`) ne s'exécute
  jamais, donc le rôle `Driver` n'est attribué à personne — ce que
  `ApiAuthorization.cs:37-39` documente comme un fait connu.

Les seuls gestes joignables touchant un livreur sont anonymes et opèrent sur un
dictionnaire mémoire : `PATCH /api/v1/drivers/me`, `POST /api/v1/drivers/me/availability`,
`POST /internal/v1/drivers/{id}/busy-state`
(`driver-service/.../Api/Endpoints/DriverEndpoints.cs:15`, `:34`, `:57`).

**Sévérité : CRITICAL.**

---

## 8. Règles de tarification de livraison

**Point d'entrée** :
`services/delivery/delivery-pricing-service/src/HBA.Delivery.Pricing.Api/Endpoints/DeliveryPricingEndpoints.cs:47`
```csharp
var admin = app.MapGroup("/api/v1/admin/delivery-pricing").WithTags("Delivery Pricing · Admin");
```
`MapGroup` **nu** — et l'hôte n'a aucune authentification : `Api/Program.cs` n'appelle ni
`AddHbaService` ni `UseHbaService`, uniquement `AddHbaPricingInfrastructure` +
`AddHbaGrpc` (`Program.cs:8-9`). Il n'y a donc **ni FallbackPolicy, ni middleware
d'authentification, ni rôle exigé**.

**Qui peut réellement appeler : n'importe qui, sans jeton.** Routes concernées (`:49-97`) :
`GET /rules`, `POST /rules`, `PATCH /rules/{id}`, `POST /rules/{id}/activate`,
`POST /rules/{id}/deactivate`.

**Effet réel** : écriture en base (`Infrastructure/Persistence/EfDeliveryPricingStore.cs`)
**et** publication d'événements par l'outbox drainé
(`DeliveryPricingInfrastructureModule.cs:25`). Le prix de toutes les livraisons de la
plateforme est donc modifiable anonymement, et le changement se propage.

**Trace d'audit : NON.** `DeliveryPricingDbContext` n'active pas `KeepsAuditTrail`, et il
n'a de toute façon **aucune migration** — le schéma n'est pas créé (cf. `DATABASE_AUDIT.md`).

**Sévérité : CRITICAL.**

---

## 9. Les trois questions nommément posées

### 9.1 `RecordAuditTrail` / `KeepsAuditTrail` : où est-il actif, où est-il éteint ?

**Mécanisme** : `shared/common/HBA.Shared.Infrastructure/Persistence/ModuleDbContext.cs`.
`KeepsAuditTrail` est `virtual` et vaut **`false` par défaut** (`:61`). Quand il est vrai,
`OnModelCreating` ajoute la table `audit_entries` au schéma du module (`:70-73`) et
`SaveChangesAsync` appelle `RecordAuditTrail()` en troisième position, après le dispatch
des événements de domaine et le drainage de l'outbox (`:82-97`). `RecordAuditTrail` sort
immédiatement si le drapeau est faux (`:145-148`), sinon écrit une ligne par entité mutée
(`:200-212`) avec l'acteur lu dans `HbaRequestContext.Current` (`:178-188`).

**État réel : 3 contextes sur 23.**

| `KeepsAuditTrail = true` | Fichier | Table réellement créée ? |
|---|---|---|
| `SellersDbContext` | `services/marketplace/seller-service/.../Persistence/SellersDbContext.cs:84` | **oui** (`Migrations/20260819160000_JournalDAudit.cs`) |
| `MealOrderingDbContext` | `services/food/food-order-service/.../Persistence/MealOrderingDbContext.cs:45` | **oui** (`Migrations/20260819190000_InitialFoodOrdering.cs:138`) |
| `ReturnRefundDbContext` | `services/marketplace/return-refund-service/.../Persistence/ReturnRefundDbContext.cs:28` | **non — le service n'a aucune migration** (§6a) |

**Les 20 autres contextes sont éteints**, dont, par ordre de gravité :

| Contexte éteint | Actions sensibles qui ne laissent aucune trace |
|---|---|
| `IdentityDbContext` | suspension d'un compte, **attribution et retrait de rôles**, création/suppression de rôles, modification des permissions (`IdentityEndpoints.cs:460-462`, `:511-514`) |
| `PaymentsDbContext` | capture, remboursement, échec de paiement (`Payment.cs:126`, `:157`, `:175`) |
| `WalletDbContext` | **approbation et rejet d'un retrait d'argent**, lancement d'un lot de règlement, marquage « payé » d'un virement (`FinancialEndpoints.cs:151-152`, `:199-201`) |
| `CatalogDbContext` | approbation, rejet, suspension d'un produit ; suppression d'une marque ou d'une catégorie (§3) |
| `ReviewsDbContext` | rejet et restauration d'un avis (§5) |
| `FoodDbContext` | approbation, rejet, suspension d'un restaurant (§4) |
| `OrderingDbContext` | reprise et remboursement d'une commande en arbitrage (`OrderEndpoints.cs:74+`) |
| `InventoryDbContext` | réservation, libération et confirmation de stock par la trappe d'exploitation (`InventoryEndpoints.cs:107-109`) |
| `DeliveriesDbContext` | création et **annulation** d'une course, révocation d'une mission |
| `DeliveryPricingDbContext` | réécriture des règles tarifaires (§8) |
| `BillingDbContext` | émission et marquage « payée » d'une facture |
| `PromotionsDbContext`, `MediaDbContext`, `MessagingDbContext`, `NotificationsDbContext`, `UsersDbContext`, `CartDbContext`, `FoodCartDbContext`, `WishlistDbContext`, `RecommendationsDbContext` | — |

**Deuxième défaut du mécanisme : le journal n'est lisible que dans un seul module.**
Les seuls lecteurs sont
`services/marketplace/seller-service/src/HBA.Merchants.Application/Members/AuditQueries.cs`
et `.../Infrastructure/Persistence/AuditTrailReader.cs`. Aucune route ne permet de lire
`audit_entries` de `food-order-service`. Il n'existe **aucune console d'audit
transversale** (aucun BFF admin — voir `SERVICES_DELIVERY_APPS.md`).

**Troisième défaut : l'acteur n'est capté que sur les hôtes qui installent le middleware.**
`HbaRequestContext.Current` est renseigné par `RequestContextMiddleware`, posé par
`app.UseHbaRequestContext(...)` depuis
`shared/common/HBA.Shared.Hosting/ServiceHostExtensions.cs:237`, c'est-à-dire uniquement
par les hôtes qui appellent `UseHbaService()`. Les 10 services satellites qui ne
l'appellent pas (§9.2) n'ont ni contexte ni acteur — mais ils n'ont pas d'audit non plus,
donc l'effet ne se cumule pas.

**Sévérité : HIGH** (les actions de plateforme les plus destructrices — rôles, argent,
suspension — sont exactement celles qui ne sont pas journalisées).

### 9.2 Quelles routes d'administration ne sont pas derrière une politique admin ?

**(a) Groupe nommé « admin » sans aucune policy — 5 routes**

| Route | Fichier |
|---|---|
| `GET /api/v1/admin/delivery-pricing/rules` | `delivery-pricing-service/.../DeliveryPricingEndpoints.cs:49` |
| `POST /api/v1/admin/delivery-pricing/rules` | `:52` |
| `PATCH /api/v1/admin/delivery-pricing/rules/{id}` | `:63` |
| `POST /api/v1/admin/delivery-pricing/rules/{id}/activate` | `:76` |
| `POST /api/v1/admin/delivery-pricing/rules/{id}/deactivate` | `:88` |

**(b) Routes d'exploitation logistique sans aucune policy — dispatch, tracking, route, proof, drivers**

Ces cinq hôtes n'installent aucune authentification (`Program.cs` sans `AddHbaService`) :

| Route | Fichier | Ce qu'elle permet |
|---|---|---|
| `POST /api/v1/dispatch/{deliveryId}/manual-assign` | `dispatch-service/.../DispatchEndpoints.cs:28` | affecter n'importe quelle course à n'importe quel livreur |
| `POST /api/v1/dispatch/{deliveryId}/retry` | `:18` | relancer le dispatch |
| `POST /internal/v1/dispatch/request` | `:41` | créer une demande de dispatch |
| `POST /internal/v1/dispatch/{deliveryId}/cancel` | `:51` | annuler un dispatch |
| `GET /internal/v1/dispatch/{deliveryId}/assignment` | `:57` | lire l'affectation |
| `POST /api/v1/tracking/sessions/{deliveryId}/locations` | `tracking-service/.../TrackingEndpoints.cs:13` | injecter la position d'un livreur arbitraire |
| `GET /api/v1/tracking/deliveries/{deliveryId}/latest` | `:24` | lire la position en direct de n'importe quelle course |
| `POST /internal/v1/tracking/sessions/start` \| `/stop` | `:34`, `:44` | ouvrir/fermer une session de suivi |
| `POST /api/v1/proofs/` \| `/{id}/submit` \| `/{id}/media/presign` | `proof-of-delivery-service/.../ProofEndpoints.cs:13`, `:27`, `:19` | fabriquer une preuve de remise |
| `GET /internal/v1/proofs/deliveries/{id}/dropoff-valid` | `:45` | — |
| `GET /api/v1/drivers/me` \| `PATCH /me` \| `POST /me/availability` \| `POST /me/vehicles` | `driver-service/.../DriverEndpoints.cs:13`, `:15`, `:34`, `:24` | lire et modifier le profil du livreur par défaut |
| `GET /internal/v1/drivers/{driverId}` | `:49` | lire le profil d'un livreur |
| `POST /internal/v1/drivers/{driverId}/busy-state` | `:57` | forcer la disponibilité d'un livreur |
| `POST /api/v1/routes/estimate` \| `/optimize`, `POST /internal/v1/routes/eta` | `route-service/.../RouteEndpoints.cs:13`, `:20`, `:48` | — |

Quatre services `food` sont dans le même cas (aucune authentification dans leur
`Program.cs`) : `menu-service` (`MenuEndpoints.cs:9`), `review-service` food
(`ReviewEndpoints.cs:9-11`, création d'avis anonyme), `availability-service`
(`AvailabilityEndpoints.cs:9`), `kitchen-prep-service` (`KitchenEndpoints.cs:9`).

**(c) Actions de gouvernance derrière une policy trop large**

| Route | Policy posée | Problème |
|---|---|---|
| `POST /api/v1/seller/returns/{id}/approve`\|`reject`\|`inspection`\|`refund-decision`\|`shipment`\|`receive` | `MapSellerGroup` (`SellerReturnsEndpoints.cs:14`) | aucun contrôle d'appartenance dans les handlers (§6c) : tout `Seller` arbitre le retour d'autrui |
| `GET /api/v1/marketplace/returns/{id}` | `MapAuthenticatedGroup` (`CustomerReturnsEndpoints.cs:18`) | aucun contrôle du demandeur : lecture du dossier de retour de n'importe qui |
| `POST /api/v1/merchants/{id}/kyb/approve` et 6 autres | `MapAdminGroup` = Admin **ou Moderator** | un modérateur de contenu valide des pièces d'identité d'entreprise et peut supprimer un vendeur |

### 9.3 Quelles actions d'administration sont attendues mais absentes ?

**Aucune route ne référence une commande inexistante.** Vérification programmatique :
extraction de tous les `new *Command(` / `new *Query(` du dépôt et confrontation aux
`ICommandHandler<>` / `IQueryHandler<>` / `IRequestHandler<>` → **un seul résidu**,
`NpgsqlCommand` (faux positif, ADO.NET). Le problème est donc l'inverse : des commandes et
méthodes de domaine complètes, sans point d'entrée.

| Action attendue | Ce qui existe | Ce qui manque | Sévérité |
|---|---|---|---|
| Vérifier / suspendre / bloquer un livreur | `Driver.Verify:176`, `Suspend:212`, `Block:226`, `ListByAccountStatusAsync` | route, commande, handler | CRITICAL |
| Exécuter un remboursement décidé | `ExecuteRefundCommand` + handler complet (`ExecuteRefundCommandHandler.cs:32`) | tout émetteur (route, consumer, job) | CRITICAL |
| Constater l'échec d'un virement vendeur | `SettlementBatch.MarkPayoutFailed:143` | **0 appelant** — un virement échoué reste `Scheduled`, le vendeur est débité sans être payé (défaut reconnu dans `FinancialEndpoints.cs:186`, « tâche #190 ») | CRITICAL |
| Autoriser un paiement (pré-autorisation) | `Payment.Authorize:113` | **0 appelant** — `PaymentStatus.Authorized` inatteignable | MEDIUM |
| Refuser une invitation d'équipe | `SellerInvitation.Decline:303` | aucune route (`MerchantEndpoints.cs:320` n'expose que l'acceptation) — `InvitationStatus.Declined` inatteignable | MEDIUM |
| Reprendre en main une course bloquée | `Delivery.RevokeAssignment:548` | **0 appelant** — aucune route de révocation de mission côté exploitation | HIGH |
| Faire avancer une course à la main | 5 commandes de progression (`DeliveryProgressCommands.cs:23-39`) | **0 émetteur** — la console d'exploitation ne peut débloquer aucune course | HIGH |
| Consulter le journal d'audit d'un module autre que `seller` | `AuditEntry` + `AuditConfiguration` partagés | lecteur et route pour les 2 autres modules qui l'activent | MEDIUM |
| Console d'administration | — | **aucun BFF admin** dans `apps/` | HIGH |
| Contrepasser un gain vendeur | `SellerEarning.Reverse` (`EarningStatus.Reversed`) | **0 appelant** — un gain versé sur une vente annulée n'est jamais repris | HIGH |

---

## 10. Défauts classés (administration)

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| A1 | CRITICAL | Tarification de livraison modifiable sans authentification | `DeliveryPricingEndpoints.cs:47`, `Program.cs:1-20` |
| A2 | CRITICAL | Tout `Seller` arbitre et chiffre le remboursement d'un retour d'autrui | `SellerReturnsEndpoints.cs:14-22`, `ReturnLifecycleCommands.cs:69-198` |
| A3 | CRITICAL | Le remboursement décidé n'est jamais exécuté (`ExecuteRefundCommand` sans émetteur) | `ExecuteRefundCommandHandler.cs:32` |
| A4 | CRITICAL | `return-refund-service` n'a aucune migration : schéma jamais créé | `find … -type d -name Migrations` → vide ; `Program.cs:21` |
| A5 | CRITICAL | Aucune validation des livreurs : ni route, ni commande | §7 |
| A6 | CRITICAL | Affectation manuelle de course non authentifiée | `DispatchEndpoints.cs:28` |
| A7 | CRITICAL | Un virement vendeur échoué ne peut pas être constaté (`MarkPayoutFailed` sans appelant) | `SettlementBatch.cs:143`, `FinancialEndpoints.cs:186` |
| A8 | HIGH | 20 contextes sur 23 sans journal d'audit, dont identity, payments, wallet, catalog, reviews | §9.1 |
| A9 | HIGH | Le catalogue de permissions n'est lu par aucun contrôle d'autorisation | `JwtTokenGenerator.cs:80` vs 0 lecteur |
| A10 | HIGH | Suivi GPS et preuve de remise ouverts anonymement | `TrackingEndpoints.cs:11`, `ProofEndpoints.cs:11` |
| A11 | HIGH | Lecture d'un dossier de retour sans contrôle du demandeur | `CustomerReturnsEndpoints.cs:18` |
| A12 | HIGH | `Delivery.RevokeAssignment` et les 5 commandes de progression sans appelant : aucune trappe d'exploitation logistique | `Delivery.cs:548`, `DeliveryProgressCommands.cs:23-39` |
| A13 | HIGH | Aucun BFF d'administration | `apps/` — 4 dossiers, aucun admin |
| A14 | MEDIUM | Modérateur habilité au KYB et à la suppression de vendeurs | `MerchantEndpoints.cs:164`, `ApiAuthorization.cs:62` |
| A15 | MEDIUM | Aucune ré-authentification exigée sur une action de plateforme | `MerchantCapabilities.cs:200` — 4 appelants, tous vendeur |
| A16 | MEDIUM | `Review.Flag()` ne lève aucun événement : l'avis signalé reste dans la note | `Review.cs:71-81` |
| A17 | MEDIUM | Les décisions de modération ne portent pas l'identité du décideur dans leurs événements | `Seller.ApproveKyb:344`, `Product.Approve:452`, `Review.Reject:83` |
| A18 | MEDIUM | Journal d'audit lisible dans un seul module sur les trois qui l'activent | `AuditQueries.cs` (seller-service uniquement) |
