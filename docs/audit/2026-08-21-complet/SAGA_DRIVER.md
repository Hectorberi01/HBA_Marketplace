# Saga — Parcours livreur (7 services `services/delivery/*`)

Analyse statique. Toutes les preuves citent un chemin relatif à la racine du dépôt,
une classe/méthode et un numéro de ligne. Aucun test n'existe pour ce domaine.

---

## 0. Résumé exécutable

| Étape du parcours | État réel |
|---|---|
| Inscription livreur | **INEXISTANTE** — `Driver.Register` n'a aucun appelant |
| Vérification / activation | **INEXISTANTE** — `Driver.Verify` n'a aucun appelant, le rôle `Driver` n'est jamais attribué |
| Disponibilité (en ligne / pause / hors ligne) | **FICTIVE** — écrite dans un `ConcurrentDictionary` de `driver-service`, jamais dans l'agrégat |
| Recherche & offre | **CODÉE MAIS INERTE** — la boucle tourne, le cache de positions n'est jamais alimenté |
| Acceptation | **INEXISTANTE** — `Delivery.AcceptByDriver` n'a aucun appelant |
| Enlèvement / transit / arrivée | **INEXISTANTES** — les 4 commandes existent, aucune route ne les envoie |
| Suivi GPS | **NON RATTACHÉ** — service séparé, anonyme, en mémoire, ETA en dur |
| Preuve de remise | **NON RATTACHÉE** — OTP universel `"123456"`, aucun lien avec `DELIVERED` |
| Remise (`Delivered`) | **INJOIGNABLE** — `MarkDeliveredCommand` n'a aucun émetteur |
| Rémunération du livreur | **MORTE** — dépend de `DeliveryCompleted`, événement jamais levé |

**Conclusion factuelle : une course peut être créée, mise en recherche, et proposée
à un livreur — et rien au-delà. La chaîne s'arrête à `DriverAssigned`, dont la seule
sortie implémentée est le refus ou l'expiration.**

---

## 1. Ce qui est réellement implémenté, service par service

### 1.1 `delivery-service` — RÉEL, mais amputé de sa surface livreur

`services/delivery/delivery-service/` — 5 projets, ~7 400 lignes hors migrations,
EF Core + PostgreSQL (schéma `deliveries`), outbox drainé
(`HBA.Delivery.Core.Infrastructure/DeliveriesModuleInstaller.cs:112`
`services.AddOutboxProcessor<DeliveriesDbContext>()`), boucle de dispatch hébergée
(`DeliveriesModuleInstaller.cs:117-119`), Redis exigé hors développement
(`DeliveriesModuleInstaller.cs:206`).

Implémenté et joignable :
- création de course par gRPC (`HBA.Delivery.Core.Api/GrpcServices/DeliveryGrpcService.cs:70`),
  appelée par `order-service` (`services/marketplace/order-service/src/HBA.Order.Api/Integration/CreateDeliveryOnOrderConfirmedHandler.cs:213`)
  et `restaurant-service` (`services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Integration/FoodOrderBridgeHandlers.cs:284`) ;
- 4 routes HTTP d'exploitation (`HBA.Delivery.Core.Api/Endpoints/DeliveryEndpoints.cs:43-47`),
  sous `MapOperationsGroup` (rôle `Admin` ou `Dispatcher`) ;
- boucle de fond : expiration des offres, ouverture des courses programmées, proposition
  (`HBA.Delivery.Core.Infrastructure/Dispatch/DeliveryDispatchService.cs:143-302`) ;
- API partenaire externe (`Domain/Partners/`), webhooks signés HMAC (`Domain/Webhooks/`).

Écrit mais **injoignable** :
- `MarkArrivedAtPickupCommand`, `MarkPickedUpCommand`, `MarkInTransitCommand`,
  `MarkArrivedAtDropoffCommand`, `MarkDeliveredCommand`
  (`HBA.Delivery.Core.Application/Commands/MarkPickedUp/DeliveryProgressCommands.cs:23-39`)
  et leur handler (`:82-236`) — **aucun `sender.Send` de ces cinq commandes n'existe
  dans le dépôt** (vérifié par recherche exhaustive sur `services/`, `apps/`, `shared/`, `tests/`) ;
- `MyDeliveriesQuery` (`Application/Queries/GetTimeline/MyDeliveriesQuery.cs:67`) —
  aucun appelant ; son propre commentaire (`:52`) annonce « CETTE ROUTE MANQUAIT », et elle
  manque toujours ;
- `Delivery.AcceptByDriver` (`Domain/Aggregates/Delivery/Delivery.cs:476`) et
  `Delivery.RevokeAssignment` (`:548`) — **zéro appelant**.

Ligne morte visible : `DeliveryEndpoints.cs:14` crée
`var deliveries = app.MapAuthenticatedGroup("/api/deliveries")` — variable jamais utilisée.
C'est le vestige du groupe livreur supprimé.

### 1.2 `delivery-pricing-service` — RÉEL, non authentifié, hors solution

Seul satellite avec un `DbContext` EF (`Infrastructure/Persistence/DeliveryPricingDbContext.cs`)
et un outbox drainé (`DeliveryPricingInfrastructureModule.cs:25`). Les 4 RPC du proto sont
implémentés. Montants en `long` (unités XOF entières) — pas de `double`.

