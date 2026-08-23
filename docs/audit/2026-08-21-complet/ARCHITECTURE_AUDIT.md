# ARCHITECTURE_AUDIT — HBAExpress

*Audit statique, en lecture seule, de l'arbre de travail au 21/08/2026. Aucun fichier métier modifié.*
*2 700 fichiers source analysés. Aucun compilateur .NET disponible : tout constat est adossé à une lecture de code, jamais à une exécution.*

---

## 1. En une page

L'ossature est bonne. Les couches sont respectées, le Domain est pur, les montants sont en `decimal`, l'outbox est en place, les échéances gRPC sont globales. Ce que l'audit établit, c'est que **cette ossature n'est pas raccordée** : les services parlent dans le vide.

Trois faits résument l'état du système.

1. **136 événements d'intégration sont déclarés. 20 arrivent réellement à un consommateur.** Ce n'est pas un handler oublié — les 96 handlers sont tous enregistrés en DI. C'est que les producteurs publient sur des topics auxquels personne ne s'abonne (§4.2).
2. **116 RPC gRPC sont déclarés. 31 sont implémentés *et* appelés.** 40 n'ont aucun corps de serveur et rendraient `UNIMPLEMENTED`, 45 sont servis sans aucun client.
3. **Le parcours d'achat s'arrête au paiement.** L'acheteur est débité, `PaymentCaptured` est publié, personne ne l'entend : la commande reste `AwaitingPayment`, le stock reste réservé, le vendeur n'est jamais crédité, aucune course n'est créée. C'est vrai pour la marketplace **et** pour la restauration.

La conséquence pratique : **aucun parcours métier de bout en bout n'est fonctionnel aujourd'hui**, quel que soit l'acteur.

---

## 2. Architecture attendue vs architecture réelle

### 2.1 Services attendus, services présents

| Domaine | Service attendu | Présent | État réel |
|---|---|---|---|
| Commun | Identity | ✅ | **COMPLET** — 158 `.cs`, rotation de refresh token avec détection de rejeu, step-up `auth_time`/`amr` |
| Commun | User | ✅ | **COMPLET** |
| Commun | Payment | ✅ | **PARTIEL** — passerelles réelles en lecture, remboursement en dur `Success:false` sur 4 fournisseurs |
| Commun | Wallet | ✅ | **PARTIEL** — hébergé dans `HBA.Financial.Api`, pas de processus autonome |
| Commun | Notification | ✅ | **PARTIEL** — `NullPushSender` enregistré en production |
| Commun | Promotion | ✅ | **PARTIEL** — aucun client ne l'appelle (§4.3) |
| Commun | Media | ✅ | **PARTIEL** — `InMemoryObjectStorage` en production |
| Commun | **File** | ❌ | **ABSENT** — aucun dossier, aucun projet. Media assure une partie du rôle |
| Marketplace | Catalog | ✅ | **COMPLET** — 197 `.cs`, le service le plus abouti avec seller |
| Marketplace | Seller | ✅ | **COMPLET** — 118 `.cs`, RBAC membre solide |
| Marketplace | Inventory | ✅ | **PARTIEL** — réservations jamais expirées |
| Marketplace | Cart | ✅ | **PARTIEL** — tarification bouchonnée, zéro test |
| Marketplace | Order | ✅ | **PARTIEL** — idempotent depuis peu, mais aval rompu |
| Marketplace | Review | ✅ | **PARTIEL** — hébergé dans `HBA.Engagement.Api` |
| Marketplace | Return & Refund | ✅ | **MAQUETTE** — 0 migration, 0 autorisation, 0 remboursement exécuté |
| Food | Restaurant | ✅ | **COMPLET** — 66 `.cs`, 17 278 lignes |
| Food | Menu | | **SQUELETTE** — 61 lignes, `ConcurrentDictionary` |
| Food | Availability | | **SQUELETTE** — 71 lignes |
| Food | Food Cart | ✅ | **PARTIEL** — réel, mais non déployé |
| Food | Food Order | ✅ | **PARTIEL** — réel, mais orphelin en aval |
| Food | Kitchen / Prep | | **SQUELETTE** — 82 lignes |
| Food | Review Food | | **SQUELETTE** — 98 lignes, **aucune authentification** |
| Delivery | Delivery | ✅ | **PARTIEL** — domaine riche, aucune surface livreur |
| Delivery | Delivery Pricing | ✅ | **PARTIEL** — persisté, mais **absent de `HBA.sln`** et sans authentification |
| Delivery | Driver | | **SQUELETTE** — un livreur codé en dur |
| Delivery | Dispatch | | **SQUELETTE** — candidats codés en dur |
| Delivery | Route | | **SQUELETTE** — Haversine à 5,8 m/s |
| Delivery | Tracking | | **SQUELETTE** — ETA en dur (540 s) |
| Delivery | Proof of Delivery | | **SQUELETTE** — OTP en dur `"123456"` |
| BFF | Client BFF | | **NON BRANCHÉ** — 11 routes sur 13 en `501`, aucune route YARP vers lui |
| BFF | Seller BFF | | **VIDE** — 1 fichier, 2 sondes de santé |
| BFF | Driver BFF | | **VIDE** — 1 fichier, 2 sondes de santé |
| BFF | Admin BFF | ❌ | **ABSENT** |
| — | API Gateway | ✅ | **PARTIEL** — YARP sain, mais route vers deux endpoints livreur inexistants |

