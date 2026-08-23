# Audit — parcours client (inscription, achat marketplace, commande food)

Périmètre : reconstitution **depuis le code** des trois parcours bout en bout — de la route
HTTP jusqu'au consumer Kafka réellement enregistré. Analyse statique (pas de compilateur .NET).
Tous les chemins sont relatifs à la racine du dépôt (`/root/audit-src`).

Méthode : pour chaque étape, on part de l'endpoint (fichier + ligne), on suit la commande, le
handler, les clients gRPC, l'événement publié, puis on **cherche le consumer enregistré en DI**
et on vérifie que le **topic sur lequel l'événement est publié figure dans la liste des topics
auxquels le service consommateur s'abonne**. Un câblage DI correct ne suffit pas : c'est
précisément là que les trois parcours se cassent.

---

## 0. Le fait transverse qui casse les trois parcours

Avant toute chose, un mécanisme unique explique la majorité des ruptures ci-dessous. Il est déjà
relevé en §1.1 de `KAFKA_EVENT_MATRIX.md` ; je l'ai revérifié dans le code et je le reprends ici
parce que **c'est le premier point de blocage de chacun des trois parcours**.

Le topic de publication est **dérivé du nom du producteur** :

```
shared/common/HBA.Shared.Infrastructure/Kafka/KafkaEventNaming.cs:38
  Topic(...) => $"{options.TopicPrefix}.{producer.Replace("-service","")}.{options.TopicVersion}"
```

La liste d'abonnement, elle, est une **constante de 13 sujets** jamais lue depuis la
configuration :

```
shared/common/HBA.Shared.Infrastructure/Kafka/KafkaEventBusOptions.cs:21-36
  identity, user, merchant, catalog, inventory, commerce, order, food,
  delivery, financial, engagement, communication, media
shared/common/HBA.Shared.Infrastructure/Kafka/KafkaIntegrationEventConsumer.cs:106
  consumer.Subscribe(_options.SubscribeTopics);
```

`AddBuildingBlocksInfrastructure` construit `KafkaEventBusOptions` champ par champ
(`shared/common/HBA.Shared.Infrastructure/DependencyInjection.cs:60-71`) et **ne lit jamais
`SubscribeTopics`** depuis `IConfiguration` : aucune variable `KAFKA__SUBSCRIBETOPICS` n'existe
d'ailleurs dans `docker-compose.dev.yml` (recherche exhaustive). Le garde-fou de démarrage
(`DependencyInjection.cs:79-90`) ne contrôle que le **préfixe** (`service.`), jamais le segment du
milieu — il laisse donc passer exactement le défaut réel.

Confrontation avec les `KAFKA__PRODUCER` de `docker-compose.dev.yml` :

