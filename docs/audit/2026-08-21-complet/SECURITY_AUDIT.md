# SECURITY_AUDIT — HBA Express

Audit statique du dépôt `/root/audit-src` (1 801 fichiers `.cs`, 16 services + passerelle + 4 BFF).
Chaque constat cite le fichier, la classe/méthode et la ligne. Les constats repris des rapports
existants (`SERVICES_*`, `GRPC_MATRIX`, `KAFKA_EVENT_MATRIX`, `DATABASE_AUDIT`) ont été revérifiés
dans le code ; les preuves ci-dessous sont les miennes.

Convention : `[SÉVÉRITÉ] Titre — fichier:ligne — ce que l'attaquant obtient — correction`.

---

## 1. Authentification

### 1.1 Émission du jeton

`services/common/identity-service/src/HBA.Identity.Infrastructure/Security/JwtTokenGenerator.cs:31-95`

| Élément | Valeur | Preuve |
|---|---|---|
| Algorithme | HS256 (`SymmetricSecurityKey` + `HmacSha256`) | `JwtTokenGenerator.cs:82-83` |
| Durée de vie de l'accès | 15 min (`AccessTokenMinutes`) | `JwtOptions.cs:110` |
| Durée du refresh | 30 jours (`RefreshTokenDays`) | `JwtOptions.cs:111` |
| Claims | `sub`, `email`, `given_name`, `family_name`, `jti`, `security_stamp`, `auth_time` (Integer64), `amr` (n valeurs), `role` (nom long WS-Fed), `permission` | `JwtTokenGenerator.cs:39-80` |
| `nbf` / `exp` | posés | `JwtTokenGenerator.cs:89-90` |

Les rôles sont écrits avec `ClaimTypes.Role`, donc le nom long
`http://schemas.microsoft.com/ws/2008/06/identity/claims/role` ; les vérificateurs alignent
`RoleClaimType` sur la même valeur (`GatewayAuthenticationOptions.cs:57`,
`ServiceHostExtensions.cs:299`). Cohérent.

### 1.2 Validation

Deux implémentations, cohérentes entre elles :

* **Passerelle** — `apps/api-gateway/src/HBA.Gateway.Api/Extensions/AuthenticationExtensions.cs:74-111`.
  `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey` à `true` ;
  `ClockSkew` ramené à 30 s (`GatewayAuthenticationOptions.cs:64`, borné à 5 min par `[Range]`) ;
  `ValidAlgorithms = [HmacSha256]` — l'épinglage ferme la confusion d'algorithme
  (`AuthenticationExtensions.cs:105`). `MapInboundClaims = false` (ligne 72), donc `sub` reste `sub`.
  Un validateur d'options refuse le démarrage si ni `SigningKey` ni `Authority`, si la clé fait
  moins de 32 octets, ou si les deux sont fournis (`GatewayAuthenticationOptions.cs:75-112`).
* **Services** — `shared/common/HBA.Shared.Hosting/ServiceHostExtensions.cs:273-320`. Mêmes
  paramètres, même épinglage (ligne 309), même `ClockSkew` de 30 s (ligne 296). Chaque service
  revalide le jeton indépendamment de la passerelle (commentaire ligne 313-319) — le principe est
  juste ; dix services ne l'appliquent pas (voir **SEC-01**).

`[MEDIUM] SEC-28 — Le socle partagé ne valide pas sa configuration d'authentification au démarrage`
— `shared/common/HBA.Shared.Hosting/ServiceHostExtensions.cs:275-311` — contrairement à la
passerelle, `AddAuthentication` lit `Authentication:Issuer/Audience/SigningKey` sans
`ValidateOnStart` ni garde équivalente à `GatewayAuthenticationOptionsValidator`. `SigningKey` vide
laisse `IssuerSigningKey` nul **et** retire l'épinglage `ValidAlgorithms` : le service démarre et
rend 401 sur tout, sans une ligne de journal qui désigne la configuration. C'est exactement le
scénario décrit dans l'en-tête de `docker-compose.dev.yml:20-26`.
*Correction* : porter le validateur de la passerelle dans `HBA.Shared.Hosting` et l'appeler depuis
`AddHbaService`.

### 1.3 Révocation — le point qui ne tient pas

Le mécanisme est écrit et complet côté domaine :

* `User.RevokeAllSessions()` fait tourner le `SecurityStamp` **et** révoque les refresh tokens
  (`services/common/identity-service/src/HBA.Identity.Infrastructure/Public/IdentityModuleApi.cs:183-199`,
  `User.cs:1012`).
* `IdentityModuleApi.ValidateAccessTokenAsync` compare le `security_stamp` du jeton à celui du
  compte, refuse un compte non `Active`, et travaille avec `ClockSkew = TimeSpan.Zero`
  (`IdentityModuleApi.cs:74-158`).

`[CRITICAL] SEC-06 — La déconnexion, la suspension et la rotation du tampon de sécurité n'invalident
aucun jeton d'accès` — `IdentityModuleApi.cs:74` (jamais appelée) — recherche exhaustive de
`ValidateAccessToken` dans le dépôt : le RPC est déclaré (`shared/proto/identity/v1/identity.proto:29`),
implémenté côté serveur (`shared/contracts/HBA.Identity.Contracts.Grpc/IdentityGrpc.cs:108`) et côté
client (`IdentityGrpc.cs:192`) — et **aucun service ni la passerelle ne l'appelle sur le chemin de
requête**. Les trois seuls consommateurs de `IIdentityModuleApi` hors identity
(`RegisterSellerCommandHandler.cs:21`, `MemberCommands.cs:136`, `NotificationDispatcher.cs:24`)
n'utilisent que `GetUserAsync`. Conséquence : le `security_stamp` est émis, stocké, comparé nulle
part.
*Ce que l'attaquant obtient* : un jeton volé reste valide jusqu'à son expiration naturelle (15 min
+ 30 s de tolérance) **après** un logout, un changement de mot de passe, une suspension de compte ou
une détection de rejeu de refresh token. `LogoutCommandHandler.cs:34` ne révoque que le refresh
token et ne fait pas tourner le tampon — donc même la déconnexion « propre » ne coupe rien
immédiatement.
*Correction* : soit un appel `ValidateAccessToken` sur les gestes sensibles (paiement, retrait,
payout, KYB), soit une liste de révocation partagée (Redis, clé `jti`/`security_stamp`) consultée par
le middleware d'authentification des services ; et `Logout` doit faire tourner le tampon.

### 1.4 Refresh token — correct

`RefreshTokenCommandHandler.cs:44-119`. Le jeton n'est stocké que haché
(`AuthTokenIssuer.cs:57`), la rotation est systématique, le rejeu d'un jeton déjà consommé coupe
toute la chaîne du compte **et est persisté avant de répondre** (`RefreshTokenCommandHandler.cs:69-86`),
la réponse reste un 401 indistinct. Le statut est vérifié **avant** consommation (ligne 60), pour
qu'un refus ne laisse pas de trace. `auth_time` est recopié et jamais rajeuni (ligne 100-115) — c'est
ce qui rend le step-up réel.

### 1.5 Step-up / MFA

* Step-up : `shared/common/HBA.Shared.Hosting/Http/StepUpAuthentication.cs:63-99`. Fenêtre de 5 min
  non configurable, claim absent = refus, `auth_time` dans le futur = refus (tolérance 1 min).
  Appliqué sur `PAYOUT_CONFIGURE` et `SELLER_CLOSE`
  (`MerchantEndpoints.cs:464-468`) et sur les capacités `Critical` de financial
  (`FinancialEndpoints.cs:676` → `MerchantCapabilities.RequiresStepUp`, `MerchantCapabilities.cs:200`).
* MFA : TOTP, exigée après le mot de passe et après le contrôle de surface
  (`LoginCommandHandler.cs:184-195`) ; un code faux compte dans le verrouillage (`.cs:251`).
  Setup/confirm/disable exposés sous `/api/identity/account/me/mfa/*`
  (`IdentityEndpoints.cs:282-284`), tous derrière `RequireAuthorization()`.
* Réauthentification : `POST /api/v1/auth/reauthenticate`, la seule route `/auth` non anonyme,
  l'identifiant vient du jeton et le corps ne porte que le mot de passe
  (`IdentityEndpoints.cs:88-89, 186-196`).

### 1.6 Vérification e-mail / téléphone

`[MEDIUM] SEC-21 — `ApproveUserCommand` n'a aucune route : un compte inscrit ne peut pas être activé`
— `services/common/identity-service/src/HBA.Identity.Application/Users/Commands/ApproveUser/ApproveUserCommand.cs:13`
— recherche exhaustive : la commande n'est référencée que par son propre handler et par deux
commentaires. `RegistrationOptions.RequireApprovalForBuyers = true` par défaut
(`RegistrationOptions.cs:18`), le compte naît `PendingVerification`, et `LoginCommandHandler.cs:152-157`
refuse la connexion dans cet état. Le groupe d'administration
(`IdentityEndpoints.cs:455-463`) expose `suspend`, `reactivate`, `roles` — pas `approve`.
*Impact* : pas une fuite, une impasse — aucun compte public ne peut se connecter sans intervention en
base. À confirmer par un test d'intégration (`ReactivateUserCommand` pourrait servir de contournement,
mais son nom et son handler visent un compte suspendu, pas un compte en attente).

`[MEDIUM] SEC-22 — Aucune vérification du numéro de téléphone` — recherche exhaustive de
`PhoneVerified` / `VerifyPhone` / `phone_verified` dans tout le dépôt : zéro occurrence. `PhoneNumber`
est validé syntaxiquement (`Domain/Users/PhoneNumber.cs`) et jamais prouvé, alors que le canal OTP
existe (`OtpChallengeUseCases.cs:24`, `IssueOtpChallengeCommand(login, channel)`) et que la
plateforme cible le Mobile Money — où le numéro **est** l'identité de paiement.
*Correction* : réutiliser le défi OTP existant pour marquer `PhoneVerified`, et l'exiger avant
`PAYOUT_CONFIGURE` / `WITHDRAWAL_REQUEST`.

