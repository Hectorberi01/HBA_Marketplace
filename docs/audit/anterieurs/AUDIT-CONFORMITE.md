# Audit de conformite — code HBA vs Cahier des charges Backend v2

17 aout 2026 · perimetre : 16 services de la section 10 du cahier des charges
Base analysee : 1 066 fichiers C#, 13 fichiers `.proto`, commit `a726cb0`

---

## Verdict en une phrase

Le code couvre **une grande partie du territoire fonctionnel** decrit par la spec, mais
**ne respecte aucun de ses contrats publics**. Ce n'est pas un projet a ecrire : c'est un
projet a renommer, versionner et normaliser — plus quelques trous reels.

## Le chiffre qui resume tout

| Dimension | Conforme | Equivalent sous un autre nom | Absent | Total |
|---|---|---|---|---|
| Agregats / tables | — | 22 | 38 | 60 |
| RPC gRPC | 13 | — | 40 | 53 |
| Evenements Kafka | 3 | 20 | 67 | 90 |
| Endpoints REST | **0** | 40 | 21 | 61 |
| Codes d'erreur normalises | **0** | — | 80 | 80 |

**Zero endpoint REST sur 61 correspond exactement au chemin de la spec.** Les 40 comptes
comme « equivalents » ont le meme segment final sous un prefixe different :
`/api/identity/auth/login` la ou la spec demande `/api/v1/auth/login`. Aucun client ecrit
sur le contrat du cahier des charges ne trouverait sa route.

---

## Les six causes racines

Presque tous les ecarts ci-dessus remontent a six decisions, pas a 200 oublis
independants. Les corriger dans cet ordre fait tomber la majorite des lignes rouges.

### 1. Les routes REST n'ont ni version ni prefixe de domaine

Spec : `/api/v1/<domaine>/<ressource>`. Code : `/api/<service>/<ressource>`, sans `v1`.
Consequence mecanique : 61 endpoints sur 61 hors contrat. C'est l'ecart le moins couteux
a corriger et le plus visible pour les equipes mobile/web.

### 2. Le nommage Kafka est derive du nom de classe .NET

`KafkaEventNaming.EventType()` transforme `OrderPlacedIntegrationEvent` en `order.placed`.
La spec impose `<domaine>.<agregat>.<action>` en trois segments — `marketplace.order.created`.
Et `KafkaEventNaming.Topic()` produit **un topic par service producteur**
(`{prefix}.{service}.{version}`) la ou la spec impose un topic par agregat
(`hba.<env>.<domaine>.<agregat>.v<major>`).

C'est l'ecart le plus structurant du dossier : ni les noms d'evenement ni les topics ne
correspondent, donc un consumer ecrit selon la spec ne recevrait **rien**. Et comme le nom
est calcule depuis le type .NET, on ne peut pas le corriger evenement par evenement sans
toucher au socle.

### 3. Le vocabulaire metier n'est pas celui de la spec

| Spec | Code |
|---|---|
| `merchants` / `outlets` | `Seller` / `Store` |
| `variants` | `Offer` |
| `payment_intents` | `Payment` |
| `stocks` | `InventoryItem` + `FulfillmentLocation` |

Ce n'est pas une absence de fonctionnalite, c'est un autre dictionnaire. La question a
trancher est politique avant d'etre technique : **on aligne le code sur la spec, ou on
met la spec a jour ?** Renommer `Seller` en `Merchant` touche 83 fichiers, les protos, les
topics et les bases.

### 4. Les signatures gRPC ne portent pas les noms du contrat

Le paiement illustre le motif general : la spec demande `CreatePaymentIntent`,
`GetPaymentStatus`, `RefundPayment` ; le code expose `InitiatePayment`, `CapturePayment`,
`FailPayment`, `RefundPayment`, `GetPayment`, `GetPaymentByOrder`. Le code est **plus riche**
que la spec — il modelise la capture et l'echec separement — mais aucun appelant conforme
ne le trouverait. Seul `RefundPayment` coincide.

### 5. Les codes d'erreur normalises n'existent pas

Zero occurrence de `VALIDATION_ERROR`, `BUSINESS_RULE_VIOLATION`, `CONFLICT`,
`DEPENDENCY_UNAVAILABLE` ou d'un quelconque `*_SERVICE_NOT_FOUND` dans les 1 066 fichiers.
L'enveloppe d'erreur de la section 5 (`success`/`error.code`/`error.message`/`meta.requestId`)
n'est donc portee par rien.

### 6. Le wallet n'est pas en partie double

