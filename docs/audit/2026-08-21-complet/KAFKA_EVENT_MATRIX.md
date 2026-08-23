# Matrice réelle des événements Kafka — HBAExpress

Audit statique de `/root/audit-src` (aucun compilateur .NET ; preuves = fichier:ligne).
Toutes les lignes sont vérifiées par lecture du code, pas par présence de fichier.

---

## 0. Résumé chiffré

| Mesure | Valeur |
|---|---|
| Classes dérivant d'`IntegrationEvent` | **136**, réparties sur **27** projets `*.Contracts` (services + `shared/contracts/`) |
| Déclarations en double (même nom, 2 espaces de noms) | 3 |
| Implémentations d'`IIntegrationEventHandler<T>` | **96** |
| Enregistrements DI `AddScoped<IIntegrationEventHandler<T>, …>` | **96** — appariement complet, **0 handler non enregistré** |
| Événements qui parcourent réellement producteur → Kafka → consumer (topologie `docker-compose.dev.yml`) | **20 sur 136** |
| Événements publiés que personne ne consomme | 51 |
| Consumers enregistrés dont l'événement n'est jamais publié | 4 |
| Événements dont producteur ET consumer existent mais dont le topic n'est écouté par personne | **37** |
| Événements « publiés » dans une file mémoire jamais drainée | 16 |
| Événements déclarés, ni publiés ni consommés | 9 |
| Services qui composent un `OutboxProcessor` | 23 modules |
| Services qui publient sans processeur d'outbox (perte totale) | **5** (dispatch, driver, route, tracking, proof-of-delivery) |
| Services ayant une table `consumer_inbox` | 7 |
| Handlers utilisant réellement `IConsumerInbox` | **6 sur 96** |

**Le défaut dominant n'est pas un handler oublié en DI — il n'y en a aucun.
C'est que les producteurs publient sur des topics auxquels aucun service ne s'abonne.**

---

## 1. Comment le bus fonctionne réellement (chaîne vérifiée)

1. `IIntegrationEventPublisher` est résolu vers `IntegrationEventQueue` — une simple
   `List<IntegrationEvent>` scopée
   (`shared/common/HBA.Shared.Infrastructure/DependencyInjection.cs:131-132`,
   `shared/common/HBA.Shared.Infrastructure/Outbox/IntegrationEventQueue.cs:15-31`).
2. `ModuleDbContext.SaveChangesAsync` draine la file vers `outbox_messages` **avant**
   `base.SaveChangesAsync` : l'écriture de l'événement et le changement métier sont
   dans la même transaction EF
   (`shared/common/HBA.Shared.Infrastructure/Persistence/ModuleDbContext.cs:78-98`, `:117-137`).
3. `OutboxProcessor<TDbContext>` (BackgroundService, poll 5 s, lot de 50) désérialise
   et appelle `IKafkaIntegrationEventPublisher`
   (`shared/common/HBA.Shared.Infrastructure/Outbox/OutboxProcessor.cs:59-203`).
4. `KafkaIntegrationEventPublisher` calcule
   `topic = {TopicPrefix}.{producteur sans « -service »}.{TopicVersion}`
   (`shared/common/HBA.Shared.Infrastructure/Kafka/KafkaEventNaming.cs:38-39`),
   `eventType` dérivé du **nom de classe .NET** (`KafkaEventNaming.cs:41-50`),
   `Key = aggregateId` (`KafkaIntegrationEventPublisher.cs:160-167`).
5. `KafkaIntegrationEventConsumer` s'abonne à `KafkaEventBusOptions.SubscribeTopics`
   (`KafkaIntegrationEventConsumer.cs:106`), résout le type par balayage d'assemblies,
   puis délègue à `IntegrationEventDispatcher`
   (`KafkaIntegrationEventConsumer.cs:304-408`).

### 1.1 CRITICAL — `SubscribeTopics` est une constante et ne correspond plus aux producteurs

`KafkaEventBusOptions.SubscribeTopics`
(`shared/common/HBA.Shared.Infrastructure/Kafka/KafkaEventBusOptions.cs:21-36`)
liste **treize** sujets en dur. `AddBuildingBlocksInfrastructure`
(`shared/common/HBA.Shared.Infrastructure/DependencyInjection.cs:61-70`) construit
`KafkaEventBusOptions` champ par champ et **ne lit jamais `SubscribeTopics` depuis la
configuration** : aucun déploiement ne peut le surcharger (vérifié : aucune occurrence
de `SUBSCRIBETOPICS` dans le dépôt).

Or `docker-compose.dev.yml` déclare **27** producteurs via `KAFKA__PRODUCER`. Le topic
produit vaut `service.{producteur sans « -service »}.v1`. Comparaison mécanique :

**20 producteurs publient dans un sujet que personne n'écoute** :

| Producteur | Topic produit | Écouté ? |
|---|---|---|
| seller-service | `service.seller.v1` | non (la liste dit `service.merchant.v1`) |
| payment-service (+ wallet, billing hébergés dedans) | `service.payment.v1` | non (la liste dit `service.financial.v1`) |
| restaurant-service | `service.restaurant.v1` | non (la liste dit `service.food.v1`) |
| cart-service | `service.cart.v1` | non (la liste dit `service.commerce.v1`) |
| review-service | `service.review.v1` | non (la liste dit `service.engagement.v1`) |
| notification-service | `service.notification.v1` | non (la liste dit `service.communication.v1`) |
| promotion-service | `service.promotion.v1` | non |
| return-refund-service | `service.return-refund.v1` | non |
| food-order-service | `service.food-order.v1` | non |
| food-cart-service | `service.food-cart.v1` | non |
| menu / availability / kitchen-prep / food-review | `service.menu.v1` … | non |
| delivery-pricing / dispatch / driver / tracking / route / proof-of-delivery | `service.delivery-pricing.v1` … | non |

Et **6 sujets sont écoutés par les 27 services sans qu'aucun producteur n'y écrive** :
`service.merchant.v1`, `service.commerce.v1`, `service.food.v1`, `service.financial.v1`,
`service.engagement.v1`, `service.communication.v1`.

Le dépôt contient déjà le contrôle qui le détecte — `check_producers()` dans
`scripts/kafka-topics.sh:127-144` — mais il n'émet qu'un avertissement sur stderr et
ne fait pas échouer le script.

**Conséquence directe, tous silencieux** (le consumer ne voit rien, le producteur réussit) :
`payment.captured` n'atteint jamais `ConfirmOrderOnPaymentCapturedHandler` ni
`ConfirmMealOrderOnPaymentCapturedHandler` → **aucune commande n'est jamais confirmée après
paiement** ; `seller.registered` n'atteint jamais `GrantSellerRoleHandler` → **un vendeur
inscrit reste `Buyer`** ; **aucune** notification ne part (les 46 handlers de
notification-service écoutent des sujets vides) ; `driver.earning.credited`,
`payout.paid`, `seller.rating.recomputed`, tout le pont restauration → morts.

> **Nuance à confirmer** : la topologie Kubernetes (`k8s/base/services/*/`) utilise les
> anciens noms à 14 services — `merchant-service`, `commerce-service`, `food-service`,
> `financial-service`, `engagement-service`, `communication-service` — qui, eux,
> **correspondent** à `SubscribeTopics`. En k8s seul `promotion-service`
> (`service.promotion.v1`) reste orphelin, mais 13 des services du dépôt n'y sont pas
> déployés du tout. Les deux topologies sont incompatibles ; celle de `docker-compose.dev.yml`,
> la seule complète, est cassée.

### 1.2 CRITICAL — Cinq services publient dans une file mémoire que rien ne draine

`dispatch-service`, `driver-service`, `route-service`, `tracking-service` et
`proof-of-delivery-service` enregistrent
`IIntegrationEventPublisher → IntegrationEventQueue` sans aucun `ModuleDbContext`
ni `AddOutboxProcessor` :

- `services/delivery/dispatch-service/src/HBA.Delivery.Dispatch.Infrastructure/Persistence/DispatchInfrastructureModule.cs:13-14`
- `services/delivery/driver-service/src/HBA.Delivery.Driver.Infrastructure/Persistence/DriversInfrastructureModule.cs:13-14`
- `services/delivery/route-service/src/HBA.Delivery.Route.Infrastructure/Persistence/RoutesInfrastructureModule.cs:13-14`
- `services/delivery/tracking-service/src/HBA.Delivery.Tracking.Infrastructure/Persistence/TrackingInfrastructureModule.cs:13-14`
- `services/delivery/proof-of-delivery-service/src/HBA.Delivery.Proof.Infrastructure/Persistence/ProofOfDeliveryInfrastructureModule.cs:13-14`

`IntegrationEventQueue.DequeueAll()` n'a **qu'un seul appelant dans tout le dépôt** :
`ModuleDbContext.DrainIntegrationEventsToOutbox` (`ModuleDbContext.cs:119`). Ces cinq
services n'ont pas de `ModuleDbContext` (leurs magasins sont des
`ConcurrentDictionary` singletons, ex.
`services/delivery/dispatch-service/src/HBA.Delivery.Dispatch.Application/Abstractions/DispatchStore.cs:7-11`).
Leurs `Program.cs` (23 lignes chacun, ex.
`services/delivery/tracking-service/src/HBA.Delivery.Tracking.Api/Program.cs:1-23`)
n'appellent ni `AddHbaService` ni `AddBuildingBlocksInfrastructure` : **ni producteur ni
consumer Kafka n'existe dans ces processus**.

**16 événements sont donc écrits dans une `List<>` puis ramassés par le GC** :
`DispatchStarted`, `DispatchOfferCreated`, `DeliveryAssigned` (variante Dispatch),
`DriverAvailabilityChanged`, `DriverVehicleUpdated`, `RouteCalculated`,
`RouteRecalculated`, `RouteDeliveryEtaUpdated`, `TrackingSessionStarted`,
`TrackingLocationSampled`, `TrackingSessionEnded`, `DeliveryEtaUpdated`,
`ProofSubmitted`, `ProofVerified`, `ProofRejected`, `DeliveryProofCompleted`.

---

## 2. Matrice des événements

Colonne « Utilisé ? » :
`oui` = le flux complet est branché ·
`topic non écouté` = producteur + consumer + DI corrects, mais §1.1 tue le message ·
`jeté (file mémoire)` = §1.2 ·
`publié, aucun consumer` = événement mort ·
`consumer orphelin` = personne ne le publie ·
`mort-né` = ni producteur ni consumer.

Chemins raccourcis : `services/` et `/src/` retirés.

