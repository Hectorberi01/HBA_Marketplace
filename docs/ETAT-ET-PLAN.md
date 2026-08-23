# HBAExpress — état d'avancement et plan d'exécution

17 août 2026, 21 h · commit `4111db6` · mesures reprises sur le code réel, pas de mémoire

---

## 1. Ce qui est fait

| # | Livrable | Commit | Vérifié par |
|---|---|---|---|
| 1 | Réorganisation du dépôt selon le design système | `a726cb0` | 118/118 projets du `.sln` résolus, 0 référence cassée |
| 2 | Audit de conformité des 16 services | — | `docs/audit/anterieurs/AUDIT-CONFORMITE.md` |
| 3 | Socle transverse (§5, §18, §19) | `4f0c283` | **compilé** |
| 4 | Normalisation centrale des codes d'erreur | `8df7608` | **compilé** |
| 5 | Contexte propagé actif dans les 14 hôtes + pilote user-service | `4111db6` | **compilé** |

Les trois derniers lots ont été compilés (`make build`), y compris les 14 hôtes après
modification de `ServiceHostExtensions`. **Aucun test n'a été exécuté** et aucun
environnement n'a été démarré : la vérification s'arrête à la compilation.

### Ce que ces livrables ont réellement changé

**Les codes d'erreur : de 0/16 à 16/16 services, sans toucher un handler.**
`ApiResults` dérive `error.code` du type d'erreur métier. Les 290 points de sortie
passant par `.Match(` rendent donc désormais `VALIDATION_ERROR`,
`BUSINESS_RULE_VIOLATION`, `CONFLICT`, `DEPENDENCY_UNAVAILABLE` ou
`<SERVICE>_SERVICE_NOT_FOUND`, sous l'enveloppe du §5, avec `meta.requestId`.

> La métrique « présence littérale du code dans le service » de l'audit initial
> n'a plus de sens : les codes ne sont plus écrits dans les services, ils sont émis
> par le bord HTTP commun. Un `grep` en trouve désormais 2/16 — dont 2 faux positifs
> venant d'un commentaire. La bonne mesure est celle des chemins de sortie, ci-dessous.

**Le contexte propagé est actif partout.** `UseHbaService` enregistre
`UseHbaRequestContext` après `UseAuthorization`, avec le nom lu dans `SERVICE_NAME`
que `docker-compose.dev.yml` pose déjà. `meta.requestId`, `correlationId`, l'acteur
et le code service sont donc renseignés dans les 14 services.

---

## 2. Ce qui reste — mesuré

### 2.1 Adoption des briques du socle

| Brique | Adopté | Total | Reste |
|---|---|---|---|
| Groupes de routes sous `/api/v1/` | **1** | 32 | 31 groupes |
| Points de sortie en **succès** enveloppés (`ApiResults.*`) | **7** | ~302 | 295 appels `Results.Ok/NoContent/Created/Accepted` |
| Points de sortie en **erreur** enveloppés | **290** (via `.Match`) | 395 | **105 appels directs** |
| Événements portant `[HbaEvent]` | **0** | 90 | 90 |
| Consumers utilisant `IConsumerInbox` | **0** | ~32 | tous |
| Endpoints avec `Idempotency-Key` | **2** | ~40 attendus | 38 |

Le détail des 105 erreurs non enveloppées : `Results.Unauthorized()` **67**,
`Results.NotFound()` 24, `Results.BadRequest()` 5, `Results.Forbid()` 4,
`Results.Conflict()` 3, `Results.Problem()` 2.

> Les 67 `Results.Unauthorized()` rendent un corps **vide** : ni code à brancher,
> ni `requestId` à citer — sur l'erreur la plus fréquente en production, un jeton
> expiré. C'est le plus gros gisement de valeur pour l'effort le plus faible.

### 2.2 Conformité aux contrats du cahier des charges

Inchangée depuis l'audit initial : aucun travail de domaine n'a encore été fait.

| Dimension | Conforme | Total |
|---|---|---|
| Agrégats / tables | 22 | 60 |
| RPC gRPC | 13 | 53 |
| Événements publiés | 12 | 58 |
| Événements consommés | 11 | 32 |
| Endpoints REST | 40 (dont 1 exact) | 61 |

### 2.3 Trous fonctionnels, vérifiés un par un

| Absent | Service | Bloque |
|---|---|---|
| `Promotion`, `Coupon`, `PromotionRule`, `CouponUsage` | promotion-service (**inexistant**) | Les **deux** checkouts du §11 appellent `ReserveCoupon`/`CommitCoupon` |
| `FoodCart` | food-cart-service | Étape 2 du checkout Food (§11.2) |
| `LedgerEntry` | wallet-service | Partie double du §10.13 |
| `Reservation` | inventory-service | TTL de réservation du §11.1 |
| `Proof`, `DriverShift` | delivery-service | Preuve de livraison (§10.14) |
| `MfaChallenge` | identity-service | `POST /auth/verify-otp` |
| `NotificationTemplate` | notification-service | Templates versionnés |
| `OpeningHour`, `ServiceZone` | restaurant-service | `IsRestaurantOpen` sans source de vérité |

---

## 3. Décisions en attente — elles bloquent du travail

| # | Décision | Conséquence si non tranchée |
|---|---|---|
| D1 | **Vocabulaire** : `Seller`/`Store` ou `Merchant`/`Outlet` ? | Chaque service aligné fige un peu plus le choix par défaut. Touche 83 fichiers, les protos, les topics et les bases. |
| D2 | **Contradiction de la spec sur `consumer_inbox`** : §19.5 dit `event_id` en PK, §11.3 dit `unique(event_id, consumer_name)`. J'ai suivi le §11.3. | Si le §19.5 est le bon, le premier consumer servi fait taire tous les autres. À corriger avant la première migration. |
| D3 | **Coquille de dépréciation** `/api/users` → `/api/v1/users` : on la garde ou rupture nette ? | Détermine le patron des 31 autres groupes de routes. |
| D4 | **Le code plus riche que la spec** (capture/échec de paiement séparés, messagerie, avis, recommandations, wishlist, facturation) : on aligne le code ou on met à jour la spec ? | Risque de supprimer des fonctionnalités qui servent. |