C'est le seul ecart qui est un **choix d'architecture**, pas un renommage. La spec impose
`accounts` + `ledger_entries` immuables (direction DEBIT|CREDIT, `balance_after`). Le code
tient des soldes : `SellerWallet`, `DriverWallet`, `PlatformWallet`, `WalletTransaction`,
`SellerEarning`, `Withdrawal`, `SettlementBatch`. Les deux modeles repondent aux memes
questions metier, mais seul le second permet de reconstruire un solde par rejeu et de
prouver l'equilibre. Passer de l'un a l'autre est une migration de donnees, pas un refactor.

---

## Ce qui manque vraiment (au-dela du renommage)

Entites de la spec sans aucun equivalent dans le code, verifiees une par une :

| Absent | Service concerne | Consequence |
|---|---|---|
| `Promotion`, `Coupon`, `PromotionRule`, `CouponUsage` | promotion-service | **Le service entier n'existe pas** (1 fichier squelette). Or les deux flux de checkout de la section 11 appellent `ReserveCoupon`/`CommitCoupon`. |
| `LedgerEntry` | wallet-service | Pas de partie double (cause racine 6). |
| `FoodCart` | food-cart-service | Le panier Food n'a pas d'agregat propre ; la section 11.2 en fait pourtant l'etape 2 du checkout. |
| `Reservation` | inventory-service | Les RPC `ReserveStock`/`ReleaseReservation` existent, mais sans agregat de reservation avec TTL. |
| `Proof`, `DriverShift` | delivery-service | Ni preuve de livraison ni gestion des vacations. |
| `MfaChallenge` | identity-service | Le endpoint `/auth/verify-otp` de la spec n'a pas d'agregat derriere. |
| `NotificationTemplate` | notification-service | Templates transactionnels versionnes absents. |
| `OpeningHour`, `ServiceZone` | restaurant-service | Horaires et zones de service, donc `IsRestaurantOpen` sans source de verite. |

A l'inverse, le code contient des domaines **entiers hors spec** qui ne doivent pas etre
perdus dans une mise en conformite : `Conversation`/`Message` (messagerie), `Review`,
`Recommendation`, `Wishlist`, `Invoice`, `CommissionRule`, `PricingRule`, `Batch`.

---

## Methode et limites

**Ce qui a ete verifie automatiquement** : presence d'un agregat par declaration
`class`/`record` et `DbSet<>` dans le dossier du service ; RPC par analyse des 13 `.proto` ;
evenements par nom de classe `*IntegrationEvent` et par chaine litterale ; routes par
extraction des `MapGroup`/`MapGet`/`MapPost`/`[Http*]` ; codes d'erreur par recherche
litterale.

**Ce qui n'a pas pu l'etre** : la solution **n'a pas ete compilee**. Ni la machine ni mon
conteneur n'ont acces au SDK .NET ou a NuGet. Aucune verification de comportement, de
schema de base reel ou de test n'entre dans ce rapport.

**Faux negatifs possibles** : un agregat porte par une classe imbriquee ou un owned type au
nom different n'est pas detecte ; une route composee via une variable plutot qu'une chaine
litterale echappe a l'extraction. Les colonnes « absent » sont donc un majorant. Les
colonnes « conforme » sont en revanche fiables : elles reposent sur une correspondance
exacte.

---

## Ce que je recommande d'attaquer, dans l'ordre

1. **Le socle transverse** (deja retenu) : enveloppe REST succes/erreur, les cinq codes
   d'erreur, `Idempotency-Key`, `consumer_inbox`, enveloppe Kafka section 19.1,
   propagation `requestId`/`traceId`/`correlationId`. Ces briques n'existent pas et tout
   le reste s'y adosse.
2. **Le nommage Kafka** (cause racine 2), parce qu'il est calcule dans `shared/` : une
   correction centrale reparerait les 90 lignes d'evenements d'un coup.
3. **Le prefixe `/api/v1/<domaine>`** (cause racine 1) : 61 endpoints, mecanique, sans risque metier.
4. **Trancher le vocabulaire** (cause racine 3) avant d'ecrire quoi que ce soit d'autre —
   c'est une decision, pas une tache.
5. **promotion-service**, seul service a ecrire integralement, et dependance des deux checkouts.
6. **Le ledger en partie double**, a planifier comme une migration a part entiere.

## Detail par service

### 1. Identity Service

`services/common/identity-service` · 143 fichiers C# · base attendue : identity_db + Redis

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 3/6 | 0/3 | 1/3 | 0/0 | 4/5 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `users_auth` | ❌ | — |
| table `roles` | ≈ | classe `Role` |
| table `permissions` | ≈ | classe `Permission` |
| table `user_roles` | ❌ | — |
| table `refresh_tokens` | ≈ | classe `RefreshToken` |
| table `mfa_challenges` | ❌ | — |
| rpc `ValidateAccessToken` | ❌ | — |
| rpc `GetUserRoles` | ❌ | — |
| rpc `RevokeUserSessions` | ❌ | — |
| publie `identity.user.registered` | ≈ | UserRegisteredIntegrationEvent |
| publie `identity.user.logged_in` | ❌ | — |
| publie `identity.token.revoked` | ❌ | — |
| `POST /api/v1/auth/register` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/auth/login` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/auth/refresh` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/auth/logout` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/auth/verify-otp` | ❌ | — |

