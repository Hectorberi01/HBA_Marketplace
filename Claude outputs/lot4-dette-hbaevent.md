# Lot 4 — la dette `[HbaEvent]` : ce que j'avais écrit était faux

Le plan disait : *« Ajouter `[HbaEvent]` aux 76… Le nom canonique doit **reproduire** le nom du repli, sinon c'est un changement de contrat déguisé. »*

J'ai vérifié avant d'écrire une ligne. **Cette règle est contredite par les 48 événements déjà attribués : aucun ne reproduit son nom de repli. Zéro sur 48.**

---

## 1. Ce que la vérification montre

Le nom sur le fil vient aujourd'hui de `KafkaEventNaming.EventType` — le nom de classe, sans le suffixe `IntegrationEvent`, découpé aux majuscules. Le publieur le pose, le consommateur résout les types avec la **même** fonction. `[HbaEvent]` n'est lu que pour la **version**.

Les 48 attributs existants portent donc des noms qui ne sont utilisés **nulle part**, et qui diffèrent tous du nom réel :

| Classe | Sur le fil aujourd'hui | Nom canonique déclaré |
|---|---|---|
| `PaymentCaptured` | `payment.captured` | **`payment.succeeded`** |
| `PayoutPaid` | `payout.paid` | **`payout.completed`** |
| `DriverEarningCredited` | `driver.earning.credited` | **`wallet.credited`** |
| `UserProfileChanged` | `user.profile.changed` | **`user.profile.updated`** |
| `RouteDeliveryEtaUpdated` | `route.delivery.eta.updated` | **`delivery.eta-updated`** |
| `UserLoggedIn` | `user.logged.in` | **`identity.user.logged_in`** |
| `ProductApproved` | `product.approved` | **`catalog.product.approved`** |
| `MediaProcessingFailed` | `media.processing.failed` | **`media.processing_failed`** |

Ce ne sont pas des coquilles. `payment.succeeded` et `payout.completed` sont des **renommages métier délibérés** — le commentaire de `HbaEventAttribute` explique même pourquoi `payment.succeeded` n'a que deux segments : quatre services le nomment ainsi, et « entre une règle et l'usage que quatre services partagent, c'est l'usage qui est le contrat ».

**Conséquence : `[HbaEvent]` n'est pas une formalisation du nom existant, c'est un renommage en attente.**

---

## 2. Ce que ça change pour le lot 4

**La bonne nouvelle.** Poser `[HbaEvent]` sur les 76 est **inerte aujourd'hui**, quel que soit le nom choisi : rien ne le lit sauf la version. Je peux le faire sans aucun risque de production.

**La mauvaise.** Le nom que je choisirais **devient le contrat futur**. Et il ne se déduit pas : les 48 existants n'obéissent à aucune règle unique — `driver.created` sans préfixe de domaine à côté de `catalog.product.approved` qui en a un, `payment.succeeded` à deux segments à côté de `identity.user.logged_in` à trois.

**Et surtout, l'ordre que j'avais donné ne suffit pas.** Je disais : attribuer les 76, vérifier, puis brancher le nommage canonique. Or brancher le nommage **renomme d'un coup les 48 déjà attribués**, pas seulement les 76. Un consommateur déployé avant son producteur cherchera un nom qui n'existe plus : « événement reçu et NON RECONNU », en silence.

Le basculement demande donc une **fenêtre de double lecture** — le consommateur accepte l'ancien ET le nouveau nom — puis un retrait de l'ancien une fois la rétention écoulée. Ce n'est pas un ordre de déploiement, c'est un mécanisme à écrire dans `ResolveEventType`.

---

## 3. Ce que je te demande

Le tableau ci-dessous propose un nom canonique pour chacun des 76, selon **une seule règle** : `{domaine du service producteur}.{agrégat}.{action}`, le domaine venant de `HbaTopics.DomaineParService`. Une ligne marquée  signale que le nom changera au basculement ; les autres sont déjà conformes.

Je ne les écris pas avant ton passage dessus : un nom d'événement est un contrat entre services, et j'en ai déjà réécrit un par script cette semaine sans le vouloir.

Trois façons de répondre, de la plus rapide à la plus fine :