Deux défauts structurels :
- `Api/Program.cs` n'appelle ni `AddHbaService` ni `UseHbaService` : **aucune
  authentification n'est branchée**. `Api/Endpoints/DeliveryPricingEndpoints.cs:47`
  crée le groupe d'administration tarifaire avec un `MapGroup` nu (§2.2 de ce rapport) ;
- **absent de `HBA.sln`** : `grep -c "HBA.Delivery.Pricing" HBA.sln` → `0`, alors que les
  six autres services du domaine y comptent 4 projets chacun.

### 1.3 `dispatch-service` — SQUELETTE

`Application/Abstractions/DispatchStore.cs` : trois `ConcurrentDictionary` (`:9-11`),
aucun `DbContext`, aucune migration.
`Infrastructure/Persistence/DispatchInfrastructureModule.cs` enregistre un
`IntegrationEventQueue` **sans `AddOutboxProcessor`** : les événements
`DispatchStarted`, `DispatchOfferCreated`, `DeliveryAssigned` sont mis en file et
jamais publiés.

Candidats codés en dur (`DispatchStore.cs:138-142`) :
```csharp
new DriverCandidate(deliveryId, Guid.Parse("00000000-0000-7000-0000-000000000017"), 920, 240, 0.91m, 1, ...),
new DriverCandidate(deliveryId, Guid.Parse("00000000-0000-7000-0000-000000000018"), 1450, 420, 0.78m, 2, ...)
```

Le domaine du service (`Domain/Aggregates/DispatchJob/DispatchAggregate.cs`) est un
`record` sans invariant, et les enums `DispatchStatus` / `AssignmentStatus` qu'il déclare
ont **zéro référence dans tout le dépôt** : le store manipule des chaînes `"OFFERING"`,
`"ASSIGNED"`, `"CANCELLED"`.

À l'inverse, `Domain/Policies/DispatchPolicy.cs` (154 l.) est la vraie politique de
classement — mais elle vit dans le namespace `HBA.Deliveries.Domain.Dispatch` et n'est
utilisée que par `delivery-service`
(`HBA.Delivery.Core.Application/Commands/AssignDriver/DispatchDeliveryCommand.cs:114`),
jamais par `dispatch-service` lui-même (`HBA.Delivery.Dispatch.Application.csproj` ne
référence pas `HBA.Delivery.Dispatch.Domain`).

### 1.4 `driver-service` — SQUELETTE, et doublon du vrai agrégat (§1.8)

`Application/Abstractions/DriverStore.cs` : trois `ConcurrentDictionary`, **un unique
livreur codé en dur** au constructeur (`:13-33`), identifiant
`00000000-0000-7000-0000-000000000017`.
`Api/Endpoints/DriverEndpoints.cs:11` : `MapGroup` nu, **aucune authentification** dans
`Api/Program.cs`. Toutes les routes `/me` (`:13`, `:15`, `:21`, `:24`, `:34`, `:44`)
opèrent sur `store.DefaultDriverId` — c'est-à-dire sur le même livreur, quel que soit
l'appelant, y compris anonyme.
`GetActiveDeliveries` (`DriverStore.cs:152-155`) rend une course fictive en dur.
`Infrastructure/Persistence/DriversInfrastructureModule.cs` : pas de `DbContext`, pas
d'`AddOutboxProcessor` → `DriverAvailabilityChangedIntegrationEvent` et
`DriverVehicleUpdatedIntegrationEvent` sont enfilés et jamais publiés (et personne ne
les consomme : aucun `IIntegrationEventHandler<DriverAvailabilityChanged…>` dans le dépôt).

Aucun document d'inscription n'est traité : `Domain/Entities/DriverDocument.cs` est un
`record` déclaré et **jamais instancié**.

### 1.5 `route-service` — SQUELETTE

`Application/Abstractions/RouteStore.cs` : un `ConcurrentDictionary`, Haversine local
(`:85-95`), vitesse constante `5.8 m/s` (`:17`, `:64`), fournisseur toujours
`"FALLBACK_HAVERSINE"`.
`Application/Abstractions/IRouteProvider.cs` déclare l'abstraction du vrai calcul
d'itinéraire : **aucune implémentation, aucun enregistrement DI**.
`Domain/Enums/RouteEnums.cs` déclare `RouteProvider { Mapbox, GoogleMaps, Osrm, FallbackHaversine }`
et `RouteOptimizationMode` : **zéro référence** dans le dépôt.
Routes anonymes (`Api/Endpoints/RouteEndpoints.cs:11`, `:32`). Pas d'outbox.

### 1.6 `tracking-service` — SQUELETTE