### 1.7 Limitation de débit

`shared/common/HBA.Shared.Hosting/Http/AuthRateLimiter.cs:58-101` : politique `auth` 30/min
partitionnée par IP, politique globale 300/min partitionnée par `sub` puis IP, `GlobalLimiter` en
filet (ligne 89). Le groupe `/api/v1/auth` la porte (`IdentityEndpoints.cs:60-61`).

`[HIGH] SEC-15 — La limite d'authentification est partitionnée sur l'adresse de la passerelle`
— `AuthRateLimiter.cs:68` et `:122` — `ClientIp` lit `HttpContext.Connection.RemoteIpAddress`, et
aucun service n'appelle `UseForwardedHeaders` (recherche exhaustive : `ForwardedHeaders` n'apparaît
que dans `apps/api-gateway/`). Derrière la passerelle, `RemoteIpAddress` est **l'adresse du
conteneur gateway** pour toutes les requêtes.
*Ce que l'attaquant obtient* : deux choses opposées et toutes deux mauvaises. (a) La protection par
client n'existe pas — 30 tentatives/min sont partagées par la plateforme entière, donc un attaquant
n'est pas ralenti *par rapport aux autres* ; (b) 30 requêtes/min suffisent à saturer la fenêtre
commune et à refuser la connexion à **tous** les utilisateurs. Le verrouillage de compte
(`User.RegisterFailedLogin`, `LoginCommandHandler.cs:251`) atténue le premier point, pas le second.
*Correction* : `UseForwardedHeaders` dans `UseHbaService` avec `KnownProxies` = la passerelle, ou
propagation d'un en-tête `X-HBA-Client-Ip` signé par la passerelle.

`[HIGH] SEC-16 — `ProxyTrust:TrustAnyProxy = true` est la valeur versionnée par défaut`
— `apps/api-gateway/src/HBA.Gateway.Api/appsettings.json` (section `ProxyTrust`) et
`Extensions/ForwardedHeadersExtensions.cs:24-36` — `KnownProxies.Clear()` + `KnownNetworks.Clear()`
désactivent tout contrôle d'origine : `X-Forwarded-For` est accepté de n'importe quel appelant.
*Ce que l'attaquant obtient* : si la passerelle est joignable autrement que par Traefik, un en-tête
suffit à changer de partition de limitation à chaque requête, donc à faire du bourrage
d'identifiants sans limite. Le code le dit lui-même (journal d'avertissement ligne 31-34) — le défaut
est le réglage livré, pas l'absence de conscience.
*Correction* : passer `TrustAnyProxy` à `false` dans `appsettings.json` et renseigner
`KnownProxies` dans les overlays k8s.

---

## 2. Autorisation

### 2.1 Recensement exhaustif des politiques

**`AddPolicy` (autorisation)** — un seul endroit dans tout le dépôt :
`apps/api-gateway/src/HBA.Gateway.Api/Extensions/AuthorizationExtensions.cs:46-70`.

* `SetDefaultPolicy` et `SetFallbackPolicy` = `RequireAuthenticatedUser()` (lignes 20-45).
* `GatewayPolicies.Authenticated` (ligne 46).
* Boucle sur `GatewayPolicies.RoleBased` (ligne 48) : `AdminOnly`, `StaffOnly`, `MerchantOnly`,
  `RestaurantOnly`, `PartnerOnly`, `DriverOnly`, `CustomerOnly`, alimentées par
  `Authorization:Roles` d'`appsettings.json`. Une politique sans rôle configuré fait
  `RequireAssertion(_ => false)` (ligne 68) — refus par défaut, choix correct.

Les deux autres `AddPolicy` du dépôt (`AuthRateLimiter.cs:66,76`,
`RateLimitingExtensions.cs:70-73`) sont des politiques de **débit**, pas d'autorisation.

**`FallbackPolicy` côté services** : `shared/common/HBA.Shared.Hosting/ServiceHostExtensions.cs:105-108`,
`RequireAuthenticatedUser()`. Un `MapGroup` nu est donc au moins fermé aux anonymes — **dans les six
services qui appellent `AddHbaService`** (voir SEC-01).

**Helpers de groupe** — `shared/common/HBA.Shared.Hosting/Http/ApiAuthorization.cs` :

| Helper | Ligne | Exigence |
|---|---|---|
| `MapAdminGroup` | 60-62 | `RequireRole(Admin, Moderator)` |
| `RequireAdmin` (route) | 87-88 | `RequireRole(Admin)`, s'additionne au groupe |
| `MapOperationsGroup` | 116-118 | `RequireRole(Admin, Dispatcher)` |
| `MapSellerGroup` | 155-157 | `RequireRole(Seller, Admin, Moderator)` |
| `MapAuthenticatedGroup` | 167-168 | `RequireAuthorization()` nu |

**`RequireClaim`** : zéro occurrence dans le dépôt.
**`IAuthorizationHandler` / `IAuthorizationRequirement`** : zéro occurrence. Toute la logique fine
est dans les handlers (`Deny*Async`) ou dans la couche Application.

