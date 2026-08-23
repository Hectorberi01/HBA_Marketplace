# Matrice gRPC réelle — HBAExpress

Audit statique de `/root/audit-src` (lecture seule). Toutes les lignes citées sont
relatives à la racine du dépôt.

**Méthode.** Les RPC sont lus dans les `.proto` compilés ; les serveurs sont
identifiés par `public override Task<...> <Rpc>(` sur une classe `*.<Api>Base`
réellement passée à `MapInternalGrpcService<T>()` ; les clients sont suivis
jusqu'à leur **appelant applicatif** (pas seulement la méthode d'enveloppe du
projet `*.Contracts.Grpc`, qui peut n'être appelée par personne).

---

## 0. Chiffres

| Mesure | Valeur |
|---|---|
| RPC déclarés dans les `.proto` compilés (`shared/proto/*/v1`) | **113** |
| RPC déclarés dans un `.proto` **non compilé** (`return_refund.proto`) | **3** |
| **Total RPC déclarés** | **116** |
| RPC avec un corps de serveur | **76** |
| RPC **sans corps de serveur** (→ `UNIMPLEMENTED` à l'appel) | **40** |
| RPC avec au moins un appelant applicatif réel | **33** |
| dont **appelés mais non implémentés** (cassés à l'exécution) | **2** |
| RPC implémentés **et** réellement appelés | **31** |
| **RPC morts** (serveur présent, aucun client) | **45** |
| Appels gRPC **sans échéance** | **0** (échéance globale de 5 s, §5) |
| Appels gRPC **sans `CancellationToken`** | **0** (§6) |
| Clients gRPC avec disjoncteur | **0** (§8) |
| Codes de statut gRPC métier utilisés (`NotFound`/`FailedPrecondition`/`AlreadyExists`/`PermissionDenied`) | **0** (§9) |

---

## 1. Duplication des `.proto` — instruit en premier

Le dépôt contient **13 copies** de `.proto` sous `services/*/*/proto/`, en plus des
23 fichiers de `shared/proto/<domaine>/v1/`.

**Elles sont toutes identiques, octet pour octet, à leur original partagé.**
Vérification : `diff` de chacun des 13 couples → aucun écart.

| Copie | Original | Écart |
|---|---|---|
| `services/delivery/delivery-pricing-service/proto/delivery_pricing.proto` | `shared/proto/deliverypricing/v1/delivery_pricing.proto` | identique |
| `services/delivery/delivery-service/proto/delivery.proto` | `shared/proto/delivery/v1/delivery.proto` | identique |
| `services/delivery/dispatch-service/proto/dispatch.proto` | `shared/proto/dispatch/v1/dispatch.proto` | identique |
| `services/delivery/driver-service/proto/driver.proto` | `shared/proto/driver/v1/driver.proto` | identique |
| `services/delivery/proof-of-delivery-service/proto/proof.proto` | `shared/proto/proof/v1/proof.proto` | identique |
| `services/delivery/route-service/proto/route.proto` | `shared/proto/route/v1/route.proto` | identique |
| `services/food/availability-service/proto/availability.proto` | `shared/proto/food/v1/food.proto` | identique |
| `services/food/menu-service/proto/menu.proto` | `shared/proto/food/v1/food.proto` | identique |
| `services/food/restaurant-service/proto/food.proto` | `shared/proto/food/v1/food.proto` | identique |
| `services/food/review-service/proto/review.proto` | `shared/proto/food/v1/food.proto` | identique |
| `services/food/food-cart-service/proto/foodcart.proto` | `shared/proto/foodcart/v1/foodcart.proto` | identique |
| `services/food/food-order-service/proto/foodorder.proto` | `shared/proto/foodorder/v1/foodorder.proto` | identique |
| `services/food/kitchen-prep-service/proto/kitchen.proto` | `shared/proto/foodorder/v1/foodorder.proto` | identique |

**Aucune divergence aujourd'hui — mais aucun mécanisme ne l'empêche demain.**
Preuve : la seule directive `<Protobuf Include=...>` du dépôt pointe vers
`shared/proto` (23 occurrences, toutes dans `shared/contracts/HBA.*.Contracts.Grpc/*.csproj`).
Les 13 copies **ne sont compilées par aucun projet** : elles ne peuvent donc pas
casser la compilation si quelqu'un en modifie une, et le service qui les héberge
(`menu-service`, `availability-service`, `kitchen-prep-service`,
`services/food/review-service`) **n'appelle même pas `AddHbaGrpc()`** — il n'expose
aucun port gRPC. Quatre services portent quatre copies du même `FoodApi` qu'aucun
d'eux ne sert.

- **MEDIUM** — 13 `.proto` non référencés, dont 4 copies du même `hba.food.v1.FoodApi`.
  Un contributeur qui corrige `services/food/menu-service/proto/menu.proto` verra
  son changement compiler, passer la revue, et n'avoir aucun effet.
  *Correctif : supprimer les copies, ou les remplacer par un lien vers `shared/proto`.*

Cas à part : `services/marketplace/return-refund-service/contracts/grpc/return_refund.proto`
(`ReturnRefundGrpc`, 3 RPC) n'a **ni original partagé, ni `<Protobuf Include>`,
ni serveur** — voir §2.

---

## 2. Matrice par RPC

Conventions des colonnes :
- **Deadline** — `5 s` = échéance posée globalement par
  `shared/common/HBA.Shared.Hosting/Grpc/InternalCallInterceptors.cs:72-75`,
  qui s'applique à **tous** les clients (les 20 enregistrements portent
  `.AddInterceptor<InternalCallClientInterceptor>()`).
- **Retry** — `aucun` = aucune `ServiceConfig`/`MethodConfig`/`EnableRetries` dans
  le dépôt (vérifié par recherche exhaustive). Les mentions « rejeu Kafka » ou
  « 2ᵉ essai applicatif » signalent un réessai posé **au-dessus** du transport.
- **Mapping d'erreurs** — `aucun` = l'appelant ne rattrape pas `RpcException` ;
  une panne de transport remonte telle quelle dans le handler.

### CatalogApi — `shared/proto/catalog/v1/catalog.proto` — serveur : catalog-service

| RPC | Serveur (fichier) | Clients réels (fichier:ligne) | Implémenté ? | Mapping d'erreurs | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| GetProduct | `shared/contracts/HBA.Catalog.Contracts.Grpc/ProductsGrpc.cs:143` | `services/marketplace/cart-service/src/HBA.Commerce.Application/Carts/Commands/AddItem/AddItemToCartCommandHandler.cs:103`<br>`services/common/notification-service/src/HBA.Communication.Notifications.Application/Notifications/EventHandlers/SellerLifecycleNotificationHandlers.cs:214` | oui | aucun | 5 s | aucun | — |
| GetProducts | **absent** | enveloppe `ProductsGrpc.cs:265`, **aucun appelant** | **non** | aucun | 5 s | aucun | **HIGH** — `UNIMPLEMENTED` latent (§4) |
| ListProductsBySeller | **absent** | aucun | **non** | — | — | — | proto mort (§1/§3) |
| GetCategory | `ProductsGrpc.cs:156` | aucun | oui | — | — | — | RPC mort (§2b) |
| GetBrand | `ProductsGrpc.cs:178` | aucun | oui | — | — | — | RPC mort |
| ValidateProduct | **absent** | aucun | **non** | — | — | — | proto mort |
| GetOffer | `ProductsGrpc.cs:47` | `AddItemToCartCommandHandler.cs:72` | oui | aucun | 5 s | aucun | — |
| GetOffers | `ProductsGrpc.cs:61` | enveloppe `:320`, aucun appelant | oui | — | — | — | RPC mort |
| ListPurchasableOffers | `ProductsGrpc.cs:80` | enveloppe `:331`, aucun appelant | oui | — | — | — | RPC mort |
| ListOffersBySku | `ProductsGrpc.cs:95` | `SellerLifecycleNotificationHandlers.cs:267` | oui | aucun | 5 s | aucun | — |

### CommerceApi — `shared/proto/commerce/v1/commerce.proto` — serveur : cart-service

| RPC | Serveur | Clients réels | Implémenté ? | Mapping | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| GetActiveCart | `shared/contracts/HBA.Commerce.Contracts.Grpc/CommerceGrpc.cs:41` | `services/marketplace/order-service/src/HBA.Order.Application/Orders/Commands/PlaceOrder/PlaceOrderCommandHandler.cs:63` | oui | aucun | 5 s | aucun | chemin critique du checkout, sans disjoncteur (§8) |
| GetCart | `CommerceGrpc.cs:53` | enveloppe `:157`, aucun appelant | oui | — | — | — | RPC mort |

### CommunicationApi — `shared/proto/communication/v1/communication.proto` — **aucun serveur**

| RPC | Serveur | Clients | Implémenté ? | Défaut |
|---|---|---|---|---|
| ListConversations, GetConversation, StartConversation, SendMessage, MarkConversationRead, ArchiveConversation (6) | **aucun** | **aucun** | **non** | **MEDIUM** — `shared/contracts/HBA.Communication.Contracts.Grpc/` ne contient **que** son `.csproj` : ni serveur, ni client. `services/common/notification-service/.../Program.cs:27` appelle `AddHbaGrpc()` et n'expose aucun service : un port HTTP/2 est ouvert pour rien. |

### DeliveryApi — `shared/proto/delivery/v1/delivery.proto` — serveur : delivery-service

| RPC | Serveur | Clients réels | Implémenté ? | Mapping | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| GetQuote | **absent** | enveloppe `shared/contracts/HBA.Deliveries.Contracts.Grpc/DeliveryGrpc.cs:125`, aucun appelant | **non** | — | — | — | non implémenté + mort |
| **LookupQuote** | **absent** | `services/marketplace/order-service/.../PlaceOrderCommandHandler.cs:371`<br>`services/food/food-order-service/src/HBA.Food.Order.Application/Commands/Orders/PlaceMealOrderCommand.cs:302` | **non** | **aucun** | 5 s | aucun | **CRITICAL** — voir §4.1 |
| CreateDelivery | `services/delivery/delivery-service/src/HBA.Delivery.Core.Api/GrpcServices/DeliveryGrpcService.cs:70` | `services/marketplace/order-service/src/HBA.Order.Api/Integration/CreateDeliveryOnOrderConfirmedHandler.cs:274` et `:311`<br>`services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Integration/FoodOrderBridgeHandlers.cs:319` et `:342` | oui | aucun | 5 s | **2ᵉ essai applicatif + rejeu Kafka** | **HIGH** — §7.2 |
| CancelDelivery | `DeliveryGrpcService.cs:150` | `services/marketplace/order-service/src/HBA.Order.Api/Integration/OrderDeliveryArbitrationHandlers.cs:264` | oui | aucun | 5 s | rejeu Kafka | **HIGH** — `RequiredPartnerId: null` (`DeliveryGrpcService.cs:170`), §10.4 |
| GetDelivery | `DeliveryGrpcService.cs:188` | aucun appelant distant | oui | — | — | — | RPC mort |
| GetDeliveryByReference | `DeliveryGrpcService.cs:199` | aucun appelant distant | oui | — | — | — | RPC mort |
| GetTracking | `DeliveryGrpcService.cs:204` | aucun appelant distant | oui | — | — | — | RPC mort |
| ResolveDriver | `DeliveryGrpcService.cs:248` | `services/common/notification-service/.../DriverEarningNotificationHandler.cs:64`<br>`.../DriverProposalNotificationHandler.cs:60`<br>`services/common/payment-service/src/HBA.Financial.Api/Endpoints/FinancialEndpoints.cs:639` | oui | aucun | 5 s | aucun | — |

### DeliveryPricingApi — `shared/proto/deliverypricing/v1/delivery_pricing.proto` — serveur : delivery-pricing-service

| RPC | Serveur | Clients réels | Implémenté ? | Mapping | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| QuoteDelivery | `services/delivery/delivery-pricing-service/src/HBA.Delivery.Pricing.Api/GrpcServices/DeliveryPricingGrpcService.cs:22` | aucun | oui | — | — | — | RPC mort |
| ValidateQuote | `DeliveryPricingGrpcService.cs:54` | aucun | oui | — | — | — | RPC mort |
| **ConsumeQuote** | `DeliveryPricingGrpcService.cs:41` | `services/delivery/delivery-service/src/HBA.Delivery.Core.Infrastructure/Grpc/PricingClient/GrpcDeliveryPricingQuoteValidator.cs:30` | oui | **partiel** — `:45` ne rattrape que `Unavailable`/`DeadlineExceeded` | 5 s | rejeu Kafka | **HIGH** — RPC **non idempotent** rejoué, §7.1 |
| GetServiceability | `DeliveryPricingGrpcService.cs:87` | aucun | oui | — | — | — | RPC mort |

### DispatchApi / DriverApi / ProofApi / RouteApi / TrackingApi — serveurs présents, **zéro client**

| Service gRPC | Serveur | RPC | Clients | Défaut |
|---|---|---|---|---|
| DispatchApi | `services/delivery/dispatch-service/src/HBA.Delivery.Dispatch.Api/GrpcServices/DispatchGrpcService.cs:20,41,52,64` | RequestDispatch, CancelDispatch, GetAssignment, AcceptOffer | **aucun** — `AddDispatchGrpcClient` (`shared/contracts/HBA.Dispatch.Contracts.Grpc/DispatchGrpcRegistration.cs:11`) n'est appelé nulle part | 4 RPC morts |
| DriverApi | `services/delivery/driver-service/src/HBA.Delivery.Driver.Api/GrpcServices/DriversGrpcService.cs:20,27,41,68` | GetDriver, GetDriversBatch, CheckDriverEligibility, SetBusyState | **aucun** — `DriversGrpcRegistration.cs:11` jamais appelé | 4 RPC morts |
| ProofApi | `services/delivery/proof-of-delivery-service/src/HBA.Delivery.Proof.Api/GrpcServices/ProofGrpcService.cs:14,30` | HasValidDropoffProof, GetProofSummary | **aucun** | 2 RPC morts |
| RouteApi | `services/delivery/route-service/src/HBA.Delivery.Route.Api/GrpcServices/RoutesGrpcService.cs:20,37,53` | EstimateRoute, OptimizeRoute, RecalculateEta | **aucun** | 3 RPC morts |
| TrackingApi | `services/delivery/tracking-service/src/HBA.Delivery.Tracking.Api/GrpcServices/TrackingGrpcService.cs:20,34,46` | GetLatestLocation, StartTrackingSession, StopTrackingSession | **aucun** | 3 RPC morts |

Note connexe : ces cinq serveurs sont adossés à des magasins **en mémoire**
(`ConcurrentDictionary`) — `DriverStore.cs:9-11`, `DispatchStore.cs:9-11`,
`RouteStore.cs:9`, `TrackingStore.cs:9-11`, `ProofStore.cs:11-12`. Ils répondent,
mais leur état ne survit pas à un redémarrage. **MEDIUM** (observation
d'architecture, à confirmer si ces services sont des maquettes assumées).

### EngagementApi — `shared/proto/engagement/v1/engagement.proto` — **aucun serveur**

| RPC | Serveur | Clients | Implémenté ? | Défaut |
|---|---|---|---|---|
| GetReview, ListReviewsByProduct, GetProductRating, GetSellerRating, GetProductRecommendations, GetUserRecommendations, GetWishlist (7) | **aucun** | **aucun** | **non** | **MEDIUM** — `shared/contracts/HBA.Engagement.Contracts.Grpc/` ne contient que son `.csproj`. `services/common/review-service/.../Program.cs:31` appelle `AddHbaGrpc()` sans rien publier. |

### FinancialApi — `shared/proto/financial/v1/financial.proto` — serveur : payment-service

| RPC | Serveur | Clients réels | Implémenté ? | Mapping | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| GetPayment | **absent** | aucun | **non** | — | — | — | proto mort |
| GetPaymentByOrder | **absent** | aucun | **non** | — | — | — | proto mort |
| InitiatePayment | **absent** | aucun | **non** | — | — | — | proto mort |
| CapturePayment | **absent** | aucun | **non** | — | — | — | proto mort |
| FailPayment | **absent** | aucun | **non** | — | — | — | proto mort |
| **RefundPayment** | `services/common/payment-service/src/HBA.Financial.Api/GrpcServices/FinancialGrpcService.cs:15` | `services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Infrastructure/Grpc/PaymentClient/PaymentGrpcClient.cs:24` (appelant : `.../Commands/ExecuteRefund/ExecuteRefundCommandHandler.cs:63`) | oui | aucun (`RpcException` non rattrapée dans `PaymentGrpcClient`) | 5 s | rejeu | **OK sur l'idempotence** : clé portée par le message et vérifiée serveur (`PaymentLifecycleCommands.cs:196-203`). **MEDIUM** : 5 s pour un appel PSP synchrone est court. |
| ComputeCommission | **absent** | aucun | **non** | — | — | — | proto mort |
| GetSellerWallet | **absent** | aucun | **non** | — | — | — | proto mort |
| GetDriverWallet | **absent** | aucun | **non** | — | — | — | proto mort |

**8 des 9 RPC financiers déclarés n'ont pas de corps.** `FinancialGrpcService`
n'a qu'une méthode.

### FoodApi — `shared/proto/food/v1/food.proto` — serveur : restaurant-service

| RPC | Serveur | Clients réels | Implémenté ? | Mapping | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| GetRestaurant | `shared/contracts/HBA.Food.Contracts.Grpc/FoodGrpc.cs:18` | `services/food/food-order-service/.../PlaceMealOrderCommand.cs:139`<br>`services/common/wallet-service/src/HBA.Financial.Wallet.Application/Earnings/AccrueEarningsOnOrderConfirmedHandler.cs:275` | oui | aucun | 5 s | aucun | — |
| GetMenu | **absent** | aucun | **non** | — | — | — | proto mort |
| GetMenuItem | `FoodGrpc.cs:114` | `services/food/food-cart-service/src/HBA.Food.Cart.Application/Commands/Carts/FoodCartCommands.cs:66` | oui | aucun | 5 s | aucun | — |
| AcceptFoodOrder | **absent** | aucun | **non** | — | — | — | proto mort |
| RejectFoodOrder | **absent** | aucun | **non** | — | — | — | proto mort |
| MarkFoodOrderReady | **absent** | aucun | **non** | — | — | — | proto mort |
| GetRestaurantByOwner | `FoodGrpc.cs:32` | aucun | oui | — | — | — | RPC mort |
| GetStaffMembership | `FoodGrpc.cs:46` | `services/food/food-order-service/src/HBA.Food.Order.Api/Endpoints/MealOrderEndpoints.cs:120` | oui | aucun | 5 s | aucun | décision d'autorisation portée par `user_id` du message — §10.3 |
| GetFoodOrder | `FoodGrpc.cs:81` | `services/marketplace/order-service/src/HBA.Order.Api/Integration/OrderDeliveryArbitrationHandlers.cs:192` | oui | aucun | 5 s | rejeu Kafka | — |

### FoodCartApi / FoodOrderApi

| RPC | Serveur | Clients réels | Implémenté ? | Mapping | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| FoodCartApi.GetActiveCart | `shared/contracts/HBA.FoodCarts.Contracts.Grpc/FoodCartsGrpc.cs:30` | `services/food/food-order-service/.../PlaceMealOrderCommand.cs:95` | oui | aucun | 5 s | aucun | — |
| FoodCartApi.GetCart | `FoodCartsGrpc.cs:42` | aucun | oui | — | — | — | RPC mort |
| FoodOrderApi.GetOrder | `shared/contracts/HBA.FoodOrders.Contracts.Grpc/FoodOrdersGrpc.cs:19` | aucun | oui | — | — | — | RPC mort |
| FoodOrderApi.HasPlacedOrder | `FoodOrdersGrpc.cs:84` | `services/food/food-cart-service/src/HBA.Food.Cart.Application/Queries/Carts/FoodCartQueries.cs:63` et `:101` | oui | aucun | 5 s | aucun | — |

### IdentityApi — serveur : identity-service

| RPC | Serveur | Clients réels | Implémenté ? | Mapping | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| GetUser | `shared/contracts/HBA.Identity.Contracts.Grpc/IdentityGrpc.cs:84` | `services/marketplace/seller-service/.../RegisterSeller/RegisterSellerCommandHandler.cs:38`<br>`.../Members/MemberCommands.cs:398`<br>`services/common/notification-service/.../SellerActivatedNotificationHandler.cs:55`<br>`.../NotificationDispatcher.cs:92`<br>`services/common/user-service/src/HBA.Users.Api/Integration/CreateUserProfileOnUserRegisteredHandler.cs:115` | oui | aucun | 5 s | aucun | — |
| GetUserByEmail | `IdentityGrpc.cs:98` | `services/marketplace/seller-service/.../MemberCommands.cs:193`<br>`services/common/notification-service/.../AdminNotificationTarget.cs:80` | oui | aucun | 5 s | aucun | — |
| **ValidateAccessToken** | `IdentityGrpc.cs:108` | **aucun** | oui | — | — | — | **HIGH** — §2b : le RPC dont le proto dit qu'il sert « les appels sensibles (paiement, retrait, action d'administration) » (`identity.proto:14-28`) n'est appelé par personne. Un compte suspendu reste utilisable jusqu'à expiration du JWT. |
| GetUserRoles | `IdentityGrpc.cs:133` | aucun | oui | — | — | — | RPC mort |
| RevokeUserSessions | `IdentityGrpc.cs:155` | aucun | oui | — | — | — | RPC mort |

### InventoryApi — serveur : inventory-service

| RPC | Serveur | Clients réels | Implémenté ? | Mapping | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| GetInventoryItem | **absent** | aucun | **non** | — | — | — | proto mort |
| ListInventoryBySku | **absent** | aucun | **non** | — | — | — | proto mort |
| GetAvailability | `shared/contracts/HBA.Inventory.Contracts.Grpc/InventoryGrpc.cs:18` | `services/marketplace/cart-service/.../AddItemToCartCommandHandler.cs:132` (via `IsInStockAsync`, `InventoryGrpc.cs:183`) | oui | aucun | 5 s | aucun | **HIGH** — §3.3 : `location_id` du proto ignoré des deux côtés |
| **ReserveStock** | `InventoryGrpc.cs:30` | `services/marketplace/order-service/.../PlaceOrderCommandHandler.cs:279` | oui | aucun | 5 s | aucun (transport) | **CRITICAL** — **non idempotent**, §7.3 |
| ReleaseReservation | `InventoryGrpc.cs:45` | `PlaceOrderCommandHandler.cs:291`<br>`services/marketplace/order-service/.../OrderLifecycleCommands.cs:320` | oui | aucun | 5 s | aucun | idempotent (`InventoryItem.cs:165` `RemoveAll`) |
| ConfirmReservation | `InventoryGrpc.cs:53` | `OrderLifecycleCommands.cs:225` | oui | aucun | 5 s | aucun | à confirmer (décrément physique) |
| GetLocation | `InventoryGrpc.cs:62` | `services/marketplace/seller-service/src/HBA.Merchants.Application/Stores/StoreCommands.cs:193`<br>`services/marketplace/order-service/.../CreateDeliveryOnOrderConfirmedHandler.cs:187`<br>`services/food/restaurant-service/.../FoodOrderBridgeHandlers.cs:257`<br>`services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Endpoints/FoodEndpoints.cs:603` | oui | aucun | 5 s | aucun | — |

### MediaApi — serveur : media-service

| RPC | Serveur | Clients réels | Implémenté ? | Mapping | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| Get | `shared/contracts/HBA.Media.Contracts.Grpc/MediaGrpcService.cs:23` | `services/marketplace/catalog-service/.../AddProductMedia/AddProductMediaCommandHandler.cs:67`<br>`services/marketplace/seller-service/.../AddKybDocument/AddKybDocumentCommandHandler.cs:82` | oui | aucun | 5 s | aucun | — |
| GetMany | `MediaGrpcService.cs:42` | aucun | oui | — | — | — | RPC mort |
| ListByOwner | `MediaGrpcService.cs:63` | aucun appelant distant | oui | — | — | — | RPC mort |
| CreateSignedUrl | `MediaGrpcService.cs:77` | aucun appelant distant | oui | — | — | — | RPC mort |

### MerchantApi — serveur : seller-service

| RPC | Serveur | Clients réels | Implémenté ? | Mapping | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| GetSeller | `shared/contracts/HBA.Merchants.Contracts.Grpc/MerchantsGrpc.cs:25` | `services/food/restaurant-service/.../FoodEndpoints.cs:537`<br>`services/common/notification-service/.../SellerOrderNotificationHandler.cs:90`, `.../RefundNotificationHandlers.cs:79`, `.../SellerActivatedNotificationHandler.cs:41`, `.../SellerLifecycleNotificationHandlers.cs:222` et `:295`, `.../MemberNotificationHandlers.cs:71`, `.../PayoutNotificationHandler.cs:39` | oui | aucun | 5 s | aucun | — |
| GetSellerByUser | `MerchantsGrpc.cs:38` | aucun | oui | — | — | — | RPC mort |
| GetStore | `MerchantsGrpc.cs:52` | `services/marketplace/catalog-service/src/HBA.Catalog.Api/Endpoints/CatalogEndpoints.cs:719`<br>`services/common/notification-service/.../MemberNotificationHandlers.cs:78` | oui | aucun | 5 s | aucun | **MEDIUM** — §3.4 : `LogoUrl`/`Description`/`OpeningHours`/`CreatedOnUtc` forcés à `null`/`[]`/`MinValue` par `MerchantsGrpc.cs:453-470` |
| ListSellerStores | `MerchantsGrpc.cs:65` | aucun | oui | — | — | — | RPC mort |
| ValidateSeller | `MerchantsGrpc.cs:79` | aucun | oui | — | — | — | RPC mort |
| **GetSellerPayout** | `MerchantsGrpc.cs:110` | `services/common/wallet-service/src/HBA.Financial.Wallet.Application/Wallets/WalletQueries.cs:169`<br>`.../WalletCommands.cs:101` et `:236` | oui | aucun | 5 s | aucun | **HIGH** — §10.2 : rend un numéro Mobile Money à quiconque détient la clé partagée |
| GetMemberAccess | `MerchantsGrpc.cs:158` | `CatalogEndpoints.cs:373`<br>`services/marketplace/inventory-service/src/HBA.Inventory.Api/Endpoints/InventoryEndpoints.cs:210`<br>`services/marketplace/order-service/src/HBA.Order.Api/Endpoints/OrderEndpoints.cs:287`<br>`services/common/payment-service/.../FinancialEndpoints.cs:703` | oui | aucun | 5 s | aucun | **HIGH** — chemin d'autorisation le plus chaud, sans disjoncteur : une panne de seller-service ferme l'espace vendeur de 4 services (§8) |
| CheckMerchantCapability | `MerchantsGrpc.cs:207` | `services/common/review-service/src/HBA.Engagement.Reviews.Application/Reviews/Commands/ReplyToReviewCommand.cs:84` | oui | aucun | 5 s | aucun | `user_id` porté par le message — §10.3 |

### OrderApi — serveur : order-service

| RPC | Serveur | Clients réels | Implémenté ? | Mapping | Deadline | Retry | Défaut |
|---|---|---|---|---|---|---|---|
| GetOrder | `shared/contracts/HBA.Order.Contracts.Grpc/OrderingGrpc.cs:22` | `services/marketplace/order-service/.../CreateDeliveryOnOrderConfirmedHandler.cs:115`<br>`services/food/restaurant-service/.../FoodOrderBridgeHandlers.cs:121` et `:244`<br>`services/common/notification-service/.../ShipmentNotificationHandlers.cs:24` et `:51`, `.../SellerLifecycleNotificationHandlers.cs:335`, `.../DeliveryTrackingNotificationHandlers.cs:66`, `.../FoodOrderNotificationHandlers.cs:52`<br>`services/common/review-service/.../SubmitReview/SubmitReviewCommandHandler.cs:32`<br>`services/common/wallet-service/.../ReverseEarningsOnOrderCancelledHandler.cs:138`, `.../AccrueEarningsOnOrderConfirmedHandler.cs:110` | oui | aucun | 5 s | rejeu Kafka | — |
| ListOrdersByBuyer | `OrderingGrpc.cs:36` | `services/marketplace/cart-service/src/HBA.Commerce.Application/Carts/CartPricer.cs:41` (via `HasPlacedOrderAsync`) | **oui, mais factice** | aucun | 5 s | aucun | **HIGH** — §3.1 : le serveur fabrique **une** `OrderSummary` synthétique (`BuyerId` + `Status = "Placed"`), il ne liste pas les commandes |
| **ListOrdersBySeller** | **absent** | `services/marketplace/seller-service/src/HBA.Merchants.Infrastructure/Integration/SellerSalesCountHandler.cs:101` (via `OrderingGrpc.cs:226`) | **non** | **aucun** | 5 s | rejeu Kafka | **CRITICAL** — §4.2 |
| ConfirmPayment | **absent** | aucun | **non** | — | — | — | proto mort |
| CancelOrder | **absent** | aucun | **non** | — | — | — | proto mort |
| MarkDelivered | **absent** | aucun | **non** | — | — | — | proto mort |
| ValidateCompletedOrder | **absent** | aucun | **non** | — | — | — | proto mort |
| GetOrderReturnContext | `OrderingGrpc.cs:53` | `services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Infrastructure/Grpc/OrderClient/OrderGrpcClient.cs:19` (appelants : `.../CreateReturn/CreateReturnCommand.cs:48`, `.../ExecuteRefund/ExecuteRefundCommandHandler.cs:51`) | oui | **`catch (Exception)` → `DependencyUnavailable`** (`OrderGrpcClient.cs:30-35`) | 5 s | rejeu | **MEDIUM** — §9.4 : le fourre-tout masque un `UNIMPLEMENTED` ou une erreur de mapping en « service indisponible » |

### PromotionApi — serveur : promotion-service — **zéro client**

| RPC | Serveur | Clients | Implémenté ? | Défaut |
|---|---|---|---|---|
| EvaluatePromotion | `shared/contracts/HBA.Promotions.Contracts.Grpc/PromotionGrpcService.cs:37` | **aucun** | oui | RPC mort |
| ReserveCoupon | `PromotionGrpcService.cs:46` | **aucun** | oui | RPC mort |
| CommitCoupon | `PromotionGrpcService.cs:82` | **aucun** | oui | RPC mort |
| ReleaseCoupon | `PromotionGrpcService.cs:104` | **aucun** | oui | RPC mort |

`AddPromotionGrpcClient` (`shared/contracts/HBA.Promotions.Contracts.Grpc/PromotionGrpcRegistration.cs:29`)
n'est appelé **nulle part**. `IPromotionModuleApi` n'est enregistré que dans
`services/common/promotion-service/src/HBA.Promotions.Infrastructure/PromotionsModuleInstaller.cs:48`,
en processus. Le commentaire de `PromotionGrpcClient.cs:131-134` affirme que
« cart-service, order-service et food-order-service » l'utilisent : **c'est faux**,
aucun des trois ne référence `IPromotionModuleApi`. **HIGH** — les coupons ne sont
évalués sur aucun parcours d'achat (à instruire dans l'audit métier).

