# Audit des machines à états

Une valeur d'énumération jamais assignée est un défaut : elle promet au lecteur, au
consommateur d'événement et à l'écran d'administration un état que le système n'atteint
jamais. Elles sont toutes listées.

Méthode : extraction programmatique de toutes les énumérations de statut du dépôt, puis
recherche exhaustive des sites d'**affectation** (`X = Enum.Valeur`, y compris via une
méthode de transition centralisée) distingués des simples **comparaisons**. Les appelants
des méthodes de domaine ont été recherchés dans `services/`, `apps/`, `shared/` et `tests/`.

## Tableau de synthèse

| # | Agrégat | Valeurs | Jamais atteintes | Statut |
|---|---|---|---|---|
| 1 | Product | 8 | 0 | **COHERENT** |
| 2 | Seller | 5 (+4 KYB) | 0 | **COHERENT** |
| 3 | Store | 4 | 0 | **PARTIAL** |
| 4 | Restaurant | 5 | 0 | **COHERENT** |
| 5 | MealOrder | 8 | 0 | **COHERENT** |
| 5b | FoodOrder | 8 | 0 | **COHERENT** |
| 6 | KitchenTicket (`FoodOrderStatus.cs`) | 4 | **4** | **BROKEN** |
| 6b | KitchenTicket (`kitchen-prep-service`) | 2 chaînes | — | **BROKEN** |
| 7 | Order (marketplace) | 8 | 0 | **COHERENT** |
| 8 | SellerOrder | — | — | **INEXISTANT** |
| 9 | InventoryReservation | aucun statut | — | **BROKEN** |
| 10 | Delivery | 11 | **2** | **PARTIAL** |
| 10b | DeliveryAssignment | 5 | **2** | **PARTIAL** |
| 11 | DispatchJob | 5 + 3 | **8** | **BROKEN** |
| 12 | Driver | 4 + 4 (+8 dupliquées) | **8 dupliquées, 6 sur 8 réelles inatteignables en pratique** | **BROKEN** |
| 13 | TrackingSession | 3 | **3** | **BROKEN** |
| 14 | Proof | 3 + 3 | **6** | **BROKEN** |
| 15 | ReturnRequest | 16 | **5** | **PARTIAL** |
| 16 | Refund | 6 | **2** | **PARTIAL** |
| 17 | Payment | 5 | **1** | **PARTIAL** |
| 18 | Wallet / Payout | 5 enums | **3** | **PARTIAL** |
| 19 | SellerMember | 5 (+2) | **1** | **PARTIAL** |
| 20 | SellerInvitation | 5 | **1** | **PARTIAL** |

**COHERENT 7 · PARTIAL 8 · BROKEN 6 · INEXISTANT 1** (sur 22 fiches).

---

## 1. Product

```
Entité — services/marketplace/catalog-service/src/HBA.Catalog.Domain/Products/Product.cs
Enum   — Products/ProductStatus.cs:32
         Draft, PendingReview, Approved, Rejected, Published, Unpublished, Suspended, Archived
```
**Transitions autorisées** — liste blanche explicite `ProductStatusTransitions.IsAllowed`
(`ProductStatus.cs:68-118`) : Draft→{PendingReview, Archived} ; PendingReview→{Approved,
Rejected} ; Rejected→{Draft, Archived} ; Approved→{Published, Suspended, Archived} ;
Published→{Unpublished, Suspended} ; Unpublished→{Published, Archived} ;
Suspended→{Approved}. `Archived` terminal. Toute mutation passe par
`Product.ChangerStatut` (`Product.cs:681-689`), qui refuse ce qui n'est pas listé.

**Transitions réellement déclenchées**
| Transition | Méthode | Commande / route |
|---|---|---|
| Draft→PendingReview | `SubmitForReview:384` (`:436`) | `ChangeProductStatusCommandHandler.cs:101` |
| PendingReview→Approved | `Approve:452` (`:465`) | `AdminReviewCommands.cs:86` ← `POST /api/v1/catalog/admin/products/{id}/approve` |
| PendingReview→Rejected | `Reject:484` (`:497`) | `AdminReviewCommands.cs:137` ← `.../reject` |
| Approved\|Unpublished→Published | `Publish:534` (`:591`) | `ChangeProductStatusCommandHandler.cs:102` |
| Published→Unpublished | `Unpublish:612` (`:614`) | `:103` |
| →Suspended | `Suspend:634` (`:636`) | `AdminReviewCommands.cs:156` ← `.../suspend` |
| Suspended→Approved | `Restore:651` (`:653`) | `AdminReviewCommands.cs:174` ← `.../restore` |
| →Archived | `Archive:668` (`:670`) | `ChangeProductStatusCommandHandler.cs:104` |
| Rejected→Draft | `Product.cs:309` (ré-ouverture après refus de révision) | interne |

**Valeurs jamais atteintes** : aucune.
**Transitions attendues mais absentes** : aucune.
**Routes permettant une transition invalide** : aucune — toutes passent par `ChangerStatut`.
**Statut : COHERENT.** (Réserve hors machine à états : aucune trace d'audit, cf.
`SAGA_ADMIN.md` §3.)

---

## 2. Seller (+ KYB)

```
Entité — services/marketplace/seller-service/src/HBA.Merchants.Domain/Sellers/Seller.cs
Enum   — Sellers/Enums.cs : SellerStatus { Pending, Active, Suspended, Closed, PendingReactivation }
                            KybStatus    { NotStarted, InReview, Verified, Rejected }
```
**Transitions autorisées / déclenchées**
| De → Vers | Méthode | Appelant |
|---|---|---|
| — → Pending | ctor `:26` | `Seller.Create` |
| Pending\|Suspended → Active | `Activate:432` (`:444`) | `ActivateSellerCommandHandler.cs:28` ← `POST /api/v1/merchants/{id}/activate` |
| PendingReactivation → Active | `ApproveReactivation:624` (`:659`) | `ApproveSellerReactivationCommandHandler.cs:28` |
| Active → Suspended | `Suspend:473` (`:496`) | `SuspendSellerCommandHandler`, **et** `RejectKyb:399-410` |
| Suspended → Active | `LiftSuspension:515` | `LiftSuspensionCommandHandler` |
| → Closed | `RequestClosure:570` (`:577`) | `RequestSellerClosureCommandHandler.cs:28` |
| Closed → PendingReactivation | `RequestReactivation:583` (`:590`) | `RequestSellerReactivationCommandHandler.cs:28` |
| KYB NotStarted\|Rejected → InReview | `AddKybDocument:229` / `SubmitKyb:281` | `AddKybDocumentCommandHandler.cs:138` |
| KYB InReview → Verified | `ApproveKyb:344` (`:356`) | `ApproveKybCommandHandler.cs:28` |
| KYB InReview → Rejected | `RejectKyb:382` (`:400`) | `RejectKybCommandHandler.cs:31` |
| KYB InReview → NotStarted | `RemoveKybDocument:323` | `RemoveKybDocumentCommandHandler.cs:29` |