**Gardes applicatives** (l'autorisation réelle) :

| Garde | Fichier:ligne | Question posée |
|---|---|---|
| `DenyUnlessOwnSellerAsync` (merchant) | `MerchantEndpoints.cs:390-471` | appartenance + capacité + step-up |
| `DenyUnlessProductOwnerAsync` | `CatalogEndpoints.cs:440` | vendeur de la fiche + capacité cadrée boutique |
| `DenyUnlessOwnerAsync` (offres) | `CatalogEndpoints.cs:511` | vendeur de l'offre + capacité |
| `DenyUnlessOwnerAsync` (stock) | `InventoryEndpoints.cs` (7 écritures) | `FulfillmentLocation.OwnerId` |
| `DenyUnlessOwnSellerAsync` (financial) | `FinancialEndpoints.cs:676` | appartenance + capacité + step-up |
| `DenyUnlessOwnDriverAsync` | `FinancialEndpoints.cs:627-646` | `DriverAccount.UserId` |
| `DenyUnlessStaffAsync` (food) | `FoodEndpoints.cs:247` | personnel de l'établissement + `FoodPermission` |
| `MemberAccessResolver.ResolveAsync` | `Members/MemberAccessResolver.cs:48-74` | appartenance + membre actif |

### 2.2 Table Route → Policy

Lecture : « Policy appliquée » = ce que le pipeline exige réellement ; « Policy attendue » = ce que
la ressource impose. Les groupes conformes sont agrégés ; les défauts sont listés route par route.

| Route | Verbe | Policy appliquée | Policy attendue | Verdict |
|---|---|---|---|---|
| `/api/v1/auth/{register,login,refresh,logout,otp/*,verify-otp,password/*,email/*,confirm-email}` | POST | `AllowAnonymous` + limiteur `auth` | anonyme | **OK** (`IdentityEndpoints.cs:63-152`) |
| `/api/v1/auth/reauthenticate` | POST | `RequireAuthorization()` | authentifié | **OK** (`:88`) |
| `/api/identity/account/me/**` | GET/PUT/POST/DELETE | `RequireAuthorization()` + `sub` du jeton | propriétaire | **OK** (`:276-315`) |
| `/api/identity/users/**`, `/api/identity/roles/**` | tous | `MapAdminGroup` + `RequireRole(Admin)` | Admin | **OK** (`:455-463, 505-514`) |
| `/api/v1/catalog/**` (vitrine) | GET | `AllowAnonymous` | anonyme, publiés seulement | **OK** (`CatalogEndpoints.cs:73-107`) |
| `/api/v1/catalog/admin/**` (24 routes) | tous | `MapAdminGroup` | Admin/Moderator | **OK** (`:124-184`) |
| `/api/v1/catalog/seller/**` (24 routes) | tous | `MapSellerGroup` + `Deny*OwnerAsync` + capacité | vendeur propriétaire | **OK** (`:216-325`) |
| `/api/v1/merchants/{me,''}` | GET/POST | `MapAuthenticatedGroup`, id du jeton | authentifié | **OK** (`MerchantEndpoints.cs:92-94`) |
| `/api/v1/merchants/{sellerId}/**` (18 routes) | tous | `MapSellerGroup` + `DenyUnlessOwnSellerAsync` | membre + capacité | **OK** (`:110-190`) |
| `/api/v1/merchants/{sellerId}/{kyb/approve,suspend,activate,…}` | POST | `MapAdminGroup` | Admin | **OK** (`:164-172`) |
| `/api/v1/merchants/{sellerId}/{members,roles}/**` | tous | `MapSellerGroup` + `MemberAccessResolver` dans la commande | membre + capacité | **OK** (`:219-303`, `MemberQueries.cs:101-175`) |
| `/api/v1/merchants/invitations/accept` | POST | `MapAuthenticatedGroup` + jeton d'invitation | authentifié | **OK** (`:320-323`) |
| `/api/orders/**` (acheteur) | tous | `MapAuthenticatedGroup` + `buyerId` du jeton | acheteur propriétaire | **OK** (`OrderEndpoints.cs:62-66`) |
| `/api/admin/orders/**` | tous | `MapAdminGroup` | Admin | **OK** (`:74-77`) |
| `/api/sellers/{sellerId}/orders` | GET | `MapSellerGroup` + `GetAccessAsync` + `ORDER_VIEW` | membre + capacité | **OK** (`:119`, `:287-300`) |
| `/api/inventory/**` (7 écritures) | POST/PUT/DELETE | `MapSellerGroup` + `DenyUnlessOwnerAsync` | vendeur du lieu | **OK** (`InventoryEndpoints.cs:155-162`) |
| `/api/inventory/{locations,low-stock,reservations/*}` | tous | `MapAdminGroup` | Admin | **OK** (`:93-110`) |
| `/api/financial/**` (payments, invoices, wallets, settlements) | tous | `MapAuthenticatedGroup` + `RequireAdmin()` ou `DenyUnlessOwn*Async` | voir §2.1 | **OK** (`FinancialEndpoints.cs:23-205`) |
| `/api/engagement/**` | tous | Authenticated / Seller / Admin + `userId` du jeton | propriétaire ou rôle | **OK** (`EngagementEndpoints.cs:17-89`) |
| `/api/notifications/**` | tous | `MapAuthenticatedGroup` + `userId` du jeton | propriétaire | **OK** |
| `/api/v1/promotions/validate`, `/api/v1/merchant/promotions/**` | tous | Authenticated + `RequireAdmin()` sur les écritures | Admin (assumé) | **OK** (`PromotionEndpoints.cs:45-72`) |
| `/api/food/restaurants/**` (vitrine) | GET | `AllowAnonymous` | anonyme | **OK** (`FoodEndpoints.cs:29-31`) |
| `/api/food/partner/**` (37 routes) | tous | `MapAuthenticatedGroup` + `DenyUnlessStaffAsync` | personnel + `FoodPermission` | **OK** (`:33-…`) |
| `/api/food/admin/**` | POST | `MapAdminGroup` | Admin/Moderator | **OK** (`:220`) |
| `/api/food/orders/**`, `/api/food/cart/**`, `/api/commerce/cart/**` | tous | Authenticated + `buyerId` du jeton | propriétaire | **OK** |
| `/api/deliveries/**` | tous | `MapOperationsGroup` (Admin/Dispatcher) | exploitation | **OK** (`DeliveryEndpoints.cs:43-47`) |
| `/api/v1/users/me/**`, `/api/geo/benin` | tous | Authenticated + `userId` du jeton ; geo anonyme | propriétaire / anonyme | **OK** (`UserEndpoints.cs:25-105`) |
| **`/api/v1/media/{id}`** | **GET** | Authenticated seulement | propriétaire de l'objet | **DÉFAUT SEC-09** |
| **`/api/v1/media/{id}/download-url`** | **GET** | Authenticated seulement | propriétaire + visibilité | **DÉFAUT SEC-07** |
| **`/api/v1/media/{id}`** | **DELETE** | Authenticated seulement | propriétaire | **DÉFAUT SEC-05** |
| **`/api/v1/media/{id}/reprocess`** | **POST** | Authenticated seulement | propriétaire | **DÉFAUT SEC-05** |
| **`/api/v1/media/`** | **POST** | Authenticated ; `ownerType`/`ownerId` du client | propriétaire déclaré vérifié | **DÉFAUT SEC-08** |
| **`/api/v1/seller/returns`** | **GET** | `MapSellerGroup` ; `sellerId` en **query string** | membre du vendeur + `RETURN_VIEW` | **DÉFAUT SEC-03** |
| **`/api/v1/seller/returns/{id}/{approve,reject,inspection,refund-decision,shipment,receive}`** | **POST** | `MapSellerGroup` seulement | vendeur du retour + `RETURN_*` | **DÉFAUT SEC-02** |
| **`/api/v1/marketplace/returns`** | **POST** | Authenticated ; aucun acheteur lu | acheteur de la commande | **DÉFAUT SEC-04** |
| **`/api/v1/marketplace/returns/{id}`, `/{id}/timeline`** | **GET** | Authenticated seulement | acheteur ou vendeur | **DÉFAUT SEC-17** |
| **`/api/v1/marketplace/returns/{id}/{cancel,evidence}`** | **POST** | Authenticated ; `ActorId` tracé, jamais comparé | acheteur du retour | **DÉFAUT SEC-17** |
| **`/api/v1/payments/intents/{id}`** | **GET** | `MapAuthenticatedGroup` seulement | acheteur ou Admin | **DÉFAUT SEC-10** |
| **`/api/v1/payments/intents`, `/api/financial/payments`** | **POST** | Authenticated ; aucun lien avec l'acheteur de la commande | acheteur de la commande | **DÉFAUT SEC-20** |
| **`/api/v1/drivers/**`** | tous | **aucune** (service sans authentification) | livreur porteur du jeton | **DÉFAUT SEC-01 + SEC-11** |
| **`/api/v1/tracking/**`, `/api/v1/routes/**`, `/api/v1/dispatch/**`, `/api/v1/proofs/**`** | tous | **aucune** | exploitation / livreur | **DÉFAUT SEC-01** |
| **`/api/v1/delivery-pricing/**`** | tous | **aucune** | authentifié | **DÉFAUT SEC-01** |
| **`/api/v1/admin/delivery-pricing/rules/**`** | GET/POST/PATCH | **aucune** | Admin | **DÉFAUT SEC-01** |
| **`/api/v1/menus/**`, `/api/v1/availability/**`, `/api/v1/kitchen/**`, `/api/v1/food/reviews/**`** | tous | **aucune** | personnel du restaurant | **DÉFAUT SEC-01** |
| **`/internal/v1/**`** (drivers, tracking, routes, dispatch, proofs, delivery-pricing) | tous | **aucune** | secret partagé `X-Internal-Key` | **DÉFAUT SEC-01** |
| `/api/v1/admin/return-policies` | POST | `MapAdminGroup` | Admin | **OK mais leurre** (SEC-25) |

### 2.3 Constats

`[CRITICAL] SEC-01 — Dix services exposent toute leur surface HTTP sans aucune authentification`
— `Program.cs` de : `services/delivery/delivery-pricing-service/src/HBA.Delivery.Pricing.Api`,
`.../dispatch-service/...Dispatch.Api`, `.../driver-service/...Driver.Api` (fichier entier, 19 lignes),
`.../proof-of-delivery-service/...Proof.Api`, `.../route-service/...Route.Api`,
`.../tracking-service/...Tracking.Api`, `services/food/availability-service/...Availability.Api`,
`.../kitchen-prep-service/...Kitchen.Api`, `.../menu-service/...Menu.Api` (fichier entier, 13 lignes),
`.../review-service/...Review.Api`.
Vérification : aucun de ces dix `Program.cs` n'appelle `AddHbaService`, `AddAuthentication`,
`UseAuthentication` ni `UseAuthorization`. Il n'y a donc ni validation de jeton, ni
`FallbackPolicy` — les `MapGroup` nus (`DriverEndpoints.cs:11`, `TrackingEndpoints.cs:11`,
`ProofEndpoints.cs:11`, `DispatchEndpoints.cs:11`, `RouteEndpoints.cs:11`,
`DeliveryPricingEndpoints.cs:13,47,99`, `MenuEndpoints.cs:9`, `AvailabilityEndpoints.cs:9`,
`KitchenEndpoints.cs:9`, `ReviewEndpoints.cs:9`) sont **entièrement publics**.
*Ce que l'attaquant obtient*, sans le moindre jeton : la grille tarifaire de livraison en écriture
(`POST/PATCH /api/v1/admin/delivery-pricing/rules`, `DeliveryPricingEndpoints.cs:52-97`) ; la position
GPS et le jeton de flux d'une course (`TrackingEndpoints.cs:24-30`) ; l'injection de positions
falsifiées (`:13`) ; l'affectation manuelle d'un livreur (`DispatchEndpoints.cs:28`) ; la création et
la soumission d'une preuve de livraison (`ProofEndpoints.cs:13-38`) — donc la clôture d'une course
non livrée ; le profil, les véhicules et la disponibilité du livreur (`DriverEndpoints.cs:13-45`) ;
et toutes les routes `/internal/v1/*` que le socle protégeait autrefois par `X-Internal-Key`
(`shared/common/HBA.Shared.Hosting/InternalRoutes.cs:8-18` : « il portait `RequireInternalCaller()`,
un filtre appliqué à huit routes `/internal/*` » — le filtre a été retiré, les routes REST non).
*Atténuation réelle et à ne pas surestimer* : ces services ne sont routés par aucune règle YARP
(`apps/api-gateway/.../appsettings.json`, section `ReverseProxy:Routes` — 44 routes, aucune vers
`Drivers`, `Tracking`, `Route`, `Dispatch`, `Proof`, `Menu`, `Availability`, `Kitchen`,
`FoodReview`, `DeliveryPricing`) et ne publient aucun port dans `docker-compose.dev.yml`
(vérifié lignes 827-1018). L'exposition est donc limitée au réseau `hba-backend`. Elle devient
totale le jour où l'on ajoute une route de passerelle — c'est-à-dire au premier lot qui branche
l'application livreur.
*Correction* : `builder.AddHbaService<TDbContext>(...)` + `app.UseHbaService()` sur les dix hôtes ;
rétablir un filtre `RequireInternalCaller()` sur les groupes `/internal/v1/*` ou les convertir en gRPC.

`[MEDIUM] SEC-19 — Aucune route YARP ne porte de politique de rôle ; les sept politiques de rôle de
la passerelle ne gardent que quatre actions BFF`
— `apps/api-gateway/.../appsettings.json` (`ReverseProxy:Routes` : 44 routes, valeurs
`AuthorizationPolicy` = `anonymous` ou `Authenticated`, jamais `AdminOnly`/`StaffOnly`/…) et
`Controllers/Bff/{DriverController.cs:32, RestaurantController.cs:44, MerchantController.cs:74,88}`
(seuls consommateurs de `GatewayPolicies.*`).
Deux conséquences vérifiables : (a) `merchants-read-v1` et `merchants-read-legacy` sont
`anonymous` sur **tous** les GET `/api/v1/merchants/**`, y compris
`/{sellerId}/members`, `/{sellerId}/roles`, `/{sellerId}/stores` — la passerelle relaie la requête
sans jeton ; seule la `FallbackPolicy` du service la refuse (401). Idem pour
`catalog-read-*` sur `/api/v1/catalog/admin/**` en GET. La défense en profondeur annoncée n'existe
pas : la passerelle ne filtre rien, tout repose sur les services ; (b) un service qui perdrait sa
`FallbackPolicy` — ce qui est déjà le cas de dix d'entre eux, cf. SEC-01 — deviendrait public sans
que la passerelle s'y oppose.
*Correction* : restreindre le motif `merchants-read-*` / `catalog-read-*` aux chemins réellement
publics, et poser `AdminOnly`/`MerchantOnly` sur les routes qui les concernent.

`[MEDIUM] SEC-25 — `POST /api/v1/admin/return-policies` ne persiste rien et renvoie 201`
— `services/marketplace/return-refund-service/.../Endpoints/ReturnPolicyEndpoints.cs:11-23` — la
route `GET` rend une constante en dur et la `POST` renvoie l'écho du corps sans toucher à la base.
*Ce que l'attaquant obtient* : rien directement. Le défaut est de sécurité par conséquence : la
politique de retour appliquée par `CreateReturnCommandHandler.cs:85`
(`_policies.GetApplicableSnapshotAsync`) est celle de la base, qui n'a **aucune** ligne écrite par
cette surface. Un administrateur croit avoir durci la fenêtre de retour ; rien n'a changé.

---

## 3. Permissions vendeur et membre

### 3.1 Catalogues déclarés

* `MerchantPermission` — 53 valeurs —
  `services/marketplace/seller-service/src/HBA.Merchants.Domain/Members/MerchantPermission.cs:35-145`,
  projetées en table `MerchantPermissions.Catalogue` (`:201-297`) avec code public, `PermissionRisk`
  et drapeau `OwnerOnly`. Un constructeur statique fait échouer le démarrage si une valeur de
  l'énumération n'a pas de ligne (`:310-331`). Sous-ensembles : `OwnerOnly` (5), `Critical` (5),
  `StoreScoped` (11, `:405-418`).
* `MerchantCapabilities` — les mêmes 53 codes côté contrat —
  `services/marketplace/seller-service/src/HBA.Merchants.Contracts/MerchantCapabilities.cs:26-200`,
  tenus synchrones par `tests/HBA.Merchants.UnitTests/CapacitesTests.cs`.
* `FoodPermission` — 7 valeurs —
  `services/food/restaurant-service/src/HBA.Food.Restaurant.Domain/Entities/Staff/StaffRole.cs:52-74`.
* Rôles plateforme — `ApiAuthorization.cs:28-48` : `Admin`, `Moderator`, `Seller` exigés ;
  `Driver`, `Dispatcher`, `FoodPartner` déclarés et non exigés (le fichier le dit lui-même, `:32-45`).
* Permissions de rôle identity — `IdentityDataSeeder.cs:38` : `users.manage`, `roles.manage`,
  `catalog.manage`. Elles sont mises dans le claim `permission` du jeton
  (`JwtTokenGenerator.cs:80`) et **aucune route ne les lit** — recherche exhaustive de
  `"users.manage"`, `"roles.manage"`, `"catalog.manage"` hors du seed : zéro. Décoratives.

### 3.2 Usage réel — permission par permission

Méthode : recherche de `MerchantPermission.X` et `MerchantCapabilities.X` dans tout le dépôt hors
`tests/`, hors les trois fichiers de déclaration.

**Utilisées (35)** : `PRODUCT_VIEW` (`CatalogEndpoints.cs:1063,1100`), `PRODUCT_CREATE` (`:1037`),
`PRODUCT_UPDATE` (`:1194,1219,1224…`, 14 sites), `PRODUCT_SUBMIT_FOR_REVIEW` (`:406`),
`PRODUCT_PUBLISH` (`:407`), `PRODUCT_UNPUBLISH` (`:408`), `OFFER_MANAGE` (`:672,753`),
`OFFER_PRICE_UPDATE` (`:683,746`), `INVENTORY_VIEW` (`InventoryEndpoints.cs:439,543`),
`INVENTORY_ADJUST` (`:579,587,593`), `STOCK_LOCATION_VIEW` (`:361`), `STOCK_LOCATION_MANAGE`
(`:395,419`), `ORDER_VIEW` (`OrderEndpoints.cs:297`), `REVIEW_REPLY`
(`ReplyToReviewCommand.cs:88`), `MEMBER_VIEW` (`MemberQueries.cs:104,119,140`), `MEMBER_INVITE`
(`SellerInvitation.cs:175,327,371`), `MEMBER_SUSPEND` (`SellerMember.cs:494,520`), `MEMBER_REVOKE`
(`SellerMember.cs:559`), `MEMBER_ASSIGN_STORE` (`SellerMember.cs:434,471`), `MEMBER_ASSIGN_ROLE`
(`SellerMember.cs:382,621`), `ROLE_VIEW` (`MemberQueries.cs:161`), `ROLE_CREATE`
(`SellerRoleCommands.cs:115`), `ROLE_UPDATE` (`:168`), `ROLE_DELETE` (`:255`), `FINANCE_VIEW`
(`FinancialEndpoints.cs:362,406,453`), `WALLET_VIEW` (`:478,491`), `PAYOUT_VIEW` (`:504,589`),
`PAYOUT_CONFIGURE` (`MerchantEndpoints.cs:570`), `WITHDRAWAL_REQUEST` (`FinancialEndpoints.cs:521`),
`SELLER_PROFILE_VIEW` (`MerchantEndpoints.cs:542`), `SELLER_PROFILE_UPDATE` (`:548,555`),
`KYB_MANAGE` (`:578,590,597`), `STORE_VIEW` (`:656,689`), `STORE_CREATE` (`:662`), `STORE_UPDATE`
(`:707,715,723`), `STORE_OPEN_CLOSE` (`:736,742`), `SELLER_CLOSE` (`:626`), `SELLER_REACTIVATE`
(`:631`), `AUDIT_VIEW` (`AuditQueries.cs:104`).

`[HIGH] SEC-12 — Dix-huit permissions sont déclarées et ne gardent rien`

| Permission | Route existante qu'elle devrait garder | Garde effective aujourd'hui |
|---|---|---|
| `RETURN_VIEW` | `GET /api/v1/seller/returns`, `/{id}` (`SellerReturnsEndpoints.cs:15-16`) | `MapSellerGroup` seul |
| `RETURN_APPROVE` | `POST /api/v1/seller/returns/{id}/approve` (`:17`) | `MapSellerGroup` seul |
| `RETURN_REJECT` | `POST …/{id}/reject` (`:18`) | `MapSellerGroup` seul |
| `RETURN_INSPECT` | `POST …/{id}/inspection` (`:19`) | `MapSellerGroup` seul |
| `RETURN_CONFIRM_RECEIVED` | `POST …/{id}/receive` (`:22`) | `MapSellerGroup` seul |
| `RETURN_DISPUTE_VIEW` | — (aucune route de litige) | — |
| `ORDER_CONFIRM` | — | aucune route vendeur d'acceptation de commande |
| `ORDER_REJECT` | — | idem |
| `ORDER_MARK_PREPARING` | — | idem |
| `ORDER_MARK_READY` | — | idem |
| `ORDER_CANCEL` | — | idem |
| `REVIEW_VIEW` | `GET /api/engagement/reviews/seller/{sellerId}` (`EngagementEndpoints.cs:20`) | Authenticated seul |
| `INVENTORY_TRANSFER` | — | aucune route de transfert |
| `STOCK_MOVEMENT_VIEW` | — | aucune route de mouvements |
| `ROLE_ASSIGN` | `PUT …/members/{memberId}/roles` (`MerchantEndpoints.cs:227`) | `MEMBER_ASSIGN_ROLE` (doublon fonctionnel) |
| `BANK_ACCOUNT_UPDATE` | — | `PAYOUT_CONFIGURE` couvre le geste |
| `OWNERSHIP_TRANSFER` | — | aucune route de transfert de propriété |
| `SECURITY_POLICY_UPDATE` | — | table `seller_security_policies` inexistante |

*Ce que l'attaquant obtient* : les six `RETURN_*` sont le cas grave — les routes **existent et sont
appelables** (`SellerReturnsEndpoints.cs:15-22`, montées par `Program.cs:16`), elles n'exigent que
le rôle plateforme `Seller`. Un vendeur légitime, ou n'importe quel compte à qui
`GrantSellerRoleHandler` a greffé le rôle, approuve et chiffre le remboursement d'un retour qui
n'est pas le sien (voir SEC-02). Les cinq `ORDER_*` et `REVIEW_VIEW` sont un défaut de conception —
elles sont attribuables dans l'écran des rôles et ne donnent accès à rien, ce qui rend le modèle de
permissions mensonger pour le vendeur qui le configure.
*Correction* : brancher les six `RETURN_*` immédiatement (c'est une ligne par route) ; retirer du
catalogue, ou marquer explicitement « réservée » dans `ListPermissionsQuery`, les douze qui n'ont
pas encore de surface.

`[MEDIUM] SEC-29 — `FoodPermission.StaffManage` et `AnalyticsRead` ne gardent aucune route`
— `StaffRole.cs:64,73` — `StaffManage` n'est lue que par `StaffQueries.cs:32` (`ListStaffQuery`) et
`RestaurantStaff.cs:437` ; recherche exhaustive : **aucun endpoint** de `FoodEndpoints.cs` n'envoie
`ListStaffQuery`, et il n'existe aucune route `/api/food/partner/**/staff`. `AnalyticsRead` n'est
citée que dans `ToCode` et les rôles par défaut. Un restaurateur ne peut donc ni voir ni composer
son équipe par l'API, alors que le modèle de rôles la suppose.

---

## 4. IDOR et confiance dans le client

Méthode : extraction automatique des 492 déclarations `Map(Get|Post|Put|Delete|Patch)` du dépôt,
puis croisement de chaque signature de handler contenant `sellerId|storeId|userId|buyerId|
customerId|driverId|restaurantId|ownerId|memberId` avec la présence d'une garde
(`Deny*Async`, `CurrentUserId`, `GetAccessAsync`, `GetStaffMembership`, `MerchantCapabilities`,
`FoodPermission`, `IsInRole`).

### 4.1 Le cas correct, pour comparaison

Ces routes prennent l'identifiant **du jeton** et ne l'acceptent jamais du client :

* `RegisterSellerAsync`, `GetMySellerAsync` — `MerchantEndpoints.cs:510-520`.
* `CreateOfferAsync` — `CatalogEndpoints.cs:648-695` : `sellerId = acces.SellerId` (ligne 688),
  jamais `request.SellerId` ; l'encadré ligne 652-657 explique le raisonnement.
* `PlaceAsync` (commande) — `OrderEndpoints.cs:342-349` : `buyerId` du jeton, et `ShippingFee` a été
  **retiré du corps** (`:307-330`) parce qu'un acheteur postait `0`.
* `ListForMyRestaurantAsync` — `MealOrderEndpoints.cs:112-128` : aucun `restaurantId` dans l'URL,
  résolu par `GetStaffMembershipAsync`.
* `AcceptInvitationAsync` — `MerchantEndpoints.cs:888-892` : aucun `sellerId`, l'invitation le porte.
* `GetUserRecommendationsAsync` — `EngagementEndpoints.cs:158-172` : `userId` de l'URL comparé au
  `sub`, 403 sinon.
* Tout `cart-service`, `food-cart-service`, `user-service`, `notification-service` : `buyerId`/`userId`
  systématiquement issus de `CurrentUserId(user)`.

### 4.2 Les défauts

`[CRITICAL] SEC-02 — Un compte porteur du rôle `Seller` décide le remboursement de n'importe quel retour`
— `services/marketplace/return-refund-service/src/HBA.Marketplace.ReturnRefund.Api/Endpoints/SellerReturnsEndpoints.cs:32-51`
et `.../Application/Commands/ReturnLifecycleCommands.cs:70-182`.
Les six handlers d'écriture (`ApproveAsync`, `RejectAsync`, `InspectAsync`, `DecideRefundAsync`,
`RegisterShipmentAsync`, `ReceiveAsync`) ne reçoivent que `Guid id` et le `ClaimsPrincipal` ;
`CurrentUserId(user)` est passé comme `ActorId` — un champ de **traçabilité**, jamais comparé.
Côté Application, `ApproveReturnCommandHandler.Handle` (`:75-83`), `DecideRefundCommandHandler.Handle`
(`:169-181`) et leurs quatre jumeaux font `Returns.GetAsync(command.ReturnId)` puis appellent
directement l'agrégat. Aucun de ces handlers ne lit `request.SellerId` ni ne le compare à quoi que ce
soit. `ReturnRequest` porte pourtant `SellerId` (`Application/DTOs/ReturnRefundDtos.cs:34`).
*Ce que l'attaquant obtient* : avec un identifiant de retour (GUID, obtenu par
`GET /api/v1/seller/returns?sellerId=<concurrent>` — cf. SEC-03 — ou par la route client, cf. SEC-17),
un vendeur approuve puis chiffre le remboursement d'un retour appartenant à un concurrent :
`DecideRefundCommand(id, Amount, Currency, …)` où le **montant vient du corps de requête**
(`SellerReturnsEndpoints.cs:42-44`). Le montant est plafonné par
`RefundCalculationPolicy.Validate(amount, breakdown, EstimatedRefundAmount, TotalRefunded())`
(`Domain/Aggregates/ReturnRequest/ReturnRequest.cs:277`) — c'est la seule chose qui limite la casse —
mais l'événement `RefundRequestedDomainEvent` part (`:285`), le statut passe à `RefundPending`, et
le vendeur légitime voit son argent sortir sur une décision qu'il n'a pas prise. Symétriquement,
`RejectAsync` détruit un retour légitime.
*Correction* : charger le retour, résoudre `MerchantAccess` via `IMerchantAccessApi.GetAccessAsync`,
refuser si `acces.SellerId != retour.SellerId`, puis exiger `RETURN_APPROVE` / `RETURN_REJECT` /
`RETURN_INSPECT` / `RETURN_CONFIRM_RECEIVED` selon la route.