### UsersApi — serveur : user-service — **zéro client**

| RPC | Serveur | Clients | Implémenté ? | Défaut |
|---|---|---|---|---|
| GetProfile | `shared/contracts/HBA.Users.Contracts.Grpc/UsersGrpc.cs:49` | **aucun** | oui | RPC mort |
| GetProfiles | `UsersGrpc.cs:64` | **aucun** | oui | RPC mort |

`AddUsersGrpcClient` (`UsersGrpc.cs:132`) n'est appelé nulle part, alors que
`SERVICES__USER` est distribué à 5 services dans `docker-compose.dev.yml`.

### ReturnRefundGrpc — `services/marketplace/return-refund-service/contracts/grpc/return_refund.proto` — **hors chaîne de compilation**

| RPC | Serveur | Clients | Implémenté ? | Défaut |
|---|---|---|---|---|
| GetReturn, GetOrderReturnSummary, ValidateRefundStatus (3) | **aucun** | **aucun** | **non** | **MEDIUM** — le seul artefact C# associé est `services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Api/GrpcServices/ReturnRefundGrpcService.cs`, une classe de **6 lignes** qui ne contient qu'une constante `public const string Contract = "ReturnRefundGrpc";` et n'hérite d'aucune base gRPC. Le nom de fichier et le nom de classe laissent croire à un serveur. `Program.cs:9` appelle `AddHbaGrpc()` sans publier de service. |