1. **« Applique la règle »** — j'écris les 76 tels quels et je vide les listes `SansDescripteur`.
2. **« Applique, sauf ceux-ci »** — tu me listes les exceptions, comme `payment.succeeded` en son temps.
3. **« On ne fige rien »** — on laisse les 76 sans attribut et on ferme d'abord la double lecture. C'est défendable : tant que le mécanisme de bascule n'existe pas, figer 76 noms de plus, c'est agrandir la dette qu'il faudra migrer.

Mon avis : **option 3, puis 1**. Écrire le mécanisme de double lecture d'abord rend le reste réversible ; l'inverse nous engage sur 124 noms sans savoir comment les changer.

---

## 4. Le tableau des 76

### `cart-service` — domaine `commerce` (1)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `CartCheckedOut` | `cart.checked.out` | `commerce.cart.checked.out`  |

### `catalog-service` — domaine `catalog` (4)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `BrandCreated` | `brand.created` | `catalog.brand.created`  |
| `CategoryCreated` | `category.created` | `catalog.category.created`  |
| `ProductDeleted` | `product.deleted` | `catalog.product.deleted`  |
| `ProductMediaRemoved` | `product.media.removed` | `catalog.product.media.removed`  |

### `delivery-service` — domaine `delivery` (7)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `DeliveryAccepted` | `delivery.accepted` | `delivery.accepted` |
| `DeliveryAssigned` | `delivery.assigned` | `delivery.assigned` |
| `DeliveryCancelled` | `delivery.cancelled` | `delivery.cancelled` |
| `DeliveryCompleted` | `delivery.completed` | `delivery.completed` |
| `DeliveryCreated` | `delivery.created` | `delivery.created` |
| `DeliveryNoDriverAvailable` | `delivery.no.driver.available` | `delivery.no.driver.available` |
| `DeliveryPickedUp` | `delivery.picked.up` | `delivery.picked.up` |

### `food-cart-service` — domaine `food-cart` (1)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `FoodCartCheckedOut` | `food.cart.checked.out` | `food-cart.food.cart.checked.out`  |

### `food-order-service` — domaine `food-order` (6)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `MealOrderCancelled` | `meal.order.cancelled` | `food-order.meal.order.cancelled`  |
| `MealOrderConfirmed` | `meal.order.confirmed` | `food-order.meal.order.confirmed`  |
| `MealOrderDelivered` | `meal.order.delivered` | `food-order.meal.order.delivered`  |
| `MealOrderPlaced` | `meal.order.placed` | `food-order.meal.order.placed`  |
| `MealOrderResumedAfterReview` | `meal.order.resumed.after.review` | `food-order.meal.order.resumed.after.review`  |
| `MealOrderUnderReview` | `meal.order.under.review` | `food-order.meal.order.under.review`  |

### `identity-service` — domaine `identity` (5)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `EmailVerificationRequested` | `email.verification.requested` | `identity.email.verification.requested`  |
| `PasswordResetRequested` | `password.reset.requested` | `identity.password.reset.requested`  |
| `UserAnonymized` | `user.anonymized` | `identity.user.anonymized`  |
| `UserEmailConfirmed` | `user.email.confirmed` | `identity.user.email.confirmed`  |
| `UserProfileUpdated` | `user.profile.updated` | `identity.user.profile.updated`  |

### `inventory-service` — domaine `inventory` (3)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `StockDepleted` | `stock.depleted` | `inventory.stock.depleted`  |
| `StockReplenished` | `stock.replenished` | `inventory.stock.replenished`  |
| `StockReserved` | `stock.reserved` | `inventory.stock.reserved`  |

### `notification-service` — domaine `communication` (1)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `MessageSent` | `message.sent` | `communication.message.sent`  |

### `order-service` — domaine `order` (7)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `OrderCancelled` | `order.cancelled` | `order.cancelled` |
| `OrderConfirmed` | `order.confirmed` | `order.confirmed` |
| `OrderDelivered` | `order.delivered` | `order.delivered` |
| `OrderPlaced` | `order.placed` | `order.placed` |
| `OrderResumedAfterReview` | `order.resumed.after.review` | `order.resumed.after.review` |
| `OrderUnderReview` | `order.under.review` | `order.under.review` |
| `SellerOrderRefused` | `seller.order.refused` | `order.seller.order.refused`  |