Routes reellement declarees : `/`, `/api/identity/account`, `/api/identity/auth`, `/confirm-email`, `/email/resend`, `/email/verify`, `/login`, `/me`, `/me/accept-terms`, `/me/change-password`, `/me/logout`, `/me/mfa/confirm`, `/me/mfa/disable`, `/me/mfa/setup`, `/password/forgot`, `/password/reset`, `/refresh`, `/register` … (+6)

DbSet exposes : `Address`, `Role`, `User`

### 2. User Service

`services/common/user-service` · 31 fichiers C# · base attendue : user_db

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 0/4 | 1/3 | 0/3 | 1/1 | 4/4 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `users` | ❌ | — |
| table `addresses` | ❌ | — |
| table `preferences` | ❌ | — |
| table `devices` | ❌ | — |
| rpc `GetUser` | ✅ | present |
| rpc `GetAddress` | ❌ | — |
| rpc `ListUserAddresses` | ❌ | — |
| publie `user.profile.updated` | ❌ | — |
| publie `user.address.created` | ❌ | — |
| publie `user.device.registered` | ❌ | — |
| consomme `identity.user.registered` | ≈ | UserRegisteredIntegrationEvent |
| `GET /api/v1/users/me` | ≈ | meme segment final, prefixe different |
| `PATCH /api/v1/users/me` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/users/me/addresses` | ≈ | meme segment final, prefixe different |
| `GET /api/v1/users/me/addresses` | ≈ | meme segment final, prefixe different |

Routes reellement declarees : `/api/geo`, `/benin`, `/me`, `/me/addresses`, `/me/addresses/{id:guid}`, `/me/addresses/{id:guid}/default`, `/me/avatar`, `/me/profile`

DbSet exposes : `Address`, `UserProfile`

### 3. Merchant Service

`services/marketplace/seller-service` · 83 fichiers C# · base attendue : merchant_db + S3

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 0/4 | 0/3 | 0/4 | 1/1 | 1/4 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `merchants` | ❌ | — |
| table `outlets` | ❌ | — |
| table `merchant_members` | ❌ | — |
| table `kyc_documents` | ❌ | — |
| rpc `GetMerchant` | ❌ | — |
| rpc `GetOutlet` | ❌ | — |
| rpc `CheckMerchantCapability` | ❌ | — |
| publie `merchant.created` | ❌ | — |
| publie `merchant.kyc.submitted` | ❌ | — |
| publie `merchant.kyc.approved` | ❌ | — |
| publie `outlet.status.changed` | ❌ | — |
| consomme `identity.user.registered` | ≈ | UserRegisteredIntegrationEvent |
| `POST /api/v1/merchants` | ❌ | — |
| `POST /api/v1/merchants/{id}/outlets` | ❌ | — |
| `GET /api/v1/merchants/{id}` | ❌ | — |
| `POST /api/v1/merchants/{id}/kyc/submit` | ≈ | meme segment final, prefixe different |

Routes reellement declarees : `/`, `/me`, `/{sellerId:guid}`, `/{sellerId:guid}/activate`, `/{sellerId:guid}/close`, `/{sellerId:guid}/kyb/approve`, `/{sellerId:guid}/kyb/documents`, `/{sellerId:guid}/kyb/documents/{documentId:guid}`, `/{sellerId:guid}/kyb/reject`, `/{sellerId:guid}/lift-suspension`, `/{sellerId:guid}/metadata`, `/{sellerId:guid}/payout-account`, `/{sellerId:guid}/profile`, `/{sellerId:guid}/reactivation`, `/{sellerId:guid}/reactivation/approve`, `/{sellerId:guid}/suspend`, `/{storeId:guid}`, `/{storeId:guid}/close` … (+7)

DbSet exposes : `Seller`, `Store`

### 4. Catalog Service

`services/marketplace/catalog-service` · 153 fichiers C# · base attendue : catalog_db + Elasticsearch + S3

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 3/4 | 1/3 | 1/3 | 0/1 | 4/4 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `products` | ≈ | classe `Product` |
| table `variants` | ❌ | — |
| table `categories` | ≈ | classe `Category` |
| table `product_media` | ≈ | classe `ProductMedia` |
| rpc `GetProduct` | ✅ | present |
| rpc `GetVariant` | ❌ | — |
| rpc `SearchProducts` | ❌ | — |
| publie `catalog.product.created` | ≈ | ProductCreatedIntegrationEvent |
| publie `catalog.product.updated` | ❌ | — |
| publie `catalog.product.published` | ❌ | — |
| consomme `inventory.stock.changed` | ❌ | — |
| `POST /api/v1/marketplace/products` | ≈ | meme segment final, prefixe different |
| `GET /api/v1/marketplace/products/{id}` | ≈ | meme segment final, prefixe different |
| `GET /api/v1/marketplace/products` | ≈ | meme segment final, prefixe different |
| `PATCH /api/v1/marketplace/products/{id}` | ≈ | meme segment final, prefixe different |

Routes reellement declarees : `/api/catalog`, `/brands`, `/brands/{id:guid}`, `/brands/{id:guid}/publish`, `/brands/{id:guid}/unpublish`, `/categories`, `/categories/{id:guid}`, `/categories/{id:guid}/publish`, `/categories/{id:guid}/unpublish`, `/offers`, `/offers/{id:guid}`, `/offers/{id:guid}/activate`, `/offers/{id:guid}/handling-time`, `/offers/{id:guid}/pause`, `/offers/{id:guid}/price`, `/offers/{id:guid}/promotion`, `/products`, `/products/images/process` … (+12)

DbSet exposes : `Brand`, `Category`, `Product`, `ProductOffer`

### 5. Inventory Service

`services/marketplace/inventory-service` · 43 fichiers C# · base attendue : inventory_db + Redis

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 0/3 | 2/4 | 0/3 | 1/2 | 1/3 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `stocks` | ❌ | — |
| table `stock_movements` | ❌ | — |
| table `reservations` | ❌ | — |
| rpc `CheckAvailability` | ❌ | — |
| rpc `ReserveStock` | ✅ | present |
| rpc `CommitReservation` | ❌ | — |
| rpc `ReleaseReservation` | ✅ | present |
| publie `inventory.stock.changed` | ❌ | — |
| publie `inventory.reservation.expired` | ❌ | — |
| publie `inventory.out_of_stock` | ❌ | — |
| consomme `marketplace.order.cancelled` | ≈ | OrderCancelledIntegrationEvent |
| consomme `marketplace.order.paid` | ❌ | — |
| `GET /api/v1/inventory/variants/{variantId}` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/inventory/adjustments` | ❌ | — |
| `POST /api/v1/inventory/check` | ❌ | — |