**Compte : 31 services présents. 9 réellement complets ou quasi. 11 squelettes ou maquettes. 2 attendus et absents (File Service, Admin BFF).**

### 2.2 Services présents mais non documentés

- `services/marketplace/return-refund-service` — aucun README, aucune mention dans la documentation d'architecture, alors qu'il porte l'argent des remboursements.
- `services/delivery/delivery-pricing-service` — **absent de `HBA.sln`** : il ne se compile pas avec la solution, aucun développeur ne le voit dans son IDE.
- Les quatre squelettes food portent le commentaire « *Ce projet est volontairement vide : voir le README du service* » — et les README ne mentionnent aucun caractère provisoire. Un lecteur conclut que le service est fini.

### 2.3 Autonomie des services — écart assumé ou non ?

Quatre « services » n'ont pas d'hôte propre :

| Module | Hébergé dans | Conséquence |
|---|---|---|
| billing | `HBA.Financial.Api` | pas de déploiement, de montée en charge ni de panne indépendants |
| wallet | `HBA.Financial.Api` | idem |
| recommendation | `HBA.Engagement.Api` | idem |
| wishlist | `HBA.Engagement.Api` | idem |

C'est une **architecture modulaire hébergée**, pas des microservices autonomes. Ce n'est pas nécessairement un défaut — c'est souvent un bon choix — mais l'écart avec la cible déclarée doit être **décidé et écrit**, pas subi. Aujourd'hui rien ne le documente.

---

## 3. Ce qui est réellement sain — à ne pas re-auditer

L'audit doit aussi dire ce qui tient. Ces points ont été vérifiés dans le code, pas supposés :

- **Pureté du Domain** : aucune couche `Domain` ne référence EF Core, ASP.NET Core, Kafka ou gRPC. Vérifié sur les 23 projets `*.Domain` (`using` et `.csproj`).
- **Direction des dépendances** : Infrastructure → Application → Domain, sans inversion.
- **Types monétaires** : **aucun** `double`/`float` sur un montant dans tout le dépôt. Tout est `decimal(18,2)`.
- **Échéances gRPC** : échéance globale de 5 s appliquée par le socle ; **0 appel sans échéance**, **0 appel sans `CancellationToken`**.
- **Séparation CQRS** : `ICommand`/`IQuery` respectés, handlers `internal sealed`.
- **Aucun accès SQL croisé** : aucun service ne lit la base d'un autre ; aucun `DbContext` partagé.
- **Identity** : rotation du refresh token avec détection de rejeu, step-up `auth_time`/`amr`, épinglage `ValidAlgorithms`, `FallbackPolicy` du socle.
- **Validation d'upload** : contrôle par *magic bytes*, pas seulement par extension déclarée.
- **Interceptor gRPC interne** : clé partagée comparée en temps constant.
- **RBAC membre vendeur** : élévation de privilège fermée, dernier propriétaire protégé sur les quatre chemins, suspension d'un membre immédiate.
- **Journaux** : aucun OTP ni jeton trouvé dans les journaux applicatifs.

---

## 4. Les écarts structurants

### 4.1 Le bus d'événements ne relie presque rien

C'est **le défaut dominant du système**, et il est unique dans sa cause.

Les producteurs dérivent le nom du topic de leur propre identité (`KafkaEventNaming.cs:38`), tandis que les consommateurs s'abonnent à une liste constante (`KafkaEventBusOptions.cs:21`, `DependencyInjection.cs:79-90`). Les deux ne coïncident que par hasard.

| Mesure | Valeur |
|---|---|
| Événements déclarés | 136 |
| Handlers écrits | 96 |
| Handlers **non enregistrés** en DI | **0** — l'appariement est parfait |
| Événements qui atteignent réellement un consommateur | **20** |
| Producteur ET consommateur existent, mais **le topic n'est écouté par personne** | **37** |
| Événements publiés que personne ne consomme | 51 |
| Événements publiés dans une file mémoire jamais drainée | 16 |
| Services publiant **sans processeur d'outbox** (perte totale) | 5 — dispatch, driver, route, tracking, proof |