---

## 3. Divergences contrat ↔ implémentation

Il n'existe **aucune divergence de schéma** au sens strict (une seule définition
compilée par service, partagée par le client et le serveur ; aucun numéro de champ
dupliqué ni réutilisé ; aucun `reserved` ; les `optional` sont cohérents et lus via
`Has*`). Les divergences sont **sémantiques** : le message transporte un champ que
personne ne remplit ou que personne ne lit.

### 3.1 `OrderApi.ListOrdersByBuyer` — le serveur ne liste rien — **HIGH**
`shared/proto/order/v1/order.proto:9` déclare `returns (ListOrdersResponse)` =
`repeated OrderSummary orders`. L'implémentation
`shared/contracts/HBA.Order.Contracts.Grpc/OrderingGrpc.cs:36-51` appelle
`_orders.HasPlacedOrderAsync(...)` et, si vrai, ajoute **une seule** `OrderSummary`
dont seuls `BuyerId` et `Status = "Placed"` sont renseignés — tous les autres
champs (montants, lignes, adresse, devise) sont vides.
Le client `OrderingGrpc.cs:215-222` ne lit que `.Count > 0`, donc le contrat tient
par accident. Tout futur appelant qui lira `response.Orders[0].GrandTotal` lira `0`.

### 3.2 `OrderApi.ListOrdersBySeller` — le filtre de statut vit côté client — **MEDIUM**
`OrderingGrpc.cs:244-248` et `:422-426` recopient à l'identique un filtre
`Confirmed|Delivered` sur des chaînes libres, deux fois, dans deux classes
(`OrderingGrpcClient` et `OrdersGrpcClient`). Le serveur, lui, n'implémente pas le
RPC (§4.2) : ce filtre ne s'exécutera jamais. Duplication d'une règle métier dans
le transport, sur un RPC mort.