`[CRITICAL] SEC-03 — Le carnet de retours d'un vendeur se lit avec un `sellerId` de query string`
— `SellerReturnsEndpoints.cs:26-27` :
```
private static async Task<IResult> ListAsync(Guid sellerId, int page, int pageSize, ISender sender, CancellationToken ct)
    => (await sender.Send(new GetSellerReturnsQuery(sellerId, page, pageSize), ct)).Match(ApiResults.Page);
```
Le préfixe du groupe est `/api/v1/seller/returns` (`:14`) — **il ne contient aucun `{sellerId}`**.
Le paramètre est donc lié depuis la chaîne de requête, et rien ne le confronte au jeton.
*Ce que l'attaquant obtient* : `GET /api/v1/seller/returns?sellerId=<concurrent>&page=1&pageSize=500`
rend, paginé, l'intégralité des retours d'un concurrent — identifiants de commande, de client, de
produit, motifs, montants remboursés, chronologie. Les identifiants de vendeur sont publics (ils
circulent dans les liens de boutique et dans `GET /api/v1/catalog/sellers/{sellerId}/products`,
`CatalogEndpoints.cs:107`). C'est aussi l'amorce d'exploitation de SEC-02 : la liste fournit les
`returnId`.
*Correction* : supprimer le paramètre, résoudre le vendeur depuis `GetAccessAsync`, exiger
`RETURN_VIEW`.