Routes reellement declarees : `/availability/{sku}`, `/items`, `/items/by-locations`, `/items/sku/{sku}`, `/items/{id:guid}`, `/items/{id:guid}/adjust`, `/items/{id:guid}/receive`, `/items/{id:guid}/reorder-threshold`, `/locations`, `/locations/{id:guid}`, `/locations/{id:guid}/address`, `/low-stock`, `/owners/{ownerId:guid}/locations`, `/reservations`, `/reservations/confirm`, `/reservations/release`

DbSet exposes : `FulfillmentLocation`, `InventoryItem`

### 6. Marketplace Cart Service

`services/marketplace/cart-service` · 38 fichiers C# · base attendue : Redis + cart_db

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 2/2 | 1/3 | 0/2 | 0/2 | 3/4 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `carts` | ≈ | classe `Cart` |
| table `cart_items` | ≈ | classe `CartItem` |
| rpc `GetCart` | ✅ | present |
| rpc `ValidateCart` | ❌ | — |
| rpc `ClearCart` | ❌ | — |
| publie `marketplace.cart.updated` | ❌ | — |
| publie `marketplace.cart.checked_out` | ❌ | — |
| consomme `catalog.product.updated` | ❌ | — |
| consomme `inventory.stock.changed` | ❌ | — |
| `GET /api/v1/marketplace/cart` | ❌ | — |
| `POST /api/v1/marketplace/cart/items` | ≈ | meme segment final, prefixe different |
| `PATCH /api/v1/marketplace/cart/items/{itemId}` | ≈ | meme segment final, prefixe different |
| `DELETE /api/v1/marketplace/cart/items/{itemId}` | ≈ | meme segment final, prefixe different |

Routes reellement declarees : `/`, `/checkout`, `/coupon`, `/food-items`, `/items`, `/items/{offerId:guid}`, `/lines/{lineId:guid}`, `/{id:guid}`

DbSet exposes : `CartAggregate`

### 7. Marketplace Order Service