### 3.3 `InventoryApi.GetAvailability` — trois champs sur cinq jamais remplis — **HIGH**
- `shared/proto/inventory/v1/inventory.proto:74` déclare
  `optional string location_id = 2` dans la requête. Le client ne l'envoie jamais
  (`InventoryGrpc.cs:131-133`) et le serveur ne le lit jamais
  (`InventoryGrpc.cs:18-28`).
- Conséquence : `InventoryModuleApi.GetAvailabilityAsync`
  (`services/marketplace/inventory-service/src/HBA.Inventory.Infrastructure/Public/InventoryModuleApi.cs:55-70`)
  somme le stock de **toutes** les localisations, alors que `ReserveStock` réserve
  **à une localisation précise** (`InventoryGrpc.cs:30-43`). Le panier peut donc
  déclarer disponible un article dont le stock est ailleurs, et la réservation
  échouera au checkout.
- `on_hand = 2` et `reserved = 3` (`inventory.proto:79-80`) ne sont **jamais**
  renseignés par le serveur : ils valent 0 pour tout appelant.

### 3.4 `MerchantApi.GetStore` — quatre champs fabriqués côté client — **MEDIUM**
`MerchantsGrpc.cs:453-470` construit une `StoreSummary` avec
`LogoUrl: null, Description: null, StatusReason: null, OpeningHours: [], CreatedOnUtc: DateTime.MinValue`.
Le proto ne transporte pas ces champs ; le contrat C# les déclare. Même interface,
deux sémantiques selon que l'appelant vit dans seller-service ou ailleurs — le
défaut que le commentaire de `MerchantsGrpc.cs:409-440` documente pour
`SellerSummary` et qui subsiste ici pour `StoreSummary`.

### 3.5 `FoodApi.GetRestaurant` — quatorze champs neutralisés — **MEDIUM**
`FoodGrpc.cs:341-364` : `AcceptsOrdersNow`, `PreparationMinutes: 0`,
`AcceptanceMode: "Manual"`, `MinimumOrderAmount: null`, `LoadLevel: "Normal"`,
`FulfillmentLocationId: null`, `PayoutSellerId: null`, `ServiceHours: []` sont
inventés à partir d'un `Status` textuel. `AccrueEarningsOnOrderConfirmedHandler.cs:275`
lit ce restaurant par gRPC pour calculer des gains : `PayoutSellerId: null` y est
une donnée financière fausse. **À confirmer** dans l'audit financier — je n'ai pas
suivi l'usage exact du champ.

### 3.6 Contrats déclarés « gRPC » qui ne parlent à personne — **HIGH**
`services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Infrastructure/ReturnRefundModuleInstaller.cs:49-53`
enregistre cinq `I*GrpcClient`. Deux seulement sont de vrais clients gRPC
(`OrderGrpcClient`, `PaymentGrpcClient`). Les trois autres sont des **bouchons
silencieux** portant « GrpcClient » dans leur nom :

| Classe | Fichier:ligne | Comportement réel |
|---|---|---|
| `InventoryGrpcClient` | `.../Grpc/InventoryClient/InventoryGrpcClient.cs:9-10` | `ProcessReturnedStockAsync` → `Result.Success()` **sans aucun appel**. Le stock retourné n'est jamais réintégré. |
| `DeliveryGrpcClient` | `.../Grpc/DeliveryClient/DeliveryGrpcClient.cs:8-9` | `CreateReturnDeliveryAsync` → fabrique `$"RET-DELIVERY-{returnId:N}"`. Aucune course de retour n'est créée ; la référence rendue est fictive. |
| `MediaGrpcClient` | `.../Grpc/MediaClient/MediaGrpcClient.cs:8-11` | `ValidateMediaAsync` vérifie seulement que la chaîne n'est pas vide. N'importe quel identifiant est accepté comme preuve photo. |

Aucun de ces trois n'a de client gRPC enregistré (`AddInventoryGrpcClient`,
`AddDeliveryGrpcClient`, `AddMediaGrpcClient` ne sont pas appelés par
`ReturnRefundModuleInstaller`) — donc rien ne casserait si on les branchait, mais
rien ne signale non plus qu'ils ne sont pas branchés.

---

## 4. RPC déclarés sans corps, ou rendant `UNIMPLEMENTED`

Les 40 RPC sans corps sont listés dans la matrice. Trois ont un **client** :

### 4.1 `DeliveryApi.LookupQuote` — **CRITICAL**
- **Proto** : `shared/proto/delivery/v1/delivery.proto:43`.
- **Serveur** : `services/delivery/delivery-service/src/HBA.Delivery.Core.Api/GrpcServices/DeliveryGrpcService.cs` n'expose que
  `CreateDelivery`(:70), `CancelDelivery`(:150), `GetDelivery`(:188),
  `GetDeliveryByReference`(:199), `GetTracking`(:204), `ResolveDriver`(:248).
  **Aucun `override LookupQuote` dans le dépôt** (recherche exhaustive sur
  `services`, `shared`, `apps`).
- **Clients** :
  - `services/marketplace/order-service/src/HBA.Order.Application/Orders/Commands/PlaceOrder/PlaceOrderCommandHandler.cs:371`
  - `services/food/food-order-service/src/HBA.Food.Order.Application/Commands/Orders/PlaceMealOrderCommand.cs:302`
  via `shared/contracts/HBA.Deliveries.Contracts.Grpc/DeliveryGrpc.cs:167`.