`Application/Abstractions/TrackingStore.cs` : trois `ConcurrentDictionary`.
ETA **codé en dur à 540 secondes** (`:86`, `:110`) et progression d'itinéraire en dur
`RouteProgress(0.35m, 5100)` (`:87`).
`Api/Endpoints/TrackingEndpoints.cs:11` : `MapGroup` nu, aucune authentification.
Le `driverId` est lu **dans le corps de la requête** (`LocationBatchRequest.DriverId`,
`TrackingStore.cs:128`, route `:13`).
Le domaine (`Domain/Aggregates/TrackingSession/TrackingAggregate.cs`) déclare
`TrackingSessionStatus { Active, Completed, Cancelled }` : **zéro référence**. Le store
manipule les chaînes `"ACTIVE"` / `"COMPLETED"`, et `"CANCELLED"` n'est écrit nulle part.
`Domain/Policies/LocationValidationPolicy.cs` et `SamplingPolicy.cs` : jamais appelées —
la plausibilité est réimplémentée dans le store (`TrackingStore.cs:120-123`).
Pas d'outbox.

### 1.7 `proof-of-delivery-service` — SQUELETTE

`Application/Abstractions/ProofStore.cs` : deux `ConcurrentDictionary`.
OTP universel (`:118-127`) :
```csharp
var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(otp)));
return hash.Length == 64 && otp is "123456";
```
Le hachage est calculé puis jeté ; la seule condition réelle est `otp is "123456"`.
`Presign` (`:30-46`) fabrique une URL `https://storage.local/...?signature=dev` : aucun
stockage réel, aucune signature.
Routes anonymes (`Api/Endpoints/ProofEndpoints.cs:11`, `:43`). Pas d'outbox.
Le domaine déclare deux modèles concurrents et inutilisés :
`Domain/Aggregates/DeliveryProof/DeliveryProof.cs` (`ProofStatus`, `ProofType`,
`ProofMediaType`, `ProofValidationPolicy`) et `Domain/Entities/*` + `Domain/Policies/*` —
**aucun des deux n'est référencé par le store**, qui redéclare ses propres records
(`ProofStore.cs:130-137`) avec des `string`.

### 1.8 Le doublon livreur — confirmé, et il est à l'envers de ce qu'on attend

Le dossier demandait de vérifier si du code livreur vit *dans* `delivery-service`.
C'est le cas, et la répartition est inversée :

- Le **vrai agrégat** `Driver` (336 l., machine à états correcte, séparation
  compte/disponibilité) est déclaré dans `driver-service`
  (`services/delivery/driver-service/src/HBA.Delivery.Driver.Domain/Aggregates/Driver/DeliveryDriver.cs:92`,
  namespace `HBA.Deliveries.Domain.Drivers`).
- Il est **persisté par `delivery-service`** : `DbSet<Driver> Drivers`
  (`delivery-service/.../Persistence/DeliveriesDbContext.cs:27`), table `deliveries.drivers`
  (`Persistence/Configurations/DriverConfiguration.cs:54`), dépôt
  `DriverRepository` (`Persistence/Repositories/DeliveryRepositories.cs:117-166`).
- `driver-service`, lui, n'a **aucun accès à cette table** : il tourne sur son
  `ConcurrentDictionary` et redéclare son propre modèle
  (`Domain/Aggregates/Driver/DriverAggregate.cs`, namespace `HBA.Drivers.Domain.Drivers`)
  **plus** un troisième jeu d'enums (`Domain/Enums/DriverEnums.cs`).

Il existe donc **trois déclarations concurrentes** de l'état d'un livreur :

| Déclaration | Fichier | Utilisée ? |
|---|---|---|
| `DriverAccountStatus` / `DriverAvailability` | `driver-service/.../Aggregates/Driver/DeliveryDriver.cs:12`, `:30` | oui, par le domaine — mais le domaine n'a pas d'appelant (§2) |
| `DriverStatus` / `DriverVerificationStatus` / `DriverAvailabilityStatus` / `VehicleType` | `driver-service/.../Aggregates/Driver/DriverAggregate.cs:106-109` | **non — zéro référence** |
| `DriverAccountStatus` / `DriverAvailabilityStatus` / `DriverVerificationStatus` / `VehicleType` | `driver-service/.../Enums/DriverEnums.cs:39-42` | **non — zéro référence** |
| chaînes `"ACTIVE"` / `"VERIFIED"` / `"AVAILABLE"` / `"BUSY"` / `"PAUSED"` / `"OFFLINE"` | `driver-service/.../Application/Abstractions/DriverStore.cs` | oui — c'est le seul modèle réellement servi par l'API |