**Valeurs jamais atteintes** : aucune, pour les deux énumérations.
**Transitions attendues mais absentes** : aucune.
**Routes permettant une transition invalide** : aucune ; toutes les méthodes gardent leur
état de départ.
**Statut : COHERENT.** Le seul agrégat du dépôt à la fois complet, routé, événementiel et
audité.

---

## 3. Store

```
Entité — services/marketplace/seller-service/src/HBA.Merchants.Domain/Stores/Store.cs
Enum   — Stores/StorePrimitives.cs : StoreStatus { Draft, Open, Closed, Suspended }
```
**Transitions autorisées** : Draft→Open (`Open:207`, refuse depuis `Suspended` `:214`) ;
Open→Closed (`Close:240`) ; *→Suspended (`Suspend:256`) ; Suspended→**Closed**
(`LiftSuspension:284`, `:292` — la levée de suspension rend une boutique **fermée**, pas
ouverte : choix délibéré, le vendeur doit rouvrir).

**Transitions réellement déclenchées** : `StoreCommands.cs:167` (`open`), `:257` (`close`),
et les routes de gouvernance `MerchantEndpoints.cs:195-196`
(`suspend`, `lift-suspension`).

**Valeurs jamais atteintes** : aucune.
**Transitions attendues mais absentes** : **Draft → Closed** et **Draft → Suspended** ne
sont pas exprimées ; `Suspend` accepte n'importe quel état de départ (`:258` ne teste que
la ré-entrance), donc une boutique en `Draft` peut être suspendue et se retrouver ensuite
`Closed` sans jamais avoir été ouverte. Il n'y a pas de table de transitions comme pour
`Product` : les gardes sont dispersées et incomplètes.
**Routes permettant une transition invalide** : `POST /api/v1/merchants/{sellerId}/stores/{storeId}/suspend`
(`MerchantEndpoints.cs:195`) sur une boutique `Draft`.
**Statut : PARTIAL.**

---

## 4. Restaurant