- **Effet** : `Grpc.Core.RpcException(StatusCode.Unimplemented)`, non rattrapée
  (aucun `catch (RpcException)` sur ce chemin). Le handler remonte l'exception :
  toute commande **marchandise portant un `DeliveryQuoteId`** échoue en 500, et
  **aucune commande de repas ne peut jamais être passée** — pour un repas le devis
  est obligatoire (`PlaceOrderCommandHandler.cs:355-360` : « Les frais de livraison
  d'un repas doivent être chiffrés par un devis avant le paiement »).
- Il n'existe **aucune** implémentation en processus de `IDeliveryDispatchApi`
  (`services/delivery/delivery-service/src/HBA.Deliveries.Contracts/DeliveryDispatchContracts.cs:231`) :
  le seul implémenteur du dépôt est `DeliveryGrpcClient`.

### 4.2 `OrderApi.ListOrdersBySeller` — **CRITICAL**
- **Proto** : `shared/proto/order/v1/order.proto:10`.
- **Serveur** : absent de `shared/contracts/HBA.Order.Contracts.Grpc/OrderingGrpc.cs`
  (3 overrides seulement : `:22`, `:36`, `:53`).
- **Client** : `shared/contracts/HBA.Order.Contracts.Grpc/OrderingGrpc.cs:226`
  (`GetSellerSalesCountAsync`), appelé par
  `services/marketplace/seller-service/src/HBA.Merchants.Infrastructure/Integration/SellerSalesCountHandler.cs:101`.
- **Effet** : `UNIMPLEMENTED` non rattrapé dans un handler d'intégration Kafka. Le
  handler lève, le message est rejoué, la reprise s'épuise, le compteur de ventes
  d'un vendeur ne se met **jamais** à jour et un flux Kafka part en lettre morte.

### 4.3 `CatalogApi.GetProducts` — **HIGH (latent)**
- **Proto** : `shared/proto/catalog/v1/catalog.proto:9`.
- **Serveur** : absent de `ProductsGrpc.cs` (le fichier implémente `GetProduct` au
  singulier, `:143`, mais pas `GetProducts`).
- **Client** : `ProductsGrpc.cs:265` (`ProductsGrpcClient.GetProductsAsync`).
  Aucun appelant applicatif aujourd'hui — le premier qui l'utilisera récoltera un
  `UNIMPLEMENTED`, et non un dictionnaire vide.

**Aucun serveur ne renvoie explicitement `StatusCode.Unimplemented`** : les 40 cas
sont des méthodes non surchargées, donc l'échec est purement d'exécution. Rien —
ni compilation, ni test, ni démarrage — ne le signale.

---

## 5. Absence de deadline / timeout — liste exhaustive

**Aucun appel gRPC du dépôt n'est sans échéance.**

L'échéance est posée **une seule fois**, dans
`shared/common/HBA.Shared.Hosting/Grpc/InternalCallInterceptors.cs:72-75` :
```
if (options.Deadline is null) { options = options.WithDeadline(DateTime.UtcNow.AddSeconds(5)); }
```
et les **20** enregistrements de clients portent tous
`.AddInterceptor<InternalCallClientInterceptor>()` (vérifié un par un :
Catalog `ProductsGrpc.cs:427`, Commerce `CommerceGrpc.cs:249`, Deliveries
`DeliveryGrpc.cs:364`, DeliveryPricing `:19`, Dispatch `:19`, Drivers `:19`,
Financial `:23`, Food `FoodGrpc.cs:399`, FoodCarts `:205`, FoodOrders `:193`,
Identity `IdentityGrpc.cs:244`, Inventory `InventoryGrpc.cs:250`, Media `:37`,
Merchants `MerchantsGrpc.cs:566`, Order `OrderingGrpc.cs:581`, Promotions `:43`,
ProofOfDelivery `:19`, Routes `:19`, Tracking `:19`, Users `UsersGrpc.cs:144`).

Aucun `GrpcChannel.ForAddress` manuel, aucun `CallOptions` construit à la main,
aucun `deadline:` explicite ailleurs.

**Réserves à porter au rapport, malgré le zéro :**

- **MEDIUM** — La protection est **conventionnelle, pas structurelle**. Un futur
  `services.AddGrpcClient<X>()` sans `.AddInterceptor<...>()` n'aurait aucune
  échéance et **rien ne le signalerait**. Il n'existe aucun test ni script qui
  vérifie que tout `AddGrpcClient` porte l'intercepteur.
- **MEDIUM** — 5 s uniformes pour tout : une lecture de cache d'autorisation
  (`GetMemberAccess`) et un appel PSP synchrone (`RefundPayment`,
  `FinancialGrpcService.cs:15` → `RefundPaymentCommandHandler` → gateway HTTP vers
  MTN/Moov/Stripe) partagent le même budget. Le second dépassera régulièrement,
  et le dépassement d'échéance sur un remboursement est **ambigu** : le PSP a pu
  être débité. L'idempotence sauve ce cas précis (`PaymentLifecycleCommands.cs:196-203`),
  pas les autres.
- **MEDIUM** — Aucune échéance côté **serveur** : `AddHbaGrpc`
  (`GrpcHostExtensions.cs:62-74`) ne configure ni `MaxConcurrentCalls` ni
  d'abandon sur dépassement. Le serveur continue de travailler après que le
  client a abandonné, sauf si le handler observe `context.CancellationToken` —
  ce qu'il fait partout (§6), donc l'effet est limité.

---

## 6. Absence de `CancellationToken` propagé

**Aucun défaut.** Vérification mécanique :

- Côté client : les **63** sites d'appel `_client.<Rpc>Async(...)` de
  `shared/contracts/**` et `services/**` passent tous `cancellationToken:`
  (analyse par expression régulière sur le corps de chaque appel — zéro
  exception).
- Côté serveur : chacune des méthodes `public override Task<...>` qui `await`
  une dépendance transmet `context.CancellationToken` (zéro exception).
  Exemples : `OrderingGrpc.cs:29`, `MerchantsGrpc.cs:32`, `FoodGrpc.cs:26`,
  `InventoryGrpc.cs:21`, `DeliveryGrpcService.cs:99`, `DeliveryPricingGrpcService.cs:36`,
  `DriversGrpcService.cs:77`.
- Les serveurs qui n'attendent rien (`DriversGrpcService.cs:20`, `:27`, `:41`,
  `ProofGrpcService.cs:14`, `:30`, `TrackingGrpcService.cs:20`) rendent
  `Task.FromResult` sur un magasin en mémoire : l'absence de jeton y est sans
  effet.

---

## 7. Retry sur des opérations non idempotentes

**Aucun réessai au niveau du transport** : pas d'`EnableRetries`, pas de
`ServiceConfig`/`MethodConfig`/`RetryPolicy` gRPC dans le dépôt (recherche
exhaustive). Le seul `RetryPolicy` gRPC-adjacent,
`services/delivery/dispatch-service/src/HBA.Delivery.Dispatch.Domain/Policies/RetryPolicy.cs`,
est une politique métier de dispatch, pas de transport.

Les réessais dangereux sont donc **applicatifs**, posés au-dessus d'un RPC qui
n'est pas rejouable.

### 7.1 `ConsumeQuote` rejoué après un `SaveChanges` non atteint — **HIGH**
`services/delivery/delivery-service/src/HBA.Delivery.Core.Application/Commands/CreateDelivery/CreateDeliveryCommand.cs:202`
consomme le devis (**mutation destructive** : `IPricingStore.ConsumeQuoteAsync`,
serveur `DeliveryPricingGrpcService.cs:41`) **avant**
`_repository.AddAsync` + `_unitOfWork.SaveChangesAsync` (`:217-218`).
La garde d'idempotence de la commande est la lecture
`GetByReferenceAsync(command.Reference, command.Source)` (`:98`) — elle ne voit
que ce qui a été **écrit**. Séquence :
1. `ConsumeQuote` réussit côté delivery-pricing (devis marqué consommé) ;
2. `SaveChangesAsync` échoue, ou le client dépasse ses 5 s alors que le serveur a
   commité, ou le handler Kafka lève plus haut ;
3. rejeu → aucune course en base → `ConsumeQuote` rappelé → « devis déjà utilisé »
   → `pricing.quote_not_usable` (`:209-211`) → **la course n'est jamais créée**,
   pour une commande payée.

Le seul rattrapage d'erreur du dépôt, `GrpcDeliveryPricingQuoteValidator.cs:45`,
ne couvre que `Unavailable` et `DeadlineExceeded` — c'est-à-dire précisément les
cas où le devis **a pu** être consommé côté serveur — et les traduit en
`DependencyUnavailable`, ce qui déclenche le rejeu.

### 7.2 `CreateDelivery` rejoué, avec un second essai à un prix différent — **HIGH**
`services/marketplace/order-service/src/HBA.Order.Api/Integration/CreateDeliveryOnOrderConfirmedHandler.cs:274`
puis `:311` : sur refus, un **second `CreateDelivery`** est émis avec
`QuoteId = null`, donc **au tarif du moment** et non au tarif payé par l'acheteur.
Le commentaire `:283-305` assume l'écart. Puis `:334` lève, ce qui provoque le
rejeu Kafka de tout le handler — donc potentiellement un troisième et un quatrième
`CreateDelivery`. Même schéma dans
`services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Integration/FoodOrderBridgeHandlers.cs:319` et `:342`.
L'idempotence tient par `(reference, source)` côté serveur
(`CreateDeliveryCommand.cs:98`), ce qui **évite le doublon de course** — mais pas
l'écart de prix, ni la consommation de devis de §7.1.

### 7.3 `ReserveStock` n'est pas idempotent — **CRITICAL**
`services/marketplace/inventory-service/src/HBA.Inventory.Domain/Stock/InventoryItem.cs:132-160` :
```
_reservations.Add(new StockReservation(Guid.NewGuid(), orderId, quantity, expiresAtUtc));
```
**Aucune vérification d'une réservation existante pour le même `orderId`.** Deux
appels `ReserveStock(sku, location, order, qty)` créent **deux** réservations et
immobilisent `2 × qty`. Or :
- l'appelant `PlaceOrderCommandHandler.cs:279` s'exécute derrière une échéance de
  5 s : un `DEADLINE_EXCEEDED` alors que le serveur a commité est indiscernable
  d'un échec ;