| Producteur (compose) | Topic écrit | Écouté ? |
|---|---|---|
| `identity-service` (:259), `user-service` (:304), `catalog-service` (:447), `inventory-service` (:488), `order-service` (:551), `delivery-service` (:865), `media-service` (:330) | `service.{identity,user,catalog,inventory,order,delivery,media}.v1` | **oui** |
| `payment-service` (:1033) | `service.payment.v1` | **non** (la liste dit `financial`) |
| `cart-service` (:518) | `service.cart.v1` | **non** (la liste dit `commerce`) |
| `restaurant-service` (:633) | `service.restaurant.v1` | **non** (la liste dit `food`) |
| `seller-service` (:392) | `service.seller.v1` | **non** (la liste dit `merchant`) |
| `review-service` (:1078) | `service.review.v1` | **non** (la liste dit `engagement`) |
| `notification-service` (:1123) | `service.notification.v1` | **non** (la liste dit `communication`) |
| `food-cart-service` (:724), `food-order-service` (:759), `kitchen-prep-service` (:793), `menu-service` (:676), `availability-service` (:700), `food-review-service` (:817), `return-refund-service` (:598), `promotion-service` (:1197), et les 6 services delivery périphériques | `service.food-cart.v1`, `service.food-order.v1`, … | **non** (aucun n'est dans la liste) |

Conséquence directe, vérifiée handler par handler plus bas :

- **`PaymentCaptured` / `PaymentFailed` ne parviennent à personne.** Le paiement est le pivot des
  deux parcours d'achat ; les deux s'arrêtent donc à `AwaitingPayment`.
- **Tout le domaine food (hors restaurant-service→lui-même) est muet**, dans les deux sens.
- Les événements de `cart-service`, `seller-service`, `review-service`, `promotion-service` et
  `return-refund-service` n'atteignent aucun consommateur.

Sévérité : **CRITICAL**. `KafkaEventBusOptions.cs:21` ; `KafkaEventNaming.cs:38` ;
`DependencyInjection.cs:60-90`.

Dans les fiches qui suivent, je note `[TOPIC MORT]` chaque arête coupée par ce mécanisme, et je
décris **aussi** ce que ferait le parcours si le mécanisme était réparé — c'est là que se trouvent
les défauts de second rang, qui survivraient au correctif.

---

# A. Inscription / connexion

## A.1 Passe-t-on par le Client BFF ?

**Non. Le Client BFF n'est branché sur rien.**

- `apps/client-bff/src/HBA.ClientBff.Api/Endpoints/ClientEndpoints.cs:13` expose
  `/api/v1/client/*`. Aucune route YARP de la passerelle ne correspond à ce préfixe
  (44 routes lues dans `apps/api-gateway/src/HBA.Gateway.Api/appsettings.json`, section
  `ReverseProxy:Routes` — vérifié exhaustivement). `ServicesOptions` ne déclare d'ailleurs
  aucune adresse `ClientBff`
  (`apps/api-gateway/src/HBA.Gateway.Infrastructure/Configuration/ServicesOptions.cs:26-68`).
- Le conteneur existe (`docker-compose.dev.yml:1218`) mais **ne publie aucun port**, et la
  passerelle est « le SEUL conteneur qui publie un port » (commentaire assumé,
  `docker-compose.dev.yml:1258-1265`).
- 11 de ses 13 routes rendent `501` en dur
  (`ClientEndpoints.cs:22-30`, `NotImplemented(...)` — panier, checkout, food, tracking,
  retours, avis).

Le client mobile attaque donc **directement les services** à travers YARP
(`/api/v1/auth/*`, `/api/cart/*`, `/api/orders/*`, `/api/payments/*`, `/api/food/*`), plus deux
contrôleurs d'agrégation **hébergés dans la passerelle elle-même**
(`Controllers/Bff/ClientExpressController.cs:31`, `Controllers/Bff/ClientFoodController.cs:19`,
préfixe `api/v1/bff/client/...` — servis localement, non proxifiés).

## A.2 Inscription

```
Acteur: visiteur anonyme
Point d'entrée: POST /api/v1/auth/register
  → passerelle, route `auth-v1` (appsettings.json, ClusterId=Identity, anonymous, limiteur `auth`)
  → services/common/identity-service/src/HBA.Identity.Api/Endpoints/IdentityEndpoints.cs:63
  → RegisterUserCommandHandler (…/Users/Commands/RegisterUser/RegisterUserCommandHandler.cs:57)
Services impliqués: api-gateway, identity-service, notification-service, user-service
Appels gRPC: aucun à l'inscription. À la consommation de `UserRegistered`, user-service
  rappelle identity par gRPC (`IIdentityModuleApi.GetUserAsync`,
  CreateUserProfileOnUserRegisteredHandler.cs) — deadline 5 s posée globalement par
  `InternalCallClientInterceptor` (cf. GRPC_MATRIX §5), aucun retry, appel idempotent (lecture).
Événements Kafka:
  • UserRegisteredIntegrationEvent   (UserDomainEventHandlers.cs:9-25) → service.identity.v1
      → consommé par user-service (CreateUserProfileOnUserRegisteredHandler, enregistré
        HBA.Users.Api/Program.cs:34) — inbox présent, garde d'idempotence explicite. OK.
      → consommé par notification-service (bienvenue).
  • EmailVerificationRequestedIntegrationEvent (RegisterUserCommandHandler.cs:138)
      → service.identity.v1 → SendEmailVerificationHandler
        (NotificationsModuleInstaller.cs:180). OK.
États successifs: (aucun) → User.PendingVerification, rôle « Buyer » assigné si le rôle existe
  en base (RegisterUserCommandHandler.cs:130-134 ; semé par IdentityDataSeeder.cs, rôles
  Buyer/Seller/Admin/Moderator/Driver/FoodPartner/Dispatcher)
Erreurs possibles: e-mail/téléphone déjà pris (409), e-mail ou téléphone invalides (400)
Compensations: sans objet (une seule écriture, une seule transaction, outbox)
Points de blocage: aucun sur ce segment
Défauts:
  [MEDIUM] Le rôle par défaut est attribué « si le rôle existe » : `if (buyerRole is not null)`
    sans `else`. Sur une base où l'amorçage a échoué, le compte naît SANS AUCUN rôle, en
    silence, et échouera plus tard sur `CustomerOnly`. — RegisterUserCommandHandler.cs:130-134
  [LOW] Rôle « Buyer » vs politique passerelle `CustomerOnly: ["Buyer"]` : cohérent aujourd'hui,
    mais l'écart avec le vocabulaire du cahier (« Customer ») est assumé en commentaire —
    IdentityDataSeeder.cs:17-31 ; appsettings.json §Authorization.Roles
Statut: COHERENT
```

## A.3 Vérification e-mail (activation)

```
Acteur: titulaire du compte, sans session
Points d'entrée (trois, tous anonymes, tous sous le limiteur `auth` 30/min) :
  POST /api/v1/auth/confirm-email  (IdentityEndpoints.cs:64)  — par {userId, token} du LIEN
  POST /api/v1/auth/email/verify   (IdentityEndpoints.cs:151) — par {email, code} de l'ÉCRAN
  POST /api/v1/auth/email/resend   (IdentityEndpoints.cs:139) — renvoi, 204 systématique
Services impliqués: identity-service, notification-service
Événements Kafka: EmailVerificationRequested → notification (OK) ;
  UserEmailConfirmedIntegrationEvent (UserDomainEventHandlers.cs:66-80) → service.identity.v1,
  **aucun consommateur dans le dépôt** (recherche exhaustive) — publié pour personne.
États successifs: PendingVerification --ConfirmEmail/ConsumeEmailVerificationCode--> Active
  (l'activation est explicite : ConfirmEmailCommandHandler.cs:46-49 et
   VerifyEmailCodeCommandHandler.cs:47-50 appellent `user.Approve()` si encore en attente)
Erreurs possibles: code faux/expiré (400), compte inconnu (404 sur /confirm-email seulement)
Compensations: sans objet
Points de blocage: aucun — c'est le seul chemin d'activation, et il est complet.
Défauts:
  [LOW] `UserEmailConfirmed` publié sans consommateur — UserDomainEventHandlers.cs:66
Statut: COHERENT
```

## A.4 Vérification téléphone / OTP — **rompu**

```
Acteur: visiteur anonyme
Point d'entrée: POST /api/v1/auth/otp/request   (IdentityEndpoints.cs:103, limiteur `otp` 5/5min)
                POST /api/v1/auth/verify-otp    (IdentityEndpoints.cs:104)
Services impliqués: identity-service SEUL
Appels gRPC: aucun
Événements Kafka: AUCUN — et c'est le défaut.
États successifs: MfaChallenge Issued → Verified (ou TooManyAttempts)
Erreurs possibles: canal non supporté (400), code invalide (400), trop de tentatives (422)
Compensations: sans objet
Points de blocage:
  1. **Le code n'est jamais envoyé.** `IssueOtpChallengeCommandHandler` génère le code, stocke
     son empreinte, puis écrit littéralement `_ = code;` — le code en clair est jeté. Aucun
     `IIntegrationEventPublisher` n'est injecté dans ce handler, aucun événement n'est publié,
     aucun `IEmailSender`/SMS n'est appelé. Le commentaire dit « le code ne sort pas d'ici
     autrement que par le canal choisi » : **ce canal n'existe pas**.
     — OtpChallengeUseCases.cs:26-93, en particulier :90
  2. **La vérification n'ouvre aucune session.** `VerifyOtpCommandHandler` rend
     `OtpVerificationDto(Verified, Channel)` et ne touche ni l'agrégat `User`, ni
     `AuthTokenIssuer`. Aucun jeton n'est émis, et rien ne marque le téléphone comme vérifié
     (`User` n'expose aucun `PhoneVerified` mis à jour sur ce chemin).
     — OtpChallengeUseCases.cs:119-152
Défauts:
  [HIGH] Le code OTP est généré puis jeté : le parcours « connexion par téléphone » et
    « vérification de numéro » sont inutilisables de bout en bout —
    OtpChallengeUseCases.cs:90
  [HIGH] `verify-otp` ne délivre aucun jeton : même si le code arrivait, l'utilisateur ne
    pourrait pas entrer — OtpChallengeUseCases.cs:141-152
  [MEDIUM] `IssueOtpChallengeCommandHandler` ne résout le compte que par e-mail
    (`_users.GetByEmailAsync(command.Login)`, :59) alors que le canal par défaut est `SMS`
    (:47) : un numéro passé en `Login` ne trouvera jamais de compte, et la réponse « défi
    factice » masquera l'incohérence — OtpChallengeUseCases.cs:47-66
Statut: BROKEN
```

## A.5 Connexion, MFA, rafraîchissement, déconnexion

```
Acteur: titulaire d'un compte Active
Points d'entrée:
  POST /api/v1/auth/login          (IdentityEndpoints.cs:65)  anonyme
  POST /api/v1/auth/refresh        (IdentityEndpoints.cs:66)  anonyme
  POST /api/v1/auth/logout         (IdentityEndpoints.cs:98)  anonyme + AllowIdempotency
  POST /api/v1/auth/reauthenticate (IdentityEndpoints.cs:88)  authentifié (step-up §37)
  POST /api/identity/account/me/mfa/{setup,confirm,disable} (IdentityEndpoints.cs:282-284)
Services impliqués: identity-service
Appels gRPC: aucun
Événements Kafka:
  • TokenRevokedIntegrationEvent — publié par LogoutByRefreshTokenCommand.cs:75 →
    service.identity.v1 → **aucun consommateur**
  • UserLoggedInIntegrationEvent — **déclaré et JAMAIS publié**. Recherche exhaustive :
    le type n'apparaît que dans sa définition
    (`shared/contracts/HBA.Identity.Contracts/IntegrationEvents/IdentityIntegrationEvents.cs`)
    et dans `tests/HBA.Identity.Tests/IdentityEventContractTests.cs:20`. Aucun appel à
    `PublishAsync(new UserLoggedInIntegrationEvent…)` nulle part.
États successifs (login): compte Deleted → 401 générique (LoginCommandHandler.cs:85) ;
  verrouillé → 401 générique (:116) ; mot de passe faux → compteur incrémenté ET PERSISTÉ
  (:249-262) ; Suspended → 403 ; PendingVerification → 403 `identity.auth.pending_approval`
  (:151) ; mauvaise surface → 403 (:171-182) ; MFA activée sans code → `MfaRequired=true`
  (:189) ; MFA fausse → compteur incrémenté (:209) ; succès → paire de jetons (:232)
Rôles et permissions dans le jeton: résolus à l'émission —
  AuthTokenIssuer.cs:49-55 (`roleNames` + `permissions` aplaties depuis les rôles)
Rafraîchissement: rotation + détection de rejeu dans le domaine
  (RefreshTokenCommandHandler.cs:67-97). Un jeton rejoué coupe TOUTE la chaîne du compte et
  le `SaveChanges` est fait avant de répondre 401 (:82) — correct.
  `auth_time` est RECOPIÉ, jamais rajeuni (:115) : le step-up reste opposable.
Erreurs possibles: 401 générique (identifiants, verrou, rejeu), 403 (suspendu, en attente,
  surface, MFA)
Compensations: sans objet
Points de blocage: aucun sur le chemin mot de passe.
Défauts:
  [MEDIUM] **La route publique historique `/api/auth/*` rend 404.** La passerelle réécrit
    `/api/auth/{**catch-all}` en `/api/identity/auth/{**catch-all}`
    (appsettings.json, route `auth`, ordre 10) et la route de garde `identity-auth-guard`
    vise le même chemin. Or identity-service **ne sert plus** `/api/identity/auth/*` : le
    groupe est `/api/v1/auth` (IdentityEndpoints.cs:60). Le commentaire de
    `HBA.Identity.Api/Program.cs:17-23` affirme encore l'inverse (« LES CHEMINS PUBLICS
    RESTENT /api/identity/* ») — l'invariant documenté est faux. Seule `auth-v1` fonctionne.
  [MEDIUM] `UserLoggedIn` déclaré, testé, jamais publié : aucune piste d'audit des connexions
    — IdentityIntegrationEvents.cs (bloc `logged_in`) vs LoginCommandHandler.cs:232
  [LOW] `TokenRevoked` publié sans consommateur — LogoutByRefreshTokenCommand.cs:75
Statut: PARTIAL (mot de passe COHERENT, OTP BROKEN, route legacy morte)
```

---

# B. Achat marketplace

## B.0 Vue d'ensemble de la chaîne réellement câblée

```
catalogue (anonyme)            catalog-service     GET /api/v1/catalog/products
détail produit                 gateway (in-proc)   GET /api/v1/bff/client/express/products/{id}
panier                         cart-service        POST /api/cart/items → /api/commerce/cart/items
commande                       order-service       POST /api/orders
paiement                       payment-service     POST /api/payments
  ├─ webhook PSP               payment-service     POST /api/payments/webhooks/{provider} (anonyme)
  └─ PaymentCaptured ─────────────────────────────────────────► [TOPIC MORT] ✂
       (attendu par order-service ConfirmOrderOnPaymentCapturedHandler)
réservation stock              inventory-service   gRPC, AVANT le paiement
commande vendeur (SellerOrder) —                   N'EXISTE PAS
livraison                      delivery-service    créée sur OrderConfirmed (jamais atteint)
livré                          order-service       sur DeliveryCompleted (jamais atteint)
avis                           review-service      POST /api/reviews
retour / remboursement         return-refund-svc   AUCUNE ROUTE PASSERELLE
```

## B.1 Catalogue → détail produit

```
Acteur: visiteur anonyme ou acheteur
Point d'entrée: GET /api/v1/catalog/products, /products/{id}
  → route `catalog-read-v1` (GET/HEAD/OPTIONS, anonymous)
  → services/marketplace/catalog-service/src/HBA.Catalog.Api/Endpoints/CatalogEndpoints.cs:104-107
  Détail enrichi : GET /api/v1/bff/client/express/products/{productId}
  → apps/api-gateway/src/HBA.Gateway.Api/Controllers/Bff/ClientExpressController.cs:68
Services impliqués: catalog-service (+ inventory, merchant via le BFF de la passerelle)
Appels gRPC: BFF passerelle → catalog/inventory/merchant en HTTP typé ; deadline gérée par
  `HbaResilience` côté passerelle (timeout total + par tentative, retry sur GET seulement —
  cf. GRPC_MATRIX §9)
Événements Kafka: aucun sur le chemin de lecture
États successifs: le filtre public est `ProductStatus.Published` uniquement
  (ProductStatus.cs:128 `IsPubliclyVisible`, appliqué CatalogModuleApi.cs:129 et
   ProductMapping.cs:49)
Erreurs possibles: 404
Compensations: sans objet
Points de blocage: aucun
Défauts: aucun bloquant relevé sur ce segment.
Statut: COHERENT
```

## B.2 Ajout au panier

```
Acteur: acheteur authentifié
Point d'entrée: POST /api/cart/items
  → route `cart` (Authenticated) + transform → /api/commerce/cart/items
  → services/marketplace/cart-service/src/HBA.Commerce.Api/Endpoints/CommerceEndpoints.cs:21
  → AddItemToCartCommandHandler
     (HBA.Commerce.Application/Carts/Commands/AddItem/AddItemToCartCommandHandler.cs)
Services impliqués: cart-service → catalog-service (gRPC), inventory-service (gRPC)
Appels gRPC:
  • ProductsApi.GetOffer / GetProduct — deadline 5 s (interceptor global), aucun retry, lectures
  • InventoryApi.IsInStock — idem
Événements Kafka: aucun à l'ajout
États successifs: (aucun panier) → Cart.Active ; lignes ajoutées
Erreurs possibles: `cart.offer.not_found` (404), `cart.offer.not_active` (409),
  `cart.product.not_available` (409), `cart.offer.without_sku` (409), `cart.out_of_stock` (409),
  `cart.catalog_unavailable` (500 métier)
Compensations: sans objet
Points de blocage: aucun
Statut: COHERENT
```

## B.3 Passage de commande

```
Acteur: acheteur authentifié
Point d'entrée: POST /api/orders
  → route `orders` (Authenticated), sans réécriture
  → services/marketplace/order-service/src/HBA.Order.Api/Endpoints/OrderEndpoints.cs:65
  → PlaceOrderCommandHandler
     (HBA.Order.Application/Orders/Commands/PlaceOrder/PlaceOrderCommandHandler.cs:61)
Services impliqués: order-service → cart-service, delivery-service, inventory-service
Appels gRPC (tous deadline 5 s via InternalCallClientInterceptor, AUCUN retry) :
  • CommerceApi.GetActiveCart      (:63)      lecture — mais servie depuis un CACHE de 2 min
  • DeliveryApi.LookupQuote        (:371)     lecture, idempotente
  • InventoryApi.ReserveStock      (:279)     **ÉCRITURE, NON IDEMPOTENTE** (GRPC_MATRIX §11-2)
  • InventoryApi.ReleaseReservation(:291)     compensation
Événements Kafka:
  • OrderPlacedIntegrationEvent (OrderDomainEventHandlers.cs:9) → service.order.v1 (écouté)
      → cart-service `CloseCartOnOrderPlacedHandler` (CartModuleInstaller.cs:49) → panier clos
États successifs: Order.Pending → (adresse, frais, réservations) → Order.AwaitingPayment
  (Order.cs:308-316) ; en cas de stock manquant → Order.Failed (Order.cs:697-699)
Erreurs possibles: `ordering.cart_empty`, `ordering.unknown_line_kind`,
  `ordering.shipping_address_required`, `ordering.food_address_incomplete`,
  `ordering.delivery_quote_{required,not_found,used,expired,foreign,address_mismatch,wrong_service}`,
  `ordering.out_of_stock`
Compensations: **présentes sur le chemin nominal** — l'échec d'une réservation libère celles
  déjà obtenues et marque la commande `Failed` (PlaceOrderCommandHandler.cs:288-296)
Points de blocage: aucun ici ; le blocage est à l'étape suivante.
Défauts:
  [CRITICAL] **La compensation ne couvre pas l'exception.** La boucle
    `foreach (… ) { var ok = await _inventoryModuleApi.TryReserveAsync(…) }`
    n'est protégée par aucun `try/catch`. Un `DEADLINE_EXCEEDED` (5 s) ou un
    `Unavailable` à la 3ᵉ ligne fait remonter l'exception : les réservations des lignes 1 et 2
    sont **déjà posées chez inventory-service** (transaction distincte, déjà commitée), et
    `_unitOfWork.SaveChangesAsync` n'est jamais appelé — la commande n'existe donc même pas
    pour porter la trace. Le stock est immobilisé sans commande, sans compensation et sans
    rien pour le retrouver. — PlaceOrderCommandHandler.cs:276-297
  [HIGH] **Le prix n'est pas revalidé côté serveur au checkout** (question 2). Il est figé à
    l'ajout au panier depuis `offer.EffectivePrice`
    (AddItemToCartCommandHandler.cs, appel `cart.AddItem(… offer.EffectivePrice …)`), relu tel
    quel par `CartPricer` puis recopié dans la commande
    (PlaceOrderCommandHandler.cs:131-138 : `l.UnitBaseAmount`, `l.FinalUnitPrice`). Aucun appel
    catalogue dans `PlaceOrderCommandHandler`. Le client ne peut PAS imposer un prix — aucun
    champ montant n'entre par la requête, ni au panier ni à la commande, et les frais de port
    viennent du devis serveur (:495) — mais un prix vieilli est facturé tel quel.
  [HIGH] **Le statut « publié » n'est pas revérifié au moment de commander** (question 1).
    `IsPurchasable`/`IsVisible` ne sont contrôlés qu'à l'ajout au panier. Une fiche suspendue,
    dépubliée ou archivée entre-temps se commande normalement : `PlaceOrderCommandHandler` ne
    relit ni l'offre ni le produit (aucun `IProductModuleApi`/`IOfferModuleApi` injecté,
    :38-43). — PlaceOrderCommandHandler.cs:61-138
  [MEDIUM] Le panier lu au checkout passe par la requête **cachée 2 minutes**
    (`CartQueries.cs:19` → `CartModuleApi.GetActiveCartAsync`) : la commande peut se figer sur
    un panier périmé.
  [MEDIUM] Frais de livraison à **zéro** pour toute commande de marchandise sans devis
    (:362-368) — assumé et journalisé, mais c'est une perte de recette à chaque commande.
Statut: PARTIAL
```

**Question 4 — double commande sur le même panier : NON, c'est fermé.**
Double garde : lecture préalable `GetByCartAsync` avant tout autre travail
(`PlaceOrderCommandHandler.cs:93-102`, qui **rend la commande existante** au lieu d'une erreur)
+ index unique en base (`OrderConfiguration.cs:147 builder.HasIndex(o => o.CartId).IsUnique()`,
migration `20260823000100_UnicitePanierParCommande`). La course entre deux requêtes simultanées
est fermée par l'index.

**Question 3 — quand le stock est-il réservé ? AVANT le paiement.**
`PlaceOrderCommandHandler.cs:276-297` réserve ligne par ligne, puis `MarkAwaitingPayment()`
(:299). Le décrément physique n'a lieu qu'à la confirmation
(`ConfirmOrderPaymentCommandHandler` → `ConfirmReservationAsync`,
`OrderLifecycleCommands.cs:224-226`). L'ordre est correct. Réserve : `TryReserveAsync` **rend
`true` pour un SKU sans enregistrement de stock** (« non suivi »,
`InventoryModuleApi.cs:98-106`) — la survente est donc possible sur tout SKU non déclaré.

## B.4 Paiement — **point de rupture n°1**

```
Acteur: acheteur authentifié, puis le PSP
Points d'entrée:
  POST /api/payments                      → /api/financial/payments
     services/common/payment-service/src/HBA.Financial.Api/Endpoints/FinancialEndpoints.cs:28
     → InitiatePaymentCommandHandler (…/InitiatePayment/InitiatePaymentCommandHandler.cs:40)
  POST /api/payments/webhooks/{provider}  → anonyme (route `payments-webhooks`)
     FinancialEndpoints.cs:75 → ProcessGatewayWebhookCommandHandler
     (…/Payments/Commands/GatewayConfirmationCommands.cs:54)
  POST /api/payments/{id}/redirect/confirm (authentifié) — filet si le webhook n'arrive pas
  POST /api/v1/payments/intents            (RequireIdempotency, FinancialEndpoints.cs:93)
Services impliqués: payment-service → order-service (gRPC), PSP
Appels gRPC: OrderingApi.GetOrder (InitiatePaymentCommandHandler.cs:64) — deadline 5 s, lecture.
  Le MONTANT vient de `order.GrandTotal` (:108), jamais du client. Bon.
Événements Kafka:
  • PaymentCreated  (PaymentDomainEventHandlers.cs:147) → service.payment.v1 → aucun consumer
  • PaymentCaptured (PaymentDomainEventHandlers.cs:30)  → service.payment.v1 → **[TOPIC MORT]**
      consumer déclaré : order-service `ConfirmOrderOnPaymentCapturedHandler`
      (PaymentOutcomeHandlers.cs:35 ; DI OrderingModuleInstaller.cs:61)
      et food-order-service `ConfirmMealOrderOnPaymentCapturedHandler`
      (MealOrderingModuleInstaller.cs:71)
  • PaymentFailed   (PaymentDomainEventHandlers.cs:57)  → service.payment.v1 → **[TOPIC MORT]**
      consumer déclaré : order-service `CancelOrderOnPaymentFailedHandler`
      (PaymentOutcomeHandlers.cs:71 ; DI OrderingModuleInstaller.cs:62)
  • PaymentRefunded / PaymentRefundFailed → service.payment.v1 → **[TOPIC MORT]** / aucun consumer
États successifs: Payment.Pending → Authorized/Captured/Failed/Refunded (PaymentIds.cs:62-68)
Erreurs possibles: `payments.order.not_found`, `payments.order.not_payable`,
  `payments.already_exists`, `payments.webhook_invalid_signature`, `payments.method_invalid`
Compensations: `RefundPaymentOnOrderCancelledHandler` (consomme `OrderCancelled`,
  service.order.v1 → écouté) ; `ReleaseEscrowOnOrderDeliveredHandler` (consomme `OrderDelivered`,
  écouté). Ces deux-là sont vivants.
Points de blocage:
  ⛔ **L'acheteur est débité et la commande reste `AwaitingPayment` pour toujours.**
     `PaymentCaptured` est publié sur `service.payment.v1`, que personne n'écoute (§0). Le
     commentaire de `PaymentOutcomeHandlers.cs:17-33` décrit exactement ce scénario comme
     « corrigé » — le câblage DI l'est ; le transport ne l'est pas.
  ⛔ **Le stock réservé n'est jamais libéré en cas d'échec de paiement** — même cause.
Défauts:
  [CRITICAL] Paiement encaissé → commande jamais `Paid`/`Confirmed` : ni décrément de stock,
    ni course, ni notification, ni règlement du vendeur.
    — PaymentDomainEventHandlers.cs:30 (publication) vs KafkaEventBusOptions.cs:21 (abonnement)
  [CRITICAL] Commande annulée par échec de paiement → réservations toujours actives (réponse à
    la question 6 : le consumer prévu est `CancelOrderOnPaymentFailedHandler`,
    `OrderLifecycleCommands.cs:275-323` **libère bien** les réservations — mais il n'est jamais
    déclenché). — OrderingModuleInstaller.cs:62
  [HIGH] **`POST /api/payments` n'a aucun contrôle de propriété.** Le handler d'endpoint prend
    la commande directement depuis le corps
    (`InitiatePaymentAsync(InitiatePaymentCommand command, …)`, FinancialEndpoints.cs:258) et
    `InitiatePaymentCommand` ne porte aucun acheteur (InitiatePaymentCommand.cs:10-16). Le
    handler ne compare jamais `order.BuyerId` au jeton. N'importe quel compte inscrit peut
    donc ouvrir un paiement sur la commande d'un tiers ; le paiement créé fait ensuite échouer
    la tentative du vrai acheteur en `payments.already_exists`
    (InitiatePaymentCommandHandler.cs:103-106) — déni d'achat, à distance, en une requête.
    À comparer aux lectures voisines, qui, elles, vérifient (`PeutVoirLePaiement`,
    FinancialEndpoints.cs:230, :240).
  [MEDIUM] Le webhook est idempotent **par état**, pas par événement : `GatewayOutcomeApplier`
    court-circuite si `Status` est déjà `Captured`/`Failed`/`Refunded`
    (GatewayConfirmationCommands.cs:32-34), et un paiement introuvable est acquitté (:74-78).
    Il n'existe en revanche aucune table de déduplication d'événements PSP : deux webhooks
    `Refunded` portant deux `refund_id` différents passeraient tous deux par
    `payment.Refund()`. Le garde-fou réel est ailleurs — `Payment.BeginRefund` dédoublonne par
    `IdempotencyKey` et plafonne à `RefundableAmount` (Payment.cs:205-234) — mais
    `Payment.Refund()` (le chemin webhook) contourne cette clé en passant
    `externalRefundId: null` (Payment.cs:157-173).
Statut: BROKEN
```

**Question 5 — le paiement est-il idempotent ?** Partiellement. La **double capture** est
fermée (`payment.Status == Captured → Result.Success()`, GatewayConfirmationCommands.cs:32, et
`Payment.Capture` exige l'état). Le **rejeu de webhook** est absorbé par le même test d'état.
Le **remboursement** est idempotent sur le chemin gRPC (clé d'idempotence, Payment.cs:205) mais
pas sur le chemin webhook. Enfin, l'**initiation** est protégée par `GetByOrderAsync` + une
réconciliation PSP (InitiatePaymentCommandHandler.cs:75-106) — correct, mais sans contrôle de
propriété (ci-dessus).

## B.5 Commande vendeur (`SellerOrder`)

**L'agrégat n'existe pas.** Recherche exhaustive de `SellerOrder` dans tout le dépôt :
aucune classe, aucune table, aucune migration, aucun `DbSet`. Ce qu'on trouve :

- un champ `Guid? SellerOrderId` dans le contrat de retour
  (`shared/contracts/HBA.Ordering.Contracts/OrderingContracts.cs:85`), **toujours renseigné à
  `null`** par la seule implémentation
  (`services/marketplace/order-service/src/HBA.Order.Infrastructure/Public/OrderingModuleApi.cs:66`) ;
- un `SellerOrderConfirmedNotificationHandler` qui, malgré son nom, consomme
  `OrderConfirmedIntegrationEvent` — c'est-à-dire la commande **acheteur**
  (`services/common/notification-service/…/EventHandlers/SellerOrderNotificationHandler.cs:36`).

Conséquence : une commande multi-vendeurs n'est jamais éclatée. Le vendeur ne dispose que d'une
lecture filtrée (`GET /api/sellers/{sellerId}/orders`, `OrderEndpoints.cs:120`) et **d'aucune
transition** — pas d'acceptation, pas de préparation, pas d'expédition côté vendeur. Le
commentaire d'`OrderEndpoints.cs:117-118` l'assume : « c'est ici que viendront les routes de
confirmation et de préparation qu'`ORDER_MANAGER` attend ».

Sévérité : **HIGH** (le parcours vendeur du cahier n'a pas de support ; le multi-vendeur est
structurellement impossible).

## B.6 Livraison → livré

```
Acteur: système (Kafka) puis livreur
Point d'entrée: OrderConfirmedIntegrationEvent (service.order.v1, écouté)
  → services/marketplace/order-service/src/HBA.Order.Api/Integration/
       CreateDeliveryOnOrderConfirmedHandler.cs:87 (DI : HBA.Order.Api/Program.cs:75-77)
Services impliqués: order-service → inventory-service (lieu), delivery-service (course)
Appels gRPC: OrderingApi.GetOrder (:115), InventoryApi.GetLocation (:187),
  DeliveryApi.CreateDelivery (:273) — deadline 5 s, aucun retry.
  `CreateDelivery` est **idempotent côté serveur** : dédoublonnage par (Reference, Source)
  — CreateDeliveryCommand.cs:98-120. Un rejeu Kafka ne crée donc pas deux courses.
Événements Kafka:
  • DeliveryCompletedIntegrationEvent (delivery-service → service.delivery.v1, **écouté**)
      → order-service `MarkOrderDeliveredOnDeliveryCompletedHandler`
        (MarkOrderDeliveredOnDeliveryCompletedHandler.cs:104 ; DI OrderingModuleInstaller.cs:74)
      → `MarkOrderDeliveredCommand` → `Order.MarkDelivered()` (Order.cs:392-412)
      → `OrderDeliveredIntegrationEvent` (service.order.v1) → payment-service libère l'escrow
  • DeliveryCancelledIntegrationEvent → `HoldOrderOnDeliveryCancelledHandler`
    (Program.cs:95) → arbitrage
États successifs: Confirmed → (course créée) → Delivered
Erreurs possibles: multi-lieux d'expédition → **arbitrage** `UnderReview` avec motif
  (CreateDeliveryOnOrderConfirmedHandler.cs:142-184) ; lieu ou adresse manquants → exception
  (rejeu Kafka × 3 puis abandon Critical) ; devis refusé → 2ᵉ tentative sans devis (:302-322)
Compensations: `CancelDeliveryOnOrderCancelledHandler` (Program.cs:99) — les deux sens sont
  branchés, avec garde anti-boucle
Points de blocage:
  ⛔ **Aucune course n'est jamais créée**, parce que `OrderConfirmed` n'est jamais publié :
     la confirmation dépend de `PaymentCaptured` (§B.4).
Défauts:
  [HIGH] `order-service` **n'enregistre aucun `IConsumerInbox`** (OrderingModuleInstaller.cs,
    à comparer à CatalogModuleInstaller.cs:90). Tous ses consumers sont non idempotents par
    construction ; ils ne survivent au rejeu que par les gardes d'état des agrégats
    (`MarkPaid` exige `AwaitingPayment`, `MarkDelivered` exige `Confirmed|UnderReview`) et par
    l'idempotence de `CreateDelivery`. La protection est accidentelle.
  [MEDIUM] Une commande donne UNE course : le multi-colis a disparu avec Shipping
    (documenté MarkOrderDeliveredOnDeliveryCompletedHandler.cs:70-80). Une commande éclatée
    entre deux lieux part en arbitrage manuel.
Statut: BROKEN (par dépendance à §B.4) — la mécanique interne, elle, est COHERENT
```

**Question 8 — la livraison est-elle créée, et par quoi ?** Oui, par
`CreateDeliveryOnOrderConfirmedHandler` (order-service, composition root), sur
`OrderConfirmedIntegrationEvent`, en `Type: "Standard"`, `Source: "HbaExpress"`, référence
`ORDER-{guid}` (`OrderDeliveryReference.For`, MarkOrderDeliveredOnDeliveryCompletedHandler.cs:50).

**Question 9 — une livraison livrée met-elle la commande à jour ?** Oui, le câblage est complet
et le consumer est réellement enregistré (`OrderingModuleInstaller.cs:74`), sur un topic
**écouté**. Le résultat de la commande est inspecté (`SagaOutcome.Exiger`, :139). Mais comme la
commande n'atteint jamais `Confirmed`, `MarkDelivered` échouerait sur l'état si une course
existait.

## B.7 Avis

```
Acteur: acheteur authentifié
Point d'entrée: POST /api/reviews → /api/engagement/reviews
  services/common/review-service/src/HBA.Engagement.Api/Endpoints/EngagementEndpoints.cs:24
  → SubmitReviewCommandHandler
    (…/Reviews/Commands/SubmitReview/SubmitReviewCommandHandler.cs:30)
Services impliqués: review-service → order-service (gRPC)
Appels gRPC: OrderingApi.GetOrder (:32) — deadline 5 s, lecture
Événements Kafka: ReviewPublished, SellerRatingRecomputed → service.review.v1 → **[TOPIC MORT]**
  (consommateurs déclarés : notification-service, seller-service — jamais atteints ;
   la note vendeur n'est donc jamais recalculée)
États successifs: Review créée `IsVerifiedPurchase = true`
Erreurs possibles: `reviews.order.not_found`, `reviews.not_owner` (403),
  `reviews.order_not_confirmed` (409), `reviews.product_not_in_order` (409),
  `reviews.already_reviewed` (409)
Compensations: sans objet
Points de blocage: la note vendeur n'est jamais recalculée ([TOPIC MORT]).
Défauts:
  [HIGH] **L'avis n'est PAS conditionné à une livraison effective** (question 10) : le handler
    accepte `Confirmed` OU `Delivered` — SubmitReviewCommandHandler.cs:47-52. Le commentaire
    l'assume (« l'app n'affiche Noter qu'après livraison ») : la garde est déportée dans le
    client. Un acheteur peut noter dès la capture du paiement, avant toute remise.
  [MEDIUM] `SellerRatingRecomputed` n'atteint jamais seller-service : la note affichée sur la
    boutique reste figée. — ReviewDomainEventHandlers.cs:74 ; consumer SellerRatingHandler.cs:43
Statut: PARTIAL
```

## B.8 Retour et remboursement — **inatteignable**

```
Acteur: acheteur authentifié
Point d'entrée déclaré: POST /api/v1/marketplace/returns
  services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Api/
    Endpoints/CustomerReturnsEndpoints.cs:15-16
Point d'entrée RÉEL: aucun. **La passerelle n'a aucune route vers return-refund-service.**
  Aucune des 44 routes de `ReverseProxy:Routes` ne vise `/returns` ni un cluster
  `ReturnRefund` ; `ServicesOptions` ne déclare pas la clé (ServicesOptions.cs:26-68) alors
  que `docker-compose.dev.yml:1312` pose bien `SERVICES__RETURNREFUND` — variable ignorée,
  puisque `Resolve` n'a pas de branche pour elle (ServicesOptions.cs:76-93).
Services impliqués (si on l'atteignait): return-refund-service → order-service (gRPC),
  payment-service (gRPC)
Appels gRPC:
  • OrderingApi.GetOrderReturnContext (OrderGrpcClient.cs:15) — mapping d'erreurs correct
    (timeout et indisponibilité distingués, :24-34)
  • FinancialApi.RefundPayment (PaymentGrpcClient.cs:24) — porte `IdempotencyKey`,
    `ReturnId`, `RefundId`
Événements Kafka: **aucun**. `ReturnRefundKafkaProducer` ne contient qu'une constante de nom
  (Kafka/Producers/ReturnRefundKafkaProducer.cs:3-6) et `ReturnRefundKafkaConsumers` qu'un
  tableau de noms de topics — sans le moindre code
  (Kafka/Consumers/ReturnRefundKafkaConsumers.cs:3-14), et ces noms
  (`marketplace.order.delivered`, `payment.refund.succeeded`…) ne correspondent à AUCUNE
  convention du dépôt.
États successifs: ReturnRequest.Created → Approved → … (ReturnStateMachine.cs)
Erreurs possibles: `return.idempotency_required`, `return.item_not_in_order`,
  `return_refund.order_not_returnable`, `return.window_expired`, `return.not_eligible`
Compensations: aucune
Points de blocage:
  ⛔ aucune route passerelle ;
  ⛔ **`ExecuteRefundCommand` n'a AUCUN appelant.** Recherche exhaustive dans tout le dépôt :
     le type n'apparaît que dans sa déclaration (ReturnLifecycleCommands.cs:17) et dans son
     handler (ExecuteRefund/ExecuteRefundCommandHandler.cs:10). Aucun endpoint, aucun worker,
     aucun consumer ne l'envoie. Un retour approuvé n'aboutit jamais à un remboursement.
  ⛔ les trois `BackgroundService` du service sont des coquilles : `ExpireReturnsWorker`,
     `RefundRetryWorker` et `OutboxPublisherWorker` journalisent « active » puis rendent
     `Task.CompletedTask` — BackgroundJobs/ReturnRefundWorkers.cs:12-41.
Défauts:
  [CRITICAL] **`POST /returns` n'identifie pas l'appelant.** La signature du handler d'endpoint
    ne prend ni `ClaimsPrincipal` ni acheteur ; `CustomerId` est lu depuis le contexte de
    commande renvoyé par order-service (CreateReturnCommand.cs:96). Le groupe n'est
    qu'`Authenticated`. N'importe quel inscrit peut donc ouvrir un retour sur la commande d'un
    tiers dès qu'il en connaît l'identifiant — et `GET /{id}` et `/{id}/timeline` n'ont pas
    davantage de contrôle de propriété (CustomerReturnsEndpoints.cs:25-27, :34, :44), ce qui
    fournit au passage les `OrderItemId` nécessaires.
  [CRITICAL] **Retours et remboursements répétables sans limite.**
    `OrderingModuleApi.GetOrderReturnContextAsync` renvoie en dur
    `AlreadyReturnedQuantity: 0` (:54) et `AlreadyRefundedAmount: 0m` (:71). Aucun retour
    passé n'est donc jamais décompté : le même article peut être retourné et remboursé
    autant de fois qu'on ouvre de dossiers. Le plafond côté paiement
    (`Payment.BeginRefund`, RefundableAmount, Payment.cs:227-234) est le seul filet restant.
  [HIGH] **La fenêtre de retour est calculée sur la mauvaise date** :
    `DeliveredAtUtc: order.CreatedAtUtc` (OrderingModuleApi.cs:67). `ReturnEligibilityPolicy`
    compare `nowUtc > deliveredAtUtc.AddDays(policy.ReturnWindowDays)`
    (ReturnEligibilityPolicy.cs:25) : la fenêtre court depuis la CRÉATION de la commande, pas
    depuis la remise. Sur une livraison lente, l'acheteur perd son droit avant d'avoir reçu.
  [MEDIUM] `SellerOrderId: null` (OrderingModuleApi.cs:66) : le retour ne peut jamais être
    rattaché à une commande vendeur — cohérent avec §B.5, mais le champ ment.
Statut: BROKEN
```

**Question 11 — le retour est-il conditionné à une éligibilité vérifiée côté serveur ?** Oui pour
l'essentiel : `GetOrderReturnContextAsync` rend `null` si la commande n'est pas `Delivered` ou
n'a pas de paiement (`OrderingModuleApi.cs:40`), le contrôle est donc bien côté order-service et
non côté client ; les lignes demandées sont confrontées aux lignes de la commande
(`CreateReturnCommand.cs:57-61`) ; la fenêtre et la politique sont évaluées dans le domaine
(`ReturnEligibilityPolicy`). **Mais** l'éligibilité s'appuie sur trois valeurs fausses (date de
livraison, quantité déjà retournée, montant déjà remboursé) et sur une identité non vérifiée.

**Question 12 — le remboursement passe-t-il par le service de paiement ?** Oui, quand il est
déclenché : `ExecuteRefundCommandHandler.cs:63-70` appelle `FinancialApi.RefundPayment` avec la
clé d'idempotence du remboursement, implémenté côté payment-service
(`FinancialGrpcService.cs:15-61` → `RefundPaymentCommandHandler`,
`PaymentLifecycleCommands.cs:139`). Le chemin est réel et correct. Il n'est simplement **jamais
emprunté** (aucun appelant d'`ExecuteRefundCommand`). Le seul remboursement effectivement câblé
est `RefundPaymentOnOrderCancelledHandler`, qui consomme `OrderCancelled` — donc le chemin
« annulation », pas le chemin « retour ».

---

# C. Commande food

## C.1 Les deux parcours food coexistent, et le neuf est celui que le client atteint

Le dépôt contient **deux chaînes food parallèles**, et c'est la source de la plupart des
incohérences :

| | Chaîne « héritée » | Chaîne « nouvelle » |
|---|---|---|
| panier | `cart-service`, `POST /api/cart/food-items` (CommerceEndpoints.cs) | `food-cart-service`, `POST /api/food/cart/items` |
| commande | `order-service`, `POST /api/orders` avec `Kind=Food` | `food-order-service`, `POST /api/food/orders` |
| agrégat | `Order` (Kind=Food) | `MealOrder` |
| ticket cuisine | `restaurant-service`, sur `OrderConfirmed` (Kind=Food) | **aucun consommateur** |
| paiement | `InitiatePayment` (lit `IOrderingModuleApi`) | **aucun chemin** |

**Les deux sont routées par la passerelle** : `/api/cart/**` → cart-service (route `cart`) et
`/api/food/cart/**` → FoodCart (route `food-cart`, ordre 5, prioritaire sur `food-read`/`food-write`
d'ordre 10/11 — le commentaire de `appsettings.json:436` explique correctement l'ordonnancement).
Les clusters `FoodCart` et `FoodOrder` reçoivent bien une adresse : `ServicesOptions` les déclare
`[Required, Url]` (ServicesOptions.cs:67-68) et `docker-compose.dev.yml:1330-1331` pose
`SERVICES__FOODCART` / `SERVICES__FOODORDER`. **Le routage est donc bon** — mais
`appsettings.json` ne renseigne pas ces deux clés dans sa section `Services` (14 clés seulement),
si bien que **la passerelle refuse de démarrer** sans ces deux variables d'environnement ; et
`appsettings.Development.json` n'a que 13 clés (ni `Promotion`, ni `FoodCart`, ni `FoodOrder`) :
un lancement hors compose échoue à la validation `[Required]`.

Trois services food sont **des maquettes en mémoire, sans base, sans authentification et sans
route passerelle** :

- `menu-service` — `MenuStore` (`ConcurrentDictionary`), `MenuEndpoints.cs:9-11`, préfixe
  `/api/v1/menus` absent de la passerelle ;
- `availability-service` — `AvailabilityStore`, `AvailabilityEndpoints.cs:9-11`, `/api/v1/availability` ;
- `kitchen-prep-service` — `KitchenStore` (`KitchenStore.cs:6-28`), `/api/v1/kitchen`. `MarkReady`
  bascule un enregistrement en mémoire et **ne publie rien** ;
- `food-review-service` — `ReviewStore.cs:9-24`, `/api/v1/food/reviews`, `Program.cs` sans aucune
  authentification, `CustomerId` fourni **dans le corps de la requête**.

Le vrai menu, la vraie disponibilité et le vrai ticket de cuisine vivent dans `restaurant-service`
(`FoodEndpoints.cs:101-105, :120-137, :179`).

## C.2 Restaurant → menu → disponibilité

```
Acteur: visiteur anonyme
Point d'entrée: GET /api/food/restaurants, /restaurants/{id}, /restaurants/{id}/menu
  → route `food-read` (GET, anonymous) → restaurant-service
  → services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Endpoints/FoodEndpoints.cs:29-31
  Agrégation : GET /api/v1/bff/client/food/restaurants/{id}
  → apps/api-gateway/src/HBA.Gateway.Api/Controllers/Bff/ClientFoodController.cs:55
Services impliqués: restaurant-service
Appels gRPC: aucun sur le chemin public
Événements Kafka: aucun
États successifs: filtre `IsPubliclyVisible` sur le restaurant ; disponibilité d'un plat portée
  par `SetItemAvailabilityAsync` (FoodEndpoints.cs:179)
Erreurs possibles: 404
Compensations: sans objet
Points de blocage: aucun
Défauts:
  [MEDIUM] `availability-service` et `menu-service` dupliquent en maquette une responsabilité
    déjà tenue par restaurant-service, sans base, sans route et sans consommateur.
Statut: COHERENT
```

## C.3 Panier food

```
Acteur: client authentifié
Point d'entrée: POST /api/food/cart/items
  → route `food-cart` (ordre 5, Authenticated) → food-cart-service
  → services/food/food-cart-service/src/HBA.Food.Cart.Api/Endpoints/FoodCartEndpoints.cs:22
  → AddItemToFoodCartCommandHandler
    (…/Application/Commands/Carts/FoodCartCommands.cs:45)
Services impliqués: food-cart-service → restaurant-service (gRPC `FoodApi.GetMenuItem`)
Appels gRPC: FoodApi.GetMenuItem (FoodCartCommands.cs:66) — deadline 5 s, lecture
Événements Kafka:
  • consommé : MealOrderPlacedIntegrationEvent → `CloseFoodCartOnMealOrderPlacedHandler`
    (DI FoodCartModuleInstaller.cs:54) — publié par food-order-service sur
    `service.food-order.v1` → **[TOPIC MORT]** : le panier food n'est JAMAIS clos.
États successifs: (aucun) → FoodCart.Active
Erreurs possibles: `food_cart.item_not_found`, `food_cart.item_unavailable`,
  `food_cart.option_duplicated`, bornes de groupes d'options
Compensations: sans objet
Points de blocage: le panier reste `Active` après la commande.
Défauts:
  [HIGH] Le panier food n'est jamais clôturé — `CloseFoodCartOnMealOrderPlacedHandler`
    n'est jamais atteint (FoodCartModuleInstaller.cs:54 vs KafkaEventBusOptions.cs:21).
    Combiné à la garde d'idempotence de `PlaceMealOrderCommandHandler` (:122-126, qui rend la
    commande existante pour le même `CartId`), **le client ne peut plus jamais passer une
    seconde commande de repas** : son panier actif reste attaché à la commande précédente.
    C'est un blocage définitif du compte sur le parcours food.
  [MEDIUM] Le commentaire de `Coter` annonce « la commande la recalcule au moment d'être
    passée » (FoodCartCommands.cs:147) : c'est faux. `PlaceMealOrderCommandHandler` recopie le
    snapshot du panier (`l.UnitBaseAmount`, `l.FinalUnitPrice`,
    PlaceMealOrderCommand.cs:165-174) sans relire la carte.
  [MEDIUM] Même bouchon de tarification que la marketplace : `NeutralPricingModuleApi`
    (Infrastructure/Public/NeutralPricingModuleApi.cs) — aucun coupon ne s'applique jamais.
Statut: PARTIAL
```

## C.4 Commande food → paiement — **point de rupture n°2**

```
Acteur: client authentifié
Point d'entrée: POST /api/food/orders
  → route `food-orders` (Authenticated) → food-order-service
  → services/food/food-order-service/src/HBA.Food.Order.Api/Endpoints/MealOrderEndpoints.cs:18
  → PlaceMealOrderCommandHandler
    (…/Application/Commands/Orders/PlaceMealOrderCommand.cs:93)
Services impliqués: food-order-service → food-cart-service, restaurant-service,
  delivery-service (tous gRPC)
Appels gRPC (deadline 5 s, aucun retry, tous en lecture) :
  • FoodCartsApi.GetActiveCart (:95)
  • FoodApi.GetRestaurant      (:139)  — vérifie `IsPubliclyVisible` ET `AcceptsOrdersNow`
  • DeliveryApi.LookupQuote    (:302)  — devis OBLIGATOIRE pour un repas (:293-300)
Événements Kafka publiés:
  • MealOrderPlaced     (MealOrderDomainEventHandlers.cs:20) → service.food-order.v1 [MORT]
  • MealOrderConfirmed  (:66)  → service.food-order.v1 [MORT] **et sans aucun consommateur**
  • MealOrderCancelled  (:108) → [MORT], **aucun consommateur**
  • MealOrderDelivered  (:181) → [MORT], **aucun consommateur**
  • MealOrderUnderReview / ResumedAfterReview → [MORT], aucun consommateur
Événements Kafka consommés (DI MealOrderingModuleInstaller.cs:71-92) :
  PaymentCaptured, PaymentFailed, FoodOrderRejected, FoodOrderCancelled, FoodOrderDelivered
  — les deux premiers sur `service.payment.v1` [MORT], les trois autres sur
  `service.restaurant.v1` [MORT].
États successifs: MealOrder.Pending → AwaitingPayment (PlaceMealOrderCommand.cs:252)
Erreurs possibles: `food_ordering.cart_empty`, `cart_without_restaurant`,
  `restaurant_not_found`, `restaurant_closed`, `below_minimum`,
  `shipping_address_required`, `address_incomplete`, `delivery_quote_*`
Compensations: aucune n'est nécessaire à ce stade (rien n'est réservé — assumé et argumenté
  PlaceMealOrderCommand.cs:244-251)
Points de blocage:
  ⛔ **Une commande de repas ne peut pas être payée.** Le seul point d'entrée de paiement,
     `POST /api/payments`, lit la commande via `IOrderingModuleApi` — c'est-à-dire
     **order-service, la marketplace** (InitiatePaymentCommandHandler.cs:64). Un identifiant de
     `MealOrder` y est inconnu → `payments.order.not_found` (404). Il n'existe aucune commande
     d'initiation propre à food (recherche exhaustive : aucun autre appelant de
     `Payment.Create`).
  ⛔ **Même si un paiement existait, il porterait le mauvais type.** `Payment.Create` est appelé
     avec `PaymentOrderType.Marketplace` **en dur** (InitiatePaymentCommandHandler.cs:122), et
     `ConfirmMealOrderOnPaymentCapturedHandler` sort immédiatement si
     `OrderType != "FOOD"` (PaymentOutcomeHandlers.cs:57-60). Le commentaire de
     `InitiatePaymentCommandHandler.cs:114-120` et celui de `PaymentIds.cs:49-58`
     (« le Food n'ayant pas encore de chemin de paiement ») reconnaissent tous deux le trou.
  ⛔ **`MealOrderConfirmedIntegrationEvent` n'a aucun consommateur.** Recherche exhaustive :
     le type n'apparaît que dans sa définition (MealOrderIntegrationEvents.cs:66) et dans son
     publieur (MealOrderDomainEventHandlers.cs:67). Aucun ticket de cuisine ne serait ouvert
     même si le paiement aboutissait — `restaurant-service` n'écoute que
     `OrderConfirmedIntegrationEvent` de la marketplace
     (FoodOrderBridgeHandlers.cs:79-80 ; DI Program.cs:66).
Défauts:
  [CRITICAL] Aucun chemin de paiement pour `MealOrder` : toute commande food passée par
    `/api/food/orders` reste `AwaitingPayment` définitivement.
    — InitiatePaymentCommandHandler.cs:64, :122
  [CRITICAL] `MealOrderConfirmed` publié pour personne : le pont vers la cuisine n'existe pas
    sur la chaîne neuve. — MealOrderDomainEventHandlers.cs:67
  [HIGH] `MealOrderCancelled` et `MealOrderDelivered` sans consommateur : même si la chaîne
    tournait, aucun remboursement et aucune libération d'escrow ne suivraient.
    — MealOrderDomainEventHandlers.cs:108, :181
  [HIGH] **Collision d'identifiants entre les deux chaînes.** `FoodOrderRejected` /
    `FoodOrderCancelled` / `FoodOrderDelivered` sont émis par restaurant-service avec un
    `OrderId` qui est celui de la commande **marketplace** (le ticket est créé depuis
    `OrderConfirmedIntegrationEvent`, FoodOrderBridgeHandlers.cs:158-163). Or ces trois
    événements sont consommés **à la fois** par order-service
    (OrderingModuleInstaller.cs:84-99) et par food-order-service
    (MealOrderingModuleInstaller.cs:84-92, `KitchenOutcomeHandlers.cs:52,94,130`), qui les
    interprète comme des `MealOrderId`. Un seul des deux peut avoir raison ; le second
    échouera systématiquement en « commande introuvable ».
  [MEDIUM] food-order-service et food-cart-service n'enregistrent **aucun `IConsumerInbox`**
    (MealOrderingModuleInstaller.cs, FoodCartModuleInstaller.cs) : consumers non idempotents.
Statut: BROKEN
```

## C.5 Cuisine (chaîne héritée, la seule qui produise un ticket)

```
Acteur: système, puis restaurateur (rôle FoodPartner / personnel)
Point d'entrée: OrderConfirmedIntegrationEvent (Kind == "Food"), service.order.v1 (écouté)
  → services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Integration/
      FoodOrderBridgeHandlers.cs:79 (DI Program.cs:66)
  → ReceiveFoodOrderCommand → ticket FoodOrder
Gestes restaurateur (restaurant-service, FoodEndpoints.cs:102-105) :
  POST /api/food/restaurants/{rid}/orders/{fid}/accept | reject | preparing | ready
Services impliqués: restaurant-service → order-service (gRPC relecture des lignes, :121),
  delivery-service (création de course quand le sac est prêt)
Événements Kafka:
  • FoodOrderReadyForPickup (restaurant-service → service.restaurant.v1) → **[TOPIC MORT]**
      consumer : `CreateDeliveryOnFoodOrderReadyHandler`, dans le MÊME service
      (FoodOrderBridgeHandlers.cs:215 ; DI Program.cs:70). restaurant-service publie sur
      `service.restaurant.v1` et s'abonne à `service.food.v1` : **il n'entend pas ses propres
      événements**.
  • FoodOrderRejected / FoodOrderCancelled / FoodOrderDelivered → [TOPIC MORT]
  • DeliveryPickedUp / DeliveryCompleted (delivery-service → service.delivery.v1, écouté)
      → consommés par restaurant-service (Program.cs:77, :81) — ce sens-là fonctionne
États successifs: ticket Received → Accepted → Preparing → ReadyForPickup → PickedUp → Delivered
Erreurs possibles: refus du restaurant, minimum de commande, restaurant fermé
Compensations: refus/annulation → `CancelOrderOnFoodOrderRefusedHandlers` (order-service,
  OrderingModuleInstaller.cs:84-85) → annulation → `RefundPaymentOnOrderCancelledHandler`.
  Le maillon final (annulation → remboursement) est vivant ; les deux premiers sont [MORT].
Points de blocage:
  ⛔ **« Repas prêt » ne déclenche aucune course.** Le ticket passe `ReadyForPickup`,
     l'événement part sur un topic que personne n'écoute — restaurant-service compris — et
     `CreateDeliveryOnFoodOrderReadyHandler` n'est jamais invoqué. Le repas est prêt, aucun
     livreur n'est demandé, la commande reste « confirmée ». C'est exactement le critère de
     rupture « cuisine prête mais commande food toujours en préparation ».
  ⛔ **« Repas refusé » ne rembourse pas.** `FoodOrderRejected` n'atteint pas order-service :
     le client est débité pour un repas qui n'existera jamais.
Défauts:
  [CRITICAL] Prêt en cuisine → aucune course, commande jamais livrée, restaurateur jamais réglé
    — FoodOrderBridgeHandlers.cs:215 (consumer) vs KafkaEventBusOptions.cs:21 (abonnement)
  [CRITICAL] Refus du restaurant → commande jamais annulée, jamais remboursée
    — OrderingModuleInstaller.cs:84 ; KitchenOutcomeHandlers.cs:31
  [MEDIUM] `kitchen-prep-service` est une maquette mémoire sans route ni événement : marquer
    un ticket « READY » chez lui n'a strictement aucun effet — KitchenStore.cs:17-27
Statut: BROKEN
```

## C.6 Avis food

```
Acteur: client authentifié
Point d'entrée déclaré: POST /api/v1/food/reviews
  services/food/review-service/src/HBA.Food.Review.Api/Endpoints/ReviewEndpoints.cs:10
Point d'entrée RÉEL: aucun — pas de route passerelle, pas de cluster `FoodReview`
  (ServicesOptions.cs n'a pas la clé ; docker-compose:1323 pose SERVICES__FOODREVIEW, ignoré)
Services impliqués: food-review-service seul
Appels gRPC: aucun
Événements Kafka: aucun
États successifs: FoodReview créée en mémoire
Erreurs possibles: `ArgumentOutOfRangeException` sur la note (500, pas d'enveloppe §25)
Compensations: aucune
Points de blocage: service inatteignable ; données volatiles.
Défauts:
  [CRITICAL] **Aucune authentification.** `Program.cs` n'appelle ni `AddAuthentication`, ni
    `UseHbaService`, ni `RequireAuthorization` ; `CustomerId`, `OrderId` et `RestaurantId`
    viennent tous du corps de la requête (`CreateReviewRequest`, ReviewStore.cs:26). Si ce
    service était exposé, n'importe qui noterait n'importe quel restaurant au nom de n'importe
    qui. — HBA.Food.Review.Api/Program.cs:1-13 ; ReviewEndpoints.cs:10
  [HIGH] Aucune vérification d'achat ni de livraison : la note n'est adossée à rien.
  [HIGH] Stockage en `ConcurrentDictionary` : tout est perdu au redémarrage — ReviewStore.cs:9
Statut: BROKEN
```

---

# D. Application explicite des critères de rupture

| Critère | Vérifié ? | Preuve |
|---|---|---|
| **Paiement réussi mais commande jamais `PAID`** | **OUI, systématique** | `PaymentCaptured` publié sur `service.payment.v1` (KafkaEventNaming.cs:38 + compose:1033) ; `SubscribeTopics` ne contient que `service.financial.v1` (KafkaEventBusOptions.cs:21). Consumer `ConfirmOrderOnPaymentCapturedHandler` (PaymentOutcomeHandlers.cs:35) jamais invoqué. |
| **Commande annulée mais réservation de stock toujours active** | **OUI** | (a) `CancelOrderOnPaymentFailedHandler` (OrderingModuleInstaller.cs:62) jamais invoqué → stock jamais libéré après échec de paiement. (b) Exception au milieu de la boucle de réservation → aucune compensation (PlaceOrderCommandHandler.cs:276-297). (c) `RefundOrderAfterReviewCommand` n'appelle aucune libération, ce qui est argumenté (OrderLifecycleCommands.cs:121-130) mais laisse la marchandise décrémentée. |
| **Livraison livrée mais commande toujours en cours** | Non atteignable, mais le câblage est bon | `MarkOrderDeliveredOnDeliveryCompletedHandler` est enregistré (OrderingModuleInstaller.cs:74) et `DeliveryCompleted` circule sur un topic écouté. La rupture est en amont : aucune course n'est créée. |
| **Remboursement effectué mais commande/portefeuille non mis à jour** | **OUI** | `PaymentRefunded` est publié sur `service.payment.v1` [MORT] ; seul notification-service le consomme, et il ne l'entend pas. Aucun consumer de `PaymentRefunded` dans order-service ni dans return-refund-service. Et `AlreadyRefundedAmount` renvoyé par order-service est **codé en dur à 0** (OrderingModuleApi.cs:71). |
| **Cuisine prête mais commande food toujours en préparation** | **OUI** | `FoodOrderReadyForPickup` publié sur `service.restaurant.v1` ; restaurant-service, qui est son propre consommateur (Program.cs:70), s'abonne à `service.food.v1`. Aucune course n'est demandée (FoodOrderBridgeHandlers.cs:215). |
| **Commande vendeur jamais créée** | **OUI, par absence d'agrégat** | Aucun type `SellerOrder` dans le dépôt ; `SellerOrderId` toujours `null` (OrderingModuleApi.cs:66). |

---

# E. Réponses directes aux douze questions du parcours marketplace

1. **Produit `PUBLISHED` à l'ajout au panier ?** Oui — `offer.IsPurchasable`
   (`OfferStatus.cs:98` : `Active` seul) et `product.IsVisible`
   (`ProductsGrpc.cs:400` : `Published`, plus `Active` en compatibilité) sont vérifiés côté
   serveur dans `AddItemToCartCommandHandler`. **À la commande ? Non** — aucun appel catalogue
   dans `PlaceOrderCommandHandler.cs:61-138`. **[HIGH]**
2. **Prix revalidé côté serveur ?** Non revalidé, mais **non imposable par le client** : le
   montant vient de `offer.EffectivePrice` à l'ajout, et aucune requête ne transporte de prix.
   Le snapshot est ensuite figé jusqu'à la commande (`PlaceOrderCommandHandler.cs:131-138`).
   Les frais de port, eux, sont imposés par le serveur depuis le devis (:495). **[HIGH]**
3. **Stock réservé, et quand ?** Réservé **avant** le paiement (`PlaceOrderCommandHandler.cs:276`),
   confirmé (décrémenté) à la capture (`OrderLifecycleCommands.cs:224`), libéré à l'annulation
   (:314). L'ordre est le bon. Réserve : SKU non suivi ⇒ réservation « réussie » sans contrôle
   (`InventoryModuleApi.cs:98-106`).
4. **Double commande sur le même panier ?** Non — lecture `GetByCartAsync` + index unique
   `CartId` (`OrderConfiguration.cs:147`).
5. **Paiement idempotent ?** Capture et rejeu de webhook : oui, par test d'état
   (`GatewayConfirmationCommands.cs:32-34`). Remboursement gRPC : oui, clé d'idempotence
   (`Payment.cs:205`). Remboursement par webhook : **non** (`Payment.Refund()` passe
   `externalRefundId: null`, `Payment.cs:157-173`). Initiation : oui, mais sans contrôle de
   propriété. **[MEDIUM/HIGH]**
6. **Stock libéré si le paiement échoue — quel consumer ?** Le consumer prévu est
   `CancelOrderOnPaymentFailedHandler` (`PaymentOutcomeHandlers.cs:71`), enregistré en DI
   (`OrderingModuleInstaller.cs:62`), et il libère effectivement
   (`OrderLifecycleCommands.cs:311-318`). **Il n'est jamais déclenché** : topic mort. **[CRITICAL]**
7. **`SellerOrder` créée ?** **L'agrégat n'existe pas** (§B.5). **[HIGH]**
8. **Livraison créée, par quoi ?** Par `CreateDeliveryOnOrderConfirmedHandler`
   (order-service, `Program.cs:75-77`), sur `OrderConfirmed`, référence `ORDER-…`. Idempotente
   côté delivery-service (`CreateDeliveryCommand.cs:98`).
9. **Livrée → commande mise à jour ?** Oui, câblage complet et topic vivant
   (`OrderingModuleInstaller.cs:74`) — mais inatteignable faute de `Confirmed`.
10. **Avis conditionné à une livraison ?** **Non** : `Confirmed` suffit
    (`SubmitReviewCommandHandler.cs:47-52`). **[HIGH]**
11. **Retour conditionné à une éligibilité serveur ?** Oui dans le principe
    (`OrderingModuleApi.cs:40` exige `Delivered`), mais l'éligibilité repose sur trois valeurs
    fausses (`DeliveredAtUtc = CreatedAtUtc` :67, `AlreadyReturnedQuantity: 0` :54,
    `AlreadyRefundedAmount: 0m` :71) et **le demandeur n'est pas identifié**
    (`CustomerReturnsEndpoints.cs:25`). **[CRITICAL]**
12. **Remboursement par le service de paiement ?** Oui, techniquement
    (`ExecuteRefundCommandHandler.cs:63` → `FinancialApi.RefundPayment` →
    `RefundPaymentCommandHandler`). Mais `ExecuteRefundCommand` **n'a aucun appelant** dans le
    dépôt. **[CRITICAL]**

---

# F. Tableau des défauts

| # | Sév. | Défaut | Preuve |
|---|---|---|---|
| S-1 | **CRITICAL** | `SubscribeTopics` est une constante de 13 sujets jamais lue depuis la configuration, alors que le topic de publication est dérivé du nom du producteur. 20 des 27 producteurs écrivent sur un sujet que personne n'écoute. Le garde-fou de démarrage ne contrôle que le préfixe. | `KafkaEventBusOptions.cs:21` ; `KafkaEventNaming.cs:38` ; `DependencyInjection.cs:60-90` ; `docker-compose.dev.yml` (27 `KAFKA__PRODUCER`) |
| S-2 | **CRITICAL** | Paiement capturé → commande jamais `Paid`/`Confirmed` : ni stock décrémenté, ni course, ni règlement vendeur. | `PaymentDomainEventHandlers.cs:30` vs `OrderingModuleInstaller.cs:61` |
| S-3 | **CRITICAL** | Paiement échoué → réservations de stock jamais libérées (le consumer existe et fonctionne, il n'est jamais appelé). | `OrderingModuleInstaller.cs:62` ; `OrderLifecycleCommands.cs:311-318` |
| S-4 | **CRITICAL** | Exception (deadline 5 s, `Unavailable`) au milieu de la boucle de réservation : les réservations déjà posées ne sont pas compensées et la commande n'est même pas persistée. Stock immobilisé sans trace. | `PlaceOrderCommandHandler.cs:276-297` |
| S-5 | **CRITICAL** | `POST /api/v1/marketplace/returns` n'identifie pas l'appelant : ouverture d'un retour sur la commande d'un tiers. `GET /{id}` et `/{id}/timeline` sans contrôle de propriété non plus. | `CustomerReturnsEndpoints.cs:15-16, 25, 34, 44` |
| S-6 | **CRITICAL** | Retours/remboursements répétables : `AlreadyReturnedQuantity` et `AlreadyRefundedAmount` codés en dur à zéro. | `OrderingModuleApi.cs:54, 71` |
| S-7 | **CRITICAL** | `ExecuteRefundCommand` sans aucun appelant ; les trois workers de return-refund sont des coquilles ; aucune route passerelle vers le service. Un retour approuvé n'est jamais remboursé. | `ReturnLifecycleCommands.cs:17` ; `ReturnRefundWorkers.cs:12-41` ; `appsettings.json` (aucune route) |
| S-8 | **CRITICAL** | Aucun chemin de paiement pour `MealOrder` : `InitiatePayment` lit la commande marketplace et fige `PaymentOrderType.Marketplace`, que le consumer food rejette. Toute commande food reste `AwaitingPayment`. | `InitiatePaymentCommandHandler.cs:64, 122` ; `PaymentOutcomeHandlers.cs:57` (food) |
| S-9 | **CRITICAL** | `MealOrderConfirmedIntegrationEvent` n'a aucun consommateur : aucun ticket de cuisine sur la chaîne food neuve. | `MealOrderDomainEventHandlers.cs:67` ; recherche exhaustive |
| S-10 | **CRITICAL** | « Repas prêt » ne crée aucune course : restaurant-service publie sur `service.restaurant.v1` et s'abonne à `service.food.v1` — il n'entend pas son propre événement. | `FoodOrderBridgeHandlers.cs:215` ; `Program.cs:70` ; `KafkaEventBusOptions.cs:21` |
| S-11 | **CRITICAL** | Refus du restaurant → commande jamais annulée ni remboursée (même cause). | `OrderingModuleInstaller.cs:84` ; `CancelOrderOnFoodOrderRefusedHandlers.cs:40` |
| S-12 | **CRITICAL** | `food-review-service` : aucune authentification, `CustomerId` dans le corps, stockage en mémoire. | `HBA.Food.Review.Api/Program.cs:1-13` ; `ReviewStore.cs:9-26` |
| S-13 | **HIGH** | `POST /api/payments` sans contrôle de propriété : un tiers ouvre un paiement sur la commande d'autrui et la bloque (`payments.already_exists`). | `FinancialEndpoints.cs:258` ; `InitiatePaymentCommand.cs:10-16` ; `InitiatePaymentCommandHandler.cs:103` |
| S-14 | **HIGH** | Le panier food n'est jamais clos ; combiné à l'idempotence par `CartId`, le client ne peut plus jamais passer une seconde commande de repas. | `FoodCartModuleInstaller.cs:54` ; `PlaceMealOrderCommand.cs:122-126` |
| S-15 | **HIGH** | Le statut « publié » n'est pas revérifié au checkout : une fiche suspendue se commande et se paie. | `PlaceOrderCommandHandler.cs:61-138` (aucun appel catalogue) |
| S-16 | **HIGH** | Le prix n'est jamais revalidé côté serveur : le snapshot du panier est facturé tel quel. | `CartPricer.cs:62` ; `PlaceOrderCommandHandler.cs:131-138` |
| S-17 | **HIGH** | L'avis n'exige pas de livraison : `Confirmed` suffit. | `SubmitReviewCommandHandler.cs:47-52` |
| S-18 | **HIGH** | La fenêtre de retour court depuis la création de la commande, pas depuis la livraison. | `OrderingModuleApi.cs:67` ; `ReturnEligibilityPolicy.cs:25` |
| S-19 | **HIGH** | Collision d'identifiants : `FoodOrderRejected/Cancelled/Delivered` portent un `OrderId` marketplace mais sont aussi consommés par food-order-service comme un `MealOrderId`. | `FoodOrderBridgeHandlers.cs:158-163` ; `KitchenOutcomeHandlers.cs:52, 94, 130` |
| S-20 | **HIGH** | Aucun agrégat `SellerOrder` : pas de commande vendeur, pas de multi-vendeur, pas de transitions vendeur. | recherche exhaustive ; `OrderingModuleApi.cs:66` ; `OrderEndpoints.cs:119-120` |
| S-21 | **HIGH** | Le code OTP est généré puis jeté (`_ = code;`) : aucun canal ne l'envoie. `verify-otp` n'émet aucun jeton. | `OtpChallengeUseCases.cs:90, 141-152` |
| S-22 | **HIGH** | `order-service`, `cart-service`, `inventory-service`, `food-order-service`, `food-cart-service`, `restaurant-service` n'enregistrent aucun `IConsumerInbox` : consumers non idempotents, protégés seulement par les gardes d'état. | `OrderingModuleInstaller.cs` ; `MealOrderingModuleInstaller.cs` vs `CatalogModuleInstaller.cs:90` |
| S-23 | **MEDIUM** | La route publique `/api/auth/*` rend 404 : la passerelle réécrit vers `/api/identity/auth/*` que le service ne sert plus. Le commentaire de `Program.cs` affirme l'inverse. | `appsettings.json` route `auth` ; `IdentityEndpoints.cs:60` ; `HBA.Identity.Api/Program.cs:17-23` |
| S-24 | **MEDIUM** | Le Client BFF est un mort-né : aucune route passerelle, aucun port publié, 11 routes sur 13 en 501. | `ClientEndpoints.cs:13, 22-30` ; `appsettings.json` ; `docker-compose.dev.yml:1218` |
| S-25 | **MEDIUM** | `appsettings.json` (section `Services`) ne déclare ni `FoodCart` ni `FoodOrder`, et `appsettings.Development.json` n'a ni `Promotion` ni ces deux-là, alors que `ServicesOptions` les marque `[Required, Url]` : la passerelle ne démarre hors compose. | `appsettings.json` §Services (14 clés) ; `appsettings.Development.json` (13 clés) ; `ServicesOptions.cs:63-68` |
| S-26 | **MEDIUM** | Le panier lu au checkout vient d'un cache de 2 minutes. | `CartQueries.cs:19` ; `CartModuleApi.cs:17-21` ; `PlaceOrderCommandHandler.cs:63` |
| S-27 | **MEDIUM** | Le webhook de remboursement contourne la clé d'idempotence (`Payment.Refund()` passe `externalRefundId: null`). | `Payment.cs:157-173` vs `Payment.cs:205-234` |
| S-28 | **MEDIUM** | `menu-service`, `availability-service`, `kitchen-prep-service` : maquettes en mémoire, sans base, sans route passerelle, sans événement. Marquer un ticket « READY » n'a aucun effet. | `MenuEndpoints.cs:9-11` ; `AvailabilityEndpoints.cs:9-11` ; `KitchenStore.cs:6-28` |
| S-29 | **MEDIUM** | Frais de livraison à zéro pour toute commande de marchandise sans devis (assumé, journalisé). | `PlaceOrderCommandHandler.cs:353-368` |
| S-30 | **MEDIUM** | `UserLoggedInIntegrationEvent` déclaré et testé, jamais publié : aucune piste d'audit des connexions. | `IdentityIntegrationEvents.cs` (bloc `logged_in`) ; `LoginCommandHandler.cs:232` |
| S-31 | **MEDIUM** | Le rôle par défaut n'est attribué que « si le rôle existe », sans branche d'échec : un compte peut naître sans rôle, en silence. | `RegisterUserCommandHandler.cs:130-134` |
| S-32 | **LOW** | `UserEmailConfirmed` et `TokenRevoked` publiés sans aucun consommateur. | `UserDomainEventHandlers.cs:66` ; `LogoutByRefreshTokenCommand.cs:75` |

---

# G. Synthèse par parcours

| Parcours | Statut | Dernier état réellement atteignable | Premier point de blocage |
|---|---|---|---|
| **A. Inscription / connexion** | **PARTIAL** | `Active` + paire de jetons | OTP : le code n'est jamais envoyé, `verify-otp` n'émet aucun jeton. Route legacy `/api/auth/*` en 404. |
| **B. Achat marketplace** | **BROKEN** | `Order.AwaitingPayment` (paiement encaissé chez le PSP) | `PaymentCaptured` sur un topic que personne n'écoute. Puis, en aval : retour/remboursement sans route ni déclencheur. |
| **C. Commande food** | **BROKEN** | `MealOrder.AwaitingPayment` (chaîne neuve) / ticket `ReadyForPickup` (chaîne héritée) | Aucun chemin de paiement pour `MealOrder` ; `MealOrderConfirmed` sans consommateur ; « repas prêt » ne crée aucune course. |