`[CRITICAL] SEC-04 — Un retour peut être ouvert sur la commande de n'importe qui`
— `CustomerReturnsEndpoints.cs:25-27` et
`.../Commands/CreateReturn/CreateReturnCommand.cs:35-116`.
Le handler HTTP ne lit **aucune** identité : `CreateAsync(CreateReturnRequestDto request, ISender sender, …)`.
Le `CustomerId` du retour est repris du contexte de commande obtenu par gRPC
(`CreateReturnCommandHandler.cs:48` puis `:96`), donc de la commande **désignée par le client**.
*Ce que l'attaquant obtient* : avec un `orderId` (les identifiants de commande circulent dans les
URL de l'acheteur, dans le carnet vendeur et dans `payments/by-order/{orderId}`), tout compte
authentifié ouvre un retour sur la commande d'un tiers. Effets en chaîne : le vendeur reçoit une
demande de retour fantôme, la fenêtre de retour est consommée, et le flux `Approve → DecideRefund →
ExecuteRefund` est amorcé sur une vente conclue. La réponse contient en outre `EstimatedRefund`,
donc le montant payé par un tiers.
*Correction* : `CreateAsync` doit lire `CurrentUserId(user)` et le handler refuser si
`context.Value.CustomerId != acheteur`.

`[HIGH] SEC-17 — Tout compte authentifié lit et modifie un retour dont il n'est pas partie`
— `CustomerReturnsEndpoints.cs:34-45` (`GetAsync`, `TimelineAsync`, `CancelAsync`, `AddEvidenceAsync`)
et `AdminReturnsEndpoints.cs:21-22` (`GetAsync`, admin, correct).
`GetReturnQuery(id)` et `GetReturnTimelineQuery(id)` ne portent aucun demandeur.
`CancelReturnCommandHandler.Handle` (`ReturnLifecycleCommands.cs:39-47`) ne compare pas `ActorId` au
`CustomerId`. `AddEvidenceCommandHandler` (`:57-67`) valide bien le média contre
`request.CustomerId` (`_media.ValidateMediaAsync(command.MediaId, request.CustomerId, …)`, ligne 61) —
c'est-à-dire contre le client **du retour**, pas contre l'appelant : cela empêche d'attacher le média
d'un tiers, pas d'écrire dans le dossier d'un tiers.
*Ce que l'attaquant obtient* : lecture du dossier de retour d'un inconnu (produits, montants,
adresse implicite via la livraison de retour, chronologie horodatée), et annulation du retour d'un
client légitime — qui perd sa fenêtre de rétractation.

`[CRITICAL] SEC-05 — Tout compte authentifié supprime n'importe quel média de la plateforme`
— `services/common/media-service/src/HBA.Media.Api/Endpoints/MediaEndpoints.cs:57` (route),
`:172-173` (handler) :
```
private static async Task<IResult> DeleteAsync(Guid id, ISender sender, CancellationToken ct)
    => (await sender.Send(new DeleteMediaCommand(id), ct)).Match(() => Results.NoContent());
```
Ni `ClaimsPrincipal`, ni propriétaire, ni type. Idem pour `ReprocessAsync` (`:175-176`).
Le groupe est `MapAuthenticatedGroup` (`:44`) : un jeton d'acheteur suffit.
*Ce que l'attaquant obtient* : effacement des photos produit d'un concurrent, du logo d'une boutique,
des pièces KYB d'un vendeur (qui repasse en revue), des preuves de livraison d'un litige en cours.
C'est exactement la seconde exploitation décrite — et refermée ailleurs — dans
`AddKybDocumentCommandHandler.cs:51-54`, restée ouverte à la source.
*Correction* : la route doit résoudre le propriétaire via `(OwnerType, OwnerId)` et le confronter au
jeton, ou passer sur le port interne gRPC et disparaître de la surface publique.

`[HIGH] SEC-07 — URL présignée délivrée sur n'importe quel média, y compris une pièce d'identité, pour
une durée choisie par le client`
— `MediaEndpoints.cs:56` (route) et `:165-170` (handler) ; implémentation
`services/common/media-service/src/HBA.Media.Infrastructure/Public/MediaModuleApi.cs:101-120`.
```
var url = await media.CreateSignedUrlAsync(id, expiresIn ?? 300, ct);
```
`CreateSignedUrlAsync` ne vérifie que « le média existe et n'est pas supprimé » (`:112-115`). Il
**ne consulte pas `media.Visibility`**, alors que `MediaTypePolicy` classe `SellerDocument`,
`DriverDocument` et `Invoice` en `MediaVisibility.Private`
(`Domain/Assets/MediaTypePolicy.cs:78-85`). `expiresIn` n'est pas plafonné, ni ici ni côté gRPC
(`shared/contracts/HBA.Media.Contracts.Grpc/MediaGrpcService.cs:88`).
*Ce que l'attaquant obtient* : avec un `mediaId`, une URL de lecture directe sur le bucket privé pour
la durée qu'il demande (`?expiresIn=31536000`) — RCCM, IFU, CNI d'un vendeur, permis d'un livreur,
facture. Le GUID n'est pas devinable, ce qui empêche le balayage ; mais il fuit dès qu'un média est
listé (`ListByOwnerAsync`, `MediaGrpcService.cs:75`) ou renvoyé par une route métier. Le fichier
l'admet lui-même (`MediaEndpoints.cs:151-163` : « cette route ne vérifie pas le droit métier »).
*Correction* : refuser `Visibility != Public` sans contrôle amont, plafonner `expiresIn` (300 s),
et faire signer les pièces KYB par seller-service qui, lui, connaît le propriétaire.