`services/marketplace/order-service` · 53 fichiers C# · base attendue : marketplace_order_db

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 1/4 | 1/4 | 2/5 | 1/3 | 4/4 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `orders` | ≈ | classe `Order` |
| table `order_sellers` | ❌ | — |
| table `order_items` | ❌ | — |
| table `order_status_history` | ❌ | — |
| rpc `GetMarketplaceOrder` | ❌ | — |
| rpc `UpdatePaymentStatus` | ❌ | — |
| rpc `MarkReadyForPickup` | ❌ | — |
| rpc `CancelOrder` | ✅ | present |
| publie `marketplace.order.created` | ❌ | — |
| publie `marketplace.order.paid` | ❌ | — |
| publie `marketplace.order.ready_for_pickup` | ❌ | — |
| publie `marketplace.order.cancelled` | ≈ | OrderCancelledIntegrationEvent |
| publie `marketplace.order.delivered` | ≈ | OrderDeliveredIntegrationEvent |
| consomme `payment.succeeded` | ❌ | — |
| consomme `payment.failed` | ≈ | PaymentFailedIntegrationEvent |
| consomme `delivery.delivered` | ❌ | — |
| `POST /api/v1/marketplace/orders/checkout` | ≈ | meme segment final, prefixe different |
| `GET /api/v1/marketplace/orders/{id}` | ≈ | meme segment final, prefixe different |
| `GET /api/v1/marketplace/orders` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/marketplace/orders/{id}/cancel` | ≈ | meme segment final, prefixe different |

Routes reellement declarees : `/`, `/{id:guid}`, `/{id:guid}/cancel`, `/{id:guid}/review/refund`, `/{id:guid}/review/resume`

DbSet exposes : `Order`

### 8. Restaurant Service

`services/food` · 68 fichiers C# · base attendue : restaurant_db + Redis

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 1/4 | 1/3 | 0/3 | 0/1 | 2/3 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `restaurants` | ≈ | classe `Restaurant` |
| table `opening_hours` | ❌ | — |
| table `service_zones` | ❌ | — |
| table `restaurant_runtime` | ❌ | — |
| rpc `GetRestaurant` | ✅ | present |
| rpc `IsRestaurantOpen` | ❌ | — |
| rpc `GetRestaurantRuntime` | ❌ | — |
| publie `restaurant.opened` | ❌ | — |
| publie `restaurant.closed` | ❌ | — |
| publie `restaurant.paused` | ❌ | — |
| consomme `merchant.kyc.approved` | ❌ | — |
| `GET /api/v1/food/restaurants` | ≈ | meme segment final, prefixe different |
| `GET /api/v1/food/restaurants/{id}` | ≈ | meme segment final, prefixe different |
| `PATCH /api/v1/food/restaurants/{id}/runtime` | ❌ | — |

Routes reellement declarees : `/api/food`, `/health/live`, `/health/ready`, `/me`, `/restaurants`, `/restaurants/pending`, `/restaurants/{id:guid}`, `/restaurants/{id:guid}/approve`, `/restaurants/{id:guid}/kitchen`, `/restaurants/{id:guid}/lift-suspension`, `/restaurants/{id:guid}/location`, `/restaurants/{id:guid}/logo`, `/restaurants/{id:guid}/menu`, `/restaurants/{id:guid}/orders`, `/restaurants/{id:guid}/pause`, `/restaurants/{id:guid}/payout-seller`, `/restaurants/{id:guid}/reject`, `/restaurants/{id:guid}/resume` … (+22)

DbSet exposes : `FoodOrder`, `Menu`, `MenuCategory`, `MenuItem`, `PreparationStation`, `Restaurant`, `RestaurantStaff`

### 9. Menu Service

`services/food` · 68 fichiers C# · base attendue : menu_db + Redis

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 4/5 | 1/3 | 0/3 | 0/0 | 3/3 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `menus` | ≈ | classe `Menu` |
| table `menu_categories` | ≈ | classe `MenuCategory` |
| table `menu_items` | ≈ | classe `MenuItem` |
| table `option_groups` | ≈ | classe `OptionGroup` |
| table `options` | ❌ | — |
| rpc `GetMenuItem` | ✅ | present |
| rpc `ValidateMenuSelection` | ❌ | — |
| rpc `GetRestaurantMenu` | ❌ | — |
| publie `menu.item.updated` | ❌ | — |
| publie `menu.item.unavailable` | ❌ | — |
| publie `menu.published` | ❌ | — |
| `GET /api/v1/food/restaurants/{id}/menu` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/food/menu/items` | ≈ | meme segment final, prefixe different |
| `PATCH /api/v1/food/menu/items/{id}/availability` | ≈ | meme segment final, prefixe different |