---

## 4. Plan d'exécution

Cinq phases, ordonnées par rapport valeur/risque. Chaque lot se termine par un
`make build` — c'est la seule vérification disponible tant qu'aucun SDK .NET n'est
accessible de mon côté.

### Phase 1 — Enveloppe et versionnement REST · *mécanique, à faible risque*

**Contenu.** Les 31 groupes de routes restants passent sous `/api/v1/<domaine>` ;
les 105 sorties d'erreur directes passent sous enveloppe ; les 295 sorties de succès
aussi ; routes de la passerelle doublées (nouveau chemin + coquille de dépréciation).

**Découpage.** Un lot par service, 15 lots. Commencer par les 67 `Results.Unauthorized()` :
un seul motif de remplacement, applicable partout, et c'est le plus gros gain isolé.

**Risque.** Faible sur le code, **réel sur les clients** : la forme des réponses change.
Les applications mobile et web doivent être livrées avec, pas après. La coquille de
dépréciation côté passerelle couvre les chemins, pas la forme du corps.

**Dépend de.** D3.

### Phase 2 — Kafka conforme au §19 · *le cœur de la correction distribuée*

**Contenu.** Annoter les 90 événements de `[HbaEvent]` ; brancher
`KafkaIntegrationEventPublisher` sur `HbaEventEnvelope` et `HbaEventNaming` quand
l'attribut est présent ; câbler la séquence du §19.5 dans
`KafkaIntegrationEventConsumer` (vérifier l'inbox → traiter → tracer → committer → ACK) ;
migrations EF pour `consumer_inbox` et `idempotency_keys` dans les 14 services.

**Pourquoi c'est la phase la plus délicate.** Elle **change les topics de production**.
Aujourd'hui un topic par service (`hba.food.v1`) ; la spec veut un topic par agrégat
(`hba.prod.food.order.v1`). Les deux mondes ne se croisent pas : pendant la bascule,
producteurs et consommateurs doivent tourner en double écriture, sinon des messages
sont perdus sans qu'aucune erreur ne le signale.

**Découpage.** Par domaine, pas par service : les événements d'un même agrégat doivent
basculer ensemble. Ordre de risque croissant : `identity`/`user` → `catalog`/`inventory`
→ `marketplace` → `food` → `payment`/`wallet`.

**Dépend de.** D2, et d'une décision sur la stratégie de bascule (double écriture ou
fenêtre de maintenance).

### Phase 3 — Trous fonctionnels · *le vrai développement*

1. **promotion-service**, entier : 4 agrégats, 3 RPC, 5 événements, 3 endpoints.
   Priorité haute — les deux checkouts du §11 en dépendent.
2. **food-cart-service** : extraire `FoodCart` du panier générique.
3. `Reservation` (inventory), `Proof` + `DriverShift` (delivery), `MfaChallenge`
   (identity), `NotificationTemplate` (notification), `OpeningHour` + `ServiceZone`
   (restaurant).

**Dépend de.** D4 pour ne pas supprimer ce qui sert.

### Phase 4 — Signatures gRPC · *40 RPC manquants sur 53*

Renommages et ajouts dans les 13 `.proto`, plus les implémentations. Le code est
souvent **plus riche** que la spec (paiement : `InitiatePayment`/`CapturePayment`/
`FailPayment` contre `GetPaymentStatus`). À traiter après D4, sous peine de perdre
des cas d'usage modélisés.

**Dépend de.** D1 et D4.

### Phase 5 — Ledger en partie double · *migration de données*

Le seul écart qui est un choix d'architecture, pas un renommage. `accounts` +
`ledger_entries` immuables en remplacement des soldes. Reprise de l'historique,
réconciliation, preuve d'équilibre. À planifier comme un projet à part entière.

---

## 5. Ordonnancement recommandé — et son alternative

**Recommandé : 1 → 2 → 3 → 4 → 5.** La phase 1 est mécanique, valide le socle à
l'échelle des 16 services, et débloque les équipes mobile et web. On apprend sur du
terrain sûr avant d'ouvrir Kafka.

**Alternative, si l'objectif est une démonstration de bout en bout :** **3-promotion → 2 → 1**.
Les deux checkouts du §11 ne peuvent pas fonctionner sans promotion-service ni sans
des événements que les consumers reçoivent réellement. Le prix à payer : commencer par
la phase la plus risquée, sans avoir éprouvé le socle ailleurs.

Le choix dépend de ce qui est attendu en premier : des **clients débranchés du monolithe**,
ou un **parcours métier démontrable**. C'est une question de produit, pas d'ingénierie.

---

## 6. Limites de cet état des lieux

- **Rien n'a été testé ni exécuté.** La vérification s'arrête à `dotnet build`. Aucun
  test unitaire, aucun test d'intégration, aucun environnement démarré.
- **Aucune migration EF n'a été générée** : `consumer_inbox` et `idempotency_keys`
  n'existent dans aucune base. Le socle est compilé, pas opérationnel.
- **Les comptages sont des ordres de grandeur fiables, pas des inventaires exacts** :
  une route composée par variable plutôt que par chaîne littérale échappe à
  l'extraction, et un agrégat porté par un owned type au nom différent n'est pas
  détecté. Les colonnes « reste à faire » sont donc des majorants.