`[HIGH] SEC-08 — Le propriétaire d'un média est déclaré par le client`
— `MediaEndpoints.cs:54` (route) et `:78-142` (handler) : `ownerType` et `ownerId` sont des
paramètres de requête, aucun contrôle. Le commentaire (`:66-76`) dit que la route « est destinée aux
BFF et aux services, PAS aux applications clientes » et « reste réservée aux administrateurs » —
elle est en réalité ouverte à tout compte authentifié, et exposée au bord par
`media-v1-write` / `media-legacy-write` (`appsettings.json`, `AuthorizationPolicy: Authenticated`).
*Ce que l'attaquant obtient* : rattacher un fichier à `(OwnerType=Seller, OwnerId=<concurrent>)`, donc
faire apparaître un contenu arbitraire dans la galerie d'un tiers ; et, combiné à SEC-05,
un cycle « je rattache, je supprime » sur les médias d'autrui.
*Note* : la validation du contenu, elle, est correcte —
`UploadValidation.CheckDocumentAsync` lit les **magic bytes** et transmet le type réel, jamais
`file.ContentType` (`shared/common/HBA.Shared.Hosting/Http/UploadValidation.cs:83-113`).

`[HIGH] SEC-09 — Métadonnées de n'importe quel média lisibles`
— `MediaEndpoints.cs:55` et `:145-149` : `GetAsync(Guid id, IMediaModuleApi media, …)`, aucun
propriétaire. Rend `OwnerType`, `OwnerId`, `OriginalFileName`, `ContentType`, taille, statut
(`MediaModuleApi.Map`, `:122-140`). Le nom de fichier original d'une pièce d'identité est déjà une
donnée personnelle (`CNI_Kossi_Adjovi.pdf`).

`[HIGH] SEC-10 — `GET /api/v1/payments/intents/{id}` rend le paiement de n'importe qui`
— `services/common/payment-service/src/HBA.Financial.Api/Endpoints/FinancialEndpoints.cs:433-436` :
```
private static async Task<IResult> GetPaymentIntentAsync(Guid id, ISender sender, CancellationToken ct)
    => (await sender.Send(new GetPaymentQuery(id), ct)).Match(payment => ApiResults.Ok(payment));
```
Son jumeau historique `GET /api/financial/payments/{id}` (`:225-233`) a été corrigé et passe par
`PeutVoirLePaiement(user, resultat.Value.BuyerId)` (`:255-256`). La route `/api/v1/` du §10.12,
ajoutée ensuite, n'a pas repris la garde. Les deux sont exposées au bord (`payments`, `Authenticated`).
*Ce que l'attaquant obtient* : montant, prestataire, référence externe, statut et `BuyerId` de
n'importe quel paiement — c'est précisément la fuite que le commentaire `:214-222` décrit comme
refermée.
*Correction* : trois lignes, recopier `PeutVoirLePaiement`.

`[HIGH] SEC-11 — driver-service sert un unique livreur codé en dur à tous les appelants`
— `services/delivery/driver-service/src/HBA.Delivery.Driver.Api/Endpoints/DriverEndpoints.cs:13-45`
et `.../HBA.Delivery.Driver.Application/Abstractions/DriverStore.cs:13` :
```
public Guid DefaultDriverId { get; } = Guid.Parse("00000000-0000-7000-0000-000000000017");
```
Les six routes `/api/v1/drivers/me*` passent `store.DefaultDriverId` sans jamais lire le jeton — qui
n'existe pas, le service n'ayant pas d'authentification (SEC-01).
*Ce que l'attaquant obtient* : lecture et écriture du profil, des véhicules et de la disponibilité
« du livreur », quel qu'il soit. `POST /me/availability` publie un événement d'intégration
(`DriverEndpoints.cs:34-42`) : un tiers met la flotte en indisponible.
*Correction* : le service est un bouchon en mémoire ; il ne doit pas quitter cet état sans
authentification ni résolution `userId → driverId`.

`[MEDIUM] SEC-20 — `InitiatePayment` n'exige pas que l'appelant soit l'acheteur de la commande`
— `FinancialEndpoints.cs:258-259` (`POST /api/financial/payments`) et `:428-431`
(`POST /api/v1/payments/intents`). Les deux lient directement `InitiatePaymentCommand` depuis le
corps, sans `ClaimsPrincipal`. Le handler lit bien la commande et son montant côté serveur
(`InitiatePaymentCommandHandler.cs:64-111`) — le montant n'est pas falsifiable — mais rien ne vérifie
que l'appelant est l'acheteur.
*Ce que l'attaquant obtient* : deux effets. (a) `PayerPhone` est libre : sur un fournisseur Mobile
Money à `RequiresPayerPhone`, une invite de débit est poussée vers un numéro choisi, avec le libellé
d'une commande réelle — un canal d'hameçonnage crédible et gratuit ; (b) `ReturnUrl` / `CancelUrl`
sont libres et transmis au prestataire : redirection ouverte sur la page de retour de paiement.
*Correction* : comparer `order.BuyerId` au `sub` ; valider `ReturnUrl` contre une liste blanche
d'origines.

`[MEDIUM] SEC-23 — Lectures de stock transverses` — `InventoryEndpoints.cs:53-57` :
`/availability/{sku}` ne filtre ni par vendeur ni par lieu. Le fichier l'assume explicitement
(`:59-84`) et a resserré `items/sku/{sku}` et `items/by-locations` via `MesLieuxAsync`. Reste
qu'un inscrit connaissant un SKU obtient le total disponible d'un concurrent.

---

## 5. Fuite inter-vendeur / inter-boutique

### 5.1 Filtrage des lectures