```
Entité — services/food/restaurant-service/src/HBA.Food.Restaurant.Domain/Aggregates/Restaurants/Restaurant.cs
Enum   — Aggregates/Restaurants/RestaurantStatus.cs
         { Draft, PendingApproval, Active, Suspended, Closed }
```
**Transitions autorisées / déclenchées**
| De → Vers | Méthode (ligne d'affectation) | Appelant |
|---|---|---|
| — → Draft | ctor `:52` | `Restaurant.Create` |
| Draft → PendingApproval | `SubmitForApproval:546` (`:589`) | `RestaurantCommands.cs:301` |
| PendingApproval → Active | `Approve:597` (`:605`) | `POST /api/food/admin/restaurants/{id}/approve` (`FoodEndpoints.cs:223`) |
| PendingApproval → Draft | `Reject:617` (`:625`) | `.../reject` (`:224`) |
| → Suspended | `Suspend:670` (`:690`) | `.../suspend` (`:225`) |
| Suspended → Active | `LiftSuspension:715` (`:723`) | `.../lift-suspension` (`:226`) |
| → Closed | `Close:732` (`:748`) | `RestaurantCommands.cs` (fermeture définitive) |

**Valeurs jamais atteintes** : aucune.
**Transitions attendues mais absentes** : aucune pour le statut. `OrderingBlockedReason`
(même fichier) et `PauseUntil`/`Resume` (`:766`, `:788`) gèrent la disponibilité, hors
machine à états.
**Routes permettant une transition invalide** : aucune.
**Statut : COHERENT.**

---

## 5. MealOrder / FoodOrder

### 5a. MealOrder (food-order-service)

```
Entité — services/food/food-order-service/src/HBA.Food.Order.Domain/Aggregates/Orders/MealOrder.cs
Enum   — Aggregates/Orders/MealOrderIds.cs
         { Pending, AwaitingPayment, Paid, Confirmed, Cancelled, Failed, Delivered, UnderReview }
```
**Transitions autorisées** : Pending→AwaitingPayment (`MarkAwaitingPayment:297`) ;
AwaitingPayment→Paid (`MarkPaid:312`) ; Paid→Confirmed (`Confirm:325`) ;
{Confirmed, UnderReview}→Delivered (`MarkDelivered:364`) ; Confirmed→UnderReview
(`MarkUnderReview:483`) ; UnderReview→Confirmed (`ResumeAfterReview:531`) ;
UnderReview→Cancelled (`CancelAfterReview:563`) ; →Cancelled (`Cancel:390`,
`RejectByRestaurant:433`) ; →Failed (`Fail:579`).

**Transitions réellement déclenchées** : `PlaceMealOrderCommand.cs:229-252`,
`MealOrderLifecycleCommands.cs:108`, `:109`, `:144`, `:159`, `:180`, `:185`, `:190` ;
routes d'arbitrage `POST /api/admin/food/orders/{id}/review/resume` et `/review/refund`
(`MealOrderEndpoints.cs:45-46`).

**Valeurs jamais atteintes** : aucune.
**Statut : COHERENT.**

### 5b. FoodOrder (restaurant-service)

```
Entité — services/food/restaurant-service/src/HBA.Food.Restaurant.Domain/Entities/Orders/FoodOrder.cs
Enum   — Entities/Orders/FoodOrderStatus.cs
         { PendingRestaurantAcceptance, Accepted, Rejected, Preparing, ReadyForPickup, PickedUp, Delivered, Cancelled }
```
**Transitions autorisées** : PendingRestaurantAcceptance→{Accepted (`:253`), Rejected
(`:293`), Cancelled} ; Accepted→Preparing (`:559`, dérivé de l'état des lignes) ;
Preparing→ReadyForPickup (`:577`, quand toutes les lignes sont `Ready`) ;
ReadyForPickup→Preparing (`:589`, réouverture d'une ligne) ;
ReadyForPickup→PickedUp (`:393`) ; PickedUp→Delivered (`:436`) ; →Cancelled (`:476`,
refusé après `PickedUp` `:467`).

**Transitions réellement déclenchées** : `FoodOrderCommands.cs:249`, `:337`, `:367-387`,
`:404`, `:420`. Le passage `PickedUp` est aussi consommé depuis
`DeliveryPickedUpIntegrationEvent`
(`Api/Integration/FoodDeliveryReturnHandlers.cs:66`) — mais cet événement n'est jamais
publié (cf. `SAGA_DRIVER.md` §2.4), donc seule la route restaurant fonctionne.

**Valeurs jamais atteintes** : aucune.
**Statut : COHERENT** pour la machine ; **fragile** en pratique : deux des trois sources
de progression (les événements de livraison) sont mortes.

---

## 6. KitchenTicket

Deux implémentations, incompatibles, toutes deux défectueuses.

### 6a. `KitchenTicketStatus` (restaurant-service)

```
Enum — services/food/restaurant-service/src/HBA.Food.Restaurant.Domain/Entities/Orders/FoodOrderStatus.cs
       KitchenTicketStatus { Pending, Preparing, Ready, Cancelled }
```
**Entité porteuse : aucune.** `FoodOrder.cs:30` explique que le ticket de cuisine est
« projeté » depuis les lignes de commande plutôt que matérialisé.
**Transitions autorisées** : aucune — il n'y a pas de méthode.
**Transitions déclenchées** : aucune.
**Valeurs jamais atteintes : les 4.** L'énumération a exactement **une référence** dans
tout le dépôt : sa propre déclaration.
**Statut : BROKEN** (énumération morte).

L'état de cuisine réellement suivi est `KitchenItemStatus { Pending, Preparing, Ready }`
(même fichier), assigné dans `Entities/Orders/FoodOrderItem.cs:112`, `:157`, `:183`,
`:205` — les 3 valeurs sont atteintes, via `StartItem`/`MarkItemReady`/`ReopenItem`
(`FoodOrderCommands.cs:371`, `:375`, `:383`). Ce niveau-là est **COHERENT**.

### 6b. `KitchenTicket` (kitchen-prep-service)

```
Entité — services/food/kitchen-prep-service/src/HBA.Food.Kitchen.Domain/Aggregates/KitchenTicketAggregate.cs:3
         record KitchenTicket(..., string Status, ...)
```
**Enum : aucun** — deux chaînes en dur, `"PREPARING"` à la création
(`Application/Abstractions/KitchenStore.cs:12`) et `"READY"` (`:24`). Pas d'annulation,
pas de garde, pas de base (`ConcurrentDictionary` `:8`), routes anonymes
(`Api/Endpoints/KitchenEndpoints.cs:9`, hôte sans authentification).
**Statut : BROKEN.**

---

## 7. Order (marketplace)

```
Entité — services/marketplace/order-service/src/HBA.Order.Domain/Orders/Order.cs
Enum   — Orders/OrderIds.cs
         { Pending, AwaitingPayment, Paid, Confirmed, Cancelled, Failed, Delivered, UnderReview }
```
**Transitions autorisées**
| De | Vers | Garde |
|---|---|---|
| Pending | AwaitingPayment | `:310` `Status != Pending` refusé |
| AwaitingPayment | Paid | `:323` |
| Paid | Confirmed | `:341` |
| Confirmed \| UnderReview | Delivered | `:407` |
| Confirmed | UnderReview | `:609` |
| UnderReview | Confirmed | `:641` (`ResumeAfterReview`) |
| UnderReview | Cancelled | `:683` (`CancelAfterReview`) |
| ≠ terminal | Cancelled | `:437` (`Cancel`, refuse après `Confirmed` sous conditions) |
| Pending \| AwaitingPayment | Cancelled | `:520` (`RejectByProvider`) |
| * | Failed | `:699` |

**Transitions réellement déclenchées** : `PlaceOrderCommandHandler.cs:206-299`,
`OrderLifecycleCommands.cs:105`, `:148`, `:155`, `:160`, `:208`, `:228`, `:265` ;
`InvoiceCommands.cs:96` ; consumers `MarkOrderDeliveredOnDeliveryCompletedHandler.cs:105`
et `OrderDeliveryArbitrationHandlers.cs:97`.

**Valeurs jamais atteintes** : aucune.
**Transitions attendues mais absentes** : aucune.
**Routes permettant une transition invalide** : aucune (routes acheteur avec contrôle du
propriétaire, routes d'arbitrage sous `MapAdminGroup`, `OrderEndpoints.cs:74+`).
**Statut : COHERENT** — avec la réserve que `Delivered` n'est atteignable, en pratique,
que par le chemin food : `MarkOrderDeliveredOnDeliveryCompletedHandler` dépend de
`DeliveryCompletedIntegrationEvent`, jamais publié.

---

## 8. SellerOrder

**N'existe pas.** Recherche exhaustive : la seule occurrence du terme dans du code est
`services/common/notification-service/.../EventHandlers/SellerOrderNotificationHandler.cs`
(un handler de notification), et un champ `SellerOrderId` transporté par le contexte de
retour (`ReturnRequest.Create`, `.../ReturnRequest.cs:95`). Aucun agrégat, aucune table,
aucun statut.

**Conséquence** : une commande multi-vendeurs n'a pas de sous-état par vendeur. Le
`SellerOrderId` que `return-refund-service` enregistre provient de `IOrderGrpcClient` et ne
désigne aucune entité persistée.
**Statut : INEXISTANT.**

---

## 9. InventoryReservation

```
Entité — services/marketplace/inventory-service/src/HBA.Inventory.Domain/Stock/StockReservation.cs:9
Enum de statut — AUCUN
```
`StockReservation` porte `OrderId`, `Quantity`, `ExpiresAtUtc` (`:23-25`) et **rien
d'autre**. Il n'existe ni `ReservationStatus` ni `StockReservationStatus` dans le dépôt
(vérifié).

**Transitions autorisées** : aucune — le cycle de vie est exprimé par la **présence ou
l'absence** de la ligne (`ReleaseReservationCommandHandler`,
`Application/Stock/Commands/ReservationCommands.cs:69` ; `ConfirmReservationCommandHandler`
`:99`).
**Transitions réellement déclenchées** : réserver / libérer / confirmer, par gRPC interne
et par les trois routes d'exploitation `POST /api/inventory/reservations[/release|/confirm]`
(`InventoryEndpoints.cs:107-109`, `MapAdminGroup`).
**Valeurs jamais atteintes** : sans objet.
**Transitions attendues mais absentes** : **toutes**. On ne peut pas distinguer une
réservation *libérée* d'une réservation *jamais créée*, ni une réservation *expirée* d'une
réservation *confirmée puis effacée*. Il n'existe aucune trace d'une libération, donc
aucun moyen de diagnostiquer une survente a posteriori.
**Routes permettant une transition invalide** : `POST /api/inventory/reservations/release`
libère une réservation quel que soit l'état de la commande — le commentaire
`InventoryEndpoints.cs:104-106` le reconnaît (« libérer la réservation d'une commande payée
fait repartir la quantité à la vente ») sans que rien ne l'empêche.
**Statut : BROKEN.**

---

## 10. Delivery

```
Entité — services/delivery/delivery-service/src/HBA.Delivery.Core.Domain/Aggregates/Delivery/Delivery.cs
Enum   — Enums/DeliveryStatus.cs:3
         Pending, SearchingDriver, DriverAssigned, DriverAccepted, ArrivedAtPickup,
         PickedUp, InTransit, ArrivedAtDropoff, Delivered, Cancelled, NoDriverAvailable
```
**Transitions autorisées (méthodes du domaine)**
| De | Vers | Méthode |
|---|---|---|
| Pending \| NoDriverAvailable | SearchingDriver | `StartSearching:433` (`:448`) |
| SearchingDriver | DriverAssigned | `AssignTo:454` (`:470`) |
| DriverAssigned | DriverAccepted | `AcceptByDriver:476` (`:493`) |
| DriverAssigned | SearchingDriver \| NoDriverAvailable | `RejectByDriver:505` (`:537`, `:532`) |
| DriverAccepted \| ArrivedAtPickup | SearchingDriver | `RevokeAssignment:548` (`:560`) |
| DriverAccepted | ArrivedAtPickup | `MarkArrivedAtPickup:569` |
| ArrivedAtPickup \| DriverAccepted | PickedUp | `MarkPickedUp:572` (`:584`) |
| PickedUp | InTransit | `MarkInTransit:589` |
| InTransit \| PickedUp | ArrivedAtDropoff | `MarkArrivedAtDropoff:592` (`:599`) |
| ArrivedAtDropoff \| InTransit | Delivered | `MarkDelivered:624` (`:683`) |
| non terminal, non collecté | Cancelled | `Cancel:716` (`:733`) |

**Transitions réellement déclenchées**
| Transition | Déclencheur réel |
|---|---|
| →Pending | `CreateDeliveryCommandHandler` (`Commands/CreateDelivery/CreateDeliveryCommand.cs:165`) |
| Pending→SearchingDriver | `CreateDeliveryCommand.cs:185` (immédiat) et `DeliveryDispatchService.cs:191` (programmé) |
| NoDriverAvailable→SearchingDriver | `DeliveryDispatchService.cs:271` |
| SearchingDriver→DriverAssigned | `DispatchDeliveryCommandHandler` (`DispatchDeliveryCommand.cs:134`), lui-même appelé par `DeliveryDispatchService.cs:282` |
| DriverAssigned→SearchingDriver \| NoDriverAvailable | `ExpireDeliveryOfferCommandHandler` (`ExpireDeliveryOfferCommand.cs:173`) ← `DeliveryDispatchService.cs:236` |
| →Cancelled | `CancelDeliveryCommandHandler` (`DeliveryProgressCommands.cs:132`) ← `POST /api/deliveries/{id}/cancel` et `DeliveryGrpcService.cs:150` |
| **DriverAssigned→DriverAccepted** | **AUCUN** — `AcceptByDriver` n'a aucun appelant |
| **→ArrivedAtPickup, →PickedUp, →InTransit, →ArrivedAtDropoff, →Delivered** | **AUCUN** — les 5 commandes (`DeliveryProgressCommands.cs:23-39`) n'ont aucun émetteur |
| **DriverAccepted→SearchingDriver (révocation)** | **AUCUN** — `RevokeAssignment` sans appelant |

**Valeurs d'enum jamais atteintes** — vérifié par recherche de `Status = DeliveryStatus.X` :
- **`ArrivedAtPickup`** : aucune affectation ; le seul chemin est
  `Advance(DriverAccepted → ArrivedAtPickup)` (`:569`), commande sans route ;
- **`InTransit`** : aucune affectation ; `MarkArrivedAtDropoff` (`:596`) et `MarkDelivered`
  (`:628`) acceptent tous deux de sauter cet état, donc même branché il resterait
  optionnel.

Les 9 autres ont bien un site d'affectation dans l'agrégat — mais **6 d'entre elles ne
sont atteignables par aucun chemin d'exécution** (DriverAccepted, PickedUp,
ArrivedAtDropoff, Delivered, et les états intermédiaires ci-dessus).
États réellement observables en base : `Pending`, `SearchingDriver`, `DriverAssigned`,
`NoDriverAvailable`, `Cancelled`. **5 sur 11.**

**Transitions attendues mais absentes**
- acceptation par le livreur (rupture de la saga) ;
- les cinq étapes d'exécution ;
- révocation d'affectation par l'exploitation ;
- aucun chemin `DriverAssigned → Cancelled` explicite : `Cancel` l'autorise
  (`:723-731` n'interdit que `PickedUp`/`InTransit`/`ArrivedAtDropoff`), mais l'offre en
  cours n'est pas close — la ligne `DeliveryAssignment` reste `Offered` indéfiniment.

**Routes permettant une transition invalide**
- aucune côté `delivery-service` (les 4 routes exposées sont gardées) ;
- **mais** `POST /api/v1/dispatch/{deliveryId}/manual-assign`
  (`dispatch-service/.../DispatchEndpoints.cs:28`) et le RPC `AcceptOffer`
  (`DispatchGrpcService.cs:64`) écrivent une affectation **sans toucher l'agrégat ni sa
  base** : ils créent un état parallèle que `Delivery` ignore. Ce n'est pas une transition
  invalide, c'est une seconde vérité.

**Absence de protection concurrente** : `DeliveryConfiguration.cs` ne contient ni
`IsConcurrencyToken` ni `IsRowVersion` ; `HasIndex(d => d.AssignedDriverId)` (`:152`) n'est
pas unique. Deux acceptations simultanées passeraient toutes deux la garde `:478`.

**Statut : PARTIAL** — machine correcte et bien gardée, coupée en deux à `DriverAssigned`.

### 10b. DeliveryAssignment (issue d'une proposition)

```
Entité — Domain/Entities/DeliveryStatusHistory.cs:45  (le fichier ne contient PAS d'historique de statut)
Enum   — AssignmentOutcome { Offered, Accepted, Rejected, Expired, Revoked } (:6)
```
Affectations : `Offered` (`:52`), `Accepted` (`:83`), `Rejected` (`:89`), `Expired`
(`:96`), `Revoked` (`:102`).
Déclencheurs réels : `Offer` ← `Delivery.AssignTo:470` ; `Expire` ← `RejectByDriver` avec
`expired: true`, seul chemin joignable (`ExpireDeliveryOfferCommand.cs:173`).
**Jamais atteintes : `Accepted`** (`Accept()` n'est appelé que par `AcceptByDriver`, sans
appelant) et **`Revoked`** (`Revoke()` n'est appelé que par `RevokeAssignment`, sans
appelant). `Rejected` non plus en pratique : aucune route de refus explicite n'existe,
seule l'expiration automatique passe.
**Statut : PARTIAL.**

**Nommage trompeur (LOW)** : le fichier s'appelle `DeliveryStatusHistory.cs` et ne
contient aucun historique de statut. Il n'existe **aucune table d'historique des états
d'une course** dans le dépôt.

---

## 11. DispatchJob

```
Entité — services/delivery/dispatch-service/src/HBA.Delivery.Dispatch.Domain/Aggregates/DispatchJob/DispatchAggregate.cs
Enums  — DispatchStatus   { Pending, Offering, Assigned, Cancelled, NoDriverFound } (:291)
         AssignmentStatus { Assigned, Accepted, Cancelled } (:292)
```
**Transitions autorisées** : aucune — `DispatchJob` et `Assignment` sont des `record`
sans méthode ni invariant.
**Transitions réellement déclenchées** : aucune sur ces enums. Le service manipule des
**chaînes** dans `Application/Abstractions/DispatchStore.cs` :
`"OFFERING"` (`:25`, `:63`), `"PENDING"` (`:59`, dans un objet de repli jamais persisté),
`"ASSIGNED"` (`:98`, `:103`), `"CANCELLED"` (`:119`).

**Valeurs d'enum jamais atteintes : les 8.** `DispatchStatus` et `AssignmentStatus` ont
**zéro référence** dans tout le dépôt en dehors de leur déclaration.
La chaîne `"NO_DRIVER_FOUND"` n'est écrite nulle part non plus : le pendant de
`NoDriverFound` n'existe dans aucun des deux modèles.

**Transitions attendues mais absentes** : offre→acceptation (`AssignmentStatus.Accepted`
jamais écrit — `AssignAsync:98` crée directement `"ASSIGNED"`), et l'échec de recherche.
**Routes permettant une transition invalide** :
- `POST /api/v1/dispatch/{id}/manual-assign` (`DispatchEndpoints.cs:28`) et
  `AcceptOffer` (`DispatchGrpcService.cs:71`) → `DispatchStore.AssignAsync:91`, qui
  **écrase `_assignments[deliveryId]` sans lire l'existant** (`:99`) et force le job à
  `"ASSIGNED"` (`:103`) **même s'il est `"CANCELLED"`**. Deux livreurs peuvent être
  affectés successivement, chacun recevant son `DeliveryAssignedIntegrationEvent` (`:106`) ;
- `POST /api/v1/dispatch/{id}/retry` (`:18`) → `RetryAsync:53`, qui remet à `"OFFERING"`
  un job annulé ou déjà affecté, sans condition (`:61-67`).
- Toutes ces routes sont **anonymes** (`MapGroup` nu + hôte sans authentification).

**Statut : BROKEN.**

---

## 12. Driver

```
Entité réelle — services/delivery/driver-service/src/HBA.Delivery.Driver.Domain/Aggregates/Driver/DeliveryDriver.cs:92
                (namespace HBA.Deliveries.Domain.Drivers) — persistée par delivery-service
Enums          — DriverAccountStatus { PendingVerification, Active, Suspended, Blocked } (:12)
                 DriverAvailability  { Offline, Available, Busy, OnBreak } (:30)
```
**Transitions autorisées**
| De → Vers | Méthode | Garde |
|---|---|---|
| — → PendingVerification / Offline | ctor `:101-102` | — |
| ≠Blocked → Active | `Verify:176` (`:193`) | refuse si `Blocked` (`:178`), idempotent (`:188`) |
| ≠Blocked → Suspended + Offline | `Suspend:212` (`:220`, `:222`) | refuse si `Blocked` |
| * → Blocked + Offline | `Block:226` (`:228`, `:230`) | aucune |
| Offline\|OnBreak → Available | `GoOnline:236` (`:245`) | exige `AccountStatus == Active` (`:238`) |
| ≠Busy → Offline | `GoOffline:249` (`:261`) | refuse si `Busy` (`:254`) |
| ≠Busy → OnBreak | `TakeBreak:265` (`:277`) | refuse si `Busy` et si compte inactif |
| Available → Busy | `MarkBusy:289` (`:296`) | exige `CanReceiveOffers` (`:291`) |
| Busy → Available | `CompleteMission:301` (`:309`) | exige `Busy` (`:303`) |

**Transitions réellement déclenchées : UNE SEULE, et elle est inatteignable.**
`CompleteMission` a exactement un appelant,
`delivery-service/.../DeliveryProgressCommands.cs:197`, dans un handler
(`MarkDeliveredCommand`) qui n'a aucun émetteur.
`Register`, `Verify`, `Suspend`, `Block`, `GoOnline`, `GoOffline`, `TakeBreak`,
`MarkBusy`, `RecordPosition` : **0 appelant** (recherche exhaustive).

**Valeurs jamais atteintes, en pratique** : `Active`, `Suspended`, `Blocked`, `Available`,
`Busy`, `OnBreak` — **6 sur 8**. Seuls `PendingVerification` et `Offline` sont écrits, par
le constructeur… lui-même jamais invoqué puisque `Register:148` n'a pas d'appelant. En
l'état, **la table `deliveries.drivers` reste vide**.

**Deux énumérations entièrement mortes, en doublon** (cf. `SAGA_DRIVER.md` §1.8) :
- `Domain/Aggregates/Driver/DriverAggregate.cs:106-109` : `DriverStatus { Active,
  Suspended, Deleted }`, `DriverVerificationStatus { Pending, Verified, Rejected }`,
  `DriverAvailabilityStatus { Offline, Available, Busy, Paused }`, `VehicleType` —
  **0 référence** ;
- `Domain/Enums/DriverEnums.cs:39-42` : les mêmes, redéclarées — **0 référence**.

**Machine réellement servie par l'API** : des chaînes dans
`Application/Abstractions/DriverStore.cs` — `"ACTIVE"`/`"VERIFIED"` en dur au constructeur
(`:20-21`), et `NormalizeAvailability` (`:157-161`) qui écrit `"BUSY"`, `"PAUSED"`,
`"AVAILABLE"` ou `"OFFLINE"`. `"PAUSED"` ne correspond à aucune valeur de l'agrégat
(`OnBreak`), et `"BLOCKED"` n'existe pas.

**Transitions attendues mais absentes** : inscription, vérification, suspension, blocage,
mise en ligne, pause, prise et fin de mission — **la totalité**.
**Routes permettant une transition invalide** :
`POST /api/v1/drivers/me/availability` (`DriverEndpoints.cs:34` → `DriverStore.SetAvailabilityAsync:88`)
écrit n'importe quel statut de disponibilité **sans consulter le statut de compte** et
**sans authentification** — l'incident exact que `DeliveryDriver.cs:78-81` déclare
« impossible par construction ».
`POST /internal/v1/drivers/{id}/busy-state` (`:57`) force `"BUSY"`/`"AVAILABLE"` de même.

**Statut : BROKEN.**

---

## 13. TrackingSession

```
Entité — services/delivery/tracking-service/src/HBA.Delivery.Tracking.Domain/Aggregates/TrackingSession/TrackingAggregate.cs:3
Enum   — TrackingSessionStatus { Active, Completed, Cancelled } (:32)
```
**Transitions autorisées** : aucune — `TrackingSession` est un `record` sans méthode.
**Transitions réellement déclenchées** : aucune sur l'enum. Le service écrit des chaînes
dans `Application/Abstractions/TrackingStore.cs` : `"ACTIVE"` (`:19`), `"COMPLETED"`
(`:42`).
**Valeurs d'enum jamais atteintes : les 3** — `TrackingSessionStatus` a **0 référence**
hors de sa déclaration. Côté chaînes, `"CANCELLED"` n'est écrit nulle part : une session
ne peut pas être annulée.
**Transitions attendues mais absentes** : l'annulation ; et surtout le rattachement à la
course — rien ne relie une session au `DeliveryStatus`, la session survit à l'annulation
de la livraison.
**Routes permettant une transition invalide** :
- `POST /internal/v1/tracking/sessions/start` (`TrackingEndpoints.cs:34` →
  `TrackingStore.StartAsync:13`) **écrase inconditionnellement** `_sessions[deliveryId]`
  (`:20`) : une session `"COMPLETED"` redevient `"ACTIVE"`, avec un `driverId` arbitraire,
  et republie `TrackingSessionStartedIntegrationEvent` ;
- `POST /api/v1/tracking/sessions/{deliveryId}/locations` (`:13` →
  `AddLocationsAsync:54`) **crée une session à la volée** si elle n'existe pas (`:61-64`),
  avec le `driverId` du corps de requête. Route anonyme.
**Statut : BROKEN.**

---

## 14. Proof (preuve de livraison)

Trois modèles concurrents, dont deux entièrement morts.

### 14a. `ProofStatus` (proof-of-delivery-service, domaine)

```
Enum — services/delivery/proof-of-delivery-service/src/HBA.Delivery.Proof.Domain/Aggregates/DeliveryProof/DeliveryProof.cs:25
       ProofStatus { Draft, Verified, Rejected }
```
**Transitions autorisées** : aucune (`record`). Une politique existe,
`ProofValidationPolicy.ResolveStatus` (`:30`), et **n'est appelée par personne**.
**Valeurs jamais atteintes : les 3** — l'enum n'est référencée nulle part ailleurs.
Idem pour `ProofType { Pickup, Dropoff }` et `ProofMediaType { Photo, Signature, Document }`,
et pour les entités parallèles `Domain/Entities/ProofVerification.cs`,
`OtpChallenge.cs`, `ProofMedia.cs` et les policies `Domain/Policies/*` : aucune n'est
utilisée par le store.
**Statut : BROKEN.**

**Machine réellement servie** : des chaînes dans
`Application/Abstractions/ProofStore.cs` — `"DRAFT"` à la création (`:21`),
`"VERIFIED"` ou `"REJECTED"` à la soumission (`:60-62`).
**Route permettant une transition invalide** :
`POST /api/v1/proofs/{id}/submit` (`ProofEndpoints.cs:27` → `SubmitAsync:48`) n'a
**aucune garde d'état** : une preuve déjà `"VERIFIED"` peut être resoumise et rebasculée
en `"REJECTED"`, en republiant `ProofRejectedIntegrationEvent` (`:97`). Route anonyme.

### 14b. `ProofOfDeliveryKind` (delivery-service — le seul modèle vérifiant réellement)

```
Enum — services/delivery/delivery-service/src/HBA.Delivery.Core.Domain/Enums/ProofOfDeliveryKind.cs:35
       { None, Pin, Photo, Signature }
```
**Transitions autorisées** : la valeur est figée à la création (`Delivery.cs:76`) et
vérifiée à la remise (`Entities/ProofOfDelivery.cs:77-161`). Le mécanisme est correct :
PIN émis par `RandomNumberGenerator` (`:70`), comparé à temps constant (`:128`), photo et
signature validées comme références de stockage (`:144-161`), verrouillage après 5 échecs
(`Delivery.cs:226-229`).
**Valeurs jamais atteintes : `Pin`, `Photo`, `Signature`.**
Le contrat par défaut vaut `"None"`
(`HBA.Deliveries.Contracts/DeliveryDispatchContracts.cs:45`) et **aucun producteur ne le
renseigne** (`CreateDeliveryOnOrderConfirmedHandler.cs:213-269`,
`FoodOrderBridgeHandlers.cs:284-317`). Toute course de la plateforme naît avec `None`.
**Statut : BROKEN** (l'énumération promet trois modes de preuve, un seul est atteint et
c'est « aucune preuve »).

---

## 15. ReturnRequest

```
Entité — services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Domain/Aggregates/ReturnRequest/ReturnRequest.cs
Enum   — Domain/Enums/ReturnEnums.cs : ReturnStatus (16 valeurs)
```
**Transitions autorisées** : table explicite `ReturnStateMachine.Transitions`
(`Domain/Policies/ReturnStateMachine.cs:7-26`), appliquée par `MoveTo`
(`ReturnRequest.cs:322-333`). Bonne conception : liste blanche, historique horodaté avec
acteur (`AddHistory:338`), jeton de concurrence (`ReturnRequestConfiguration.cs:26` —
**le seul du dépôt**).

**Transitions réellement déclenchées**
| Vers | Méthode | Appelant |
|---|---|---|
| Requested | ctor `:46` | `ReturnRequest.Create` |
| Approved \| AwaitingApproval | `Create:152-155` (auto-approbation selon la politique) | `CreateReturnCommandHandler.cs:92` |
| Approved | `Approve:182` | `ApproveReturnCommandHandler` ← `POST /api/v1/seller/returns/{id}/approve` |
| Rejected | `Reject:194` | `RejectReturnCommandHandler` ← `.../reject` **et** `POST /api/v1/admin/returns/{id}/override` |
| Cancelled | `Cancel:207` | `CancelReturnCommandHandler` ← `POST /api/v1/marketplace/returns/{id}/cancel` |
| AwaitingReturn | `RegisterShipment:219` | `.../shipment` |
| Received | `Receive:240` | `.../receive` |
| InspectionPending | `Inspect:257` | `.../inspection` |
| RefundPending | `DecideRefund:275` | `.../refund-decision` |
| Refunded | `MarkRefundSucceeded:289` | `ExecuteRefundCommandHandler.cs:79` — **handler sans émetteur** |
| Closed | `Close:309` | `CloseReturnCommandHandler` ← `POST /api/v1/admin/returns/{id}/close` |

**Valeurs jamais atteintes — 5 :**
- **`EligibilityCheck`** : figure dans la table de transitions (`:10`, `:11`) mais aucun
  `MoveTo(ReturnStatus.EligibilityCheck, …)` n'existe ;
- **`InReturnTransit`** : `MarkInTransit:237` est le seul chemin, et **n'a aucun
  appelant** (aucune commande, aucune route, aucun consumer) ;
- **`ManualReview`** : déclarée comme sortie de `EligibilityCheck`, `InReturnTransit` et
  `RefundPending` (`:11`, `:15`, `:18`) — jamais assignée ;
- **`Expired`** : déclarée comme sortie de `AwaitingApproval` et `AwaitingReturn`
  (`:12`, `:14`) — jamais assignée ; aucune tâche de fond n'expire un retour ;
- **`RejectedAfterInspection`** : déclarée comme sortie de `Received` et
  `InspectionPending` (`:16`, `:17`) — `Inspect` ne fait que passer à `InspectionPending`,
  il n'existe aucun chemin vers ce refus après inspection.

`EligibilityCheck` et `ManualReview` apparaissent aussi côté **départ** dans la table
(`:11`, `:20`) : les 7 transitions qui en partent sont donc inaccessibles elles aussi.

**Transitions attendues mais absentes** : contrôle d'éligibilité, expiration automatique,
mise en revue manuelle, refus après inspection, prise en charge du retour par le
transporteur.
**Routes permettant une transition invalide** : la machine elle-même est étanche, mais
`SellerReturnsEndpoints.cs:14-22` autorise **tout compte portant le rôle `Seller`** à faire
avancer le dossier d'un **autre** vendeur (aucun contrôle d'appartenance dans les handlers,
`ReturnLifecycleCommands.cs:69-198`), y compris `refund-decision`.

**Défaut bloquant hors machine à états** : le service **n'a aucune migration**
(`find services/marketplace/return-refund-service -type d -name Migrations` → vide) alors
que `Api/Program.cs:21` appelle `MigrateHbaDatabaseAsync`. Le schéma n'est jamais créé.
**Statut : PARTIAL** (machine bien conçue, 5 états morts, service non déployable).

---

## 16. Refund

```
Entité — .../Aggregates/ReturnRequest/Refund.cs:8
Enum   — Domain/Enums/ReturnEnums.cs : RefundStatus { Pending, Processing, Succeeded, PartiallySucceeded, Failed, Cancelled }
```
**Transitions autorisées** : **aucune garde**. `MarkProcessing:42`, `MarkSucceeded:48` et
`MarkFailed:56` sont des `void` sans vérification de l'état de départ. Un remboursement
`Succeeded` peut être repassé `Processing` puis `Failed`.
**Transitions réellement déclenchées** : `Pending` (ctor `:24`) ;
`Failed` (`ExecuteRefundCommandHandler.cs:74`) ; `Succeeded` (via
`ReturnRequest.MarkRefundSucceeded:289` → `refund.MarkSucceeded`,
`ExecuteRefundCommandHandler.cs:79`). **Ce handler n'a aucun émetteur** — voir
`SAGA_ADMIN.md` §6b : `ExecuteRefundCommand` n'est instancié nulle part.
En pratique, **un `Refund` reste `Pending` pour toujours**.
**Valeurs jamais atteintes** :
- **`PartiallySucceeded`** : lue par `ReturnRequest.TotalRefunded:336` pour calculer le
  cumul déjà remboursé, et **jamais écrite** — le calcul de plafond est donc faux dès
  qu'un remboursement partiel devrait exister ;
- **`Cancelled`** : **0 référence** dans le dépôt.
- (`Processing`, `Succeeded`, `Failed` sont écrites mais inatteignables faute d'émetteur.)
**Transitions attendues mais absentes** : l'exécution du remboursement, et l'annulation.
**Routes permettant une transition invalide** : aucune route n'atteint ces méthodes.
**Statut : PARTIAL** (BROKEN en pratique : aucun remboursement n'est jamais versé).

---

## 17. Payment

```
Entité — services/common/payment-service/src/HBA.Financial.Payments.Domain/Payments/Payment.cs
Enums  — Payments/PaymentIds.cs : PaymentStatus { Pending, Authorized, Captured, Failed, Refunded }
                                  PaymentRefundStatus { Processing, Succeeded, Failed }
```
**Transitions autorisées**
| De | Vers | Méthode |
|---|---|---|
| Pending | Authorized | `Authorize:113` (garde `:115`) |
| Pending \| Authorized | Captured | `Capture:126` (garde `:128`) |
| ≠Captured, ≠Refunded | Failed | `Fail:142` (garde `:144`) |
| Captured | (refund) | `BeginRefund:176` (garde `:184`) |
| Captured | Refunded | `MarkRefundSucceeded:250` (`:266`, si tout est remboursé) |

**Transitions réellement déclenchées** : `Capture` ← `PaymentLifecycleCommands.cs:54` et
`GatewayConfirmationCommands.cs:32` (webhook prestataire) ; `Fail` ← même chemin ;
`BeginRefund` ← `PaymentLifecycleCommands.cs:212` ; `MarkRefundSucceeded` ←
`PaymentLifecycleCommands.cs:262` ; `ReleaseEscrow:314` ←
`ReleaseEscrowOnOrderDeliveredHandler.cs:32`.

**Valeurs jamais atteintes : `Authorized`.** `Payment.Authorize:113` n'a **aucun
appelant** (recherche exhaustive : les seules occurrences de `.Authorize(` du dépôt sont
des méthodes privées de signature HTTP dans `Infrastructure/Gateways/Real/*`).
Le flux passe directement `Pending → Captured` (`:128` l'autorise). La pré-autorisation
promise par l'énumération n'existe pas.
**Transitions attendues mais absentes** : autorisation puis capture différée ; annulation
d'une autorisation.
**Routes permettant une transition invalide** : aucune ; les gardes sont dans l'agrégat
et le commentaire `PaymentConfiguration.cs:16` documente le risque de double webhook —
traité par la garde d'état.
**Statut : PARTIAL.**

`PaymentRefundStatus` : les 3 valeurs sont assignées (`PaymentRefund.cs:28`, `:50`, `:58`,
`:67`). **COHERENT.**

---

## 18. Wallet / Payout

Cinq énumérations dans `services/common/wallet-service/`.

### `WithdrawalStatus` — `Domain/Wallets/WalletPrimitives.cs`
`{ Pending, Completed, Failed, Requested, Rejected, Processing }`
Affectations : `Requested` (ctor `Wallets/Withdrawal.cs:29`), `Processing` (`:115`),
`Completed` (`:127`), `Failed` (`:135`), `Rejected` (`:143`).
Déclencheurs : `POST /api/financial/wallets/withdrawals/{id}/approve` et `/reject`
(`FinancialEndpoints.cs:151-152`, `.RequireAdmin()`), puis `WalletCommands.cs:322`, `:340`,
`:411` et `WithdrawalSettlement.cs:52`, `:71`.
**Valeur jamais atteinte : `Pending`** — 0 référence.
**Gardes hors de l'agrégat** : `Complete`, `Fail`, `Reject`, `MarkProcessing` sont des
`void` sans garde (`:113-145`) ; la seule protection est applicative
(`WalletCommands.cs:216`, `:389` : `if (!withdrawal.IsPendingApproval)`). Un futur appelant
qui l'oublierait pourrait rejeter un retrait déjà versé. **MEDIUM.**
**Statut : PARTIAL.**

### `SettlementStatus` — `Domain/Batches/SettlementBatch.cs`
`{ Pending, Processing, Completed, PartiallyFailed, Cancelled }` — les 5 sont assignées
(`:94`, `:199`, `:191`, `:195`, `:183`). **COHERENT.**

### `PayoutStatus` (lot de règlement) — `Domain/Batches/SettlementBatch.cs`
`{ Scheduled, Paid, Failed }` — `Scheduled` (`:53`), `Paid` (`:67`), `Failed` (`:72`).
`Failed` n'est écrit que par `payout.MarkFailed()` appelé depuis
`SettlementBatch.MarkPayoutFailed:143` — **qui n'a aucun appelant**.
**Valeur jamais atteinte : `Failed`.** Conséquence financière : un virement vendeur refusé
par le prestataire reste `Scheduled` ; le vendeur est débité de son solde et n'est jamais
payé. Le défaut est **reconnu dans le code** (`FinancialEndpoints.cs:186`, « tâche #190 »).
**Statut : PARTIAL — impact CRITICAL.**

### `EarningStatus` — `Domain/Earnings/SellerEarning.cs`
`{ Accrued, Released, Settled, Reversed }` — `Accrued` (`:54`), `Released` (`:132`, `:200`),
`Settled` (`:156`, `:176`).
**Valeur jamais atteinte : `Reversed`.** Aucun appelant de la méthode de contrepassation.
Un gain libéré sur une vente ensuite annulée ou remboursée n'est jamais repris.
**Statut : PARTIAL — impact HIGH.**

### `CustomerRefundStatus` — `Domain/Wallets/WalletPrimitives.cs`
`{ Processing, Completed, Failed }` — les 3 assignées (`Wallets/CustomerRefund.cs:33`,
`:67`, `:76`, `:85`). **COHERENT.**

### `PayoutStatus` (passerelle) — `payment-service/.../Gateways/IPayoutGateway.cs`
`{ Pending, Started, Processing, Sent, Failed, Unknown }` — énumération de traduction d'un
statut prestataire, sans persistance. Hors périmètre machine à états.

---

## 19. SellerMember

```
Entité — services/marketplace/seller-service/src/HBA.Merchants.Domain/Members/SellerMember.cs
Enums  — MemberStatus { Invited, Active, Suspended, Revoked, Left }
         StoreMembershipStatus { Active, Suspended }
```
**Transitions autorisées** : Active→Suspended (`Suspend:492`, `:511`) ;
Suspended→Active (`Reactivate:518`, `:543`, refuse depuis `Revoked`/`Left` `:531`) ;
*→Revoked (`Revoke:557`, `:576`) ; *→Left (`Leave:584`, `:597`, refuse si déjà
`Left`/`Revoked` `:592`).
**Transitions réellement déclenchées** : `MemberCommands.cs:532` (`suspend`), `:538`
(`activate`), `:557`/`Revoke` (`DELETE /{memberId}`), `:575` (`DELETE /me`) — toutes sous
`MapSellerGroup` (`MerchantEndpoints.cs:220`), gardées par la résolution d'appartenance et
les permissions `MerchantPermission`.
**Valeur jamais atteinte : `Invited`.** Les trois constructions du membre
(`:251-252` propriétaire, `:309-310` ajout direct, `:358-359` acceptation d'invitation)
posent toutes `MemberStatus.Active`. L'état « invité, pas encore entré » n'existe pas :
c'est `SellerInvitation` qui le porte.
**Transitions attendues mais absentes** : Invited→Active (l'acceptation crée un membre
neuf plutôt que de faire transiter un membre invité).
**Routes permettant une transition invalide** : aucune.
**Statut : PARTIAL.**

`StoreMembershipStatus` : les 2 valeurs sont assignées (`:110`, `:139`, `:145`).
**COHERENT.**

---

## 20. SellerInvitation

```
Entité — services/marketplace/seller-service/src/HBA.Merchants.Domain/Members/SellerInvitation.cs
Enum   — InvitationStatus { Pending, Accepted, Declined, Expired, Revoked }
```
**Transitions autorisées** : →Pending (ctor `:106`, et `Refresh:365` → `:441`) ;
Pending→Accepted (`Accept:254`, `:296`) ; Pending→Declined (`Decline:303`, `:318`) ;
Pending→Expired (`:282`, à la lecture d'une invitation périmée) ;
Pending→Revoked (`Revoke:324`, `:344`).
**Transitions réellement déclenchées** : `POST /api/v1/merchants/invitations/...` pour
l'acceptation (`MerchantEndpoints.cs:320`) ; `POST /{sellerId}/members/invitations/{id}/resend`
(`:225` → `Refresh`) ; `DELETE .../invitations/{id}` (`:226` → `Revoke`).
**Valeur jamais atteinte : `Declined`.** `Decline:303` a **0 appelant** : aucune route ne
permet de refuser une invitation. Un invité qui ne veut pas entrer doit attendre
l'expiration ou demander une révocation.
**Transitions attendues mais absentes** : le refus explicite.
**Routes permettant une transition invalide** : aucune.
**Statut : PARTIAL.**

---

## Annexe — Valeurs d'énumération jamais assignées (liste complète)

| Énumération | Fichier | Valeurs mortes |
|---|---|---|
| `KitchenTicketStatus` | `food/restaurant-service/.../Orders/FoodOrderStatus.cs` | Pending, Preparing, Ready, Cancelled (**4/4**) |
| `DispatchStatus` | `delivery/dispatch-service/.../DispatchAggregate.cs:291` | Pending, Offering, Assigned, Cancelled, NoDriverFound (**5/5**) |
| `AssignmentStatus` | idem `:292` | Assigned, Accepted, Cancelled (**3/3**) |
| `TrackingSessionStatus` | `delivery/tracking-service/.../TrackingAggregate.cs:32` | Active, Completed, Cancelled (**3/3**) |
| `ProofStatus` / `ProofType` / `ProofMediaType` | `delivery/proof-of-delivery-service/.../DeliveryProof.cs:24-26` | **8/8** |
| `DriverStatus` / `DriverVerificationStatus` / `DriverAvailabilityStatus` / `VehicleType` | `delivery/driver-service/.../DriverAggregate.cs:106-109` | **14/14** |
| `DriverAccountStatus` / `DriverAvailabilityStatus` / `DriverVerificationStatus` / `VehicleType` | `delivery/driver-service/.../Enums/DriverEnums.cs:39-42` | **16/16** |
| `RouteProvider` / `RouteOptimizationMode` | `delivery/route-service/.../Enums/RouteEnums.cs` | **7/7** |
| `DeliveryStatus` | `delivery/delivery-service/.../Enums/DeliveryStatus.cs` | ArrivedAtPickup, InTransit (**2/11**) |
| `AssignmentOutcome` | `delivery/delivery-service/.../DeliveryStatusHistory.cs:6` | Accepted, Revoked (**2/5**) |
| `ProofOfDeliveryKind` | `delivery/delivery-service/.../Enums/ProofOfDeliveryKind.cs` | Pin, Photo, Signature (**3/4**) |
| `ReturnStatus` | `marketplace/return-refund-service/.../ReturnEnums.cs` | EligibilityCheck, InReturnTransit, ManualReview, Expired, RejectedAfterInspection (**5/16**) |
| `RefundStatus` | idem | PartiallySucceeded, Cancelled (**2/6**) |
| `PaymentStatus` | `common/payment-service/.../PaymentIds.cs` | Authorized (**1/5**) |
| `WithdrawalStatus` | `common/wallet-service/.../WalletPrimitives.cs` | Pending (**1/6**) |
| `PayoutStatus` (lot) | `common/wallet-service/.../SettlementBatch.cs` | Failed (**1/3**) |
| `EarningStatus` | `common/wallet-service/.../SellerEarning.cs` | Reversed (**1/4**) |
| `MemberStatus` | `marketplace/seller-service/.../SellerMember.cs` | Invited (**1/5**) |
| `InvitationStatus` | `marketplace/seller-service/.../SellerInvitation.cs` | Declined (**1/5**) |
| `ReviewStatus` (food) | `food/review-service/.../Enums/ReviewStatus.cs:3` | Published, Hidden, Reported (**3/3** — l'enum n'a aucune référence : `FoodReview` ne porte pas de statut, `ReviewStore.cs` n'en assigne aucun) |

**Total : 83 valeurs d'énumération de statut déclarées et jamais assignées.**