Routes reellement declarees : `/api/food`, `/health/live`, `/health/ready`, `/me`, `/restaurants`, `/restaurants/pending`, `/restaurants/{id:guid}`, `/restaurants/{id:guid}/approve`, `/restaurants/{id:guid}/kitchen`, `/restaurants/{id:guid}/lift-suspension`, `/restaurants/{id:guid}/location`, `/restaurants/{id:guid}/logo`, `/restaurants/{id:guid}/menu`, `/restaurants/{id:guid}/orders`, `/restaurants/{id:guid}/pause`, `/restaurants/{id:guid}/payout-seller`, `/restaurants/{id:guid}/reject`, `/restaurants/{id:guid}/resume` … (+22)

DbSet exposes : `FoodOrder`, `Menu`, `MenuCategory`, `MenuItem`, `PreparationStation`, `Restaurant`, `RestaurantStaff`

### 10. Food Cart Service

`services/food` · 68 fichiers C# · base attendue : Redis + food_cart_db

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 0/2 | 0/3 | 0/2 | 0/2 | 1/3 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `food_carts` | ❌ | — |
| table `food_cart_items` | ❌ | — |
| rpc `GetFoodCart` | ❌ | — |
| rpc `ValidateFoodCart` | ❌ | — |
| rpc `ClearFoodCart` | ❌ | — |
| publie `food.cart.updated` | ❌ | — |
| publie `food.cart.checked_out` | ❌ | — |
| consomme `menu.item.unavailable` | ❌ | — |
| consomme `restaurant.closed` | ❌ | — |
| `GET /api/v1/food/cart` | ❌ | — |
| `POST /api/v1/food/cart/items` | ≈ | meme segment final, prefixe different |
| `DELETE /api/v1/food/cart` | ❌ | — |

Routes reellement declarees : `/api/food`, `/health/live`, `/health/ready`, `/me`, `/restaurants`, `/restaurants/pending`, `/restaurants/{id:guid}`, `/restaurants/{id:guid}/approve`, `/restaurants/{id:guid}/kitchen`, `/restaurants/{id:guid}/lift-suspension`, `/restaurants/{id:guid}/location`, `/restaurants/{id:guid}/logo`, `/restaurants/{id:guid}/menu`, `/restaurants/{id:guid}/orders`, `/restaurants/{id:guid}/pause`, `/restaurants/{id:guid}/payout-seller`, `/restaurants/{id:guid}/reject`, `/restaurants/{id:guid}/resume` … (+22)

DbSet exposes : `FoodOrder`, `Menu`, `MenuCategory`, `MenuItem`, `PreparationStation`, `Restaurant`, `RestaurantStaff`

### 11. Food Order Service