| Événement | Producteur (fichier:ligne) | Consumers (fichier:ligne) | Enregistré en DI ? | Topic | Clé de partition | Version | Utilisé ? |
|---|---|---|---|---|---|---|---|
| `BrandCreated` | `marketplace/catalog-service/HBA.Catalog.Application/Brands/EventHandlers/BrandCreatedDomainEventHandler.cs:17` | **aucun** | — | service.catalog.v1 | Id (aléatoire) | — | publié, aucun consumer |
| `BrandRequestApproved` | `marketplace/catalog-service/HBA.Catalog.Application/Brands/EventHandlers/BrandRequestDomainEventHandlers.cs:42` | **aucun** | — | service.catalog.v1 | SellerId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `BrandRequested` | `marketplace/catalog-service/HBA.Catalog.Application/Brands/EventHandlers/BrandRequestDomainEventHandlers.cs:26` | **aucun** | — | service.catalog.v1 | SellerId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `CartCheckedOut` | `marketplace/cart-service/HBA.Commerce.Application/Carts/EventHandlers/CartCheckedOutDomainEventHandler.cs:17` | **aucun** | — | service.cart.v1 | CartId | — | publié, aucun consumer |
| `CategoryCreated` | `marketplace/catalog-service/HBA.Catalog.Application/Categories/EventHandlers/CategoryCreatedDomainEventHandler.cs:17` | **aucun** | — | service.catalog.v1 | Id (aléatoire) | — | publié, aucun consumer |
| `CouponUsed` | `common/promotion-service/HBA.Promotions.Application/Promotions/PromotionDomainEventHandlers.cs:92` | **aucun** | — | service.promotion.v1 | OrderId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `DeliveryAccepted` | `delivery/delivery-service/HBA.Delivery.Core.Application/EventHandlers/DeliveryDomainEventHandlers.cs:128` | delivery-service · `delivery/delivery-service/HBA.Delivery.Core.Application/Webhooks/EnqueueWebhookOnDeliveryEvents.cs:136`<br>notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/DeliveryTrackingNotificationHandlers.cs:72` | oui (2) | service.delivery.v1 | DeliveryId | — | oui |
| `DeliveryAssigned` doublon | `delivery/delivery-service/HBA.Delivery.Core.Application/EventHandlers/DeliveryDomainEventHandlers.cs:89`<br>`delivery/dispatch-service/HBA.Delivery.Dispatch.Application/Abstractions/DispatchStore.cs:106` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/DriverProposalNotificationHandler.cs:40` | oui (1) | service.delivery.v1, service.dispatch.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `DeliveryCancelled` | `delivery/delivery-service/HBA.Delivery.Core.Application/EventHandlers/DeliveryDomainEventHandlers.cs:191` | order-service · `marketplace/order-service/HBA.Order.Api/Integration/OrderDeliveryArbitrationHandlers.cs:96`<br>delivery-service · `delivery/delivery-service/HBA.Delivery.Core.Application/Webhooks/EnqueueWebhookOnDeliveryEvents.cs:166` | oui (2) | service.delivery.v1 | DeliveryId | — | oui |
| `DeliveryCompleted` | `delivery/delivery-service/HBA.Delivery.Core.Application/EventHandlers/DeliveryDomainEventHandlers.cs:169` | order-service · `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/MarkOrderDeliveredOnDeliveryCompletedHandler.cs:104`<br>delivery-service · `delivery/delivery-service/HBA.Delivery.Core.Application/Webhooks/EnqueueWebhookOnDeliveryEvents.cs:156`<br>restaurant-service · `food/restaurant-service/HBA.Food.Restaurant.Api/Integration/FoodDeliveryReturnHandlers.cs:125`<br>wallet-service · `common/wallet-service/HBA.Financial.Wallet.Application/Earnings/CreditDriverOnDeliveryCompletedHandler.cs:51` | oui (4) | service.delivery.v1 | DeliveryId | — | oui |
| `DeliveryCreated` | `delivery/delivery-service/HBA.Delivery.Core.Application/EventHandlers/DeliveryDomainEventHandlers.cs:54` | delivery-service · `delivery/delivery-service/HBA.Delivery.Core.Application/Webhooks/EnqueueWebhookOnDeliveryEvents.cs:126` | oui (1) | service.delivery.v1 | DeliveryId | — | oui |
| `DeliveryEtaUpdated` | `delivery/tracking-service/HBA.Delivery.Tracking.Application/Abstractions/TrackingStore.cs:107` | **aucun** | — | service.tracking.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `DeliveryNoDriverAvailable` | `delivery/delivery-service/HBA.Delivery.Core.Application/EventHandlers/DeliveryDomainEventHandlers.cs:213` | delivery-service · `delivery/delivery-service/HBA.Delivery.Core.Application/Webhooks/EnqueueWebhookOnDeliveryEvents.cs:176`<br>notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/DeliveryTrackingNotificationHandlers.cs:144` | oui (2) | service.delivery.v1 | DeliveryId | — | oui |
| `DeliveryPickedUp` | `delivery/delivery-service/HBA.Delivery.Core.Application/EventHandlers/DeliveryDomainEventHandlers.cs:147` | delivery-service · `delivery/delivery-service/HBA.Delivery.Core.Application/Webhooks/EnqueueWebhookOnDeliveryEvents.cs:146`<br>restaurant-service · `food/restaurant-service/HBA.Food.Restaurant.Api/Integration/FoodDeliveryReturnHandlers.cs:65`<br>notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/DeliveryTrackingNotificationHandlers.cs:102` | oui (3) | service.delivery.v1 | DeliveryId | — | oui |
| `DeliveryPricingRuleActivated` | `delivery/delivery-pricing-service/HBA.Delivery.Pricing.Infrastructure/Persistence/EfDeliveryPricingStore.cs:206` | **aucun** | — | service.delivery-pricing.v1 | Id (aléatoire) | v1 (attribut, non utilisé) | publié, aucun consumer |
| `DeliveryPricingRuleCreated` | `delivery/delivery-pricing-service/HBA.Delivery.Pricing.Infrastructure/Persistence/EfDeliveryPricingStore.cs:137` | **aucun** | — | service.delivery-pricing.v1 | Id (aléatoire) | v1 (attribut, non utilisé) | publié, aucun consumer |
| `DeliveryPricingRuleDeactivated` | `delivery/delivery-pricing-service/HBA.Delivery.Pricing.Infrastructure/Persistence/EfDeliveryPricingStore.cs:207` | **aucun** | — | service.delivery-pricing.v1 | Id (aléatoire) | v1 (attribut, non utilisé) | publié, aucun consumer |
| `DeliveryPricingRuleUpdated` | `delivery/delivery-pricing-service/HBA.Delivery.Pricing.Infrastructure/Persistence/EfDeliveryPricingStore.cs:179` | **aucun** | — | service.delivery-pricing.v1 | Id (aléatoire) | v1 (attribut, non utilisé) | publié, aucun consumer |
| `DeliveryProofCompleted` | `delivery/proof-of-delivery-service/HBA.Delivery.Proof.Application/Abstractions/ProofStore.cs:88` | **aucun** | — | service.proof-of-delivery.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `DeliveryQuoteConsumed` | `delivery/delivery-pricing-service/HBA.Delivery.Pricing.Infrastructure/Persistence/EfDeliveryPricingStore.cs:113` | **aucun** | — | service.delivery-pricing.v1 | DeliveryId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `DeliveryQuoteCreated` | `delivery/delivery-pricing-service/HBA.Delivery.Pricing.Infrastructure/Persistence/EfDeliveryPricingStore.cs:60` | **aucun** | — | service.delivery-pricing.v1 | Id (aléatoire) | v1 (attribut, non utilisé) | publié, aucun consumer |
| `DeliveryQuoteExpired` | **aucun** | **aucun** | — | — | Id (aléatoire) | v1 (attribut, non utilisé) | mort-né |
| `DispatchNoDriverFound` | **aucun** | **aucun** | — | — | DeliveryId | v1 (attribut, non utilisé) | mort-né |
| `DispatchOfferCreated` | `delivery/dispatch-service/HBA.Delivery.Dispatch.Application/Abstractions/DispatchStore.cs:43`<br>`delivery/dispatch-service/HBA.Delivery.Dispatch.Application/Abstractions/DispatchStore.cs:81` | **aucun** | — | service.dispatch.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `DispatchOfferExpired` | **aucun** | **aucun** | — | — | DeliveryId | v1 (attribut, non utilisé) | mort-né |
| `DispatchStarted` | `delivery/dispatch-service/HBA.Delivery.Dispatch.Application/Abstractions/DispatchStore.cs:35`<br>`delivery/dispatch-service/HBA.Delivery.Dispatch.Application/Abstractions/DispatchStore.cs:73` | **aucun** | — | service.dispatch.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `DriverAvailabilityChanged` | `delivery/driver-service/HBA.Delivery.Driver.Application/Abstractions/DriverStore.cs:98`<br>`delivery/driver-service/HBA.Delivery.Driver.Application/Abstractions/DriverStore.cs:125` | **aucun** | — | service.driver.v1 | Id (aléatoire) | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `DriverCreated` | **aucun** | **aucun** | — | — | UserId | v1 (attribut, non utilisé) | mort-né |
| `DriverEarningCredited` | `common/wallet-service/HBA.Financial.Wallet.Application/Wallets/CreditDriverEarningCommand.cs:216` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/DriverEarningNotificationHandler.cs:41` | oui (1) | service.payment.v1 | DeliveryId | v1 (attribut, non utilisé) | **topic non écouté** |
| `DriverSuspended` | **aucun** | **aucun** | — | — | Id (aléatoire) | v1 (attribut, non utilisé) | mort-né |
| `DriverVehicleUpdated` | `delivery/driver-service/HBA.Delivery.Driver.Application/Abstractions/DriverStore.cs:78` | **aucun** | — | service.driver.v1 | Id (aléatoire) | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `DriverVerified` doublon | `delivery/delivery-service/HBA.Delivery.Core.Application/EventHandlers/DeliveryDomainEventHandlers.cs:111` | identity-service · `common/identity-service/HBA.Identity.Application/Users/EventHandlers/BusinessRoleGrantHandlers.cs:256` | oui (1) | service.delivery.v1 | UserId | v1 (attribut, non utilisé) | oui |
| `EmailVerificationRequested` | `common/identity-service/HBA.Identity.Application/Users/Commands/RequestEmailVerification/RequestEmailVerificationByEmailCommand.cs:111`<br>`common/identity-service/HBA.Identity.Application/Users/Commands/RequestEmailVerification/RequestEmailVerificationCommandHandler.cs:55`<br>`common/identity-service/HBA.Identity.Application/Users/Commands/RegisterUser/RegisterUserCommandHandler.cs:139` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/AccountEmailHandlers.cs:25` | oui (1) | service.identity.v1 | UserId | — | oui |
| `FoodCartCheckedOut` | `food/food-cart-service/HBA.Food.Cart.Application/Abstractions/EventHandlers/FoodCartEventHandlers.cs:73` | **aucun** | — | service.food-cart.v1 | CartId | — | publié, aucun consumer |
| `FoodOrderAccepted` | `food/restaurant-service/HBA.Food.Restaurant.Application/Orders/FoodOrderDomainEventHandlers.cs:60` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/FoodOrderNotificationHandlers.cs:75` | oui (1) | service.restaurant.v1 | OrderId | — | **topic non écouté** |
| `FoodOrderCancelled` | `food/restaurant-service/HBA.Food.Restaurant.Application/Orders/FoodOrderDomainEventHandlers.cs:197` | order-service · `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/CancelOrderOnFoodOrderRefusedHandlers.cs:73`<br>food-order-service · `food/food-order-service/HBA.Food.Order.Application/Abstractions/EventHandlers/KitchenOutcomeHandlers.cs:73`<br>promotion-service · `common/promotion-service/HBA.Promotions.Api/Integration/ReleaseCouponsOnOrderCancelledHandlers.cs:94` | oui (3) | service.restaurant.v1 | OrderId | — | **topic non écouté** |
| `FoodOrderDelivered` | `food/restaurant-service/HBA.Food.Restaurant.Application/Orders/FoodOrderDomainEventHandlers.cs:180` | order-service · `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/MarkOrderDeliveredOnFoodOrderDeliveredHandler.cs:56`<br>food-order-service · `food/food-order-service/HBA.Food.Order.Application/Abstractions/EventHandlers/KitchenOutcomeHandlers.cs:113` | oui (2) | service.restaurant.v1 | OrderId | — | **topic non écouté** |
| `FoodOrderPickedUp` | `food/restaurant-service/HBA.Food.Restaurant.Application/Orders/FoodOrderDomainEventHandlers.cs:154` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/FoodOrderNotificationHandlers.cs:159` | oui (1) | service.restaurant.v1 | OrderId | — | **topic non écouté** |
| `FoodOrderPreparing` | `food/restaurant-service/HBA.Food.Restaurant.Application/Orders/FoodOrderDomainEventHandlers.cs:108` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/FoodOrderNotificationHandlers.cs:105` | oui (1) | service.restaurant.v1 | OrderId | — | **topic non écouté** |
| `FoodOrderReadyForPickup` | `food/restaurant-service/HBA.Food.Restaurant.Application/Orders/FoodOrderDomainEventHandlers.cs:136` | restaurant-service · `food/restaurant-service/HBA.Food.Restaurant.Api/Integration/FoodOrderBridgeHandlers.cs:215`<br>notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/FoodOrderNotificationHandlers.cs:132` | oui (2) | service.restaurant.v1 | OrderId | — | **topic non écouté** |
| `FoodOrderReceived` | `food/restaurant-service/HBA.Food.Restaurant.Application/Orders/FoodOrderDomainEventHandlers.cs:41` | **aucun** | — | service.restaurant.v1 | OrderId | — | publié, aucun consumer |
| `FoodOrderRejected` | `food/restaurant-service/HBA.Food.Restaurant.Application/Orders/FoodOrderDomainEventHandlers.cs:86` | order-service · `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/CancelOrderOnFoodOrderRefusedHandlers.cs:40`<br>food-order-service · `food/food-order-service/HBA.Food.Order.Application/Abstractions/EventHandlers/KitchenOutcomeHandlers.cs:31` | oui (2) | service.restaurant.v1 | OrderId | — | **topic non écouté** |
| `KybDocumentRemoved` | `marketplace/seller-service/HBA.Merchants.Application/Sellers/EventHandlers/SellerDomainEventHandlers.cs:173` | media-service · `common/media-service/HBA.Media.Application/Assets/EventHandlers/DeleteMediaOnKybDocumentRemovedHandler.cs:42` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `MealOrderCancelled` | `food/food-order-service/HBA.Food.Order.Application/Abstractions/EventHandlers/MealOrderDomainEventHandlers.cs:109` | **aucun** | — | service.food-order.v1 | OrderId | — | publié, aucun consumer |
| `MealOrderConfirmed` | `food/food-order-service/HBA.Food.Order.Application/Abstractions/EventHandlers/MealOrderDomainEventHandlers.cs:67` | **aucun** | — | service.food-order.v1 | OrderId | — | publié, aucun consumer |
| `MealOrderDelivered` | `food/food-order-service/HBA.Food.Order.Application/Abstractions/EventHandlers/MealOrderDomainEventHandlers.cs:182` | **aucun** | — | service.food-order.v1 | OrderId | — | publié, aucun consumer |
| `MealOrderPlaced` | `food/food-order-service/HBA.Food.Order.Application/Abstractions/EventHandlers/MealOrderDomainEventHandlers.cs:21` | food-cart-service · `food/food-cart-service/HBA.Food.Cart.Application/Abstractions/EventHandlers/FoodCartEventHandlers.cs:31` | oui (1) | service.food-order.v1 | OrderId | — | **topic non écouté** |
| `MealOrderResumedAfterReview` | `food/food-order-service/HBA.Food.Order.Application/Abstractions/EventHandlers/MealOrderDomainEventHandlers.cs:162` | **aucun** | — | service.food-order.v1 | OrderId | — | publié, aucun consumer |
| `MealOrderUnderReview` | `food/food-order-service/HBA.Food.Order.Application/Abstractions/EventHandlers/MealOrderDomainEventHandlers.cs:140` | **aucun** | — | service.food-order.v1 | OrderId | — | publié, aucun consumer |
| `MediaDeleted` | `common/media-service/HBA.Media.Application/Assets/EventHandlers/MediaDomainEventHandlers.cs:55` | **aucun** | — | service.media.v1 | MediaId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `MediaProcessingFailed` | `common/media-service/HBA.Media.Application/Assets/EventHandlers/MediaDomainEventHandlers.cs:83` | **aucun** | — | service.media.v1 | MediaId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `MediaReady` | `common/media-service/HBA.Media.Application/Assets/EventHandlers/MediaDomainEventHandlers.cs:33` | **aucun** | — | service.media.v1 | MediaId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `MessageSent` | `common/notification-service/HBA.Communication.Application/Conversations/EventHandlers/MessageSentDomainEventHandler.cs:17` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/MessageNotificationHandler.cs:24` | oui (1) | service.notification.v1 | MessageId | — | **topic non écouté** |
| `NotificationFailed` | **aucun** | **aucun** | — | — | Id (aléatoire) | v1 (attribut, non utilisé) | mort-né |
| `NotificationSent` | **aucun** | **aucun** | — | — | Id (aléatoire) | v1 (attribut, non utilisé) | mort-né |
| `OrderCancelled` | `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/OrderDomainEventHandlers.cs:70` | order-service · `marketplace/order-service/HBA.Order.Api/Integration/OrderDeliveryArbitrationHandlers.cs:248`<br>notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/OrderNotificationHandlers.cs:35`<br>promotion-service · `common/promotion-service/HBA.Promotions.Api/Integration/ReleaseCouponsOnOrderCancelledHandlers.cs:34`<br>wallet-service · `common/wallet-service/HBA.Financial.Wallet.Application/Earnings/ReverseEarningsOnOrderCancelledHandler.cs:39`<br>payment-service · `common/payment-service/HBA.Financial.Payments.Application/Payments/EventHandlers/RefundPaymentOnOrderCancelledHandler.cs:52` | oui (5) | service.order.v1 | OrderId | — | oui |
| `OrderConfirmed` | `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/OrderDomainEventHandlers.cs:37` | seller-service · `marketplace/seller-service/HBA.Merchants.Infrastructure/Integration/SellerSalesCountHandler.cs:48`<br>order-service · `marketplace/order-service/HBA.Order.Api/Integration/CreateDeliveryOnOrderConfirmedHandler.cs:64`<br>restaurant-service · `food/restaurant-service/HBA.Food.Restaurant.Api/Integration/FoodOrderBridgeHandlers.cs:79`<br>notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerOrderNotificationHandler.cs:36`<br>notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/OrderNotificationHandlers.cs:21`<br>wallet-service · `common/wallet-service/HBA.Financial.Wallet.Application/Earnings/AccrueEarningsOnOrderConfirmedHandler.cs:36` | oui (6) | service.order.v1 | OrderId | — | oui |
| `OrderDelivered` | `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/OrderDomainEventHandlers.cs:131` | wallet-service · `common/wallet-service/HBA.Financial.Wallet.Application/Earnings/ReleaseEarningsOnOrderDeliveredHandler.cs:16`<br>payment-service · `common/payment-service/HBA.Financial.Payments.Application/Payments/EventHandlers/ReleaseEscrowOnOrderDeliveredHandler.cs:13` | oui (2) | service.order.v1 | OrderId | — | oui |
| `OrderPlaced` | `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/OrderDomainEventHandlers.cs:17` | cart-service · `marketplace/cart-service/HBA.Commerce.Application/Carts/EventHandlers/CloseCartOnOrderPlacedHandler.cs:14`<br>notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/OrderNotificationHandlers.cs:7` | oui (2) | service.order.v1 | OrderId | — | oui |
| `OrderResumedAfterReview` | `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/OrderDomainEventHandlers.cs:114` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/OrderNotificationHandlers.cs:109` | oui (1) | service.order.v1 | OrderId | — | oui |
| `OrderUnderReview` | `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/OrderDomainEventHandlers.cs:93` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/OrderNotificationHandlers.cs:77` | oui (1) | service.order.v1 | OrderId | — | oui |
| `PasswordResetRequested` | `common/identity-service/HBA.Identity.Application/Users/Commands/PasswordReset/RequestPasswordResetCommandHandler.cs:72` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/AccountEmailHandlers.cs:72` | oui (1) | service.identity.v1 | UserId | — | oui |
| `PaymentCaptured` | `common/payment-service/HBA.Financial.Payments.Application/Payments/EventHandlers/PaymentDomainEventHandlers.cs:30` | order-service · `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/PaymentOutcomeHandlers.cs:35`<br>food-order-service · `food/food-order-service/HBA.Food.Order.Application/Abstractions/EventHandlers/PaymentOutcomeHandlers.cs:38` | oui (2) | service.payment.v1 | OrderId | v1 (attribut, non utilisé) | **topic non écouté** |
| `PaymentCreated` | `common/payment-service/HBA.Financial.Payments.Application/Payments/EventHandlers/PaymentDomainEventHandlers.cs:147` | **aucun** | — | service.payment.v1 | OrderId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `PaymentFailed` | `common/payment-service/HBA.Financial.Payments.Application/Payments/EventHandlers/PaymentDomainEventHandlers.cs:57` | order-service · `marketplace/order-service/HBA.Order.Application/Orders/EventHandlers/PaymentOutcomeHandlers.cs:71`<br>food-order-service · `food/food-order-service/HBA.Food.Order.Application/Abstractions/EventHandlers/PaymentOutcomeHandlers.cs:83`<br>notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerLifecycleNotificationHandlers.cs:319` | oui (3) | service.payment.v1 | OrderId | v1 (attribut, non utilisé) | **topic non écouté** |
| `PaymentRefundFailed` | `common/payment-service/HBA.Financial.Payments.Application/Payments/EventHandlers/PaymentDomainEventHandlers.cs:111` | **aucun** | — | service.payment.v1 | OrderId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `PaymentRefunded` | `common/payment-service/HBA.Financial.Payments.Application/Payments/EventHandlers/PaymentDomainEventHandlers.cs:85` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/RefundNotificationHandlers.cs:134` | oui (1) | service.payment.v1 | OrderId | v1 (attribut, non utilisé) | **topic non écouté** |
| `PayoutPaid` | `common/wallet-service/HBA.Financial.Wallet.Application/Batches/EventHandlers/PayoutPaidDomainEventHandler.cs:17` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/PayoutNotificationHandler.cs:19` | oui (1) | service.payment.v1 | SellerId | v1 (attribut, non utilisé) | **topic non écouté** |
| `ProductApproved` | `marketplace/catalog-service/HBA.Catalog.Application/Products/EventHandlers/ProductLifecycleDomainEventHandlers.cs:56` | **aucun** | — | service.catalog.v1 | ProductId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `ProductArchived` | `marketplace/catalog-service/HBA.Catalog.Application/Products/EventHandlers/ProductLifecycleDomainEventHandlers.cs:159` | **aucun** | — | service.catalog.v1 | ProductId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `ProductCreated` | `marketplace/catalog-service/HBA.Catalog.Application/Products/EventHandlers/ProductCreatedDomainEventHandler.cs:22` | **aucun** | — | service.catalog.v1 | ProductId | — | publié, aucun consumer |
| `ProductDeleted` | `marketplace/catalog-service/HBA.Catalog.Application/Products/Commands/DeleteProduct/DeleteProductCommandHandler.cs:47` | **aucun** | — | service.catalog.v1 | ProductId | — | publié, aucun consumer |
| `ProductMediaRemoved` | `marketplace/catalog-service/HBA.Catalog.Application/Products/EventHandlers/ProductMediaRemovedDomainEventHandler.cs:22` | **aucun** | — | service.catalog.v1 | ProductId | — | publié, aucun consumer |
| `ProductPublished` | `marketplace/catalog-service/HBA.Catalog.Application/Products/EventHandlers/ProductLifecycleDomainEventHandlers.cs:92` | **aucun** | — | service.catalog.v1 | ProductId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `ProductRejected` | `marketplace/catalog-service/HBA.Catalog.Application/Products/EventHandlers/ProductLifecycleDomainEventHandlers.cs:74` | **aucun** | — | service.catalog.v1 | ProductId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `ProductRestored` | `marketplace/catalog-service/HBA.Catalog.Application/Products/EventHandlers/ProductLifecycleDomainEventHandlers.cs:143` | **aucun** | — | service.catalog.v1 | ProductId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `ProductSubmitted` | `marketplace/catalog-service/HBA.Catalog.Application/Products/EventHandlers/ProductLifecycleDomainEventHandlers.cs:38` | **aucun** | — | service.catalog.v1 | ProductId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `ProductSuspended` | `marketplace/catalog-service/HBA.Catalog.Application/Products/EventHandlers/ProductLifecycleDomainEventHandlers.cs:126` | **aucun** | — | service.catalog.v1 | ProductId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `ProductUnpublished` | `marketplace/catalog-service/HBA.Catalog.Application/Products/EventHandlers/ProductLifecycleDomainEventHandlers.cs:110` | **aucun** | — | service.catalog.v1 | ProductId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `PromotionCreated` doublon | `common/promotion-service/HBA.Promotions.Application/Promotions/PromotionDomainEventHandlers.cs:36` | **aucun** | — | service.promotion.v1 | Id (aléatoire) | v1 (attribut, non utilisé) | publié, aucun consumer |
| `PromotionExhausted` | `common/promotion-service/HBA.Promotions.Application/Promotions/PromotionDomainEventHandlers.cs:72` | **aucun** | — | service.promotion.v1 | Id (aléatoire) | v1 (attribut, non utilisé) | publié, aucun consumer |
| `ProofRejected` | `delivery/proof-of-delivery-service/HBA.Delivery.Proof.Application/Abstractions/ProofStore.cs:97` | **aucun** | — | service.proof-of-delivery.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `ProofSubmitted` | `delivery/proof-of-delivery-service/HBA.Delivery.Proof.Application/Abstractions/ProofStore.cs:72` | **aucun** | — | service.proof-of-delivery.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `ProofVerified` | `delivery/proof-of-delivery-service/HBA.Delivery.Proof.Application/Abstractions/ProofStore.cs:80` | **aucun** | — | service.proof-of-delivery.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `RestaurantApproved` | `food/restaurant-service/HBA.Food.Restaurant.Application/Restaurants/RestaurantDomainEventHandlers.cs:27` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/RestaurantLifecycleNotificationHandlers.cs:22`<br>identity-service · `common/identity-service/HBA.Identity.Application/Users/EventHandlers/BusinessRoleGrantHandlers.cs:234` | oui (2) | service.restaurant.v1 | Id (aléatoire) | — | **topic non écouté** |
| `RestaurantRejected` | `food/restaurant-service/HBA.Food.Restaurant.Application/Restaurants/RestaurantDomainEventHandlers.cs:48` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/RestaurantLifecycleNotificationHandlers.cs:47` | oui (1) | service.restaurant.v1 | Id (aléatoire) | — | **topic non écouté** |
| `RestaurantReopened` | `food/restaurant-service/HBA.Food.Restaurant.Application/Restaurants/RestaurantDomainEventHandlers.cs:86` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/RestaurantLifecycleNotificationHandlers.cs:100` | oui (1) | service.restaurant.v1 | Id (aléatoire) | — | **topic non écouté** |
| `RestaurantSuspended` | `food/restaurant-service/HBA.Food.Restaurant.Application/Restaurants/RestaurantDomainEventHandlers.cs:67` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/RestaurantLifecycleNotificationHandlers.cs:77` | oui (1) | service.restaurant.v1 | Id (aléatoire) | — | **topic non écouté** |
| `ReturnRefundApproved` | **aucun** | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/RefundNotificationHandlers.cs:24` | oui (1) | — | OrderId | — | **consumer orphelin** |
| `ReturnRefunded` | **aucun** | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/RefundNotificationHandlers.cs:45`<br>wallet-service · `common/wallet-service/HBA.Financial.Wallet.Application/Earnings/ReverseEarningsOnReturnRefundedHandler.cs:62` | oui (2) | — | OrderId | — | **consumer orphelin** |
| `ReviewPublished` | `common/review-service/HBA.Engagement.Reviews.Application/Reviews/EventHandlers/ReviewDomainEventHandlers.cs:26` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerLifecycleNotificationHandlers.cs:193` | oui (1) | service.review.v1 | ProductId | — | **topic non écouté** |
| `ReviewRejected` | `common/review-service/HBA.Engagement.Reviews.Application/Reviews/EventHandlers/ReviewDomainEventHandlers.cs:111` | **aucun** | — | service.review.v1 | ProductId | — | publié, aucun consumer |
| `RouteCalculated` | `delivery/route-service/HBA.Delivery.Route.Application/Abstractions/RouteStore.cs:37` | **aucun** | — | service.route.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `RouteDeliveryEtaUpdated` | `delivery/route-service/HBA.Delivery.Route.Application/Abstractions/RouteStore.cs:74` | **aucun** | — | service.route.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `RouteRecalculated` | `delivery/route-service/HBA.Delivery.Route.Application/Abstractions/RouteStore.cs:67` | **aucun** | — | service.route.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `SellerActivated` | `marketplace/seller-service/HBA.Merchants.Application/Sellers/EventHandlers/SellerDomainEventHandlers.cs:37` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerActivatedNotificationHandler.cs:17` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerClosed` | `marketplace/seller-service/HBA.Merchants.Application/Sellers/EventHandlers/SellerDomainEventHandlers.cs:55` | catalog-service · `marketplace/catalog-service/HBA.Catalog.Infrastructure/Integration/SellerLifecycleCatalogHandlers.cs:56`<br>notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerLifecycleNotificationHandlers.cs:55` | oui (2) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerDeleted` | `marketplace/seller-service/HBA.Merchants.Application/Sellers/EventHandlers/SellerDomainEventHandlers.cs:147` | catalog-service · `marketplace/catalog-service/HBA.Catalog.Infrastructure/Integration/SellerLifecycleCatalogHandlers.cs:154` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerKybApproved` | `marketplace/seller-service/HBA.Merchants.Application/Sellers/EventHandlers/SellerDomainEventHandlers.cs:230` | **aucun** | — | service.seller.v1 | SellerId | — | publié, aucun consumer |
| `SellerKybRejected` | `marketplace/seller-service/HBA.Merchants.Application/Sellers/EventHandlers/SellerDomainEventHandlers.cs:73` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerLifecycleNotificationHandlers.cs:133` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerKybSubmitted` | `marketplace/seller-service/HBA.Merchants.Application/Sellers/EventHandlers/SellerDomainEventHandlers.cs:201` | **aucun** | — | service.seller.v1 | SellerId | — | publié, aucun consumer |
| `SellerMemberActivated` | `marketplace/seller-service/HBA.Merchants.Application/Members/MemberEventHandlers.cs:156` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/MemberNotificationHandlers.cs:277` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerMemberInvited` | `marketplace/seller-service/HBA.Merchants.Application/Members/MemberCommands.cs:329` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/MemberEmailHandlers.cs:31` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerMemberJoined` | `marketplace/seller-service/HBA.Merchants.Application/Members/MemberEventHandlers.cs:40` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/MemberNotificationHandlers.cs:84`<br>identity-service · `common/identity-service/HBA.Identity.Application/Users/EventHandlers/BusinessRoleGrantHandlers.cs:297` | oui (2) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerMemberRevoked` | `marketplace/seller-service/HBA.Merchants.Application/Members/MemberEventHandlers.cs:184` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/MemberNotificationHandlers.cs:326`<br>identity-service · `common/identity-service/HBA.Identity.Application/Users/EventHandlers/BusinessRoleGrantHandlers.cs:338` | oui (2) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerMemberRolesUpdated` | `marketplace/seller-service/HBA.Merchants.Application/Members/MemberEventHandlers.cs:63` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/MemberNotificationHandlers.cs:134` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerMemberStoreAssigned` | `marketplace/seller-service/HBA.Merchants.Application/Members/MemberEventHandlers.cs:84` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/MemberNotificationHandlers.cs:165` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerMemberStoreUnassigned` | `marketplace/seller-service/HBA.Merchants.Application/Members/MemberEventHandlers.cs:105` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/MemberNotificationHandlers.cs:205` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerMemberSuspended` | `marketplace/seller-service/HBA.Merchants.Application/Members/MemberEventHandlers.cs:136` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/MemberNotificationHandlers.cs:246` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerRatingRecomputed` | `common/review-service/HBA.Engagement.Reviews.Application/Reviews/EventHandlers/ReviewDomainEventHandlers.cs:74` | seller-service · `marketplace/seller-service/HBA.Merchants.Infrastructure/Integration/SellerRatingHandler.cs:43` | oui (1) | service.review.v1 | SellerId | — | **topic non écouté** |
| `SellerReactivated` | `marketplace/seller-service/HBA.Merchants.Application/Sellers/EventHandlers/SellerDomainEventHandlers.cs:129` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerLifecycleNotificationHandlers.cs:155` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerRegistered` | `marketplace/seller-service/HBA.Merchants.Application/Sellers/EventHandlers/SellerDomainEventHandlers.cs:18` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerLifecycleNotificationHandlers.cs:17`<br>identity-service · `common/identity-service/HBA.Identity.Application/Users/EventHandlers/BusinessRoleGrantHandlers.cs:204` | oui (2) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerSuspended` | `marketplace/seller-service/HBA.Merchants.Application/Sellers/EventHandlers/SellerDomainEventHandlers.cs:92` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerLifecycleNotificationHandlers.cs:86` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `SellerSuspensionLifted` | `marketplace/seller-service/HBA.Merchants.Application/Sellers/EventHandlers/SellerDomainEventHandlers.cs:111` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerLifecycleNotificationHandlers.cs:106` | oui (1) | service.seller.v1 | SellerId | — | **topic non écouté** |
| `ShipmentDelivered` | **aucun** | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/ShipmentNotificationHandlers.cs:38`<br>wallet-service · `common/wallet-service/HBA.Financial.Wallet.Application/Earnings/ReleaseSellerEarningsOnShipmentDeliveredHandler.cs:18` | oui (2) | — | OrderId | — | **consumer orphelin** |
| `ShipmentReadyForPickup` | **aucun** | **aucun** | — | — | OrderId | — | mort-né |
| `ShipmentShipped` | **aucun** | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/ShipmentNotificationHandlers.cs:11` | oui (1) | — | OrderId | — | **consumer orphelin** |
| `StockDepleted` | `marketplace/inventory-service/HBA.Inventory.Application/Stock/EventHandlers/InventoryDomainEventHandlers.cs:36` | notification-service · `common/notification-service/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerLifecycleNotificationHandlers.cs:245` | oui (1) | service.inventory.v1 | Id (aléatoire) | — | oui |
| `StockReplenished` | `marketplace/inventory-service/HBA.Inventory.Application/Stock/EventHandlers/InventoryDomainEventHandlers.cs:54` | **aucun** | — | service.inventory.v1 | Id (aléatoire) | — | publié, aucun consumer |
| `StockReserved` | `marketplace/inventory-service/HBA.Inventory.Application/Stock/EventHandlers/InventoryDomainEventHandlers.cs:17` | **aucun** | — | service.inventory.v1 | OrderId | — | publié, aucun consumer |
| `StoreClosed` | `marketplace/seller-service/HBA.Merchants.Application/Stores/StoreDomainEventHandlers.cs:20` | **aucun** | — | service.seller.v1 | SellerId | — | publié, aucun consumer |
| `StoreOpened` | `marketplace/seller-service/HBA.Merchants.Application/Stores/StoreDomainEventHandlers.cs:38` | **aucun** | — | service.seller.v1 | SellerId | — | publié, aucun consumer |
| `StoreSuspended` | `marketplace/seller-service/HBA.Merchants.Application/Stores/StoreDomainEventHandlers.cs:58` | **aucun** | — | service.seller.v1 | SellerId | — | publié, aucun consumer |
| `StoreSuspensionLifted` | `marketplace/seller-service/HBA.Merchants.Application/Stores/StoreDomainEventHandlers.cs:83` | **aucun** | — | service.seller.v1 | SellerId | — | publié, aucun consumer |
| `TokenRevoked` | `common/identity-service/HBA.Identity.Application/Users/Commands/Logout/LogoutByRefreshTokenCommand.cs:75` | **aucun** | — | service.identity.v1 | UserId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `TrackingLocationSampled` | `delivery/tracking-service/HBA.Delivery.Tracking.Application/Abstractions/TrackingStore.cs:98` | **aucun** | — | service.tracking.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `TrackingSessionEnded` | `delivery/tracking-service/HBA.Delivery.Tracking.Application/Abstractions/TrackingStore.cs:45` | **aucun** | — | service.tracking.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `TrackingSessionStarted` | `delivery/tracking-service/HBA.Delivery.Tracking.Application/Abstractions/TrackingStore.cs:23` | **aucun** | — | service.tracking.v1 | DeliveryId | v1 (attribut, non utilisé) | **jeté (file mémoire)** |
| `UserAddressCreated` | `common/user-service/HBA.Users.Application/Addresses/AddAddressCommand.cs:69` | **aucun** | — | service.user.v1 | UserId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `UserAnonymized` | `common/identity-service/HBA.Identity.Application/Users/EventHandlers/UserDomainEventHandlers.cs:62` | seller-service · `marketplace/seller-service/HBA.Merchants.Infrastructure/Integration/UserAnonymizedSellerPurgeHandler.cs:59`<br>user-service · `common/user-service/HBA.Users.Api/Integration/SyncUserProfileOnIdentityChangeHandlers.cs:117` | oui (2) | service.identity.v1 | UserId | — | oui |
| `UserDeviceRegistered` | `common/user-service/HBA.Users.Application/Devices/DeviceUseCases.cs:73` | **aucun** | — | service.user.v1 | UserId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `UserEmailConfirmed` | `common/identity-service/HBA.Identity.Application/Users/EventHandlers/UserDomainEventHandlers.cs:76` | **aucun** | — | service.identity.v1 | UserId | — | publié, aucun consumer |
| `UserLoggedIn` | **aucun** | **aucun** | — | — | UserId | v1 (attribut, non utilisé) | mort-né |
| `UserProfileChanged` | `common/user-service/HBA.Users.Application/Profiles/ProfileCommands.cs:121` | **aucun** | — | service.user.v1 | UserId | v1 (attribut, non utilisé) | publié, aucun consumer |
| `UserProfileUpdated` | `common/identity-service/HBA.Identity.Application/Users/EventHandlers/UserDomainEventHandlers.cs:40` | user-service · `common/user-service/HBA.Users.Api/Integration/SyncUserProfileOnIdentityChangeHandlers.cs:47` | oui (1) | service.identity.v1 | UserId | — | oui |
| `UserRegistered` | `common/identity-service/HBA.Identity.Application/Users/EventHandlers/UserDomainEventHandlers.cs:18` | user-service · `common/user-service/HBA.Users.Api/Integration/CreateUserProfileOnUserRegisteredHandler.cs:57` | oui (1) | service.identity.v1 | UserId | v1 (attribut, non utilisé) | oui |

> `DeliveryAssigned` apparaît « jeté » parce qu'un de ses deux producteurs est
> `dispatch-service` (§1.2). Sa variante `HBA.Deliveries.Contracts`, publiée par
> `delivery-service` (`delivery/delivery-service/HBA.Delivery.Core.Application/EventHandlers/DeliveryDomainEventHandlers.cs:89`),
> part bien sur `service.delivery.v1` et atteint son consumer.

---

## 3. Événements publiés que personne ne consomme (événements morts) — 51

Aucun `IIntegrationEventHandler<T>` n'existe nulle part pour ces types. Le message est
produit, stocké par le courtier, et acquitté sans effet par les 27 consumers
(`KafkaIntegrationEventConsumer.cs:361-400`, journal `Information`, une fois par type).

**catalog-service** (`service.catalog.v1`, sujet pourtant écouté) — 15 :
`BrandCreated`, `BrandRequested`, `BrandRequestApproved`, `CategoryCreated`,
`ProductCreated`, `ProductDeleted`, `ProductSubmitted`, `ProductApproved`,
`ProductRejected`, `ProductPublished`, `ProductUnpublished`, `ProductSuspended`,
`ProductRestored`, `ProductArchived`, `ProductMediaRemoved`.
Le consommateur historique était le module Search, resté dans le monolithe
(`scripts/check-event-consumers.py:56-58`). **Le catalogue publie donc 15 événements dans
le vide : aucun index de recherche ne se met à jour.**

**seller-service** — `SellerKybSubmitted`, `SellerKybApproved`, `StoreOpened`,
`StoreClosed`, `StoreSuspended`, `StoreSuspensionLifted`.

**inventory-service** — `StockReserved`, `StockReplenished` (`service.inventory.v1`, écouté).

**media-service** — `MediaReady`, `MediaDeleted`, `MediaProcessingFailed`
(`service.media.v1`, écouté). Aucun service ne réagit à un média prêt : les fiches
produit ne sont jamais mises à jour par événement.

**identity-service** — `UserEmailConfirmed`, `TokenRevoked` (`service.identity.v1`, écouté).
`TokenRevoked` sans consumer signifie qu'**aucune invalidation de cache d'autorisation
n'a lieu** à la révocation d'un jeton.

**user-service** — `UserProfileChanged`, `UserAddressCreated`, `UserDeviceRegistered`.

**payment-service** — `PaymentCreated`, `PaymentRefundFailed`.
**promotion-service** — `PromotionCreated`, `PromotionExhausted`, `CouponUsed`.
**review-service** — `ReviewRejected`.
**cart-service** — `CartCheckedOut`.
**food-cart-service** — `FoodCartCheckedOut`.
**food-order-service** — `MealOrderConfirmed`, `MealOrderCancelled`, `MealOrderDelivered`,
`MealOrderUnderReview`, `MealOrderResumedAfterReview`.
**restaurant-service** — `FoodOrderReceived`.
**delivery-pricing-service** — `DeliveryQuoteCreated`, `DeliveryQuoteConsumed`,
`DeliveryPricingRuleCreated/Updated/Activated/Deactivated`.

(Les 16 événements de dispatch/driver/route/tracking/proof ne sont pas comptés ici :
ils ne sont jamais publiés du tout — §1.2.)

---

## 4. Consumers sans producteur — 4 handlers enregistrés qui n'ont rien à recevoir

| Événement | Consumer enregistré | Producteur |
|---|---|---|
| `ReturnRefundedIntegrationEvent` | `ReverseEarningsOnReturnRefundedHandler` — `common/wallet-service/HBA.Financial.Wallet.Infrastructure/SettlementModuleInstaller.cs:119` | **aucun** |
| `ReturnRefundedIntegrationEvent` | `ReturnRefundedNotificationHandler` — `common/notification-service/…/NotificationsModuleInstaller.cs:255` | **aucun** |
| `ReturnRefundApprovedIntegrationEvent` | `ReturnRefundApprovedNotificationHandler` — `…/NotificationsModuleInstaller.cs:254` | **aucun** |
| `ShipmentShippedIntegrationEvent` | `ShipmentShippedNotificationHandler` — `…/NotificationsModuleInstaller.cs:246` | **aucun** (module Shipping resté au monolithe) |
| `ShipmentDeliveredIntegrationEvent` | `ShipmentDeliveredNotificationHandler` + `ReleaseSellerEarningsOnShipmentDeliveredHandler` — `SettlementModuleInstaller.cs:133` | **aucun** |

**CRITICAL — argent.** `return-refund-service` existe, possède un `ReturnRefundDbContext`,
un `OutboxProcessor` (`ReturnRefundModuleInstaller.cs:60`) et référence
`HBA.Returns.Contracts` (`HBA.Marketplace.ReturnRefund.Application.csproj`), mais **ne
construit aucun `IntegrationEvent`** : recherche exhaustive de
`new ReturnRefunded…` / `new ReturnRefundApproved…` dans tout le dépôt → seules les
déclarations (`shared/contracts/HBA.Returns.Contracts/IntegrationEvents/ReturnsIntegrationEvents.cs:14,34`)
et les consumers. Conséquence : quand un retour est remboursé, **le gain du vendeur n'est
jamais contre-passé** (`ReverseEarningsOnReturnRefundedHandler` ne s'exécute jamais) et
l'acheteur n'est pas notifié. La plateforme paie le vendeur pour une marchandise rendue.

`PaymentRefundedIntegrationEvent` **est** publié
(`common/payment-service/HBA.Financial.Payments.Application/Payments/EventHandlers/PaymentDomainEventHandlers.cs:85`)
mais wallet-service n'a aucun handler pour lui : il n'y a donc pas de chemin de repli.

---

## 5. Handlers écrits mais non enregistrés en DI

**Résultat : aucun.** Les 96 implémentations d'`IIntegrationEventHandler<T>` du dépôt ont
toutes un `AddScoped<IIntegrationEventHandler<T>, X>` correspondant (96 enregistrements,
appariement 1-pour-1 sur le couple (événement, classe)). Aucun scan d'assemblies n'est
utilisé — tous les enregistrements sont explicites, dans un `*ModuleInstaller.cs` ou un
`Program.cs`, ce qui rend la vérification exhaustive fiable.

Répartition des 96 enregistrements : `NotificationsModuleInstaller.cs` 46 ·
`SettlementModuleInstaller.cs` 6 · `OrderingModuleInstaller.cs` 6 ·
`DeliveriesModuleInstaller.cs` 6 · `MealOrderingModuleInstaller.cs` 5 ·
`IdentityModuleInstaller.cs` 5 · `Food.Restaurant.Api/Program.cs` 4 ·
`Users.Api/Program.cs` 3 · `Order.Api/Program.cs` 3 · `SellersModuleInstaller.cs` 3 ·
`Promotions.Api/Program.cs` 2 · `CatalogModuleInstaller.cs` 2 ·
`PaymentsModuleInstaller.cs` 2 · `CartModuleInstaller.cs` 1 ·
`FoodCartModuleInstaller.cs` 1 · `MediaModuleInstaller.cs` 1.

**Ce qui remplace ce défaut, en pire.** Un handler correctement enregistré ne s'exécute
que si trois autres conditions sont vraies, et elles ne le sont pas :

1. **Le sujet doit être écouté** — 37 événements dotés d'un consumer enregistré échouent
   ici (§1.1). Exemples : `PaymentCaptured` → `ConfirmOrderOnPaymentCapturedHandler`
   (`marketplace/order-service/HBA.Order.Infrastructure/OrderingModuleInstaller.cs:61`),
   `SellerRegistered` → `GrantSellerRoleHandler`
   (`common/identity-service/HBA.Identity.Infrastructure/IdentityModuleInstaller.cs:90`),
   les 46 handlers de notification-service.
2. **L'hôte doit héberger un consumer Kafka** — les 9 services stubs (§1.2 + menu,
   availability, kitchen-prep, food-review) n'appellent pas
   `AddBuildingBlocksInfrastructure` et n'ont donc pas de `KafkaIntegrationEventConsumer`.
3. **L'assembly du contrat doit être chargée** — sinon `ResolveEventType`
   (`KafkaIntegrationEventConsumer.cs:433-467`) rend `null` et le message est acquitté
   avec un simple `LogWarning`, une fois par type.

**MEDIUM — enregistrements d'idempotence inutiles.** `identity-service`
(`IdentityModuleInstaller.cs:57`), `notification-service`
(`NotificationsModuleInstaller.cs:77`), `payment-service`
(`PaymentsModuleInstaller.cs:52`) et `promotion-service`
(`PromotionsModuleInstaller.cs:52`) enregistrent `IConsumerInbox` et portent la table
`consumer_inbox`, mais **aucun de leurs handlers ne l'injecte**. La garde existe, elle
est câblée, et personne ne s'en sert.

---

## 6. Incohérences de nom de topic / d'`eventType`

### 6.1 CRITICAL — `[HbaEvent]`, `HbaEventNaming` et `HbaEventEnvelope` sont du code mort

59 déclarations d'événement portent `[HbaEvent(...)]` (ex.
`shared/contracts/HBA.Identity.Contracts/IntegrationEvents/IdentityIntegrationEvents.cs:6`,
`services/common/payment-service/src/HBA.Financial.Payments.Contracts/IntegrationEvents/PaymentsIntegrationEvents.cs:17`).
`HbaEventNaming` sait en dériver le topic canonique
(`shared/common/HBA.Shared.Infrastructure/Kafka/HbaEventNaming.cs:46-47`) et
`HbaEventEnvelope` porte l'enveloppe du §19.1 avec `environment`, `actor`, `traceId`,
`partitionKey`, `metadata.schema`.

**Aucun des deux n'est référencé hors de son propre fichier.** `KafkaIntegrationEventPublisher`
utilise exclusivement `KafkaEventNaming` et `KafkaEventEnvelope`
(`KafkaIntegrationEventPublisher.cs:104-130`). Vérification : l'unique occurrence de
`HbaEventNaming` en dehors de sa définition est dans `scripts/k8s-kafka-topics.py`
(commentaire). Conséquences :

- l'`eventType` réellement émis vient du **nom de classe .NET** (`KafkaEventNaming.cs:41-50`),
  exactement ce que `HbaEventAttribute` était censé empêcher
  (`shared/common/HBA.Shared.IntegrationEvents/HbaEventAttribute.cs:16-23`) ;
- `PaymentCapturedIntegrationEvent` est annoté `payment.succeeded` mais émis en
  `payment.captured` ; `DriverEarningCreditedIntegrationEvent` annoté `wallet.credited`
  est émis en `driver.earning.credited` ; `PayoutPaidIntegrationEvent` annoté
  `payout.completed` est émis en `payout.paid` ; `MediaProcessingFailedIntegrationEvent`
  annoté `media.processing_failed` est émis en `media.processing.failed`.
  **Ces divergences sont sans effet aujourd'hui uniquement parce que le consumer
  re-dérive le nom avec la même fonction** — producteur et consommateur partagent le bug,
  donc ils s'accordent. Le jour où l'un bascule sur `[HbaEvent]`, le flux se coupe.
- `k8s/overlays/{dev,staging,prod}/kafka-topics.yaml` provisionne 14 topics
  `hba.<env>.<domaine>.<agrégat>.v1` (+ 14 `.dlq`) **auxquels aucun service ne publie ni
  ne s'abonne**. Les topics réellement utilisés (`service.*.v1`) ne sont, eux, déclarés
  nulle part en k8s : le courtier les crée à la volée à 1 partition, sans réplication —
  précisément ce que le fichier généré dit vouloir éviter
  (`k8s/overlays/prod/kafka-topics.yaml:1-12`).

### 6.2 HIGH — trois événements déclarés deux fois, résolution par ordre alphabétique

`ResolveEventType` (`KafkaIntegrationEventConsumer.cs:433-467`) trie les candidats par
`FullName` ordinal et prend le premier :

| `eventType` | Candidats | Retenu |
|---|---|---|
| `delivery.assigned` | `HBA.Deliveries.Contracts.IntegrationEvents.DeliveryAssignedIntegrationEvent` (`delivery/delivery-service/HBA.Deliveries.Contracts/IntegrationEvents/DeliveryIntegrationEvents.cs:48`) · `HBA.Dispatch.Contracts.…` (`shared/contracts/HBA.Dispatch.Contracts/IntegrationEvents/DispatchIntegrationEvents.cs:34`) | `Deliveries` |
| `driver.verified` | `HBA.Deliveries.Contracts.…` (`DeliveryIntegrationEvents.cs:61`) · `HBA.Drivers.Contracts.…` (`shared/contracts/HBA.Drivers.Contracts/IntegrationEvents/DriverIntegrationEvents.cs:13`) | `Deliveries` |
| `promotion.created` | `HBA.Pricing.Contracts.…` (`shared/contracts/HBA.Pricing.Contracts/IntegrationEvents/PricingIntegrationEvents.cs:6`) · `HBA.Promotions.Contracts.…` (`shared/contracts/HBA.Promotions.Contracts/IntegrationEvents/PromotionIntegrationEvents.cs:17`) | `Pricing` |

Les handlers concernés (`GrantDriverRoleHandler`, `NotifyDriverOnDeliveryAssignedHandler`)
importent bien `HBA.Deliveries.Contracts.IntegrationEvents` : ils gagnent le tirage
aujourd'hui. Le jour où l'un des services référence `HBA.Drivers.Contracts` ou
`HBA.Dispatch.Contracts` en plus, le tri peut basculer et le handler cesse d'être appelé
**sans erreur**. Pour `promotion.created`, les deux formes ont des champs différents
(`Type/ScopeType` contre `Name/Scope/Value/Budget/…`) : la désérialisation vers la
mauvaise donnerait des champs nuls, pas une exception.

### 6.3 MEDIUM — `AggregateType` de l'enveloppe est faux

`KafkaEventNaming.AggregateType(eventType)` (`KafkaEventNaming.cs:52-56`) prend le
**premier segment de l'eventType**. `PaymentCapturedIntegrationEvent` (agrégat `Payment`)
donne `payment` — correct par hasard ; `SellerMemberStoreAssignedIntegrationEvent`
(agrégat `SellerMember`) donne `seller` ; `StockDepletedIntegrationEvent` (agrégat
`InventoryItem`) donne `stock` ; `MealOrderPlacedIntegrationEvent` donne `meal`.
`HbaEventAttribute.AggregateType`, renseigné sur 59 déclarations, n'est jamais lu.

### 6.4 LOW — `shared/kafka-schemas/` est vide

Le répertoire ne contient qu'un `README.md` (44 lignes de prose). **Aucun schéma n'y est
publié** : il n'y a donc rien à comparer aux classes C#, et la promesse du README
(« on ajoute, on ne retire pas ; on versionne, on ne renomme pas ») n'est adossée à aucun
artefact vérifiable ni à aucun contrôle CI. `schema-id` est posé en dur à `"0"` dans
chaque en-tête Kafka (`KafkaIntegrationEventPublisher.cs:147`).

---

## 7. Outbox

### 7.1 Transactionnalité — correcte là où un `ModuleDbContext` existe

`ModuleDbContext.SaveChangesAsync` (`shared/common/HBA.Shared.Infrastructure/Persistence/ModuleDbContext.cs:78-98`)
enchaîne `DispatchDomainEvents` → `DrainIntegrationEventsToOutbox` → `RecordAuditTrail` →
`base.SaveChangesAsync`. Les lignes d'outbox sont ajoutées au `ChangeTracker` et
committées **par le même `SaveChanges`** que le changement métier : l'atomicité est réelle.
`OutboxIntegrationEventPublisher` (`Outbox/OutboxIntegrationEventPublisher.cs:13-40`)
implémente la même garantie mais **n'est enregistré nulle part** — code mort.

### 7.2 Processeurs d'outbox par service

23 modules appellent `AddOutboxProcessor<T>` (`OutboxRegistration.cs:42-66`) :
Billing, Identity, Media, Messaging, Notifications, Payments, Promotions,
Recommendations, Reviews, Users, Wallet, Wishlist, DeliveryPricing, Deliveries,
FoodCart, MealOrdering, Food(Restaurant), Cart, Catalog, Inventory, Ordering,
ReturnRefund, Sellers.

**Services qui publient sans processeur d'outbox — perte totale et silencieuse** :

| Service | Fichier | Événements perdus |
|---|---|---|
| dispatch-service | `delivery/dispatch-service/HBA.Delivery.Dispatch.Infrastructure/Persistence/DispatchInfrastructureModule.cs:13-14` | 3 |
| driver-service | `delivery/driver-service/HBA.Delivery.Driver.Infrastructure/Persistence/DriversInfrastructureModule.cs:13-14` | 2 |
| route-service | `delivery/route-service/HBA.Delivery.Route.Infrastructure/Persistence/RoutesInfrastructureModule.cs:13-14` | 3 |
| tracking-service | `delivery/tracking-service/HBA.Delivery.Tracking.Infrastructure/Persistence/TrackingInfrastructureModule.cs:13-14` | 4 |
| proof-of-delivery-service | `delivery/proof-of-delivery-service/HBA.Delivery.Proof.Infrastructure/Persistence/ProofOfDeliveryInfrastructureModule.cs:13-14` | 4 |

(menu, availability, kitchen-prep, food-review n'ont ni outbox ni publication : rien à perdre.)

### 7.3 Reprise, backoff, lettres mortes

- **Backoff** : exponentiel base 10 s, plafond 30 min, jitter ±20 %
  (`Outbox/OutboxRetryPolicy.cs:5-22`). Le filtre `NextAttemptAtUtc` sort le message
  empoisonné du lot (`OutboxProcessor.cs:132-138`) — le blocage de tête de file est bien fermé.
- **Plafond** : `MaxAttempts = 10` → `DeadLetteredOnUtc`, log `Critical`, métrique
  `IOutboxMetrics.DeadLettered` (`OutboxProcessor.cs:205-241`).
- **HIGH — la file de lettres mortes n'est pas consultable.** `OutboxMessage.cs:65`,
  `OutboxProcessor.cs:221` et `KafkaIntegrationEventPublisher.cs:84` renvoient tous vers
  `GET /admin/outbox/dead-letters`. **Cet endpoint n'existe pas** : aucune occurrence de
  `dead-letters` dans une route du dépôt, et `OutboxContextRegistration` — peuplé
  explicitement pour ça (`OutboxRegistration.cs:54-58`) — n'est injecté nulle part. Un
  message enterré est donc définitivement perdu, sans moyen de rejeu.
- **HIGH — pas de verrou de ligne.** La lecture ne pose pas de
  `SELECT … FOR UPDATE SKIP LOCKED` (`OutboxProcessor.cs:132-138`) ; le commentaire
  l'assume (`OutboxProcessor.cs:50-57`). **Toute mise à l'échelle horizontale d'un service
  duplique chaque publication.** Le garde-fou annoncé — `OUTBOX_ENABLED=false` sur les BFF —
  **n'est posé nulle part** : aucune occurrence de `OUTBOX_ENABLED` dans
  `docker-compose.dev.yml` ni dans `k8s/`. `OutboxRegistration.Enabled` vaut donc
  toujours `true` (`OutboxRegistration.cs:39-40`).
- **MEDIUM — `Type` est un nom .NET pleinement qualifié**
  (`Serialization/EventTypeName.cs:9`). Renommer une classe ou une assembly rend
  indésérialisables toutes les lignes d'outbox en attente, qui partent en lettre morte.

### 7.4 Côté consumer : pas de DLQ

`KafkaIntegrationEventConsumer` retente 3 fois (2 s, 4 s) puis journalise `Critical` et
**committe l'offset quand même** (`KafkaIntegrationEventConsumer.cs:213-259`, `:120-130`).
Aucun topic `.dlq` n'est produit — les 14 topics `.dlq` de `k8s/overlays/*/kafka-topics.yaml`
ne sont écrits par personne. **Un événement dont le traitement échoue durablement est
définitivement perdu.**

Corollaire documenté mais dangereux (`KafkaIntegrationEventConsumer.cs:377-383`) : un
événement reçu **avant** que son handler existe est acquitté pour le groupe et ne
reviendra jamais. Ajouter le handler plus tard ne rattrape rien.

Le commentaire de `SendEmailVerificationHandler`
(`common/notification-service/…/AccountEmailHandlers.cs:44-46`) affirme qu'« un échec
laisse le message d'outbox non traité, donc rejoué au tour suivant ». **C'est faux depuis
le passage à Kafka** : la ligne d'outbox est marquée traitée dès la production réussie
(`OutboxProcessor.cs:187-191`) ; l'échec côté consumer donne 3 essais puis la perte.

---

## 8. Idempotence des consumers

### 8.1 Le dispositif existe et n'est presque pas utilisé

`IConsumerInbox` / `EfConsumerInbox<T>` / `ConsumerInboxEntry` clé
`(EventId, ConsumerName)` (`shared/common/HBA.Shared.Infrastructure/Inbox/*.cs`) sont
corrects : `MarkProcessedAsync` n'appelle pas `SaveChanges`, la trace est committée avec
l'effet (`Inbox/EfConsumerInbox.cs:28-45`).

Enregistré dans **7** services (catalog, seller, notification, user, identity, promotion,
payment). **Utilisé par 6 handlers sur 96** :

| Handler | Fichier |
|---|---|
| `SellerClosedProductInvalidationHandler` | `marketplace/catalog-service/HBA.Catalog.Infrastructure/Integration/SellerLifecycleCatalogHandlers.cs:97` |
| `SellerDeletedProductPurgeHandler` | `…/SellerLifecycleCatalogHandlers.cs:159` |
| `SellerSalesCountHandler` | `marketplace/seller-service/HBA.Merchants.Infrastructure/Integration/SellerSalesCountHandler.cs:56` |
| `UserAnonymizedSellerPurgeHandler` | `…/Integration/UserAnonymizedSellerPurgeHandler.cs:66` |
| `SellerRatingHandler` | `…/Integration/SellerRatingHandler.cs:50` |
| `CreateUserProfileOnUserRegisteredHandler` | `common/user-service/HBA.Users.Api/Integration/CreateUserProfileOnUserRegisteredHandler.cs:65` |

Aucune inbox dans wallet-service, order-service, food-order-service, restaurant-service,
delivery-service, cart-service, food-cart-service, media-service, inventory-service.

### 8.2 Ce qui protège les 90 autres, et ce qui ne protège rien

**Idempotence métier réelle (acceptable)** :

| Handler | Garde | Preuve |
|---|---|---|
| `CreditDriverOnDeliveryCompletedHandler` → `CreditDriverEarningCommand` | unicité `(referenceType, DeliveryId)` au grand livre | `common/wallet-service/HBA.Financial.Wallet.Application/Wallets/CreditDriverEarningCommand.cs:119-127` |
| `AccrueEarningsOnOrderConfirmedHandler` | `ExistsForOrderAsync` | `…/Earnings/AccrueEarningsOnOrderConfirmedHandler.cs:105` |
| `ReverseEarningsOnReturnRefundedHandler` | écriture de contre-passation déjà présente | `…/Earnings/ReverseEarningsOnReturnRefundedHandler.cs:98-103` |
| `ReleaseEarningsOnOrderDeliveredHandler` | ne traite que les gains `Accrued` | `…/Earnings/ReleaseEarningsOnOrderDeliveredHandler.cs:13-14` |
| `RefundPaymentOnOrderCancelledHandler` | ne rembourse que si `Status == "Captured"` | `common/payment-service/…/RefundPaymentOnOrderCancelledHandler.cs:85-96` |
| `CreateDeliveryOnOrderConfirmedHandler` → `CreateDeliveryCommand` | clé `(Reference, Source)` | `delivery/delivery-service/HBA.Delivery.Core.Application/Commands/CreateDelivery/CreateDeliveryCommand.cs:98-104` |
| handlers de transition d'état (`ConfirmOrderOnPaymentCaptured`, `MarkOrderDelivered…`) | liste blanche de transitions du domaine | — |

**Effets métier NON rejouables — un défaut chacun** :

| Sévérité | Handler | Effet dupliqué au rejeu |
|---|---|---|
| HIGH | **Les 46 handlers de notification-service**, dont 14 fichiers passent par `NotificationDispatcher.NotifyAsync` (`common/notification-service/…/Notifications/NotificationDispatcher.cs:52-74`) crée une ligne `Notification`, `SaveChanges`, puis envoie push **et** e-mail. Aucun `eventId`, aucune clé d'unicité. | Notification in-app en double, push en double, e-mail en double à chaque reprise du consumer (3 essais) ou rééquilibrage de partitions. |
| HIGH | `SendEmailVerificationHandler` (`…/AccountEmailHandlers.cs:38-53`) et `SendPasswordResetEmailHandler` (`:72`) : `_email.SendAsync` sans garde, exception non capturée → jusqu'à 3 envois. | Jusqu'à 3 e-mails porteurs d'un jeton de réinitialisation. |
| HIGH | `SendSellerInvitationEmailHandler` (`…/MemberEmailHandlers.cs:31`) | Invitations en double. |
| MEDIUM | `ReceiveFoodOrderOnOrderConfirmedHandler` (`food/restaurant-service/HBA.Food.Restaurant.Api/Integration/FoodOrderBridgeHandlers.cs:79-140`) : ouvre un ticket de cuisine par appel gRPC, sans clé d'idempotence visible côté appelant. | Deux tickets pour une commande — **à confirmer** côté `ReceiveFoodOrderCommand`. |
| MEDIUM | `WebhookOnDelivery*` ×6 (`delivery/delivery-service/HBA.Delivery.Core.Application/Webhooks/EnqueueWebhookOnDeliveryEvents.cs:126-186`) | Webhooks partenaires en double. |
| MEDIUM | `DeleteMediaOnKybDocumentRemovedHandler` (`common/media-service/…/DeleteMediaOnKybDocumentRemovedHandler.cs:42`) | Suppression rejouée — inoffensive, mais non tracée. |

Aucun décrément de stock ni aucune capture de paiement n'est piloté par événement
(vérifié : `StockReserved` et `PaymentCreated` n'ont aucun consumer) — ces deux
catégories de risque sont absentes par construction.

### 8.3 Le rejeu est certain, pas hypothétique

`EnableAutoCommit = false` + `AutoOffsetReset.Earliest` + commit **après** traitement
(`KafkaIntegrationEventConsumer.cs:98-131`) = livraison « au moins une fois ». Un crash
entre `HandleAsync` et `consumer.Commit` rejoue le message au démarrage suivant. La
boucle de reprise à 3 essais (`:213-236`) rejoue le handler **dans le même processus**.

---

## 9. Double publication possible

- **Publication hors outbox : aucune.** `IKafkaIntegrationEventPublisher` n'a qu'un seul
  appelant, `OutboxProcessor.ProcessBatchAsync` (`OutboxProcessor.cs:122`). Vérifié par
  recherche exhaustive.
- **Publication avant commit : aucune non plus** pour les services à `ModuleDbContext` :
  `PublishAsync` empile dans une liste mémoire (`IntegrationEventQueue.cs:19-23`), le
  drain a lieu dans `SaveChanges`.
- **CRITICAL — double publication par réplication.** §7.3 : sans verrou de ligne et sans
  `OUTBOX_ENABLED=false` nulle part, **deux répliques d'un même service publient chaque
  message deux fois**. Pour `DriverEarningCreditedIntegrationEvent` ou `PayoutPaid`, cela
  se traduit par des notifications de gain en double ; pour tout futur consumer non
  idempotent, par un double effet métier.
- **MEDIUM — perte silencieuse symétrique.** Un `PublishAsync` dans un chemin de code qui
  ne finit pas par `SaveChangesAsync` sur le `ModuleDbContext` du module perd l'événement
  sans trace. C'est exactement ce qui arrive aux 5 services de §1.2, mais le motif est
  reproductible partout : rien dans le type `IntegrationEventQueue` ne signale une file
  non drainée en fin de scope.

---

## 10. Effet métier avant le commit de la base

- `ModuleDbContext.SaveChangesAsync` **dispatche les domain events avant** le commit
  (`ModuleDbContext.cs:80-86`). Les handlers de domaine du dépôt se contentent de mettre
  des `IntegrationEvent` en file, donc l'ordre est sain — mais rien ne l'impose : un
  handler de domaine qui appellerait un service externe agirait avant le commit.
- Côté consumer, **il n'y a aucune transaction du tout**. `IntegrationEventDispatcher`
  (`shared/common/HBA.Shared.Infrastructure/Events/IntegrationEventDispatcher.cs:19-34`)
  invoque les handlers en séquence dans un scope DI ; si le 2ᵉ handler échoue, le 1ᵉʳ a
  déjà committé et la reprise le rejouera. Exemple concret :
  `OrderConfirmedIntegrationEvent` a **6 consumers** répartis sur 5 services ; un échec
  chez l'un ne défait rien chez les autres.
- `NotificationDispatcher.NotifyAsync` (`…/NotificationDispatcher.cs:64-73`) committe la
  notification puis envoie push et e-mail — l'ordre est correct (effet externe après
  commit), mais il n'y a pas d'outbox pour ces envois : un plantage entre les deux perd
  le push sans que rien ne le signale.

---

## 11. Versionnement des événements

- **`IntegrationEvent` ne porte aucun champ de version**
  (`shared/common/HBA.Shared.IntegrationEvents/IntegrationEvent.cs:8-12` : `Id`,
  `OccurredOnUtc`, rien d'autre).
- L'enveloppe transporte `EventVersion`, **codé en dur à `1`**
  (`KafkaIntegrationEventPublisher.cs:113`) et posé tel quel dans l'en-tête
  `event-version` (`:143`). `HbaEventAttribute.Version`, renseigné sur 59 déclarations,
  n'est jamais lu.
- **Le consumer ne lit jamais `EventVersion`** : `DispatchAsync`
  (`KafkaIntegrationEventConsumer.cs:304-408`) n'utilise que `envelope.EventType` et
  `envelope.Data`.
- **Conséquence.** Un champ ajouté est ignoré à la désérialisation (`JsonSerializerDefaults.Web`,
  tolérant) : compatible. Un champ **renommé ou retiré** devient `null`/`default` chez le
  consommateur, **sans erreur** — et sur une propriété `required` non nullable comme
  `OrderCancelledIntegrationEvent.Reason`, cela donne une valeur vide propagée jusqu'à
  l'e-mail client. Un changement de sémantique passe totalement inaperçu. Le topic ne
  porte pas non plus la version majeure (`service.<producteur>.v1` : le `v1` est
  `TopicVersion`, global au bus, pas au contrat) : **il n'existe aujourd'hui aucun moyen
  de faire coexister deux versions d'un même événement.**

---

## 12. Données sensibles inutiles dans les événements

| Sévérité | Champ | Fichier | Problème |
|---|---|---|---|
| **CRITICAL** | `PasswordResetRequestedIntegrationEvent.ResetToken` | `shared/contracts/HBA.Identity.Contracts/IntegrationEvents/IdentityIntegrationEvents.cs:91` | Jeton de réinitialisation **en clair** dans la charge Kafka **et** dans `identity.outbox_messages.Content` (JSON en clair, `OutboxMessage.cs:17`). Quiconque lit le topic ou la table prend n'importe quel compte. Rétention topic : 7 jours (`k8s/overlays/prod/kafka-topics.yaml`) ; la ligne d'outbox n'est jamais purgée. |
| **CRITICAL** | `EmailVerificationRequestedIntegrationEvent.VerificationToken` | `…/IdentityIntegrationEvents.cs:23` | Idem — le commentaire du fichier (`:16-17`) assume « le jeton EN CLAIR ». |
| **HIGH** | `SellerMemberInvitedIntegrationEvent.InvitationToken` | `services/marketplace/seller-service/src/HBA.Merchants.Contracts/IntegrationEvents/MemberIntegrationEvents.cs:45` | Jeton d'invitation en clair → prise de contrôle d'un compte vendeur. |
| MEDIUM | `Email` + `FirstName` | `…/IdentityIntegrationEvents.cs:10-11` (`UserRegistered`), `:21-22`, `:30` (`UserEmailConfirmed`) | `UserEmailConfirmed` n'a aucun consumer : l'adresse circule sans raison. |
| MEDIUM | `FirstName`/`LastName`/`AvatarUrl` | `shared/contracts/HBA.Users.Contracts/IntegrationEvents/UserIntegrationEvents.cs:24-30` | `UserProfileChanged` n'a aucun consumer. |
| MEDIUM | `Latitude`/`Longitude` | `shared/contracts/HBA.Tracking.Contracts/IntegrationEvents/TrackingIntegrationEvents.cs:18-19` | Position GPS nominative d'un livreur, publiée par échantillon. Sans effet aujourd'hui (§1.2), mais aucune politique de rétention distincte n'est prévue : le topic `service.tracking.v1` serait auto-créé avec les réglages par défaut du courtier. |

Point positif vérifié : `UserAnonymizedIntegrationEvent` ne porte que `UserId`, avec la
justification explicite au bon endroit (`…/IdentityIntegrationEvents.cs:53-58`). Aucun
événement ne porte de numéro de téléphone, d'adresse postale ni de secret de paiement.

---

## 13. `correlationId` / `traceId`

- **Ni l'un ni l'autre n'est porté par `IntegrationEvent`**
  (`shared/common/HBA.Shared.IntegrationEvents/IntegrationEvent.cs:8-12`).
- L'enveloppe porte `CorrelationId`, alimenté par
  `_configuration["CorrelationId"] ?? Activity.Current?.TraceId ?? eventId`
  (`KafkaIntegrationEventPublisher.cs:119`). **`_configuration["CorrelationId"]` n'existe
  dans aucun fichier de configuration du dépôt** : la valeur réelle est toujours le
  `TraceId` ou, à défaut, l'`eventId` — donc jamais le `correlationId` métier de la
  requête d'origine, que `HbaRequestContext.CorrelationId` détient pourtant
  (`shared/common/HBA.Shared.Application/Context/HbaRequestContext.cs:33`).
- **Le consumer ne lit jamais `envelope.CorrelationId`** (`KafkaIntegrationEventConsumer.cs:304-408`).
- **`HbaRequestContext` n'est jamais ouvert côté consumer.** Son seul point de
  remplissage est `RequestContextMiddleware` (HTTP,
  `shared/common/HBA.Shared.Hosting/Http/RequestContextMiddleware.cs:66-86`). Dans un
  handler Kafka, `HbaRequestContext.Current` vaut donc `Empty`. Deux conséquences
  vérifiables :
  - `SellerClosedProductInvalidationHandler` écrit
    `HbaRequestContext.Current.CorrelationId` dans l'inbox
    (`…/SellerLifecycleCatalogHandlers.cs:140`) : **la colonne est toujours vide** ;
  - `ModuleDbContext.RecordAuditTrail` (`ModuleDbContext.cs:178-198`) enregistre acteur
    `null` et corrélation `null` pour **toute** mutation déclenchée par un événement.
- **Ce qui fonctionne** : la trace OpenTelemetry. `OutboxMessage.TraceParent` capture
  `Activity.Current?.Id` à l'écriture (`ModuleDbContext.cs:134`,
  `OutboxIntegrationEventPublisher.cs:32`), `OutboxProcessor` le restitue en parent du
  span de publication (`OutboxProcessor.cs:166-171`), l'en-tête `traceparent` est posé
  (`KafkaIntegrationEventPublisher.cs:155-158`) et relu côté consumer avec
  `AddLink` (`KafkaIntegrationEventConsumer.cs:199-205`, `:280-302`). La chaîne de trace
  est donc complète ; **c'est la corrélation métier qui est perdue**, y compris dans le
  journal d'audit.
- `CausationId` et `SagaId` sont posés à `null` en dur
  (`KafkaIntegrationEventPublisher.cs:120-121`) : l'en-tête `saga-id` n'est jamais émis.

---

## 14. Clé de partition

`Key = KafkaEventNaming.AggregateId(event)` (`KafkaIntegrationEventPublisher.cs:106`, `:164`),
qui balaie une liste fixe de 18 noms de propriétés **dans l'ordre** et prend la première
trouvée, avec repli sur `integrationEvent.Id` (`KafkaEventNaming.cs:10-30`, `:58-85`).
Trois partitions en local (`scripts/kafka-topics.sh:74`), trois en k8s.

### 14.1 HIGH — 22 événements partent avec une clé aléatoire

Aucune propriété de la liste ne correspond → repli sur `IntegrationEvent.Id`, un `Guid`
neuf par message. **Deux événements du même agrégat tombent dans des partitions
différentes ; leur ordre n'est plus garanti.**

| Événement | Propriété qui aurait dû servir |
|---|---|
| `StockDepleted`, `StockReplenished` | `InventoryItemId` / `Sku` |
| `RestaurantApproved`, `RestaurantRejected`, `RestaurantSuspended`, `RestaurantReopened` | `RestaurantId` |
| `PromotionCreated` (×2), `PromotionExhausted` | `PromotionId` |
| `DriverAvailabilityChanged`, `DriverSuspended`, `DriverVehicleUpdated` | `DriverId` |
| `NotificationSent`, `NotificationFailed` | `NotificationId` |
| `BrandCreated`, `CategoryCreated` | `BrandId` / `CategoryId` |
| `DeliveryQuoteCreated`, `DeliveryQuoteExpired` | `QuoteId` |
| `DeliveryPricingRuleCreated/Updated/Activated/Deactivated` | `PricingRuleId` |

Cas le plus concret : **`StockDepleted` puis `StockReplenished` sur le même SKU peuvent
être consommés dans le désordre**, laissant un article annoncé en rupture alors qu'il est
réapprovisionné.

### 14.2 MEDIUM — clés incohérentes au sein d'un même agrégat

| Agrégat | Événements | Clés |
|---|---|---|
| `DeliveryQuote` | `DeliveryQuoteCreated` / `DeliveryQuoteConsumed` | `Id` aléatoire / `DeliveryId` |
| `Promotion`/`Coupon` | `PromotionCreated` / `CouponUsed` | `Id` aléatoire / `OrderId` |
| `Conversation` | `MessageSent` | `MessageId` — unique par message, donc équivalent à une clé aléatoire : **l'ordre des messages d'une conversation n'est pas garanti** (`services/common/notification-service/src/HBA.Communication.Contracts/IntegrationEvents/MessagingIntegrationEvents.cs:6`) |
| `Driver` | `DriverCreated`, `DriverVerified` | `UserId` (avant `DriverId` dans la liste de candidats) |

### 14.3 Ce qui est correct

Les agrégats à fort enjeu d'ordre sont bien servis, par chance de l'ordre de la liste :
`OrderId` pour les 7 événements `Order*`, les 7 `FoodOrder*`, les 6 `MealOrder*`, les 5
`Payment*`, `StockReserved`, `CouponUsed` et les 3 `Shipment*` ;
`DeliveryId` pour les 8 `Delivery*`, les 3 `Tracking*`, les 3 `Dispatch*` ;
`SellerId` pour les 10 `Seller*`/`Store*` et les 8 `SellerMember*`.
Une même commande garde donc son ordre de bout en bout — **là où le message arrive**.

Réserve : le champ `HbaEventEnvelope.PartitionKey` prévu par le §19.2 n'existe pas dans
l'enveloppe réellement émise (`KafkaEventEnvelope`), donc un consommateur non .NET ne
peut pas savoir sur quelle clé le message a été partitionné.

---

## 15. Les dix défauts les plus graves

| # | Sév. | Événement / périmètre | Défaut | Preuve |
|---|---|---|---|---|
| 1 | CRITICAL | 37 événements dotés d'un consumer enregistré (dont `payment.captured`, `payment.failed`, `seller.registered`, tout le pont restauration, les 46 notifications) | Le producteur publie sur `service.<producteur>.v1` ; `SubscribeTopics` est une constante à 13 sujets, non configurable, qui ne contient aucun de ces noms. Silencieux des deux côtés. | `Kafka/KafkaEventBusOptions.cs:21-36` + `DependencyInjection.cs:61-70` + `KafkaEventNaming.cs:38-39` vs `docker-compose.dev.yml` (27 `KAFKA__PRODUCER`) |
| 2 | CRITICAL | `ReturnRefunded`, `ReturnRefundApproved` | Consumers enregistrés, **aucun producteur** : le gain vendeur n'est jamais contre-passé après remboursement. `PaymentRefunded` est publié mais wallet n'a pas de handler pour lui — pas de repli. | `SettlementModuleInstaller.cs:119`, `NotificationsModuleInstaller.cs:254-255`, absence de `new ReturnRefunded…` dans tout le dépôt |
| 3 | CRITICAL | 16 événements de dispatch/driver/route/tracking/proof | `IIntegrationEventPublisher` résolu vers `IntegrationEventQueue` sans `ModuleDbContext` ni `OutboxProcessor` : les événements sont ajoutés à une `List<>` scopée et jamais drainés. | `DispatchInfrastructureModule.cs:13-14` (+4 jumeaux) ; unique appelant de `DequeueAll` = `ModuleDbContext.cs:119` |
| 4 | CRITICAL | Tous | Jeton de réinitialisation de mot de passe et jeton de vérification d'e-mail publiés **en clair** sur Kafka et stockés en clair dans `outbox_messages`. Prise de contrôle de compte par lecture du topic ou de la table. | `HBA.Identity.Contracts/IntegrationEvents/IdentityIntegrationEvents.cs:23,91` ; `Outbox/OutboxMessage.cs:17` |
| 5 | CRITICAL | Tous | Aucun verrou de ligne dans l'outbox (`OutboxProcessor.cs:132-138`) et `OUTBOX_ENABLED` **absent de tout le dépôt** : deux répliques d'un service publient chaque événement deux fois. | `OutboxRegistration.cs:39-40` ; aucune occurrence dans `docker-compose.dev.yml` / `k8s/` |
| 6 | HIGH | Tous | Aucune file de lettres mortes exploitable : `GET /admin/outbox/dead-letters`, cité par trois fichiers, n'existe pas ; `OutboxContextRegistration` n'est injecté nulle part. Message enterré = perte définitive. | `OutboxMessage.cs:65`, `OutboxProcessor.cs:221`, `OutboxRegistration.cs:54-58` |
| 7 | HIGH | Les 46 handlers de notification-service + les 3 handlers d'e-mail de compte | Aucune idempotence : la table `consumer_inbox` est enregistrée dans notification-service mais aucun handler ne l'injecte. Le consumer retente 3 fois → notification, push et e-mail (jeton compris) en double ou triple. | `NotificationsModuleInstaller.cs:77` (inbox inutilisée) ; `NotificationDispatcher.cs:52-74` ; `KafkaIntegrationEventConsumer.cs:213-236` |
| 8 | HIGH | `StockDepleted`, `StockReplenished`, `Restaurant*`, `Promotion*`, `Driver*`, `MessageSent`, `Brand/CategoryCreated`, `DeliveryQuote*`, `DeliveryPricingRule*` (22) | Clé de partition = `IntegrationEvent.Id`, aléatoire par message : l'ordre des événements d'un même agrégat n'est pas garanti sur 3 partitions. | `KafkaEventNaming.cs:10-30,58-85` ; `KafkaIntegrationEventPublisher.cs:164` |
| 9 | HIGH | Tous | Aucun DLQ côté consumer : après 3 échecs l'offset est committé et l'événement est perdu. Les 14 topics `.dlq` provisionnés en k8s ne sont écrits par personne. | `KafkaIntegrationEventConsumer.cs:120-130,238-258` ; `k8s/overlays/prod/kafka-topics.yaml` |
| 10 | HIGH | Tous | `[HbaEvent]`, `HbaEventNaming` et `HbaEventEnvelope` sont du code mort : `eventVersion` est figé à 1, l'`eventType` reste dérivé du nom de classe .NET, et les topics `hba.<env>.*.v1` provisionnés en k8s ne sont ni produits ni consommés. Aucune cohabitation de versions n'est possible. | `KafkaIntegrationEventPublisher.cs:104-130` (n'utilise que `KafkaEventNaming`) ; `HbaEventNaming.cs` sans appelant ; `k8s/overlays/*/kafka-topics.yaml` |

### Défauts notables suivants

11. **HIGH** — `correlationId` métier perdu : `HbaRequestContext` n'est jamais ouvert côté
    consumer, `envelope.CorrelationId` n'est jamais lu, et le journal d'audit enregistre
    acteur `null` pour toute mutation issue d'un événement
    (`RequestContextMiddleware.cs:66-86` ; `KafkaIntegrationEventConsumer.cs:304-408` ;
    `ModuleDbContext.cs:178-198`).
12. **HIGH** — 3 événements déclarés dans deux espaces de noms, résolus par tri
    alphabétique (`KafkaIntegrationEventConsumer.cs:433-467`).
13. **MEDIUM** — 51 événements publiés sans aucun consommateur, dont les 15 du catalogue
    (index de recherche jamais alimenté) et `TokenRevoked` (aucune invalidation de cache
    d'autorisation).
14. **MEDIUM** — `shared/kafka-schemas/` ne contient qu'un README ; `schema-id` vaut `"0"`
    en dur (`KafkaIntegrationEventPublisher.cs:147`). Aucun contrôle de compatibilité.
15. **MEDIUM** — `OutboxMessage.Type` est un nom .NET pleinement qualifié
    (`Serialization/EventTypeName.cs:9`) : tout renommage de classe ou d'assembly envoie
    les lignes en attente en lettre morte.

---

## 16. Sur `scripts/check-event-consumers.py`

Exécuté depuis `/root/audit-src` : `python3 scripts/check-event-consumers.py` rend
`Monolithe introuvable (/root/src) — comparaison impossible` et sort en 0. Le script
compare `<dépôt>/src` à `<parent>/src` ; **ni l'un ni l'autre n'existe** dans l'extraction
auditée (le code vit sous `services/` et `shared/`). Le script est donc **inopérant en
l'état** : ses deux contrôles — consommateurs perdus et noms d'événements ambigus —
rendent tous deux zéro résultat quel que soit l'état du code. Le contrôle des doublons
(`doublons()`, lignes 76-104) aurait trouvé les trois cas du §6.2 s'il avait pointé sur
`services/` et `shared/`.

C'est un défaut d'outillage à part entière : `scripts/check-all.sh` l'invoque et le voit
réussir.
