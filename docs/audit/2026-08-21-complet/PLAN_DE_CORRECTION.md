# PLAN DE CORRECTION — HBAExpress

*Plan d'exécution couvrant les **162 anomalies** de l'audit du 21/08/2026.*
*Aucune correction n'a été appliquée. Ce document dit quoi faire, dans quel ordre, et pourquoi cet ordre-là.*

`PRIORITY_FIX_PLAN.md` donne la hiérarchie P0→P3. **Ce document-ci est la version exécutable** : découpage en lots livrables, dépendances entre lots, définition de « terminé » pour chacun, et couverture explicite des 162 anomalies — y compris les 87 MEDIUM et LOW, qui ne sont pas des restes mais du travail nommé.

---

## Ce qui gouverne l'ordre

Quatre règles, dont trois sont des contraintes dures.

**1. L'inbox avant les topics.** *(contrainte dure)*
La majorité des sagas rompues ont une cause unique : les producteurs et les consommateurs Kafka ne calculent pas le même nom de topic. Corriger ce point débloque tout — et c'est exactement pour cela qu'il ne faut pas le corriger en premier. Au moment où les messages recirculeront, ils arriveront sur **90 handlers non idempotents sur 96**. Kafka livre *au moins une fois* : un rejeu recréditera un vendeur, réservera deux fois du stock, enverra trois e-mails de réinitialisation de mot de passe. Le défaut d'idempotence est aujourd'hui masqué par celui des topics. Il se révélera à l'instant précis où on lèvera le masque.

**2. Les fuites qui n'attendent pas.** *(contrainte dure)*
ISSUE-071 — les jetons de réinitialisation de mot de passe circulent **en clair** dans Kafka et dorment dans `identity.outbox_messages`, table jamais purgée. Ce défaut ne demande aucune faille applicative pour être exploité : un dump, une sauvegarde, un compte de lecture analytique suffisent. Il passe devant tout, y compris devant ce qui empêche la plateforme de démarrer.

**3. L'unicité en base avant l'idempotence applicative.** *(contrainte dure)*
Une relecture applicative traite le cas courant — un second appel retrouve le premier résultat. Elle ne voit pas deux requêtes **simultanées** : les deux lisent « rien encore » avant que l'une ait écrit. Seule une contrainte en base ferme la course, et elle la ferme du bon côté : la seconde insertion échoue au lieu d'encaisser deux fois. Le dépôt applique déjà ce raisonnement sur `orders.CartId` ; il faut l'étendre.

**4. Les tests avec les lots, pas après.** *(règle de méthode)*
Il n'y a aujourd'hui **aucun test** sur le paiement, le stock, l'idempotence, la concurrence ou les transitions d'état. Un lot « tests » en fin de plan ne serait jamais fait. Chaque lot ci-dessous porte ses tests dans sa définition de terminé.

---

## Trois décisions à prendre avant de coder

Ces trois points bloquent des pans entiers et **ne sont pas techniques**. Tant qu'ils ne sont pas tranchés, les lots qui en dépendent restent en attente.

### D-1 · Qui supporte une remise financée par la plateforme ?

promotion-service n'a **aucune notion de financeur** : `Promotion` porte un périmètre, un type, une valeur, un budget — rien d'autre. Le reste de la plateforme suppose pourtant la distinction : `CartContracts.cs:33` porte `SellerDiscount` **et** `PlatformDiscount`, et wallet calcule le gain du vendeur sur `UnitBasePrice - SellerDiscount`. Mais le seul producteur écrit `SellerDiscount: 0m` en dur.

Brancher promotion-service tel quel fait **supporter aux vendeurs les coupons de la plateforme**, silencieusement, via le calcul des gains.

- **(a)** Le vendeur ne supporte que ses propres remises → ajouter le financeur au modèle `Promotion`, migration, propagation jusqu'au calcul des gains. *Lot 4.1 en version longue.*
- **(b)** Le vendeur supporte tout → statu quo technique, mais à écrire dans le contrat vendeur. *Lot 4.1 en version courte.*