- `ReserveStockRequest` porte bien `order_id` (`inventory.proto:88`) — la clé
  d'idempotence **existe dans le contrat**, elle n'est simplement pas utilisée
  comme telle ;
- la compensation `ReleaseReservation` (`:291`) supprime **toutes** les
  réservations de l'`orderId` (`InventoryItem.cs:165`), ce qui masque le problème
  sur le chemin d'échec mais pas sur le chemin nominal.

Effet : stock fantôme immobilisé, ruptures artificielles, et une commande qui
consomme deux fois son stock si le checkout est rejoué.

### 7.4 Opérations sûres, pour mémoire
- `RefundPayment` : clé d'idempotence transportée
  (`financial.proto` `idempotency_key`, `PaymentGrpcClient.cs:33`) et vérifiée
  serveur (`PaymentLifecycleCommands.cs:196-203`, `:169-172`). **Correct.**
- `ReleaseReservation` : `RemoveAll` — idempotent.
- `CancelDelivery` : le domaine refuse une seconde annulation ; réponse en bande.

---

## 8. Absence de disjoncteur

**Aucun client gRPC du dépôt n'a de disjoncteur.** Les seules occurrences de
`CircuitBreaker` sont dans la passerelle HTTP :
`apps/api-gateway/src/HBA.Gateway.Infrastructure/Resilience/HbaResilience.cs:39-46`
et `.../Configuration/OutboundOptions.cs:33,37`.

Le maillage interne est donc **moins protégé que le trafic nord-sud** :

| | Timeout | Retry | Disjoncteur |
|---|---|---|---|
| Passerelle → services (HTTP) | oui (`AddTimeout`, total + par tentative) | oui, **GET/HEAD seulement** (`HbaResilience.cs:75-85`) | **oui** |
| Service → service (gRPC) | 5 s | non | **non** |

Conséquences les plus lourdes, par volume d'appels :
- `MerchantApi.GetMemberAccess` (`MerchantsGrpc.cs:158`) est appelé sur **chaque
  requête vendeur** de catalog, inventory, order et payment
  (`CatalogEndpoints.cs:373`, `InventoryEndpoints.cs:210`, `OrderEndpoints.cs:287`,
  `FinancialEndpoints.cs:703`). Sans disjoncteur, une lenteur de seller-service
  fait attendre 5 s **chaque** requête vendeur des quatre services, jusqu'à
  saturation de leurs pools de threads. C'est exactement la propagation que
  l'échéance de 5 s prétend éviter (`InternalCallInterceptors.cs:56-71`) : elle
  borne une requête, elle ne coupe pas la source.
- `CommerceApi.GetActiveCart` (`PlaceOrderCommandHandler.cs:63`) et
  `InventoryApi.ReserveStock` (`:279`) sont en série sur le checkout : 5 s + 5 s
  par ligne de commande, sans coupure.

Les commentaires du code annoncent pourtant un disjoncteur comme acquis —
`PromotionGrpcClient.cs:146` (« La politique de résilience — délai, reprise,
disjoncteur — se pose à l'enregistrement du client »),
`MediaGrpcClient.cs:124`, `promotion.proto:48-54` (« ouvrirait le DISJONCTEUR de
l'appelant »). **Il n'y en a aucun.** — **HIGH**

---

## 9. Codes de statut gRPC mal utilisés

Recherche exhaustive de `StatusCode.*` dans `services`, `shared`, `apps` : **cinq**
occurrences seulement, dont deux dans l'intercepteur. Aucun serveur du dépôt
n'émet jamais `NotFound`, `FailedPrecondition`, `AlreadyExists`,
`PermissionDenied`, `ResourceExhausted` ou `Aborted` pour une raison métier.

### 9.1 Tout ce qui n'est pas un GUID malformé passe « en bande » — **MEDIUM**
Le parti pris est explicite et cohérent (`CommerceGrpc.cs:65-70`,
`identity.proto` / `IdentityGrpc.cs:124-129`, `promotion.proto:48-54`,
`DeliveryGrpcService.cs:101-106`) : absence → `found = false`, refus métier →
`succeeded = false` + `reason`. C'est défendable et bien documenté.
Le coût, lui, n'est pas documenté :
- `CreateDelivery` (`DeliveryGrpcService.cs:112`) et `RefundPayment`
  (`FinancialGrpcService.cs:47`) renvoient `$"{Error.Code} — {Error.Message}"` et
  `$"{Error.Code}:{Error.Message}"` — **deux séparateurs différents** pour la même
  idée. L'appelant `DeliveryGrpc.cs:240` et `PaymentGrpcClient.cs:39-43` ne
  reparsent ni l'un ni l'autre : le code d'erreur normalisé (§25 du contexte) est
  perdu, seul un texte libre survit.
- Un refus métier et une panne deviennent indiscernables dès qu'un appelant se
  contente de `if (!succeeded)`. C'est le cas de
  `CreateDeliveryOnOrderConfirmedHandler.cs:276` et `:317`.

### 9.2 `Unavailable` pour une erreur de configuration — **MEDIUM**
`shared/common/HBA.Shared.Hosting/Grpc/InternalCallInterceptors.cs:119-120` :
clé interne absente → `StatusCode.Unavailable, "Internal API not configured."`.
`Unavailable` est le code « réessaie plus tard » ; ici l'erreur est **permanente**
jusqu'à redéploiement. Un appelant doté d'un retry (il n'y en a pas aujourd'hui,
mais §8 en réclame un) martèlerait indéfiniment. `FailedPrecondition` ou
`Internal` seraient corrects.

### 9.3 `NotFound` pour un refus d'authentification — **MEDIUM**
`InternalCallInterceptors.cs:127` : clé absente ou fausse → `StatusCode.NotFound`.
Le choix est argumenté (`:93-97` : ne pas confirmer l'existence de l'API).
Le prix : `NotFound` est **aussi** le code naturel d'une ressource absente, et le
seul appelant qui filtre les statuts, `GrpcDeliveryPricingQuoteValidator.cs:45`,
ne rattrape que `Unavailable`/`DeadlineExceeded`. Une clé mal déployée y produit
donc une `RpcException(NotFound)` **non rattrapée**, qui remonte brute dans
`CreateDeliveryCommand` — un incident d'authentification déguisé en défaut de
domaine, sans trace exploitable. `Unauthenticated` avec un message neutre aurait
le même effet dissuasif sans cette collision.

### 9.4 `catch (Exception)` qui écrase tout — **MEDIUM**
`services/marketplace/return-refund-service/.../Grpc/OrderClient/OrderGrpcClient.cs:30-35`
convertit **toute** exception en `DependencyUnavailable`. Un `UNIMPLEMENTED`, un
`InvalidArgument` (GUID malformé, `OrderingGrpc.cs:59`) ou une
`NullReferenceException` de mapping deviennent « Le service Order est
indisponible ». Le `catch (OperationCanceledException)` de `:24` est par ailleurs
quasi mort : un dépassement d'échéance gRPC lève `RpcException(DeadlineExceeded)`,
pas `OperationCanceledException`.

### 9.5 Exceptions nues côté serveur — **MEDIUM**
Aucun serveur ne pose de filtre d'exception. Toute exception non gérée dans un
handler (accès base, mapping) est convertie par `Grpc.AspNetCore` en
`StatusCode.Unknown` avec le message générique « Exception was thrown by handler »
— `EnableDetailedErrors = false` (`GrpcHostExtensions.cs:73`, choix délibéré et
correct pour la confidentialité). Résultat : côté appelant, une panne de base de
données de seller-service et un bug de sérialisation sont le **même** `Unknown`
sans détail. Il manque un intercepteur serveur de traduction
`Error → StatusCode` symétrique de l'enveloppe HTTP §25.

---

## 10. Sécurité

### 10.0 L'intercepteur interne est-il branché partout ? — **oui**
`AddHbaGrpc` (`GrpcHostExtensions.cs:62-64`) ajoute
`InternalCallServerInterceptor` **globalement** via `options.Interceptors.Add<>`,
et non route par route : tout service qui appelle `AddHbaGrpc()` protège tous ses
services gRPC. Les **17** appels à `MapInternalGrpcService<T>()` proviennent tous
de `Program.cs` qui appellent `AddHbaGrpc()` :
identity `:11/:26`, media `:10/:21`, payment `:37/:49`, promotion `:20/:59`,
user `:14/:56`, delivery-pricing `:9/:18`, delivery `:10/:19`, dispatch `:9/:17`,
driver `:9/:17`, proof `:9/:17`, route `:9/:17`, tracking `:9/:17`,
food-cart `:20/:31`, food-order `:33/:45`, restaurant `:19/:88`, cart `:16/:28`,
catalog `:41/:47`, inventory `:11/:31`, order `:103/:109`, seller `:14/:55`.
`AllowAnonymous()` (`GrpcHostExtensions.cs:111`) est compensé par l'intercepteur —
le raisonnement de `:85-104` tient. **Aucun serveur gRPC non protégé.**

Trois services appellent `AddHbaGrpc()` **sans publier de service** :
notification `Program.cs:27`, review `Program.cs:31`, return-refund `Program.cs:9`.
Ils ouvrent un port HTTP/2 inutile. **LOW.**

### 10.1 Une clé unique et symétrique pour toute la plateforme — **HIGH**
`shared/common/HBA.Shared.Hosting/InternalRoutes.cs:66-81` : `Internal:ApiKey` est
**la même** pour les 20+ services (`docker-compose.dev.yml:49`,
`k8s/base/common/secret.yaml:36`). L'intercepteur n'atteste **pas** quel service
appelle — le commentaire `:74-78` le reconnaît. Conséquence directe : **tout
service compromis peut appeler n'importe quel RPC de n'importe quel service, en
affirmant n'importe quelle identité d'utilisateur ou de vendeur.** La comparaison
à temps constant (`:51-63`) est correcte ; le modèle, lui, n'a pas de granularité.

### 10.2 `sellerId` / `userId` / `storeId` portés par le message
L'appelant authentifié n'existe pas au niveau gRPC : la seule chose que le serveur
sait, c'est que l'appelant possède la clé. Toutes les décisions ci-dessous
reposent donc sur un identifiant **du corps du message** :

| RPC | Identifiant cru | Fichier:ligne | Ce qu'un porteur de clé obtient | Sévérité |
|---|---|---|---|---|
| `MerchantApi.GetSellerPayout` | `seller_id` | `MerchantsGrpc.cs:110-137` | Le **numéro Mobile Money** et le nom du titulaire de n'importe quel vendeur, énumérables. Le commentaire `:93-101` justifie l'exposition par « un seul appelant légitime : wallet-service » — rien ne le vérifie. | **HIGH** |
| `FinancialApi.RefundPayment` | `payment_id`, `amount` | `FinancialGrpcService.cs:15-61` | Le déclenchement d'un **remboursement** sur n'importe quel paiement, pour un montant choisi (borné seulement par `RefundableAmount`, `PaymentLifecycleCommands.cs:190`). Aucune vérification que le `return_id` fourni appartient au paiement. | **HIGH** |
| `InventoryApi.ReleaseReservation` | `order_id` | `InventoryGrpc.cs:45-51` | La **libération du stock** réservé par n'importe quelle commande. | **HIGH** |
| `DeliveryApi.CancelDelivery` | `reference` + `source` | `DeliveryGrpcService.cs:150-186`, en particulier `RequiredPartnerId: null` (`:170`) | L'**annulation de n'importe quelle course** par référence, la garde d'appartenance partenaire étant explicitement désactivée sur le chemin gRPC (`:142-147`). Les références sont devinables (`ORDER-…`, et `CreateDeliveryCommand.cs:100-112` documente que « 1 », « 1000 », « ORDER-1 » sont les premières commandes de tout site). | **HIGH** |
| `MerchantApi.GetMemberAccess` / `CheckMerchantCapability` | `user_id` | `MerchantsGrpc.cs:158`, `:207` | L'**intégralité des permissions vendeur** de n'importe quel compte, ou une réponse « autorisé » pour un `user_id` choisi. `CheckMerchantCapability` vérifie bien l'appartenance (`:226`, `:250`) — mais à partir du `user_id` du message. | **HIGH** |
| `FoodApi.GetStaffMembership` | `user_id` | `FoodGrpc.cs:46-79` | L'appartenance et les permissions du personnel d'un restaurant, utilisées pour autoriser dans `MealOrderEndpoints.cs:120`. | **MEDIUM** |
| `DeliveryPricingApi.QuoteDelivery` | `seller_id`, `store_id` | `DeliveryPricingGrpcService.cs:22-39` | Un devis établi au nom d'un vendeur arbitraire (RPC mort aujourd'hui). | **LOW** |
| `PromotionApi.ReserveCoupon` | `user_id`, `cart_id` | `PromotionGrpcService.cs:46-80` | Une réservation de coupon au nom d'un utilisateur arbitraire (RPC mort aujourd'hui). | **LOW** |