### `restaurant-service` — domaine `food` (12)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `FoodOrderAccepted` | `food.order.accepted` | `food.order.accepted` |
| `FoodOrderCancelled` | `food.order.cancelled` | `food.order.cancelled` |
| `FoodOrderDelivered` | `food.order.delivered` | `food.order.delivered` |
| `FoodOrderPickedUp` | `food.order.picked.up` | `food.order.picked.up` |
| `FoodOrderPreparing` | `food.order.preparing` | `food.order.preparing` |
| `FoodOrderReadyForPickup` | `food.order.ready.for.pickup` | `food.order.ready.for.pickup` |
| `FoodOrderReceived` | `food.order.received` | `food.order.received` |
| `FoodOrderRejected` | `food.order.rejected` | `food.order.rejected` |
| `RestaurantApproved` | `restaurant.approved` | `food.restaurant.approved`  |
| `RestaurantRejected` | `restaurant.rejected` | `food.restaurant.rejected`  |
| `RestaurantReopened` | `restaurant.reopened` | `food.restaurant.reopened`  |
| `RestaurantSuspended` | `restaurant.suspended` | `food.restaurant.suspended`  |

### `return-refund-service` — domaine `return-refund` (2)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `ReturnRefundApproved` | `return.refund.approved` | `return-refund.return.refund.approved`  |
| `ReturnRefunded` | `return.refunded` | `return-refund.return.refunded`  |

### `review-service` — domaine `engagement` (3)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `ReviewPublished` | `review.published` | `engagement.review.published`  |
| `ReviewRejected` | `review.rejected` | `engagement.review.rejected`  |
| `SellerRatingRecomputed` | `seller.rating.recomputed` | `engagement.seller.rating.recomputed`  |

### `seller-service` — domaine `merchant` (24)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `KybDocumentRemoved` | `kyb.document.removed` | `merchant.kyb.document.removed`  |
| `SellerActivated` | `seller.activated` | `merchant.seller.activated`  |
| `SellerClosed` | `seller.closed` | `merchant.seller.closed`  |
| `SellerDeleted` | `seller.deleted` | `merchant.seller.deleted`  |
| `SellerKybApproved` | `seller.kyb.approved` | `merchant.seller.kyb.approved`  |
| `SellerKybRejected` | `seller.kyb.rejected` | `merchant.seller.kyb.rejected`  |
| `SellerKybSubmitted` | `seller.kyb.submitted` | `merchant.seller.kyb.submitted`  |
| `SellerMemberActivated` | `seller.member.activated` | `merchant.seller.member.activated`  |
| `SellerMemberInvited` | `seller.member.invited` | `merchant.seller.member.invited`  |
| `SellerMemberJoined` | `seller.member.joined` | `merchant.seller.member.joined`  |
| `SellerMemberRevoked` | `seller.member.revoked` | `merchant.seller.member.revoked`  |
| `SellerMemberRolesUpdated` | `seller.member.roles.updated` | `merchant.seller.member.roles.updated`  |
| `SellerMemberStoreAssigned` | `seller.member.store.assigned` | `merchant.seller.member.store.assigned`  |
| `SellerMemberStoreUnassigned` | `seller.member.store.unassigned` | `merchant.seller.member.store.unassigned`  |
| `SellerMemberSuspended` | `seller.member.suspended` | `merchant.seller.member.suspended`  |
| `SellerOwnershipTransferred` | `seller.ownership.transferred` | `merchant.seller.ownership.transferred`  |
| `SellerReactivated` | `seller.reactivated` | `merchant.seller.reactivated`  |
| `SellerRegistered` | `seller.registered` | `merchant.seller.registered`  |
| `SellerSuspended` | `seller.suspended` | `merchant.seller.suspended`  |
| `SellerSuspensionLifted` | `seller.suspension.lifted` | `merchant.seller.suspension.lifted`  |
| `StoreClosed` | `store.closed` | `merchant.store.closed`  |
| `StoreOpened` | `store.opened` | `merchant.store.opened`  |
| `StoreSuspended` | `store.suspended` | `merchant.store.suspended`  |
| `StoreSuspensionLifted` | `store.suspension.lifted` | `merchant.store.suspension.lifted`  |

### `— aucun producteur —` — domaine `?` (4)

| Classe | Sur le fil aujourd'hui | Proposition canonique |
|---|---|---|
| `ProductCreated` | `product.created` | `?.product.created`  |
| `ShipmentDelivered` | `shipment.delivered` | `?.shipment.delivered`  |
| `ShipmentReadyForPickup` | `shipment.ready.for.pickup` | `?.shipment.ready.for.pickup`  |
| `ShipmentShipped` | `shipment.shipped` | `?.shipment.shipped`  |