Sans décision : **aucune promotion n'est possible sur la plateforme.**

### D-2 · Faut-il construire l'agrégat `SellerOrder` ?

Il n'existe pas ; `OrderingModuleApi.cs:66` renvoie `SellerOrderId: null` en dur. Sans lui : les cinq permissions `ORDER_*` ne gardent aucune route, le rôle `ORDER_MANAGER` ne peut que lire, le vendeur ne peut ni confirmer ni préparer ni remettre au livreur, et une commande à deux vendeurs n'a pas d'état par vendeur.

C'est le **seul point de l'audit qui demande de construire un agrégat**, pas de corriger du code : états, transitions, permissions, événements, migration, découpage à la création de commande. Compter un lot entier.

- **(a)** Le construire maintenant → débloque tout le parcours vendeur et la remise au livreur.
- **(b)** Le reporter → assumer que le vendeur n'a qu'une lecture, et **retirer** les cinq permissions qui promettent le contraire. Un rôle qui promet une autorité qu'il n'exerce pas est pire que son absence.

### D-3 · Que faire des onze squelettes ?

Quatre côté food (menu, availability, kitchen-prep, review), cinq côté delivery (dispatch, driver, route, tracking, proof), plus les deux BFF vides. Ils portent un README qui ne dit nulle part qu'ils sont provisoires — un lecteur conclut qu'ils sont finis.

- **(a)** Les finir → chiffrer chacun ; c'est le gros du reste à faire.
- **(b)** Les retirer du dépôt et replier leurs fonctions dans les services réels.
- **(c)** Les garder en marquant clairement leur état — **minimum absolu**, à faire dans tous les cas au lot 0.5.

Le domaine delivery ne fonctionne dans aucune hypothèse tant que driver-service et le cache de positions ne sont pas réels : c'est le lot 5.2, incontournable.

---

## Les vagues

Neuf vagues, 34 lots. Les durées sont des ordres de grandeur pour un développeur connaissant le dépôt — **et elles sont incertaines : aucun compilateur .NET n'était disponible pendant l'audit**, donc rien n'a été vérifié à l'exécution. Traiter ces chiffres comme des repères de séquencement, pas comme un engagement.

---

### VAGUE 0 — Arrêter l'hémorragie et rendre le système démarrable
**~2 jours. Aucune dépendance. À faire avant tout le reste.**

| Lot | Objet | Anomalies | Détail |
|---|---|---|---|
| **0.1** | **Jetons en clair dans Kafka et l'outbox** | ISSUE-071 | Retirer `ResetToken` et `VerificationToken` des charges ; publier un identifiant de demande, le service de notification récupère le jeton par appel authentifié. **Purger `identity.outbox_messages`** — la table n'a jamais été purgée. Traiter aussi `SellerMemberInvitedIntegrationEvent.InvitationToken` (HIGH). |
| **0.2** | **Faire démarrer la passerelle** | ISSUE-034 | `Services:FoodCart` et `Services:FoodOrder` dans `appsettings.json`, `docker-compose.dev.yml`, configmap k8s. `scripts/check-service-addresses.py` couvre déjà ce contrôle. **Rien n'est testable tant que ce lot n'est pas passé.** |
| **0.3** | **Les bouchons doivent refuser de démarrer** | ISSUE-010, 054, 055 | `SimulatedPayoutGateway`, `InMemoryObjectStorage`, `NullPushSender` sont enregistrés en production. Appliquer la règle déjà en vigueur dans le dépôt : `AddXGrpcClient` **lève à la construction de l'hôte** quand l'adresse manque. Un bouchon en production empêche le démarrage, il ne se contente pas d'un avertissement. |
| **0.4** | **Migrations manquantes et inertes** | ISSUE-065, 066, 067 | Migration initiale pour `return_refund` (le service appelle `Migrate()` sur un schéma inexistant) et pour `delivery_pricing` ; ajouter `[Migration]`/`[DbContext]` aux deux migrations inertes. Inscrire delivery-pricing-service dans `HBA.sln` — il n'y est pas. |
| **0.5** | **Marquer les squelettes** | D-3(c) | Un en-tête explicite dans chaque README des onze squelettes. Coût : une heure. Évite qu'un lecteur — ou un futur audit — les compte comme faits. |