Le seul RPC qui **vérifie** au lieu d'accepter est `CheckMerchantCapability` —
et il le fait dans les termes du §36 (`MerchantsGrpc.cs:199-206`) : le `seller_id`
reçu est confronté à l'appartenance résolue depuis le `user_id`. Mais le `user_id`,
lui, reste celui du message.

**Ce qu'il manque, concrètement** : l'intercepteur devrait transporter et
vérifier une identité d'appelant (mTLS, ou au minimum une clé **par service** +
une liste blanche de RPC par appelant), et les RPC porteurs de données
financières (`GetSellerPayout`) ou d'effet monétaire (`RefundPayment`,
`ReleaseReservation`, `CancelDelivery`) devraient être restreints à leur appelant
déclaré.

---

## 11. Propagation du traçage

**`traceparent` : propagé.** `shared/common/HBA.Shared.Hosting/Telemetry/TelemetryExtensions.cs:88`
active `AddHttpClientInstrumentation()` et `:109` `AddSource("Grpc.Net.Client")` ;
`AddHbaTelemetry` est appelé par `ServiceHostExtensions.cs:154`, donc par **tous**
les services via `AddHbaService`. Un canal gRPC .NET passe par
`SocketsHttpHandler` : le `DistributedContextPropagator` d'OpenTelemetry y injecte
`traceparent`/`tracestate` automatiquement, et le volet serveur est couvert par
`AddAspNetCoreInstrumentation` (`:71`) puisqu'un appel gRPC entrant est une
requête HTTP/2. **Correct — mais à confirmer par une trace réelle** : l'analyse
est statique et l'injection dépend du propagateur par défaut, non redéfini ici.

**`x-correlation-id` : perdu sur tout le flux événementiel — MEDIUM.**
`InternalCallClientInterceptor.cs:46-52` lit la corrélation depuis
`_accessor.HttpContext?.Items[...]`. Un appel gRPC émis depuis un **consumer
Kafka** ou un `BackgroundService` n'a pas de `HttpContext` : la corrélation est
alors simplement omise, sans journal. Cela concerne la majorité des appels
mutants du dépôt :
`CreateDeliveryOnOrderConfirmedHandler.cs:274`, `FoodOrderBridgeHandlers.cs:319`,
`OrderDeliveryArbitrationHandlers.cs:264`, `SellerSalesCountHandler.cs:101`,
et l'ensemble des handlers de `services/common/notification-service/.../EventHandlers/`.
Le commentaire `:37-41` explique pourquoi la corrélation doit traverser le saut
gRPC ; sur le chemin asynchrone, elle ne le traverse pas.

**LOW** — `InternalCallClientInterceptor` ne surcharge que `AsyncUnaryCall`. Tous
les RPC du dépôt sont unaires, donc sans effet aujourd'hui ; un futur RPC en flux
n'aurait ni clé interne, ni corrélation, ni échéance.

---

## 12. Services appelés en HTTP alors qu'un contrat gRPC existe

### 12.1 `apps/api-gateway` — 13 clients HTTP doublonnant des contrats gRPC — **MEDIUM**
`apps/api-gateway/src/HBA.Gateway.Infrastructure/DependencyInjection.cs:49-61`
enregistre `IIdentityClient`, `IUserClient`, `IMerchantClient`, `ICatalogClient`,
`IInventoryClient`, `ICommerceClient`, `IOrderClient`, `IFoodClient`,
`IDeliveryClient`, `IFinancialClient`, `IEngagementClient`, `ICommunicationClient`,
`IMediaClient` — tous en `AddHttpClient<...>` (`:85`) vers `Services:<X>` **port
8080**, avec Polly. Douze de ces treize domaines ont un contrat gRPC.

C'est le rôle légitime d'une passerelle nord-sud (agrégation BFF sur REST), et je
ne le classe pas comme faute. Le défaut est ailleurs : **c'est la seule couche du
système qui a un disjoncteur** (§8). Les mêmes lectures (`GetProduct`,
`GetAvailability`, `GetStore`) sont donc protégées quand elles viennent de la
passerelle, et nues quand elles viennent d'un service voisin.

### 12.2 `apps/client-bff` — un client HTTP dans un dossier « GrpcClients » — **MEDIUM**
`apps/client-bff/src/HBA.ClientBff.Infrastructure/GrpcClients/MarketplaceOrder/ClientOrderGateway.cs`
— le chemin dit `GrpcClients`, le namespace dit
`HBA.ClientBff.Infrastructure.GrpcClients.MarketplaceOrder` (`:9`), et la classe
est un `HttpClient` brut (`:15`) qui fait `GET /api/orders`, `GET /api/orders/{id}`,
`POST /api/orders` (`:24-31`). Il relaie le `Authorization` de l'utilisateur
(`:80-84`), ce qui est le bon comportement pour un BFF — le RPC `OrderApi` n'offre
d'ailleurs ni liste paginée ni création. Le défaut est de **nommage** : rien
n'indique au lecteur que ce chemin ne passe pas par gRPC.
Il lit `Services:Order` (`DownstreamServicesOptions.cs:7-10`) comme **adresse
HTTP** (`docker-compose.dev.yml` : `SERVICES__ORDER=http://order-service:8080`)
alors que les clients gRPC lisent la **même clé** et en réécrivent le port
(`OrderingGrpc.cs:575-580`). Une clé, deux protocoles, deux ports : fonctionne,
mais mérite d'être écrit quelque part.

### 12.3 `new HttpClient()` sans durée de vie — **MEDIUM (hors gRPC)**
`services/common/payment-service/src/HBA.Financial.Payments.Infrastructure/Gateways/Simulation/MobileMoneyPaymentGateway.cs:64`
instancie un `HttpClient` par appel, hors `IHttpClientFactory` — épuisement de
sockets sous charge. Sans rapport avec gRPC, relevé au passage.

Aucun autre appel HTTP inter-services : les `IHttpClientFactory` restants visent
des tiers (Resend, Stripe, PayPal, MTN, Moov, FedaPay, Cloudinary, Rembg, S3) ou
des webhooks partenaires (`WebhookDispatchService.cs`).

---

## 13. Adresses `Services:<Nom>` — compose et configmap

### 13.1 Le script du dépôt passe
```
$ python3 scripts/check-service-addresses.py
31 service(s) examiné(s), 0 adresse(s) manquante(s).
```

### 13.2 Il passe parce qu'il ne regarde pas au bon endroit — **HIGH**
`scripts/check-service-addresses.py:73-79` (`programmes()`) ne collecte que les
fichiers nommés **`Program.cs`**. Or trois enregistrements de clients gRPC vivent
dans des **installeurs de module** :

| Appel | Fichier:ligne | Clé exigée |
|---|---|---|
| `AddDeliveryPricingGrpcClient` | `services/delivery/delivery-service/src/HBA.Delivery.Core.Infrastructure/DeliveriesModuleInstaller.cs:60` | `Services:DeliveryPricing` |
| `AddOrderingGrpcClient` | `services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Infrastructure/ReturnRefundModuleInstaller.cs:47` | `Services:Order` |
| `AddFinancialGrpcClient` | `.../ReturnRefundModuleInstaller.cs:48` | `Services:Financial` |