**Sévérité : HIGH.** Deux services publient une notion de « livreur » incompatible
(enum contre chaîne, `OnBreak` contre `Paused`, `Blocked` absent côté store, `Deleted`
présent d'un côté seulement), et l'API livreur exposée par `driver-service` ne lit pas la
table où les livreurs sont censés vivre.

### 1.9 Violation d'architecture : dépendances croisées entre services

Les « microservices » du domaine partagent leurs assemblies Domain au moment de la compilation :

- `delivery-service/.../HBA.Delivery.Core.Application.csproj:6-7` référence
  `driver-service/.../HBA.Delivery.Driver.Domain.csproj` **et**
  `dispatch-service/.../HBA.Delivery.Dispatch.Domain.csproj` ;
- même chose pour `HBA.Delivery.Core.Infrastructure.csproj:7-8` et `HBA.Delivery.Core.Api.csproj:6` ;
- réciproquement `driver-service/.../HBA.Delivery.Driver.Domain.csproj:4`,
  `dispatch-service/.../HBA.Delivery.Dispatch.Domain.csproj:4` et
  `delivery-pricing-service/.../HBA.Delivery.Pricing.Domain.csproj:4-5` référencent
  `delivery-service/.../HBA.Delivery.Core.Domain.csproj`.

**Sévérité : MEDIUM.** Ces quatre services ne sont pas déployables indépendamment, et la
règle « une base logique par service » est déjà violée : la table `drivers` vit dans le
schéma `deliveries`.

---

## 2. Les cinq parcours, reconstruits

### 2.1 Inscription livreur — documents → véhicule → vérification → ACTIF

**Chaîne attendue** (le code la décrit dans ses commentaires) :
`Driver.Register` → dépôt de pièces → `Driver.Verify` → `DriverVerifiedDomainEvent` →
`DriverVerifiedIntegrationEvent` → `GrantDriverRoleHandler` → rôle `Driver` côté Identity.

**Chaîne réelle : elle n'existe à aucun maillon.**

| Maillon | Preuve |
|---|---|
| `Driver.Register` | `driver-service/.../DeliveryDriver.cs:148` — **0 appelant** dans tout le dépôt |
| `IDriverRepository.AddAsync` | `driver-service/.../Repositories/IDriverRepository.cs:160`, implémenté `delivery-service/.../DeliveryRepositories.cs:164` — **0 appelant** |
| `RegisterDriverCommandHandler` | référencé par deux commentaires (`delivery-service/.../Configurations/DriverConfiguration.cs:43`, `services/common/identity-service/.../IdentityDataSeeder.cs:46`) — **la classe n'existe pas** |
| Documents | `DriverDocument` (`driver-service/.../Entities/DriverDocument.cs:10`) — jamais instancié ; aucune route de téléversement |
| Véhicule | `POST /api/v1/drivers/me/vehicles` (`DriverEndpoints.cs:24`) écrit dans le dictionnaire mémoire, sans lien avec `Driver.Vehicle` |
| `Driver.Verify()` | `DeliveryDriver.cs:176` — **0 appelant** |
| Route d'administration « vérifier un livreur » | **inexistante** : aucun `MapAdminGroup` ne concerne les livreurs (recherche exhaustive `MapAdminGroup(` → 15 occurrences, aucune sur `driver`) |
| `ListByAccountStatusAsync` (« qui attend ? ») | `IDriverRepository.cs:157`, implémenté `DeliveryRepositories.cs:153` — **0 appelant** |

Conséquence en chaîne : `DriverVerifiedDomainEvent` n'est jamais levé, donc
`DriverVerifiedIntegrationEvent` n'est jamais publié
(`delivery-service/.../EventHandlers/DeliveryDomainEventHandlers.cs:103-118`), donc
`GrantDriverRoleHandler`
(`services/common/identity-service/src/HBA.Identity.Application/Users/EventHandlers/BusinessRoleGrantHandlers.cs:256`)
ne s'exécute jamais. Le rôle `Driver` **n'est attribué à personne** — ce que
`shared/common/HBA.Shared.Hosting/Http/ApiAuthorization.cs:37-39` reconnaît explicitement.

**Sévérité : CRITICAL** (parcours métier entier absent).

### 2.2 Disponibilité — HORS LIGNE → DISPONIBLE → OCCUPÉ → DISPONIBLE

Deux implémentations, aucune fonctionnelle.

**(a) L'agrégat.** `GoOnline` (`DeliveryDriver.cs:236`), `GoOffline` (`:249`),
`TakeBreak` (`:265`), `MarkBusy` (`:289`), `CompleteMission` (`:301`).
Les gardes sont correctes — `GoOnline` refuse si `AccountStatus != Active` (`:238-243`),
`GoOffline` et `TakeBreak` refusent si `Busy` (`:254`, `:267`).
**Appelants : `CompleteMission` en a exactement un**
(`delivery-service/.../DeliveryProgressCommands.cs:197`), et il est lui-même injoignable.
`GoOnline`, `GoOffline`, `TakeBreak`, `MarkBusy`, `RecordPosition` : **zéro appelant**.

**(b) Le store mémoire.** `POST /api/v1/drivers/me/availability`
(`DriverEndpoints.cs:34`) → `DriverStore.SetAvailabilityAsync` (`DriverStore.cs:88`).
Route **anonyme**, `driverId` ignoré au profit de `DefaultDriverId`, normalisation par
chaîne (`:157-161`), aucun contrôle du statut de compte. **Un compte suspendu peut donc
se remettre « AVAILABLE » depuis cette route** — l'incident que le commentaire de
`DeliveryDriver.cs:78-81` prétend « impossible par construction ».

**Conséquence directe : `MarkBusy` n'ayant aucun appelant, aucun livreur n'atteint jamais
`Busy` ; `CompleteMission` (`:303`) refuse alors avec `driver.not_on_mission`.** Le
« retour à disponible » de fin de course est mort dans les deux sens.

**Sévérité : CRITICAL.**

### 2.3 Affectation — demande → recherche → candidats → offre → acceptation

Deux chemins concurrents, l'un correct et inerte, l'autre joignable et dangereux.

**Chemin A — `delivery-service` (correct sur le papier, inerte en pratique)**

1. `CreateDeliveryCommandHandler` crée la course et appelle `StartSearching()`
   sauf si elle est programmée (`Commands/CreateDelivery/CreateDeliveryCommand.cs:181-189`).
2. `DeliveryDispatchService` tourne toutes les 5 s sous verrou PostgreSQL
   (`Dispatch/DeliveryDispatchService.cs:106-113`), expire les offres périmées (`:216-247`),
   ouvre les fenêtres programmées (`:170-213`), puis propose (`:250-302`).
3. `DispatchDeliveryCommandHandler` (`Commands/AssignDriver/DispatchDeliveryCommand.cs:72-143`)
   demande au cache `FindNearbyAsync` (`:94`), charge les livreurs, classe par
   `DispatchPolicy.Rank` (`:114`), et appelle `delivery.AssignTo(best.DriverId)` (`:134`).

**Le point de rupture est le cache.** `IDriverLocationCache.SetAsync`
(`Application/Abstractions/DeliveryAbstractions.cs:73`) est implémenté deux fois
(`Infrastructure/Redis/InMemoryDriverLocationCache.cs:38`,
`Infrastructure/Redis/RedisDriverLocationCache.cs:55`) et **n'a aucun appelant**.
`FindNearbyAsync` rend donc toujours une liste vide, `DispatchDeliveryCommandHandler:97-100`
sort immédiatement, et **aucune course n'est jamais proposée**. La course tourne
indéfiniment entre `SearchingDriver` et `NoDriverAvailable` (`DeliveryDispatchService.cs:269-280`
la rouvre à chaque tour).

**Chemin B — `dispatch-service` (joignable, sans aucun contrôle)**

`POST /api/v1/dispatch/{deliveryId}/manual-assign` (`DispatchEndpoints.cs:28`) et
`AcceptOffer` en gRPC (`GrpcServices/DispatchGrpcService.cs:64-73`) appellent tous deux
`DispatchStore.AssignAsync` (`DispatchStore.cs:91-113`), qui :
- n'exige **aucune authentification** (groupe `MapGroup` nu, hôte sans middleware d'auth) ;
- ne vérifie **ni l'existence de la course, ni celle du livreur, ni son éligibilité** ;
- écrase `_assignments[deliveryId]` sans condition (`:99`) ;
- publie `DeliveryAssignedIntegrationEvent` (`:106`) — dans une file jamais drainée.