**Terminé quand :** la passerelle démarre ; aucun bouchon ne démarre en production ; aucun jeton en clair dans une charge sérialisée ; `scripts/check-migrations.py` passe.

---

### VAGUE 1 — Fermer les accès
**~4 jours. Dépend de : 0.2.**

| Lot | Objet | Anomalies |
|---|---|---|
| **1.1** | **Médias** — URL signées sans contrôle de propriété (CNI, RCCM, pièces KYB accessibles à tout inscrit) ; `DELETE /media/{id}` sans authentification. Respecter `Visibility`, plafonner `expiresIn` côté serveur, suppression logique sur les pièces probantes | ISSUE-020, 021 |
| **1.2** | **return-refund** — gardes d'appartenance sur les cinq routes vendeur ; `sellerId` depuis le jeton et non la query string ; identité obligatoire côté client | ISSUE-017, 018, 019 |
| **1.3** | **Les dix services sans authentification** — `AddHbaService` dans les dix `Program.cs`, politique admin explicite sur les routes d'administration (dont la tarification de livraison, aujourd'hui modifiable sans jeton) ; food-review-service : identité issue du jeton | ISSUE-016, 023 |
| **1.4** | **Révocation effective** — appeler `ValidateAccessTokenAsync` dans le socle (le `security_stamp` n'est vérifié nulle part : une suspension met 15 min à mordre) ; limiteur de débit derrière `UseForwardedHeaders`, retirer `TrustAnyProxy` | ISSUE-022, 037 |
| **1.5** | **Le statut du vendeur entre dans l'autorisation** — un vendeur suspendu et son équipe continuent aujourd'hui de publier, d'ajuster le stock et de demander des retraits ; consommer `SellerSuspended` côté catalog/inventory ; ajouter `SellerRole` à l'éviction de cache ; fermeture de boutique qui arrête réellement la vente | ISSUE-024, 025, 036, 041 |
| **1.6** | **Gardes de propriété résiduelles** — deux routes de paiement ; fuite inter-vendeur du carnet de commandes (lignes des autres vendeurs + GPS et téléphone de l'acheteur) | ISSUE-035, 038, 039 |

**Terminé quand :** un test d'autorisation par route d'écriture ; vendeur A sur ressource de B → 403 pour chacun des services concernés ; suspension → refus au prochain appel.

---

### VAGUE 2 — Le bus d'événements
**~6 jours. Dépend de : rien. L'ordre interne est une contrainte dure.**

| Lot | Objet | Anomalies |
|---|---|---|
| **2.1** | **Inbox généralisée** — brancher `IConsumerInbox` sur **tous** les handlers à effet de bord. Le dispositif existe (`EfConsumerInbox`) et sert déjà dans 6 handlers : c'est du câblage, pas de la conception. Cinq services ont créé la table `consumer_inbox` sans jamais résoudre l'interface. Traiter en priorité les 46 handlers de notification-service (in-app + push + e-mail en double à chaque reprise) et les envois de jetons par e-mail (jusqu'à 3 e-mails de réinitialisation) | ISSUE-008 |
| **2.2** | **Unifier le nommage des topics** — une seule fonction de dérivation, appelée des deux côtés. Au passage : `[HbaEvent]`, `HbaEventNaming` et `HbaEventEnvelope` sont du **code mort** jamais référencé, l'`eventType` réellement émis vient du nom de classe .NET ; trois événements sont déclarés deux fois et résolus par ordre alphabétique ; les 14 topics provisionnés dans `k8s/overlays/*/kafka-topics.yaml` ne correspondent pas à ce qui est émis | ISSUE-001 |
| **2.3** | **Vérifier les cinq chaînes rompues** — paiement capturé → commande payée ; paiement échoué → stock libéré ; commande food confirmée → ticket cuisine ; repas prêt → course créée ; repas livré/annulé → séquestre libéré | ISSUE-002, 003, 004, 005, 006 |
| **2.4** | **Versionnement** — `IntegrationEvent` ne porte aucune version, l'enveloppe transporte `EventVersion` **codé en dur à 1**, et le consumer ne le lit jamais. Décider la convention avant que les contrats ne se figent | KAFKA §11 |
| **2.5** | **Corrélation** — `x-correlation-id` est perdu sur tout le flux événementiel (`traceparent`, lui, est bien propagé). Sans lui, un incident traversant trois services n'est pas reconstituable | GRPC §11 |

**Terminé quand :** un test de contrat vérifie, pour chaque `IntegrationEvent`, que le topic calculé côté producteur est égal à celui calculé côté consommateur ; chaque handler à effet de bord a son test « double livraison du même `eventId` → un seul effet » ; les cinq chaînes ont leur E2E.

---

### VAGUE 3 — L'argent et le stock
**~7 jours. Dépend de : 2.1 (l'idempotence de consommation).**

| Lot | Objet | Anomalies |
|---|---|---|
| **3.1** | **Unicité en base sur les objets financiers** — `payments.ProviderReference` (deux paiements, le webhook en encaisse un au hasard, l'autre reste `Pending` pour toujours) ; `customer_refunds` sans aucune clé d'idempotence (**double versement Mobile Money**) ; `payment_refunds.ExternalRefundId` ; `refunds.IdempotencyKey` (la colonne existe, l'index unique non). Migrations avec garde-fou anti-doublon, sur le modèle de `20260823000100_UnicitePanierParCommande` | ISSUE-072, 073, + DATABASE §5 |
| **3.2** | **Les remboursements aboutissent** — `ExecuteRefundCommand` n'a aucun émetteur (aucun remboursement n'est jamais versé) ; `Success:false` codé en dur chez quatre fournisseurs, et le handler **lève**, donc rejeu infini ; webhook partiel enregistré comme total ; `from == to` autorisé et `TotalRefunded()` qui ignore les `Pending` ; quantités déjà retournées codées à 0 ; plafond autoréférentiel | ISSUE-009, 011, 012, 013, 014, 049 |
| **3.3** | **Compensations financières** — virement de lot refusé jamais compensé (`MarkPayoutFailed` sans appelant : vendeur débité, jamais payé) ; gain non repris sur vente annulée (`SellerEarning.Reverse` sans appelant) ; invariant comptable §10.13 écrit, testé, jamais appelé en production | ISSUE-015, 050, 051 |
| **3.4** | **Appel externe avant persistance** — `InitiateCustomerRefundCommandHandler` appelle le PSP **avant tout `SaveChangesAsync`** : un incident laisse l'argent parti et aucune ligne, réconciliation aveugle. Écrire `Processing` d'abord, comme le fait déjà correctement `RefundPaymentCommandHandler`. Même motif dans `OrderLifecycleCommands.cs:223` et `ReturnLifecycleCommands.cs:154` | ISSUE-074, 032 |
| **3.5** | **Stock** — `ReserveStock` non idempotent alors que `order_id` est déjà dans le contrat proto (un dépassement d'échéance de 5 s suivi d'un rejeu réserve deux fois) ; réservations expirées jamais libérées (`ExpiresAtUtc` écrite, jamais lue, aucun balayeur) ; compensation manquante si la persistance échoue après la boucle ; `StockReservation` sans statut ; SKU sans ligne de stock réputé disponible sans limite ; unicité `(InventoryItemId, OrderId)` | ISSUE-075, 031, 032, 045, 046 |

**Terminé quand :** un test de concurrence par objet financier (deux exécutions simultanées → un seul effet) ; un test d'injection d'échec après appel PSP ; réservation expirée libérée, réservation confirmée intouchée.

---

### VAGUE 4 — Les décisions structurantes
**Démarrable dès que D-1 et D-2 sont tranchées. Indépendante des vagues 2 et 3.**

| Lot | Objet | Anomalies |
|---|---|---|
| **4.1** | **Promotions** — selon D-1. Implémenter le fournisseur réel d'`IPricingModuleApi` (`NeutralPricingModuleApi` est aujourd'hui la seule implémentation : tout coupon refusé, toute remise à 0) ; brancher `promotion-service`, qui expose un contrat complet que personne n'appelle ; balayeur d'expiration du budget réservé | ISSUE-033, 052, 053 |
| **4.2** | **`SellerOrder`** — selon D-2. En version (a) : agrégat, états, transitions, migration, découpage à la création de commande, puis raccordement des cinq routes `ORDER_*` et de la remise au livreur. En version (b) : retirer les cinq permissions | ISSUE-026, 027 |

---

### VAGUE 5 — La livraison
**~10 jours. Dépend de : D-3 et de la vague 2.**

| Lot | Objet | Anomalies |
|---|---|---|
| **5.1** | **Concurrence sur la course** — deux livreurs peuvent accepter la même mission : `AssignAsync` écrase sans relire, `Delivery` n'a **aucun jeton de concurrence** (seul `ReturnRequest` en a un dans tout le dépôt) et aucun index unique sur `AssignedDriverId`. Ajouter `UsePostgresRowVersion()` + contrainte | ISSUE-028 |
| **5.2** | **driver-service réel** — inscription, documents, vérification. Aujourd'hui `DriverStore.cs:13` expose un `DefaultDriverId` codé en dur sur lequel opèrent les six routes `/api/v1/drivers/me*`. Alimenter `IDriverLocationCache` — `SetAsync` n'a aucun appelant, donc **aucune course n'est jamais proposée à personne**. Raccorder les commandes de progression de `delivery-service`, qui sont correctes et sans appelant | ISSUE-029, 030 |
| **5.3** | **Preuve et suivi** — OTP universel `"123456"`, `submit` rejouable sur une preuve déjà vérifiée ; `RequiredProof` renseigné par aucun producteur, donc toutes les courses naissent en `None` et sont livrables sans preuve ; suivi non réservé au livreur affecté (route anonyme, `driverId` dans le corps, jeton de flux fabriqué et jamais vérifié) | ISSUE-056, 057, 058 |
| **5.4** | **Découpler et fiabiliser** — références `.csproj` croisées entre les quatre services (non déployables séparément) ; l'agrégat `Driver` déclaré **trois fois**, dont deux mortes, vivant dans driver-service mais persisté par delivery-service ; cinq services publient sans processeur d'outbox | ISSUE-007, 069, 070 |

**Terminé quand :** deux acceptations simultanées → une seule réussit ; un E2E complet inscription → offre → acceptation → enlèvement → preuve → livré → livreur de nouveau disponible.

---

### VAGUE 6 — La chaîne food
**~4 jours. Dépend de : vague 2.**

| Lot | Objet | Anomalies |
|---|---|---|
| **6.1** | **Paiement d'une commande food** — aucun chemin n'existe : `InitiatePayment` lit la commande marketplace et fige `PaymentOrderType.Marketplace` ; `MealOrderPlaced` n'a que le panier comme consommateur | ISSUE-059 |
| **6.2** | **Panier food** — jamais clos, et l'idempotence par `CartId` fait qu'après une première tentative **le client ne peut plus jamais commander de repas** | ISSUE-060 |
| **6.3** | **Arbitrage** — `UnderReview` inatteignable, les deux routes admin échouent toujours | ISSUE-061 |
| **6.4** | **Sort des quatre squelettes food** — selon D-3. Et retirer le parcours restauration dupliqué dans order-service (`Kind = Food`), qui fait coexister deux chaînes concurrentes | D-3, duplication |

---

### VAGUE 7 — Vendeur, membre et administration
**~7 jours. Dépend de : 4.2 pour les routes de commande.**

| Lot | Objet | Anomalies |
|---|---|---|
| **7.1** | **Trace d'audit** — `KeepsAuditTrail` est vrai sur **3 contextes sur 23**, et l'un des trois n'a pas de table. Ne laissent aucune trace : rôles, suspensions, captures, remboursements, approbations de retrait, modération produits/avis/restaurants, annulations de course, tarification. Corriger aussi `AuditQueries.cs:29-33`, qui affirme le contraire au lecteur. Le mécanisme `RecordAuditTrail` est complet et bien conçu — il suffit de l'activer et d'enregistrer l'acteur | ISSUE-042, 043 |
| **7.2** | **Transfert de propriété** — `OWNERSHIP_TRANSFER` n'a aucune route : le rôle OWNER ne peut jamais changer de porteur, et `SELLER_CLOSE` étant `OwnerOnly`, le dossier devient inadministrable si le propriétaire disparaît | ISSUE-040 |
| **7.3** | **Stock vendeur** — aucun journal de mouvements, aucun transfert, alors que `INVENTORY_TRANSFER` et `STOCK_MOVEMENT_VIEW` sont attribuées et que le rôle `INVENTORY_MANAGER` promet « transferts ». Implémenter, ou retirer les permissions | ISSUE-044 |
| **7.4** | **Catalogue** — aucune offre ne passe `OutOfStock` ni ne revient en vente ; prix et statut « publié » jamais revalidés entre l'ajout au panier et le paiement | ISSUE-047, 048 |
| **7.5** | **Routes d'administration absentes** — validation des livreurs (aucune route), arbitrage des retours (schéma inexistant, traité au lot 0.4), et les 18 à 23 permissions qui ne gardent rien : les brancher ou les retirer | SECURITY §3, SAGA_ADMIN §9.3 |
| **7.6** | **Parcours client résiduels** — chemin OTP dont le code est généré puis jeté (`_ = code;`) ; route publique historique `/api/auth/*` en 404 ; aucune route de la passerelle vers return-refund ; Client BFF non branché (11 routes sur 13 en 501) | ISSUE-062, 063, 064 |

---

### VAGUE 8 — Robustesse et performance
**~8 jours. Indépendante — peut être menée en parallèle à partir de la vague 3.**

| Lot | Objet | Volume |
|---|---|---|
| **8.1** | **Index manquants** — `reviews.SellerId`, `deliveries.QuoteId`, `withdrawals.Status`, `settlement_batches`, `conversations`, `commission_rules`, `stock_reservations.ExpiresAtUtc`, `return_*.ReturnId`. Et retirer `IX_outbox_messages_ProcessedOnUtc`, index mort créé par 15 migrations initiales, absent du modèle, qui coûte une écriture à chaque message | 10 anomalies · DATABASE §4 |
| **8.2** | **Contraintes d'unicité restantes** — paniers actifs en double (marketplace et food), devis consommé par deux courses, coupon « une fois par compte » contournable par deux paniers simultanés, jetons de rafraîchissement au même hash | 8 anomalies · DATABASE §5 |
| **8.3** | **Jetons de concurrence manquants** — `withdrawals`, `meal_orders`, `drivers`, `carts`, `invoices`, `promotions.BudgetConsumed` (deux réservations simultanées dépassent le budget) | 8 anomalies · DATABASE §6 |
| **8.4** | **Requêtes non paginées** — 94 `ToListAsync()` sans borne. Les plus dangereuses d'abord : `ListLowStockAsync` remonte **toute** la table de stock avec toutes ses réservations ; les moyennes de notes chargent toutes les notes d'un vendeur au lieu d'un `AVG()` SQL ; l'historique acheteur et vendeur n'a aucune borne | 13 anomalies · DATABASE §12 |
| **8.5** | **N+1** — jusqu'à 99 requêtes pour trouver un slug libre à chaque création de produit ; un aller-retour par vendeur dans le lot de reversement (5 000 vendeurs = 5 000 requêtes sous un verrou unique) | 5 anomalies · DATABASE §11 |
| **8.6** | **Cascade sur données financières** — `payments → payment_refunds` et `return_requests → refunds → refund_attempts` : un `DELETE` mal ciblé efface aujourd'hui la preuve qu'un client a été remboursé. `Restrict` + suppression logique | 6 anomalies · DATABASE §8 |
| **8.7** | **Nullable et horodatage** — contraintes `CHECK` liant un statut à ses colonnes obligatoires ; 73 tables sur 131 sans aucun `Created*Utc`, et 41 avec un `Created*Utc` sans jamais d'`Updated*Utc` — dont `orders`, `payments`, `deliveries`, `withdrawals`, précisément les agrégats dont on veut savoir *quand la ligne a été touchée pour la dernière fois* en incident | 8 anomalies · DATABASE §9 |
| **8.8** | **gRPC** — aucun disjoncteur sur aucun client ; **aucun** code de statut métier utilisé (`NotFound`, `FailedPrecondition`, `AlreadyExists`, `PermissionDenied`), donc un refus métier et une panne sont indiscernables et l'appelant ne peut pas décider s'il doit réessayer ; `Unavailable` pour une erreur de configuration ; `NotFound` pour un refus d'authentification ; 13 clients HTTP dans la passerelle doublonnant des contrats gRPC ; `new HttpClient()` sans durée de vie | 21 anomalies · GRPC §8-§12 |
| **8.9** | **Deux représentations de l'argent** — `numeric(18,2)` partout, sauf `promotions` et `delivery_pricing` en entier signé (choix assumé et commenté : le franc CFA n'a pas de sous-unité). Cohérent en soi, mais la cohabitation est un risque de conversion : **à documenter, pas à corriger à l'aveugle** | 1 · DATABASE §7 |

---

### VAGUE 9 — Hygiène
**~3 jours. À tout moment.**

| Lot | Objet |
|---|---|
| **9.1** | Supprimer les 13 copies de `.proto` non compilées (dont 4 du même `FoodApi`) — corriger l'une d'elles compile, passe la revue, et n'a aucun effet. Retirer ou brancher les 45 RPC morts. |
| **9.2** | Les **83 valeurs d'énumération de statut jamais assignées** : les atteindre ou les retirer. Une valeur inatteignable promet un état que le système n'atteint jamais. |
| **9.3** | Documenter l'écart assumé : billing, wallet, recommendation et wishlist sont des **modules hébergés**, pas des services autonomes. Le choix est défendable ; ne pas l'écrire ne l'est pas. |
| **9.4** | Corriger `scripts/check-migrations.py` : il ne vérifie que les tables, jamais les colonnes ; il ne voit pas les migrations dépourvues d'attributs ; `check_sql_identifier_case` est **totalement mort** — il ne cherche que dans des chaînes `"""…"""`, et aucune migration du dépôt n'en utilise. Les deux défauts les plus dangereux du dépôt lui échappent tous les deux. |
| **9.5** | Restes de nommage `HBA.Deliveries.*` après renommage ; `shared/kafka-schemas/` vide ; observabilité (métriques métier, sondes de disponibilité). |

---

## Couverture des 162 anomalies

| Famille | Nombre | Traitée par |
|---|---:|---|
| Bus d'événements (topics, inbox, outbox, versionnement, corrélation) | 22 | Vague 2, lot 5.4 |
| Argent (paiement, remboursement, portefeuille, versement) | 24 | Vague 3, lot 0.3 |
| Autorisation, IDOR, fuites inter-vendeur, secrets | 27 | Vagues 0.1 et 1 |
| Stock et réservations | 9 | Lot 3.5 |
| Livraison et livreur | 18 | Vague 5 |
| Chaîne food | 11 | Vague 6 |
| Parcours vendeur, membre, administration | 19 | Vagues 4.2 et 7 |
| Base de données (index, unicité, concurrence, cascade, nullable, N+1, pagination) | 59 | Lot 0.4, vague 8 |
| gRPC (statuts, disjoncteur, protos, RPC morts) | 21 | Lots 8.8, 9.1 |
| Machines d'état (83 valeurs inatteignables) | 1 famille | Lot 9.2 |
| Tests | 1 famille | **Dans chaque lot**, jamais isolé |
| Documentation, nommage, code mort | 24 | Vague 9 |

*Certaines anomalies apparaissent dans deux familles (une fuite de secret est aussi un défaut d'événement) : la somme des lignes dépasse 162, la couverture ne laisse rien de côté.*

---

## Chemin critique

```
0.1 jetons en clair ─┐
0.2 passerelle ──────┼─→ VAGUE 1 (accès) ──────────────────┐
0.3 bouchons ────────┤                                     │
0.4 migrations ──────┘                                     ├─→ VAGUE 7 (vendeur/admin)
                                                           │
2.1 inbox ─→ 2.2 topics ─→ 2.3 chaînes ─→ VAGUE 3 (argent) ┤
                                        └→ VAGUE 6 (food) ─┘
D-2 ─→ 4.2 SellerOrder ─────────────────────────────────────→ 7.x routes commande
D-1 ─→ 4.1 promotions
D-3 ─→ 5.2 driver-service ─→ 5.1, 5.3, 5.4

VAGUE 8 et VAGUE 9 : en parallèle, à partir de la vague 3.
```

**Durée cumulée du chemin critique : de l'ordre de 6 à 8 semaines** pour un développeur, hors décisions D-1/D-2/D-3 et hors finition des onze squelettes. Ce chiffre est un repère, pas un engagement : rien n'a été compilé ni exécuté pendant l'audit.

---

## Méthode, pour chaque lot

- **Une anomalie à la fois**, ou un lot cohérent restreint. Pas de refactorisation massive non nécessaire.
- **Un test qui échoue avant, qui passe après.** C'est ce qui distingue une correction d'une retouche.
- **Compatibilité des contrats publics préservée** : routes `/api/v1/merchants/...`, assemblies `HBA.Merchants.*`, vocabulaire `Seller`/`Store`.
- **Migrations écrites à la main**, avec `[DbContext]` + `[Migration]`, sans `.Designer.cs`, snapshot édité — convention du dépôt. Toute migration qui ajoute une contrainte d'unicité porte un garde-fou anti-doublon lisible, sur le modèle de `20260823000100_UnicitePanierParCommande`.
- **Commentaires et prose en français**, dans le style « POURQUOI CECI EXISTE » du dépôt.
- **Rejouer les scripts de contrôle** après chaque lot : `check-di.py`, `check-config-and-guards.py`, `check-migrations.py`, `check-grpc-stubs.py`, `check-infra.py`, `check-usings.py`, `check-k8s.py`, `check-event-consumers.py`, `check-service-addresses.py`. En gardant à l'esprit qu'ils ne voient pas tout — lot 9.4.

---

## Une remarque pour finir

Sur les 162 anomalies, une part importante n'est pas du code manquant mais **du code écrit qui n'est pas atteint** : 96 handlers corrects que rien n'appelle, un mécanisme d'audit complet activé sur 3 contextes, une inbox fonctionnelle utilisée par 6 handlers, un invariant comptable écrit et testé mais jamais appelé, `MarkPayoutFailed` et `SellerEarning.Reverse` sans appelant, 45 RPC servis sans client.

Ce n'est pas la même chose que du travail à faire, et c'est plutôt une bonne nouvelle : le raisonnement métier a été mené, souvent avec soin. Ce qui manque, ce sont les raccordements — et les raccordements se corrigent vite. C'est ce qui rend le plan ci-dessus réaliste malgré le nombre.