Ces trois clés ne sont **jamais contrôlées** par le script. Vérification manuelle :

- `docker-compose.dev.yml` : les trois sont présentes
  (`delivery-service: SERVICES__DELIVERYPRICING`, `return-refund-service:
  SERVICES__ORDER` et `SERVICES__FINANCIAL`). Développement : **OK**.
- `k8s/base/common/configmap.yaml` : **`SERVICES__DELIVERYPRICING` est absent**
  (le fichier déclare Identity, User, Media, Merchant, Catalog, Inventory,
  Commerce, Order, Food, FoodCart, FoodOrder, Delivery, Financial, Engagement,
  Communication, Promotion — lignes 44-65 ; aucune occurrence de
  `DELIVERYPRICING` dans tout `k8s/`).

**Conséquence : en Kubernetes, delivery-service ne démarre pas.**
`DeliveryPricingGrpcRegistration.cs:13-14` lève
`InvalidOperationException("Services:DeliveryPricing est absent…")` à la
**construction de l'hôte**. Le pod entre en `CrashLoopBackOff` au premier
déploiement — et avec lui tout le parcours logistique.

### 13.3 Clés distribuées mais jamais lues par un client gRPC — **LOW**
`SERVICES__DRIVERS` et `SERVICES__ROUTES` (dispatch-service), `SERVICES__DRIVERS`
(tracking-service), `SERVICES__DELIVERY` (dispatch, tracking, proof),
`SERVICES__MEDIA` (proof), `SERVICES__USER` (order, restaurant, payment, review,
notification), `SERVICES__CATALOG` (inventory, payment, review),
`SERVICES__FOOD`/`SERVICES__MERCHANT` (cart), `SERVICES__INVENTORY`/`SERVICES__MEDIA`
(return-refund) : aucun `Add*GrpcClient` correspondant. Configuration morte, qui
donne l'illusion d'une dépendance branchée.

### 13.4 Noms d'hôtes divergents entre compose et configmap — **MEDIUM, à confirmer**
`k8s/base/common/configmap.yaml` pointe vers `merchant-service`,
`commerce-service`, `food-service`, `financial-service`, `engagement-service`,
`communication-service` (lignes 47, 50, 52, 62, 63, 64). `docker-compose.dev.yml`
nomme ces mêmes services `seller-service`, `cart-service`, `restaurant-service`,
`payment-service`, `review-service`, `notification-service`. `k8s/base/services/`
ne contient qu'un gabarit `_service` sans instance nommée : **je ne peux pas
trancher** si les `Service` Kubernetes portent les noms du configmap. Si ce n'est
pas le cas, six adresses inter-services sont irrésolvables en production.
*À vérifier dans `k8s/overlays/`.*

---

## 14. Défauts classés

| # | Sév. | RPC / objet | Défaut | Fichier:ligne |
|---|---|---|---|---|
| 1 | **CRITICAL** | `DeliveryApi.LookupQuote` | Appelé sur le checkout marchandise **et** repas, aucun corps de serveur → `UNIMPLEMENTED` non rattrapé. Aucune commande de repas ne peut être passée. | `shared/proto/delivery/v1/delivery.proto:43` ; client `PlaceOrderCommandHandler.cs:371`, `PlaceMealOrderCommand.cs:302` ; serveur absent de `DeliveryGrpcService.cs` |
| 2 | **CRITICAL** | `InventoryApi.ReserveStock` | Non idempotent : deux appels pour le même `order_id` créent deux réservations. Sous échéance 5 s, un `DeadlineExceeded` après commit double le stock immobilisé. | `InventoryItem.cs:132-160` ; appelant `PlaceOrderCommandHandler.cs:279` |
| 3 | **CRITICAL** | `OrderApi.ListOrdersBySeller` | Appelé par un handler Kafka, aucun corps de serveur → `UNIMPLEMENTED`, rejeu, lettre morte, compteur de ventes vendeur jamais mis à jour. | `order.proto:10` ; client `OrderingGrpc.cs:226` ← `SellerSalesCountHandler.cs:101` |
| 4 | **HIGH** | `Services:DeliveryPricing` | Absent de `k8s/base/common/configmap.yaml` → delivery-service ne démarre pas en Kubernetes. Non détecté par `check-service-addresses.py`, qui ne lit que les `Program.cs`. | `DeliveriesModuleInstaller.cs:60` ; `configmap.yaml:44-65` ; `scripts/check-service-addresses.py:73-79` |
| 5 | **HIGH** | `DeliveryPricingApi.ConsumeQuote` | Mutation destructive appelée **avant** `SaveChanges` et rejouée par Kafka ; la garde d'idempotence ne voit que ce qui est écrit → devis consommé, course jamais créée, commande payée bloquée. | `CreateDeliveryCommand.cs:202` vs `:217-218` et `:98` ; `GrpcDeliveryPricingQuoteValidator.cs:45` |
| 6 | **HIGH** | Tous les clients gRPC | **Aucun disjoncteur.** `GetMemberAccess`, appelé sur chaque requête vendeur de 4 services, fait attendre 5 s par requête pendant une panne de seller-service. Les commentaires du code affirment le contraire. | `HbaResilience.cs:39` (seule occurrence, HTTP) ; `PromotionGrpcClient.cs:146`, `MediaGrpcClient.cs:124` |
| 7 | **HIGH** | `MerchantApi.GetSellerPayout` + clé interne unique | Numéro Mobile Money de n'importe quel vendeur rendu à tout porteur de la clé partagée, identique pour les 20+ services et sans identité d'appelant. | `MerchantsGrpc.cs:110-137` ; `InternalRoutes.cs:66-81` ; `docker-compose.dev.yml:49` |
| 8 | **HIGH** | `FinancialApi.RefundPayment`, `InventoryApi.ReleaseReservation`, `DeliveryApi.CancelDelivery` | Effet monétaire ou logistique déclenché à partir d'un identifiant **du message**, sans vérification d'appartenance. `CancelDelivery` désactive explicitement la garde partenaire sur le chemin gRPC. | `FinancialGrpcService.cs:15` ; `InventoryGrpc.cs:45` ; `DeliveryGrpcService.cs:170` |
| 9 | **HIGH** | return-refund-service | Trois classes nommées `*GrpcClient` sont des bouchons muets : stock retourné jamais réintégré, course de retour fabriquée en mémoire, preuve photo jamais validée. | `InventoryGrpcClient.cs:9-10`, `DeliveryGrpcClient.cs:8-9`, `MediaGrpcClient.cs:8-11` |
| 10 | **HIGH** | `InventoryApi.GetAvailability` | `location_id` déclaré au proto, ignoré du client et du serveur ; la disponibilité somme **toutes** les localisations alors que la réservation est par localisation. `on_hand`/`reserved` jamais remplis. | `inventory.proto:74,79-80` ; `InventoryGrpc.cs:18-28` ; `InventoryModuleApi.cs:55-70` |
| 11 | **HIGH** | `PromotionApi` (4 RPC) | Serveur complet, **zéro client** : `AddPromotionGrpcClient` n'est appelé nulle part, aucun parcours d'achat n'évalue de coupon. Le commentaire du client affirme trois appelants inexistants. | `PromotionGrpcRegistration.cs:29` ; `PromotionGrpcClient.cs:131-134` |
| 12 | **HIGH** | `IdentityApi.ValidateAccessToken` | Le RPC censé couvrir « paiement, retrait, action d'administration » n'a aucun appelant : un compte suspendu reste actif jusqu'à expiration du JWT. | `identity.proto:14-30` ; `IdentityGrpc.cs:108` |
| 13 | **HIGH** | `OrderApi.ListOrdersByBuyer` | Implémentation factice : renvoie une `OrderSummary` synthétique à deux champs au lieu de la liste des commandes. | `OrderingGrpc.cs:36-51` |
| 14 | **HIGH** | `FinancialApi` (8/9 RPC) | Huit RPC financiers déclarés sans corps ; `FinancialGrpcService` n'a qu'une méthode. | `financial.proto:8-16` ; `FinancialGrpcService.cs` |
| 15 | **MEDIUM** | 13 `.proto` dupliqués | Copies non compilées de protos partagés (dont 4 du même `FoodApi`), modifiables sans effet ni erreur. | `services/*/*/proto/*.proto` ; `<Protobuf Include>` seulement dans `shared/contracts/**` |
| 16 | **MEDIUM** | Codes de statut | Aucun serveur n'émet `NotFound`/`FailedPrecondition`/`AlreadyExists`/`PermissionDenied`. `Unavailable` pour une config manquante, `NotFound` pour un refus d'auth, `Unknown` pour toute exception. | `InternalCallInterceptors.cs:119-127` ; `GrpcHostExtensions.cs:73` |
| 17 | **MEDIUM** | Traçage asynchrone | `x-correlation-id` omis dès que l'appel gRPC part d'un consumer Kafka (pas de `HttpContext`) — c'est-à-dire sur la majorité des appels mutants. | `InternalCallInterceptors.cs:46-52` |
| 18 | **MEDIUM** | Échéance | Protection conventionnelle : un `AddGrpcClient` sans `.AddInterceptor<InternalCallClientInterceptor>()` n'aurait aucune échéance, et rien ne le vérifie. 5 s uniformes de la lecture de cache au paiement PSP. | `InternalCallInterceptors.cs:72-75` |
| 19 | **MEDIUM** | `ReturnRefundGrpc` (3 RPC) | Proto hors chaîne de compilation ; la classe `ReturnRefundGrpcService` est une coquille de 6 lignes portant une constante. | `return_refund.proto` ; `ReturnRefundGrpcService.cs:1-6` |
| 20 | **LOW** | 45 RPC morts | Serveurs implémentés, testés, déployés et joignables, sans aucun appelant — dont 16 pour dispatch/driver/route/tracking/proof, adossés à des magasins en mémoire. | voir §2 |