**Sévérité : CRITICAL** (affectation arbitraire non authentifiée) + **CRITICAL**
(chaîne nominale rompue au cache de positions).

### 2.4 Enlèvement — affecté → trajet → session de suivi → SUR PLACE → RÉCUPÉRÉ

- `Delivery.MarkArrivedAtPickup` (`Delivery.cs:569`) exige `DriverAccepted` ;
  `MarkPickedUp` (`:572`) tolère `ArrivedAtPickup` **ou** `DriverAccepted` (`:579`).
- Ces deux transitions ne sont atteignables que depuis `DriverAccepted`, qui n'est jamais
  atteint (§2.3). Elles sont donc mortes deux fois : par l'amont et par l'absence de route.
- **Aucune session de suivi n'est ouverte par la logistique.** `tracking-service` n'est
  appelé par personne : recherche exhaustive de `ITrackingGrpcClient` / `TrackingApi` côté
  `delivery-service` → aucun résultat ; `delivery-service` ne référence que
  `HBA.DeliveryPricing.Contracts.Grpc` (`HBA.Delivery.Core.Infrastructure.csproj:9`).
  La session se crée toute seule au premier point envoyé
  (`TrackingStore.AddLocationsAsync:61-64`), par un appelant anonyme qui déclare lui-même
  le `driverId`.
- Aucun itinéraire n'est demandé non plus : `route-service` n'a aucun client dans le dépôt.

### 2.5 Livraison — EN TRANSIT → itinéraire/ETA → suivi → À DESTINATION → preuve → LIVRÉ

- `MarkInTransit` (`Delivery.cs:589`) exige `PickedUp` ; `MarkArrivedAtDropoff` (`:592`)
  accepte `InTransit` **ou** `PickedUp` (`:596`) ; `MarkDelivered` (`:624`) accepte
  `ArrivedAtDropoff` **ou** `InTransit` (`:628`).