Correct dans catalog, merchant, order, financial et engagement — chaque lecture vendeur passe par
`GetAccessAsync` puis compare `acces.SellerId` à la ressource :
`CatalogEndpoints.cs:448-458` (produit), `:706-742` (offres d'une boutique),
`OrderEndpoints.cs:287-300`, `FinancialEndpoints.cs:676-700`, `MerchantEndpoints.cs:390-471`.
Défauts : **SEC-03** (retours), **SEC-23** (stock), **SEC-09/07** (médias).

### 5.2 Cadrage par boutique

`MemberAccess.HasInStore(storeId, capacite)` est utilisé dès que la route nomme une boutique
(`MerchantEndpoints.cs:427-430`), et `MerchantPermissions.StoreScoped` (`MerchantPermission.cs:405-418`)
énumère les 11 permissions réellement cadrées — avec, ligne 382-403, la liste explicite de ce qui ne
l'est pas et pourquoi. C'est la partie la mieux tenue du dépôt : un responsable de la boutique A ne
change ni les horaires, ni les prix, ni les offres de la boutique B.
Reste assumé et documenté : `INVENTORY_*`, `ORDER_*`, `REVIEW_*`, `FINANCE_*`, `MEMBER_*` s'appliquent
au vendeur entier.

### 5.3 Cache d'autorisation

Architecture : un seul cache, chez seller-service, clé `sellers:access:{userId}`
(`Application/SellersCacheKeys.cs:65`), TTL 2 min (`:85`), cache négatif 30 s (`:42`), servi par
`MerchantAccessApi.GetAccessAsync` (`Infrastructure/Public/MerchantAccessApi.cs:46-57`). Les cinq
services appelants ne cachent rien côté client, et le choix est argumenté
(`shared/contracts/HBA.Merchants.Contracts.Grpc/MerchantsGrpc.cs:472-483`) : une invalidation par
Kafka n'atteindrait qu'une réplique du groupe de consommateurs. Le raisonnement est juste.

La clé est bien **par utilisateur**, pas par vendeur — ce qui est la bonne granularité ici, puisque
c'est le rattachement d'un compte qui est mis en cache.

**Suspension d'un membre** : prise en compte immédiatement. Toute mutation de `SellerMember`
(`Added|Modified|Deleted`) évince `sellers:access:{userId}` dans le même `SaveChangesAsync` que
l'écriture (`Infrastructure/Persistence/SellersDbContext.cs:236-255`, appelé par `:156-168`), donc
globalement puisque le cache est Redis. `MemberAccessResolver` (chemin des routes d'équipe) ne cache
rien du tout et refuse explicitement un membre `!CanAct` (`MemberAccessResolver.cs:65-69`).

`[HIGH] SEC-13 — Retirer une permission d'un rôle vendeur ne prend effet qu'au bout de deux minutes`
— `services/marketplace/seller-service/src/HBA.Merchants.Infrastructure/Persistence/SellersDbContext.cs:199-256`.
`CollectCacheKeysToEvict` parcourt `ChangeTracker.Entries<Seller>()` (`:215`),
`Entries<SellerMember>()` (`:236`) et `Entries<KybDocument>()` (`:263`). **`SellerRole` n'y figure
pas.** Or `UpdateSellerRoleCommand` remplace en bloc la liste de permissions d'un rôle
(`Application/Members/SellerRoleCommands.cs:168`) et `DeleteSellerRoleCommand` supprime le rôle
(`:255,280`), sans toucher aux lignes `SellerMember`. Aucune éviction manuelle non plus (recherche de
`cache`/`Cache` dans `SellerRoleCommands.cs` : une seule occurrence, `_roles.Remove(role)`).
*Ce que l'attaquant obtient* : un membre dont on vient de retirer `OFFER_PRICE_UPDATE`,
`INVENTORY_ADJUST` ou `WITHDRAWAL_REQUEST` conserve ces droits pendant 120 s **sur les cinq services
appelants**. C'est la fenêtre exacte du départ conflictuel d'un employé — le geste que l'on fait juste
après avoir retiré le droit est précisément celui qu'on veut empêcher.
*Correction* : ajouter une boucle `ChangeTracker.Entries<SellerRole>()` dans
`CollectCacheKeysToEvict` et évincer `sellers:access:{userId}` pour tous les membres portant ce rôle
(une requête sur `seller_members` avant `SaveChanges`), ou versionner la clé par
`(userId, roleVersion)`.

---

## 6. Médias et upload

### 6.1 Validation du contenu — correcte

`shared/common/HBA.Shared.Hosting/Http/UploadValidation.cs` :
* Taille vérifiée **avant** toute lecture d'octet (`:76-81`, commentaire `:61-63`).
* Type déduit des **magic bytes** via `FileSignature.Detect` (`:85-92`), liste **blanche**, refus si
  signature inconnue (`:100-109`).
* Le type **réel** est renvoyé et c'est lui qui part vers le stockage
  (`MediaEndpoints.cs:134-137`), jamais `IFormFile.ContentType`.
* Plafonds : 5 Mo images, 10 Mo documents (`:52-53`), affinés par nature dans
  `MediaTypePolicy.For` (`:65-89` : 10 Mo produit, 15 Mo pièce KYB, 20 Mo pièce jointe).
* `CatalogEndpoints.cs:617-638` : détourage borné à 12 Mo, réservé aux vendeurs avec
  `PRODUCT_UPDATE` (`:583-591`).

### 6.2 Les pièces KYB sont-elles protégées différemment ?

**Dans le modèle, oui** — `MediaTypePolicy.For` (`Domain/Assets/MediaTypePolicy.cs:78-85`) impose
`MediaVisibility.Private`, préfixe `sellers/documents`, aucune variante dérivée, rétention 365 jours.
`MediaModuleApi.Map` ne remplit l'URL publique que si `IsPubliclyReadable` (`:135-140`).
Le rattachement d'une pièce est correctement gardé : `AddKybDocumentAsync` exige `KYB_MANAGE`
(`MerchantEndpoints.cs:575-580`), et `AddKybDocumentCommandHandler` vérifie que le média est bien
`(OwnerType=Seller, OwnerId=sellerId, MediaType=SellerDocument)`
(`Application/Sellers/Commands/AddKybDocument/AddKybDocumentCommandHandler.cs:29-72`).

**À l'accès, non** — la distinction s'arrête à la porte : `CreateSignedUrlAsync` ignore
`Visibility` (SEC-07), `DeleteAsync` ignore le propriétaire (SEC-05), `GetAsync` aussi (SEC-09).
Une pièce d'identité et une photo de produit sont, pour ces trois routes, le même objet.

### 6.3 Présignature côté livraison

`[CRITICAL, inclus dans SEC-01] ` — `services/delivery/proof-of-delivery-service/src/HBA.Delivery.Proof.Api/Endpoints/ProofEndpoints.cs:19-25` :
`POST /api/v1/proofs/{id}/media/presign` est atteignable sans jeton. L'implémentation
(`Application/Abstractions/ProofStore.cs:30-45`) rend aujourd'hui une URL factice
(`https://storage.local/{objectKey}?signature=dev`, 10 min) : le bouchon limite l'impact
**aujourd'hui**, et le rendra maximal le jour du branchement réel.

---

## 7. Secrets et PII

### 7.1 Secrets

Hygiène globalement correcte :
* `k8s/base/common/secret.yaml:26-36` : objet `Secret` **vide** dans Git, valeurs
  (`AUTHENTICATION__SIGNINGKEY`, `INTERNAL__APIKEY`) renseignées par un gestionnaire externe.
* `apps/api-gateway/.../appsettings.json` : `SigningKey: ""` + un champ `_lire` qui explique
  pourquoi.
* `InternalCallOptions.ApiKey` documenté « jamais dans un appsettings versionné »
  (`shared/common/HBA.Shared.Hosting/InternalRoutes.cs:71-80`), et
  `InternalCallServerInterceptor` **refuse** quand la clé est absente plutôt que de laisser passer
  (`Grpc/InternalCallInterceptors.cs:113-121`). Comparaison à temps constant
  (`InternalRoutes.cs:51-63`).

`[INFO] SEC-30 — Secrets de développement versionnés, assumés` — `docker-compose.dev.yml:8-13,29,49,56,165,262,277`
(`AUTHENTICATION__SIGNINGKEY`, `INTERNAL__APIKEY`, `hba/hba`, `ADMIN__PASSWORD: Admin123!`).
L'en-tête du fichier le déclare et en explique la portée. Le seul risque est la réutilisation :
`IdentitySeedExtensions.cs:32-34,74-83` refuse de démarrer hors `Development` sans `ADMIN__EMAIL` et
`ADMIN__PASSWORD` (`:85-105`), et ne réinitialise jamais un mot de passe existant (`:53-58`). Correct.

`[INFO] SEC-32 — Clé de signature de développement dans un fichier versionné`
— `apps/api-gateway/src/HBA.Gateway.Api/appsettings.Development.json:11`
(`"SigningKey": "dev-only-signing-key-change-me-32b!!"`). Chargée uniquement en `Development` et
écrasée par `AUTHENTICATION__SIGNINGKEY` dans compose. Sans danger, mais c'est la seule clé HMAC
littérale d'un `appsettings` du dépôt : à retirer pour que la règle reste sans exception.

Recherche exhaustive `(SigningKey|ApiKey|Secret|Password|AccessKey|Token)\s*=\s*"…"` dans les `.cs`
hors `tests/` : **deux** résultats, tous deux ci-dessus. Aucune vraie fuite.

### 7.2 PII et secrets en journal

`[HIGH] SEC-14 — Jetons de réinitialisation et de vérification publiés en clair sur Kafka et stockés
en clair dans l'outbox`
— `shared/contracts/HBA.Identity.Contracts/IntegrationEvents/IdentityIntegrationEvents.cs:23`
(`EmailVerificationRequestedIntegrationEvent.VerificationToken`) et `:91`
(`PasswordResetRequestedIntegrationEvent.ResetToken`). Le second porte un encadré qui l'assume
(`:80-86`) : « ce record ne doit JAMAIS être journalisé ». Le message transite par
`outbox_messages.Content`, JSON en clair (`shared/common/HBA.Shared.Infrastructure/Outbox/OutboxMessage.cs:17`).
*Ce que l'attaquant obtient* : quiconque lit le topic Kafka ou la table d'outbox — un opérateur, une
sauvegarde, un accès en lecture à la base identity — prend le compte de son choix, en demandant
d'abord une réinitialisation. Le jeton est à usage unique et court, ce qui ne change rien à qui lit
le flux en direct.
*Correction* : ne publier que `userId` et un identifiant opaque de défi, et laisser
notification-service redemander le jeton à identity par gRPC ; ou chiffrer le champ.

`[MEDIUM] SEC-24 — Adresses e-mail complètes journalisées en `Information``
— `services/common/notification-service/src/HBA.Communication.Notifications.Infrastructure/Email/ResendEmailSender.cs:71`
(`"E-mail « {Subject} » envoyé à {To}."`) et `:61-64` (`LogError` avec `{To}` **et** le corps de
réponse du prestataire). C'est le chemin de production. Les handlers d'e-mail, eux, sont exemplaires :
`AccountEmailHandlers.cs:50-52` et `:97-99` ne journalisent que `{UserId}`, avec le raisonnement écrit.
*Correction* : masquer (`k***@domaine.tld`) ou remplacer par `UserId`.

`[INFO] SEC-31 — L'expéditeur d'e-mail de développement écrit les jetons en clair`
— `.../Email/DevelopmentEmailSender.cs:32-38` (`{TextBody}` contient le lien, donc le jeton). Assumé
(`:10-22`) et **gardé** : `NotificationsModuleInstaller.cs:149-162` refuse de démarrer en Production
sans canal e-mail configuré. Vérifié.

**Ce qui n'est pas journalisé, et c'est à porter au crédit du dépôt** : recherche exhaustive de
`Log(Information|Warning|Debug|Error)` contenant `otp|token|password|phone|code` dans tout le dépôt
hors `tests/` — 11 résultats, tous vérifiés, aucun ne contient de secret hormis les deux ci-dessus.
Le code OTP n'est ni renvoyé au client (`OtpChallengeDto(ChallengeId, Channel, ExpiresAtUtc)`,
`OtpChallengeUseCases.cs:12`) ni journalisé.
L'échec d'authentification côté passerelle est réduit à `LogDebug` sans motif exposé
(`AuthenticationExtensions.cs:125-127`).

---

## 8. Mass assignment

`[MEDIUM] SEC-18 — Le vendeur fixe son propre taux de commission à l'inscription`
— `MerchantEndpoints.cs:948` :
`public sealed record RegisterSellerRequest(string ShopName, decimal? CommissionRate, SellerCompanyInfo? Metadata);`
puis `:520` : `new RegisterSellerCommand(userId, request.ShopName, request.CommissionRate ?? 0.10m, request.Metadata)`.
Le validateur autorise `0m..1m` (`RegisterSellerCommandValidator.cs:12`), et
`Seller.Register` écrit la valeur telle quelle (`Domain/Sellers/Seller.cs:25`).
*Ce que l'attaquant obtient* : `POST /api/v1/merchants {"shopName":"…","commissionRate":0}` — le
dossier vendeur porte durablement un taux nul. **Aujourd'hui la colonne n'est lue par aucun calcul
d'argent** : `ProductOffer` utilise `IOfferPricingSettings.CommissionRate`, issu de
`IPlatformPricing` (`Catalog.Infrastructure/OfferPricingSettings.cs:36`,
`shared/common/HBA.Shared.Infrastructure/Configuration/PlatformPricing.cs:83`), et le domaine le dit
lui-même (`Seller.cs:69-85` : « conservée sans être lue »). Elle est en revanche exposée par gRPC
(`MerchantsGrpc.cs:270,451`) et affichée au vendeur. Le jour où `financial` implémentera
`ComputeCommission` (déclaré au proto, sans serveur — cf. GRPC_MATRIX), ce champ devient une perte
d'argent directe.
*Correction* : retirer `CommissionRate` du corps ; le taux est une donnée négociée, pas une
déclaration.

Autres DTO d'entrée examinés — **pas de mass assignment** :
* `PlaceOrderRequest` — `ShippingFee` a été retiré du corps, l'encadré `OrderEndpoints.cs:307-330`
  documente l'exploitation (livraison gratuite) qui a motivé la suppression. Reste
  `DeliveryQuoteId`, relu côté serveur.
* `CreateProductRequest` (`CatalogEndpoints.cs:1332-1347`) — plus de `SellerId` (`:1301`) ; le statut
  n'est pas dans le corps, il passe par `POST /{id}/status` gardé par trois permissions distinctes.
* `CreateOfferRequest` — pas de `SellerId` ; `StoreId` vient du corps mais **restreint** l'accès au
  lieu de l'élargir (`CatalogEndpoints.cs:661-676`).
* `SubmitReviewRequest` — auteur du jeton (`EngagementEndpoints.cs:109-113`).
* `UpsertRecommendationCommand` lié directement depuis le corps (`EngagementEndpoints.cs:174`) mais
  la route est `MapAdminGroup` (`:81-83`) — acceptable.
* `CreateInvoiceCommand`, `CreateCommissionRuleCommand`, `RunSettlementCommand` liés directement
  (`FinancialEndpoints.cs:461,366,592`) — tous `RequireAdmin()`.
* `DecideRefundDto(decimal Amount, string Currency)` (`ReturnRefundDtos.cs:67`) — le montant vient du
  client, mais il est plafonné par `RefundCalculationPolicy` ; le défaut est l'absence
  d'autorisation (SEC-02), pas le champ.

`[MEDIUM] SEC-26 — L'idempotence n'est pas cloisonnée pour les appels anonymes`
— `shared/common/HBA.Shared.Hosting/Http/IdempotencyEndpointFilter.cs:84` :
`var scope = HbaRequestContext.Current.Actor?.Id ?? string.Empty;`. Sur un endpoint anonyme, le
cloisonnement disparaît. Un seul cas concerné aujourd'hui : `POST /api/v1/auth/logout`
(`IdentityEndpoints.cs:98-99`, `AllowAnonymous().AllowIdempotency()`). Impact faible (la réponse
mise en cache est `{revoked:true}`), mais la règle ne doit pas dépendre de la liste des routes
anonymes du jour.

`[MEDIUM] SEC-27 — L'intercepteur gRPC interne ne couvre que les appels unaires`
— `shared/common/HBA.Shared.Hosting/Grpc/InternalCallInterceptors.cs:100-133` : seul
`UnaryServerHandler` est surchargé ; `ServerStreamingServerHandler`, `ClientStreamingServerHandler` et
`DuplexStreamingServerHandler` ne le sont pas, alors que `MapInternalGrpcService` pose
`AllowAnonymous()` sur l'ensemble du service (`Grpc/GrpcHostExtensions.cs:106-113`). Aucun `stream`
dans les 36 `.proto` du dépôt aujourd'hui (vérifié) : le trou est latent, il s'ouvrira au premier
flux de suivi GPS.

---

## 9. Tableau récapitulatif

| # | Sévérité | Titre | Fichier:ligne |
|---|---|---|---|
| SEC-01 | CRITICAL | Dix services exposent toute leur surface HTTP sans authentification | `services/{delivery/{delivery-pricing,dispatch,driver,proof-of-delivery,route,tracking},food/{availability,kitchen-prep,menu,review}}-service/src/*.Api/Program.cs` |
| SEC-02 | CRITICAL | Décision de remboursement d'un retour sans contrôle d'appartenance | `SellerReturnsEndpoints.cs:32-51` ; `ReturnLifecycleCommands.cs:70-182` |
| SEC-03 | CRITICAL | `sellerId` en query string → carnet de retours d'un concurrent | `SellerReturnsEndpoints.cs:26-27` |
| SEC-04 | CRITICAL | Retour ouvert sur la commande d'autrui | `CustomerReturnsEndpoints.cs:25-27` ; `CreateReturnCommand.cs:35-116` |
| SEC-05 | CRITICAL | Suppression de n'importe quel média par tout compte authentifié | `MediaEndpoints.cs:57,172-176` |
| SEC-06 | CRITICAL | Logout / suspension sans effet : `ValidateAccessToken` n'est jamais appelée | `IdentityModuleApi.cs:74` ; `LogoutCommandHandler.cs:34` |
| SEC-07 | HIGH | URL présignée sur pièce KYB/CNI, durée choisie par le client | `MediaEndpoints.cs:56,165-170` ; `MediaModuleApi.cs:101-120` |
| SEC-08 | HIGH | Propriétaire d'un média déclaré par le client | `MediaEndpoints.cs:54,78-142` |
| SEC-09 | HIGH | Métadonnées de n'importe quel média lisibles | `MediaEndpoints.cs:55,145-149` |
| SEC-10 | HIGH | `GET /api/v1/payments/intents/{id}` sans contrôle de propriété | `FinancialEndpoints.cs:433-436` |
| SEC-11 | HIGH | driver-service : identité livreur codée en dur | `DriverStore.cs:13` ; `DriverEndpoints.cs:13-45` |
| SEC-12 | HIGH | 18 permissions déclarées ne gardent rien (dont les 6 `RETURN_*`) | `MerchantPermission.cs:35-145` |
| SEC-13 | HIGH | Le cache d'autorisation ignore les mutations de rôle vendeur (2 min) | `SellersDbContext.cs:199-256` |
| SEC-14 | HIGH | Jetons de réinitialisation / vérification en clair sur Kafka et en outbox | `IdentityIntegrationEvents.cs:23,91` ; `OutboxMessage.cs:17` |
| SEC-15 | HIGH | Limiteur `auth` partitionné sur l'IP de la passerelle | `AuthRateLimiter.cs:68,122` |
| SEC-16 | HIGH | `ProxyTrust:TrustAnyProxy = true` versionné | `appsettings.json` ; `ForwardedHeadersExtensions.cs:24-36` |
| SEC-17 | HIGH | Lecture / annulation d'un retour par un tiers | `CustomerReturnsEndpoints.cs:34-45` ; `ReturnLifecycleCommands.cs:39-67` |
| SEC-18 | MEDIUM | `CommissionRate` fixé par le client à l'inscription vendeur | `MerchantEndpoints.cs:520,948` |
| SEC-19 | MEDIUM | Aucune politique de rôle sur les 44 routes YARP ; GET merchants anonyme au bord | `appsettings.json` (`ReverseProxy:Routes`) |
| SEC-20 | MEDIUM | `InitiatePayment` : ni acheteur vérifié, ni `ReturnUrl` validée, ni `PayerPhone` bornée | `FinancialEndpoints.cs:258,428` |
| SEC-21 | MEDIUM | `ApproveUserCommand` sans route : compte figé en `PendingVerification` | `ApproveUserCommand.cs:13` ; `IdentityEndpoints.cs:455-463` |
| SEC-22 | MEDIUM | Aucune vérification de numéro de téléphone | absence vérifiée dans tout le dépôt |
| SEC-23 | MEDIUM | Lectures de stock transverses (`/availability/{sku}`) | `InventoryEndpoints.cs:53-57` |
| SEC-24 | MEDIUM | Adresses e-mail complètes journalisées en `Information` | `ResendEmailSender.cs:61,71` |
| SEC-25 | MEDIUM | `POST /api/v1/admin/return-policies` ne persiste rien | `ReturnPolicyEndpoints.cs:11-23` |
| SEC-26 | MEDIUM | Idempotence non cloisonnée pour les appels anonymes | `IdempotencyEndpointFilter.cs:84` |
| SEC-27 | MEDIUM | Intercepteur gRPC interne limité aux appels unaires | `InternalCallInterceptors.cs:100-133` |
| SEC-28 | MEDIUM | Socle partagé sans validation de la configuration d'authentification au démarrage | `ServiceHostExtensions.cs:273-320` |
| SEC-29 | MEDIUM | `FoodPermission.StaffManage` / `AnalyticsRead` sans surface HTTP | `StaffRole.cs:64,73` ; `StaffQueries.cs:32` |
| SEC-30 | INFO | Secrets de développement versionnés (assumés et gardés) | `docker-compose.dev.yml:8-13,29,49` |
| SEC-31 | INFO | `DevelopmentEmailSender` journalise les jetons (gardé en Production) | `DevelopmentEmailSender.cs:32-38` |
| SEC-32 | INFO | Clé HMAC de développement dans `appsettings.Development.json` | `apps/api-gateway/.../appsettings.Development.json:11` |

**Compte : 6 CRITICAL · 11 HIGH · 12 MEDIUM · 3 INFO — 32 constats.**

### Ordre de traitement suggéré

1. **SEC-02 / SEC-03 / SEC-04 / SEC-17** — un seul service, une seule garde à écrire
   (`IMerchantAccessApi` + comparaison du `CustomerId`). C'est le plus gros gain par ligne du dépôt.
2. **SEC-05 / SEC-07 / SEC-08 / SEC-09** — media-service : quatre routes, une garde de propriété.
3. **SEC-01** — deux lignes par `Program.cs`, dix fois. À faire avant tout branchement de la
   passerelle vers ces services.
4. **SEC-06** — décision d'architecture (liste de révocation ou appel de validation) ; à trancher
   avant la mise en production des paiements.
5. Le reste, par sévérité.