`services/food` · 68 fichiers C# · base attendue : food_order_db

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 2/3 | 1/5 | 2/7 | 2/4 | 5/5 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `food_orders` | ≈ | classe `FoodOrder` |
| table `food_order_items` | ≈ | classe `FoodOrderItem` |
| table `food_status_history` | ❌ | — |
| rpc `GetFoodOrder` | ✅ | present |
| rpc `AcceptOrder` | ❌ | — |
| rpc `RejectOrder` | ❌ | — |
| rpc `MarkReady` | ❌ | — |
| rpc `UpdatePaymentStatus` | ❌ | — |
| publie `food.order.created` | ❌ | — |
| publie `food.order.paid` | ❌ | — |
| publie `food.order.accepted` | ❌ | — |
| publie `food.order.rejected` | ❌ | — |
| publie `food.order.ready` | ❌ | — |
| publie `food.order.cancelled` | ≈ | OrderCancelledIntegrationEvent |
| publie `food.order.delivered` | ≈ | OrderDeliveredIntegrationEvent |
| consomme `payment.succeeded` | ❌ | — |
| consomme `payment.failed` | ≈ | PaymentFailedIntegrationEvent |
| consomme `delivery.picked_up` | ✅ | chaine litterale |
| consomme `delivery.delivered` | ❌ | — |
| `POST /api/v1/food/orders/checkout` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/food/orders/{id}/accept` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/food/orders/{id}/reject` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/food/orders/{id}/ready` | ≈ | meme segment final, prefixe different |
| `GET /api/v1/food/orders/{id}` | ≈ | meme segment final, prefixe different |

Routes reellement declarees : `/api/food`, `/health/live`, `/health/ready`, `/me`, `/restaurants`, `/restaurants/pending`, `/restaurants/{id:guid}`, `/restaurants/{id:guid}/approve`, `/restaurants/{id:guid}/kitchen`, `/restaurants/{id:guid}/lift-suspension`, `/restaurants/{id:guid}/location`, `/restaurants/{id:guid}/logo`, `/restaurants/{id:guid}/menu`, `/restaurants/{id:guid}/orders`, `/restaurants/{id:guid}/pause`, `/restaurants/{id:guid}/payout-seller`, `/restaurants/{id:guid}/reject`, `/restaurants/{id:guid}/resume` … (+22)

DbSet exposes : `FoodOrder`, `Menu`, `MenuCategory`, `MenuItem`, `PreparationStation`, `Restaurant`, `RestaurantStaff`

### 12. Payment Service

`services/common/payment-service` · 72 fichiers C# · base attendue : payment_db + Kafka + Redis

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 0/3 | 1/3 | 2/4 | 0/2 | 0/4 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `payment_intents` | ❌ | — |
| table `payment_transactions` | ❌ | — |
| table `refunds` | ❌ | — |
| rpc `CreatePaymentIntent` | ❌ | — |
| rpc `GetPaymentStatus` | ❌ | — |
| rpc `RefundPayment` | ✅ | present |
| publie `payment.created` | ❌ | — |
| publie `payment.succeeded` | ❌ | — |
| publie `payment.failed` | ≈ | PaymentFailedIntegrationEvent |
| publie `payment.refunded` | ≈ | PaymentRefundedIntegrationEvent |
| consomme `marketplace.order.created` | ❌ | — |
| consomme `food.order.created` | ❌ | — |
| `POST /api/v1/payments/intents` | ❌ | — |
| `GET /api/v1/payments/intents/{id}` | ❌ | — |
| `POST /api/v1/payments/{id}/refunds` | ❌ | — |
| `POST /api/v1/payments/webhooks/{provider}` | ❌ | — |

Routes reellement declarees : `/`, `/by-order/{orderId:guid}`, `/compute`, `/drivers/{driverId:guid}`, `/drivers/{driverId:guid}/transactions`, `/platform`, `/platform/transactions`, `/seller/{sellerId:guid}`, `/sellers/{sellerId:guid}`, `/sellers/{sellerId:guid}/payouts`, `/sellers/{sellerId:guid}/statement`, `/sellers/{sellerId:guid}/statement/lines`, `/sellers/{sellerId:guid}/transactions`, `/sellers/{sellerId:guid}/withdrawals`, `/stats`, `/webhooks/{provider}`, `/withdrawals/pending`, `/withdrawals/processing` … (+15)

DbSet exposes : `Payment`, `SavedPaymentMethod`

### 13. Wallet & Settlement Service

`services/common/wallet-service` · 77 fichiers C# · base attendue : wallet_db (double-entry)

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 2/4 | 0/3 | 0/4 | 2/3 | 3/3 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `accounts` | ❌ | — |
| table `ledger_entries` | ❌ | — |
| table `wallet_transactions` | ≈ | classe `WalletTransaction` |
| table `payouts` | ≈ | classe `Payout` |
| rpc `GetBalance` | ❌ | — |
| rpc `CreditSale` | ❌ | — |
| rpc `CreatePayout` | ❌ | — |
| publie `wallet.credited` | ❌ | — |
| publie `wallet.debited` | ❌ | — |
| publie `payout.requested` | ❌ | — |
| publie `payout.completed` | ❌ | — |
| consomme `payment.succeeded` | ❌ | — |
| consomme `marketplace.order.delivered` | ≈ | OrderDeliveredIntegrationEvent |
| consomme `food.order.delivered` | ≈ | OrderDeliveredIntegrationEvent |
| `GET /api/v1/wallets/me` | ≈ | meme segment final, prefixe different |
| `GET /api/v1/wallets/me/transactions` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/wallets/me/payouts` | ≈ | meme segment final, prefixe different |

DbSet exposes : `CustomerRefund`, `DriverWallet`, `PlatformWallet`, `SellerEarning`, `SellerWallet`, `SettlementBatch`, `WalletTransaction`, `Withdrawal`

### 14. Delivery Service

`services/delivery` · 94 fichiers C# · base attendue : delivery_db + Redis GEO + TimescaleDB

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 2/5 | 3/5 | 3/7 | 0/3 | 4/6 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `deliveries` | ≈ | classe `Delivery` |
| table `drivers` | ≈ | classe `Driver` |
| table `driver_shifts` | ❌ | — |
| table `delivery_status_history` | ❌ | — |
| table `proofs` | ❌ | — |
| rpc `QuoteDelivery` | ❌ | — |
| rpc `CreateDelivery` | ✅ | present |
| rpc `GetDelivery` | ✅ | present |
| rpc `CancelDelivery` | ✅ | present |
| rpc `GetDriver` | ❌ | — |
| publie `delivery.created` | ✅ | chaine litterale |
| publie `delivery.assigned` | ≈ | DeliveryAssignedIntegrationEvent |
| publie `delivery.picked_up` | ✅ | chaine litterale |
| publie `delivery.on_the_way` | ❌ | — |
| publie `delivery.delivered` | ❌ | — |
| publie `delivery.failed` | ❌ | — |
| publie `driver.location.updated` | ❌ | — |
| consomme `food.order.accepted` | ❌ | — |
| consomme `food.order.ready` | ❌ | — |
| consomme `marketplace.order.ready_for_pickup` | ❌ | — |
| `POST /api/v1/deliveries/quote` | ≈ | meme segment final, prefixe different |
| `GET /api/v1/deliveries/{id}` | ❌ | — |
| `POST /api/v1/drivers/me/availability` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/deliveries/{id}/accept` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/deliveries/{id}/pickup` | ≈ | meme segment final, prefixe different |
| `POST /api/v1/deliveries/{id}/complete` | ❌ | — |