- Conséquence de la double tolérance : `DeliveryStatus.InTransit` **n'est jamais assigné**
  (aucun `Status = DeliveryStatus.InTransit` dans le dépôt) et `ArrivedAtPickup` non plus
  (le seul chemin passe par `Advance(DriverAccepted → ArrivedAtPickup)`, dont la commande
  n'a pas de route). Deux états promis au client qui n'existeront jamais en base.
- ETA : produit par `tracking-service` en dur (540 s) et par `route-service`
  (distance/5.8) ; ni l'un ni l'autre n'est consommé par `delivery-service`, dont le DTO
  de suivi (`Infrastructure/Public/DeliveryModuleApi.cs:102-109`) ne porte pas d'ETA du tout.
- Preuve : voir §3.4.
- `Delivered` : `MarkDeliveredCommand` n'a aucun émetteur → **l'état terminal n'est jamais
  atteint par le chemin livreur.**

---

## 3. Les six questions, réponses avec preuve

### 3.1 Un livreur suspendu peut-il recevoir une offre ?

**Par le chemin `delivery-service` : non.**
`DispatchPolicy.Rank` écarte tout candidat dont `CanReceiveOffers` est faux
(`dispatch-service/.../Domain/Policies/DispatchPolicy.cs:88-91`), et
`Driver.CanReceiveOffers` exige `AccountStatus == Active && Availability == Available`
(`driver-service/.../DeliveryDriver.cs:145-146`). Correct.

**Par le chemin `dispatch-service` : oui, sans restriction.**
`DispatchStore.BuildCandidates` (`DispatchStore.cs:138-142`) rend deux GUID constants sans
consulter quoi que ce soit, et `AssignAsync` (`:91`) accepte n'importe quel `driverId`
fourni dans le corps de `POST /api/v1/dispatch/{id}/manual-assign` — route anonyme.
`DriverEligibilityPolicy` (`driver-service/.../Policies/DriverEligibilityPolicy.cs:59`)
et `DriverStore.CheckEligibility` (`DriverStore.cs:134`) existent, mais **ne sont appelés
par aucun des deux chemins d'affectation** : `CheckEligibility` n'est joignable que par
`POST /internal/v1/drivers/eligibility` (`DriverEndpoints.cs:54`), que personne n'appelle.

**Réponse : oui, dès qu'on emprunte `dispatch-service`. Sévérité HIGH.**
Remarque aggravante : la garde correcte est de toute façon sans objet, puisqu'aucun livreur
n'atteint jamais `Active` (§2.1).

### 3.2 Deux livreurs peuvent-ils accepter la même mission ?

Trois constats, dans cet ordre.

1. **Dans `delivery-service`, personne ne peut accepter** : `AcceptByDriver`
   (`Delivery.cs:476`) n'a aucun appelant. La question ne se pose pas en pratique.
2. **La garde logique est bonne mais non protégée en base.** `AcceptByDriver` exige
   `Status == DriverAssigned` (`:478`) et une offre `Offered` pour ce livreur (`:483-488`).
   Or `Delivery` **n'a aucun jeton de concurrence** : `DeliveryConfiguration.cs` ne contient
   ni `IsConcurrencyToken` ni `IsRowVersion` (vérifié : la seule occurrence de
   `IsConcurrencyToken` du dépôt entier est
   `services/marketplace/return-refund-service/.../ReturnRequestConfiguration.cs:26`).
   Deux transactions concurrentes lisent donc le même `DriverAssigned`, passent la garde
   toutes les deux, et la seconde écrase la première sans erreur.
   `DeliveryDispatchService.cs:93-96` le reconnaît par écrit : « Rien dans l'agrégat ne
   l'attrape — `Delivery` n'a pas de jeton de concurrence ». Le verrou `SingleRunnerLock`
   protège la *proposition*, pas l'*acceptation*.
3. **Aucune contrainte d'unicité en base.** `DeliveryConfiguration.cs:152` pose
   `HasIndex(d => d.AssignedDriverId)` **non unique** ; `:126` pose
   `assignment.HasIndex(a => a.DriverId)` non unique. Rien n'empêche deux lignes
   `delivery_assignments` en `Accepted` sur la même course, ni un même livreur affecté à
   plusieurs courses actives.
4. **Dans `dispatch-service`, oui, ouvertement** : `AssignAsync` (`DispatchStore.cs:98-104`)
   écrase l'affectation existante sans la lire. Deux appels successifs à `manual-assign` ou
   `AcceptOffer` avec deux `driverId` différents produisent deux
   `DeliveryAssignedIntegrationEvent` et laissent le dernier gagner.

**Réponse : oui — par `dispatch-service` de façon triviale, et par `delivery-service` dès
que le chemin d'acceptation sera branché, faute de verrou optimiste et de contrainte
d'unicité. Sévérité CRITICAL** (deux livreurs se déplacent, un seul est payé).

### 3.3 Le suivi est-il réservé au livreur affecté ?

**Non, à aucun niveau.**

- `POST /api/v1/tracking/sessions/{deliveryId}/locations` (`TrackingEndpoints.cs:13`) :
  groupe `MapGroup` nu, hôte sans authentification. Le `driverId` vient du **corps**
  (`LocationBatchRequest`, `TrackingStore.cs:128`). N'importe qui peut donc injecter la
  position d'un livreur arbitraire sur une course arbitraire.
- `TrackingStore.AddLocationsAsync` (`:54-115`) ne compare jamais le `driverId` reçu à
  celui de la session existante (`:61-64` : si la session existe, le paramètre `driverId`
  est utilisé tel quel dans le snapshot `:80-87`).
- `GET /api/v1/tracking/deliveries/{deliveryId}/latest` (`:24`) : anonyme, rend
  latitude/longitude et ETA de n'importe quelle course.
- `GET /api/v1/tracking/deliveries/{deliveryId}/stream-token` (`:29`) fabrique un jeton
  `trk_{Guid}` **sans le stocker ni le vérifier nulle part** — un jeton décoratif.
- `POST /internal/v1/tracking/sessions/start` (`:34`) : anonyme, accepte
  `(deliveryId, driverId)` arbitraires.

Le seul contrôle correct du dépôt est côté `delivery-service` :
`DeliveryModuleApi.GetTrackingAsync` (`Infrastructure/Public/DeliveryModuleApi.cs:90-100`)
n'expose la position que si la course est en cours **et** pour le livreur affecté. Mais ce
chemin lit le cache Redis, jamais alimenté (§2.3), et la route qui l'expose
(`DeliveryEndpoints.cs:46`) est réservée à `Admin`/`Dispatcher` — le client n'a donc aucune
route de suivi légitime.

**Sévérité : HIGH** (falsification de position + fuite de position en direct).

### 3.4 La preuve de livraison est-elle obligatoire, et selon quelle règle ?

**Règle du domaine (correcte)** : `Delivery.MarkDelivered` (`Delivery.cs:624`) exige une
preuve **si et seulement si** `RequiredProof != None` (`:642`). La vérification est réelle :
`ProofOfDelivery.Capture` (`Entities/ProofOfDelivery.cs:77`) compare le PIN à temps constant
au code émis (`:108-136`) et refuse une photo/signature qui ne ressemble pas à une référence
de stockage (`:144-161`). Le PIN est émis cryptographiquement à la création si
`RequiredProof == Pin` (`Delivery.cs:76`, `ProofOfDelivery.IssuePin:70`). Cinq échecs
verrouillent la course (`Delivery.cs:226-229`, `:644-650`), et le compteur est bien persisté
même en cas d'échec (`DeliveryProgressCommands.cs:182-185`).

**Règle effective : aucune preuve n'est jamais exigée.**
`RequiredProof` vaut `"None"` par défaut dans le contrat
(`HBA.Deliveries.Contracts/DeliveryDispatchContracts.cs:45`), et **aucun des deux
producteurs de courses ne le renseigne** :
- `order-service` : `CreateDeliveryOnOrderConfirmedHandler.cs:213-269` — le paramètre
  `RequiredProof` n'apparaît pas ;
- `restaurant-service` : `FoodOrderBridgeHandlers.cs:284-317` — idem.

Toute course de la plateforme naît donc avec `RequiredProof = None`, et
`ProofOfDeliveryKind.Pin/Photo/Signature` ne sont **jamais assignés**.

**Sévérité : HIGH.** Le dispositif de preuve, entièrement écrit et correct, est
inopérant par défaut de paramétrage à l'appel.

### 3.5 Une livraison peut-elle passer à LIVRÉ sans preuve valide ?

**Oui — c'est le comportement nominal**, par conjonction de deux faits :
1. `RequiredProof` vaut toujours `None` (§3.4), donc `MarkDelivered(null, taux)` passe la
   garde `:642` sans rien vérifier et pose `Status = Delivered` (`:683`) ;
2. `proof-of-delivery-service` est **totalement déconnecté** : son
   `DeliveryProofCompletedIntegrationEvent` (`ProofStore.cs:88-92`) n'est publié nulle part
   (pas d'outbox) et n'a **aucun consommateur** dans le dépôt ; son
   `GET /internal/v1/proofs/deliveries/{id}/dropoff-valid` (`ProofEndpoints.cs:45`), qui
   existe précisément pour garder la remise, **n'est appelé par personne**.

Et si l'on branchait ce service tel quel, la preuve serait sans valeur : l'OTP accepté est
`"123456"` pour tous (`ProofStore.cs:126`), et `ProofValidationPolicy.ResolveStatus`
(`Domain/Aggregates/DeliveryProof/DeliveryProof.cs:30`) déclare `Verified` dès qu'**un
média existe**, sans vérifier son contenu — un `Presign` suffit à créer ce média
(`ProofStore.cs:39-43`), sans qu'aucun fichier soit téléversé.

**Sévérité : CRITICAL.** C'est le cas décrit par `ProofOfDelivery.cs:19-21` : « une absence
de preuve qui se présente comme une preuve ».

### 3.6 Le livreur redevient-il disponible après la livraison — par quel mécanisme ?

**Mécanisme prévu** : `MarkDeliveredCommandHandler`
(`DeliveryProgressCommands.cs:143-202`) charge le livreur (`:192`) et appelle
`driver?.CompleteMission()` (`:197`) dans la même unité de travail que la remise (`:200`).
L'intention est explicitée en tête de fichier (`:70-79`).

**Mécanisme réel : aucun.** Trois ruptures cumulées :
1. `MarkDeliveredCommand` n'a **aucun émetteur** — la méthode n'est jamais exécutée ;
2. même exécutée, `CompleteMission` (`DeliveryDriver.cs:301`) échoue : elle exige
   `Availability == Busy` (`:303`), et `MarkBusy` (`:289`) **n'a aucun appelant** — le
   livreur n'est jamais passé `Busy` ;
3. l'échec est **silencieux** : `driver?.CompleteMission();` (`:197`) ignore le `Result`
   retourné. Le commentaire `:194-196` justifie de ne pas annuler la remise, mais rien
   n'est journalisé ni compensé — l'incident serait invisible.

Le chemin parallèle `POST /internal/v1/drivers/{id}/busy-state`
(`DriverEndpoints.cs:57` → `DriverStore.SetBusyStateAsync:107`) écrit `"AVAILABLE"` /
`"BUSY"` dans le dictionnaire mémoire de `driver-service` — sans authentification, et sans
aucun rapport avec la table `deliveries.drivers`. Il n'a lui non plus aucun appelant.

**Sévérité : CRITICAL** si la chaîne était branchée (livreur bloqué « en course » à vie) ;
**INFO** aujourd'hui puisqu'aucune course n'atteint la remise.

---

## 4. Effets de bord confirmés hors du domaine

- **Le livreur n'est jamais payé.** `CreditDriverOnDeliveryCompletedHandler`
  (`services/common/wallet-service/src/HBA.Financial.Wallet.Application/Earnings/CreditDriverOnDeliveryCompletedHandler.cs:110`)
  envoie `CreditDriverEarningCommand` sur `DeliveryCompletedIntegrationEvent`. Cet
  événement dérive de `DeliveryCompletedDomainEvent`, levé uniquement par
  `Delivery.MarkDelivered` (`Delivery.cs:704`) — jamais exécuté. Toute la chaîne aval
  (portefeuille livreur, notification `DriverEarningCreditedIntegrationEvent`) est morte.
- **La commande marketplace n'atteint jamais « livrée ».**
  `MarkOrderDeliveredOnDeliveryCompletedHandler`
  (`services/marketplace/order-service/.../MarkOrderDeliveredOnDeliveryCompletedHandler.cs:105`)
  dépend du même événement. `OrderStatus.Delivered` n'est donc atteignable que par le
  chemin food (`FoodOrderCommands.cs:420`) ou par `MealOrderLifecycleCommands.cs:159`.
- **La passerelle appelle deux routes inexistantes.**
  `apps/api-gateway/src/HBA.Gateway.Infrastructure/HttpClients/Delivery/DeliveryClient.cs:22`
  et `:27` appellent `/api/deliveries/drivers/me` et `/api/deliveries/drivers/me/missions`.
  `DeliveryEndpoints.cs` ne déclare que quatre routes sous `/api/deliveries`, aucune sous
  `/drivers`. Les trois écrans du BFF livreur (`DriverController.cs:50`, `:63`, `:85`)
  rendent donc systématiquement une erreur de service.

---

## 5. Défauts classés (domaine livreur)

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| D1 | CRITICAL | Inscription, vérification et activation d'un livreur : aucun code joignable | `DeliveryDriver.cs:148`, `:176` — 0 appelant ; aucun `RegisterDriverCommand` dans le dépôt |
| D2 | CRITICAL | Acceptation d'une mission impossible | `Delivery.cs:476` — 0 appelant |
| D3 | CRITICAL | Enlèvement, transit, remise impossibles | `DeliveryProgressCommands.cs:23-39` — 0 émetteur |
| D4 | CRITICAL | Affectation manuelle non authentifiée et sans contrôle | `DispatchEndpoints.cs:28`, `DispatchStore.cs:91-113` |
| D5 | CRITICAL | Livraison marquée LIVRÉ sans preuve, par construction | `DeliveryDispatchContracts.cs:45` + producteurs qui ne renseignent pas `RequiredProof` |
| D6 | CRITICAL | Aucun verrou optimiste sur `Delivery` ni contrainte d'unicité d'affectation | `DeliveryConfiguration.cs:152`, absence de `IsConcurrencyToken` |
| D7 | CRITICAL | Cache de positions jamais alimenté → aucune course n'est jamais proposée | `IDriverLocationCache.SetAsync` — 0 appelant |
| D8 | HIGH | OTP de preuve universel `"123456"` | `ProofStore.cs:126` |
| D9 | HIGH | Suivi GPS anonyme, `driverId` déclaré par l'appelant | `TrackingEndpoints.cs:13`, `TrackingStore.cs:128` |
| D10 | HIGH | Routes d'administration tarifaire de livraison anonymes | `DeliveryPricingEndpoints.cs:47` |
| D11 | HIGH | Trois modèles concurrents du livreur, dont deux morts | §1.8 |
| D12 | HIGH | Événements de 5 satellites enfilés et jamais drainés (pas d'`AddOutboxProcessor`) | `DispatchInfrastructureModule.cs`, `DriversInfrastructureModule.cs`, `TrackingInfrastructureModule.cs`, `RoutesInfrastructureModule.cs`, `ProofOfDeliveryInfrastructureModule.cs` |
| D13 | MEDIUM | Dépendances croisées entre services du domaine (non déployables séparément) | §1.9 |
| D14 | MEDIUM | `delivery-pricing-service` absent de `HBA.sln` | `grep -c "HBA.Delivery.Pricing" HBA.sln` → 0 |
| D15 | MEDIUM | ETA et progression codés en dur (540 s, 35 %, 5 100 m) | `TrackingStore.cs:86-87`, `:110` |
| D16 | MEDIUM | `IRouteProvider` déclaré, jamais implémenté ni enregistré | `route-service/.../IRouteProvider.cs` |
| D17 | LOW | Variable de groupe morte dans les endpoints delivery | `DeliveryEndpoints.cs:14` |
| D18 | LOW | Jeton de flux de suivi fabriqué et jamais vérifié | `TrackingEndpoints.cs:29` |