Il faut mesurer ce que cela signifie : **le travail a été fait**. Les handlers existent, ils sont corrects, ils sont câblés. Ils ne reçoivent rien parce qu'une convention de nommage diverge entre les deux extrémités. C'est un défaut à correction unique et à effet massif — voir P0-1 du plan.

### 4.2 L'idempotence de consommation est presque absente

`EfConsumerInbox` existe et fonctionne. **6 handlers sur 96 l'utilisent.** Sept services ont une table `consumer_inbox` ; cinq l'ont créée sans jamais résoudre `IConsumerInbox`.

Kafka garantit une livraison *au moins une fois*. Aujourd'hui, un rejeu de partition recrédite un vendeur, réserve une seconde fois du stock, renvoie une notification. Ce défaut est masqué par le précédent — les messages n'arrivent pas — et **se révélera au moment exact où on corrigera les topics**. L'ordre de correction n'est donc pas négociable : inbox d'abord, ou en même temps.

### 4.3 Les contrats gRPC : beaucoup de déclaratif, peu de raccordé

| Mesure | Valeur |
|---|---|
| RPC déclarés | 116 |
| RPC avec un corps de serveur | 76 |
| RPC **sans corps** (→ `UNIMPLEMENTED`) | **40** |
| RPC **morts** (servis, aucun client) | **45** |
| RPC implémentés **et** appelés | **31** |
| RPC appelés **mais non implémentés** (cassés à l'exécution) | **2** |
| Clients avec disjoncteur | **0** |
| Codes de statut métier utilisés (`NotFound`, `FailedPrecondition`, `AlreadyExists`, `PermissionDenied`) | **0** |

Deux conséquences concrètes : `promotion-service` expose un contrat complet que **personne n'appelle** — d'où l'absence totale de promotions actives ; et tout échec distant remonte en `Internal`/`Unknown`, ce qui interdit à l'appelant de distinguer « ressource absente » de « service indisponible », donc de décider s'il doit réessayer.

**13 copies de `.proto`** existent sous `services/*/*/proto/`. Elles sont aujourd'hui identiques octet pour octet à leurs originaux de `shared/proto/`, mais **aucune n'est compilée** : corriger l'une d'elles compile, passe la revue, et n'a aucun effet.

### 4.4 L'agrégat `SellerOrder` n'existe pas

L'architecture cible, les permissions déclarées (`ORDER_CONFIRM`, `ORDER_REJECT`, `ORDER_MARK_PREPARING`, `ORDER_MARK_READY`, `ORDER_CANCEL`) et le parcours vendeur le supposent tous. Il n'y a **aucune classe `SellerOrder`** dans le dépôt ; `OrderingModuleApi.cs:66` renvoie `SellerOrderId: null` en dur.

Conséquences en chaîne : le vendeur n'a qu'une **lecture** de la commande globale ; les cinq permissions ci-dessus ne gardent aucune route ; le rôle `ORDER_MANAGER` ne peut rien faire ; et une commande multi-vendeurs n'a pas d'état par vendeur, donc « confirmée » n'a pas de sens à l'échelle où le vendeur agit. C'est le seul défaut de l'audit qui exige de **construire un agrégat**, pas de corriger du code.

### 4.5 La trace d'audit est éteinte presque partout

`KeepsAuditTrail` est vrai dans **3 contextes sur 23** (`SellersDbContext`, `MealOrderingDbContext`, `ReturnRefundDbContext` — ce dernier n'ayant aucune migration, sa table n'existe pas : il en reste **2 réels**).

Ne laissent donc aucune trace : les changements de rôle, les suspensions de compte, les captures et remboursements, les approbations de retrait, la modération de produits, d'avis et de restaurants, les annulations de course, la tarification. Et `AuditQueries.cs:29-33` affirme le contraire au lecteur.

### 4.6 Migrations : deux services ne peuvent pas démarrer

`DeliveryPricingDbContext` et `ReturnRefundDbContext` ont un modèle EF complet et **zéro migration**. `return-refund-service/Program.cs:21` appelle pourtant `Migrate()` : le schéma n'est jamais créé, le service est inopérant dès le premier accès.

Deux autres migrations sont **inertes** : `20260824000000_AddOrderPaymentId.cs` et `20260824010000_AddPaymentRefunds.cs` n'ont pas les attributs `[Migration]`/`[DbContext]`, donc EF ne les charge pas. La colonne `ordering.orders."PaymentId"` est dans le modèle et n'existe dans aucune base.

### 4.7 Tests : les parcours critiques ne sont pas couverts

- **Domaine delivery : zéro test.** Aucun projet, alors que `HBA.Delivery.Core.Application.csproj:22` déclare `InternalsVisibleTo("Delivery.UnitTests")` — projet inexistant.
- **cart-service : zéro test.**
- **food-cart-service, food-order-service : zéro test.**
- **order-service : 8 tests**, tous sur des codes HTTP. Ni le calcul du total, ni la réservation de stock, ni l'idempotence récemment ajoutée ne sont couverts.
- Aucun test de contrat gRPC, aucun test de contrat Kafka, aucun test de concurrence, aucun test d'idempotence, aucun E2E.
- Les services solides (catalog, seller, identity) sont, eux, correctement testés — l'écart de rigueur à l'intérieur du dépôt est considérable.

---

## 5. Réponses aux quatre questions posées

**1. Quels sont les défauts d'implémentation actuels ?**
157 anomalies retenues, dont 34 CRITICAL. Le détail est dans `IMPLEMENTATION_DEFECTS.md`. Elles se regroupent en cinq familles : le bus d'événements débranché, l'absence d'idempotence de consommation, les compensations manquantes (stock, escrow, gains), l'autorisation absente sur trois services entiers, et onze services qui sont des maquettes déployées.

**2. Quels sont les écarts entre l'architecture cible et le code présent ?**
Les couches, la pureté du Domain, CQRS, l'outbox et les types monétaires sont conformes. Les écarts portent sur : l'autonomie (4 modules sans hôte propre), la complétude (11 squelettes, File Service et Admin BFF absents), le raccordement (BFF non branchés, 45 RPC morts), l'idempotence (6/96), et l'audit (2 contextes sur 23).

**3. Les communications inter-services sont-elles cohérentes ?**
**Non.** En synchrone, 31 RPC sur 116 fonctionnent réellement, sans disjoncteur ni codes d'erreur métier. En asynchrone, 20 événements sur 136 atteignent un consommateur. Cinq services publient sans processeur d'outbox : leurs événements sont perdus à coup sûr.

**4. Les parcours métier sont-ils cohérents ?**

| Acteur | Statut | Là où ça s'arrête |
|---|---|---|
| **Client** — inscription | PARTIAL | le chemin OTP jette le code généré (`_ = code;`) |
| **Client** — achat marketplace | **BROKEN** | débité, commande figée en `AwaitingPayment` |
| **Client** — commande food | **BROKEN** | aucun chemin de paiement n'existe pour `MealOrder` |
| **Vendeur** — intégration | PARTIAL | suspendre un vendeur ne l'empêche pas de vendre |
| **Vendeur** — produit | PARTIAL | correct ; l'acteur n'est pas enregistré |
| **Vendeur** — stock | **BROKEN** | aucun journal de mouvements, aucun transfert, réservations jamais expirées |
| **Vendeur** — commandes | **BROKEN** | l'agrégat `SellerOrder` n'existe pas |
| **Vendeur** — retours | **BROKEN** | aucune autorisation : arbitrage du dossier d'un concurrent |
| **Vendeur** — finances | **COHERENT** | appartenance + capacité + step-up sur chaque route |
| **Membre vendeur** | PARTIAL | solide sur l'essentiel ; `ORDER_MANAGER` en lecture seule |
| **Livreur** | **BROKEN** | aucune inscription ; aucune course n'est jamais proposée |
| **Admin** | PARTIAL | les décisions s'écrivent en base et ne déclenchent rien en aval |

---

## 6. Dépendances circulaires

Aucune dépendance circulaire entre assemblies n'a été trouvée. En revanche, les quatre services du domaine delivery ont des références `.csproj` croisées (`driver-service` → `delivery-service`, etc.) : **ils ne sont pas déployables séparément**, ce qui contredit leur existence comme services distincts.

---

## 7. Duplications de code entre services

| Duplication | Où | Gravité |
|---|---|---|
| L'agrégat `Driver` est déclaré **trois fois** (delivery-service, driver-service, dispatch-service), dont deux mortes ; il vit dans `driver-service` mais est persisté par `delivery-service` | `services/delivery/*` | **HIGH** |
| Parcours restauration dupliqué entre `order-service` (héritage `Kind = Food`) et `food-order-service` | marketplace + food | **HIGH** |
| 13 copies de `.proto`, dont 4 du même `hba.food.v1.FoodApi` | `services/*/*/proto/` | MEDIUM |
| Restes de l'ancien nommage `HBA.Deliveries.*` après renommage en `HBA.Delivery.Core.*` | delivery-service | LOW |

---

*Rapports liés : `SERVICES_AUDIT.md`, `GRPC_MATRIX.md`, `KAFKA_EVENT_MATRIX.md`, `DATABASE_AUDIT.md`, `SECURITY_AUDIT.md`, `SAGA_CLIENT.md`, `SAGA_SELLER.md`, `SAGA_SELLER_MEMBER.md`, `SAGA_DRIVER.md`, `SAGA_ADMIN.md`, `STATE_MACHINE_AUDIT.md`, `IMPLEMENTATION_DEFECTS.md`, `PRIORITY_FIX_PLAN.md`.*