Routes reellement declarees : `/`, `/health/live`, `/health/ready`, `/me`, `/me/location`, `/me/missions`, `/me/missions/{deliveryId:guid}/accept`, `/me/missions/{deliveryId:guid}/arrived-dropoff`, `/me/missions/{deliveryId:guid}/arrived-pickup`, `/me/missions/{deliveryId:guid}/delivered`, `/me/missions/{deliveryId:guid}/in-transit`, `/me/missions/{deliveryId:guid}/picked-up`, `/me/missions/{deliveryId:guid}/reject`, `/me/offline`, `/me/online`, `/quote`, `/register`, `/{driverId:guid}/block` … (+5)

DbSet exposes : `DeliveryQuote`, `DeliveryZone`, `Driver`, `Partner`, `PricingRule`, `WebhookDelivery`

### 15. Notification Service

`services/common/notification-service` · 100 fichiers C# · base attendue : notification_db + providers

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 2/3 | 0/2 | 0/2 | 1/5 | 1/3 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `notification_templates` | ❌ | — |
| table `notifications` | ≈ | classe `Notification` |
| table `notification_preferences` | ≈ | classe `NotificationPreference` |
| rpc `SendNotification` | ❌ | — |
| rpc `GetPreferences` | ❌ | — |
| publie `notification.sent` | ❌ | — |
| publie `notification.failed` | ❌ | — |
| consomme `payment.succeeded` | ❌ | — |
| consomme `food.order.accepted` | ❌ | — |
| consomme `delivery.assigned` | ≈ | DeliveryAssignedIntegrationEvent |
| consomme `delivery.delivered` | ❌ | — |
| consomme `marketplace.order.paid` | ❌ | — |
| `GET /api/v1/notifications` | ❌ | — |
| `POST /api/v1/notifications/{id}/read` | ≈ | meme segment final, prefixe different |
| `PUT /api/v1/notifications/preferences` | ❌ | — |

Routes reellement declarees : `/`, `/conversations`, `/conversations/{id:guid}`, `/conversations/{id:guid}/archive`, `/conversations/{id:guid}/messages`, `/conversations/{id:guid}/messages/{messageId:guid}`, `/conversations/{id:guid}/messages/{messageId:guid}/mine`, `/conversations/{id:guid}/messages/{messageId:guid}/reaction`, `/conversations/{id:guid}/read`, `/read-all`, `/unread-count`, `/{id:guid}`, `/{id:guid}/read`

DbSet exposes : `Conversation`, `DeviceToken`, `Notification`, `NotificationPreference`

### 16. Promotion Service

`services/common/promotion-service` · 1 fichiers C# · base attendue : promotion_db + Redis

| Agregats | RPC gRPC | Ev. publies | Ev. consommes | REST | Codes d'erreur |
|---|---|---|---|---|---|
| 0/4 | 0/3 | 1/3 | 2/2 | 0/3 | 0/5 |

| Attendu par la spec | Etat | Ce qui existe dans le code |
|---|---|---|
| table `promotions` | ❌ | — |
| table `promotion_rules` | ❌ | — |
| table `coupons` | ❌ | — |
| table `coupon_usages` | ❌ | — |
| rpc `EvaluatePromotion` | ❌ | — |
| rpc `ReserveCoupon` | ❌ | — |
| rpc `CommitCoupon` | ❌ | — |
| publie `promotion.created` | ≈ | PromotionCreatedIntegrationEvent |
| publie `promotion.exhausted` | ❌ | — |
| publie `coupon.used` | ❌ | — |
| consomme `marketplace.order.cancelled` | ≈ | OrderCancelledIntegrationEvent |
| consomme `food.order.cancelled` | ≈ | OrderCancelledIntegrationEvent |
| `POST /api/v1/promotions/validate` | ❌ | — |
| `POST /api/v1/merchant/promotions` | ❌ | — |
| `GET /api/v1/merchant/promotions` | ❌ | — |

Routes reellement declarees : `/health/live`, `/health/ready`
