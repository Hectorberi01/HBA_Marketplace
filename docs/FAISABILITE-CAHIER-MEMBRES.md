# Faisabilité — cahier des charges « Gestion des membres d'un vendeur Marketplace »

Lecture du cahier (56 sections) confrontée à l'état réel du dépôt au 19 août 2026.
Chaque constat ci-dessous est vérifié dans le code, chemin à l'appui.

---

## Verdict

**Le cahier est réalisable à environ 70 % tel qu'il est écrit.** Il est bien
construit : ses règles non négociables (§55) recoupent presque toutes celles que
l'audit du dépôt avait déjà identifiées, et deux d'entre elles referment des trous
réels et documentés.

Mais **il chiffre le module en supposant acquis quatre socles qui n'existent pas**,
et **une de ses idées centrales — le rôle par boutique — est aujourd'hui
inapplicable sur deux services sur trois**, faute de données pour l'appliquer.

Répartition :

| | Sections | État |
|---|---|---|
| Réalisable tel quel | §1–9, §15, §18, §21–29, §32–33, §36, §42–47, §49, §51 | Le dépôt a tout ce qu'il faut |
| Réalisable après un lot préalable non chiffré | §16–17, §31, §34–35, §37, §50 | Redis, audit, step-up |
| **Inapplicable sans changer le modèle d'un autre service** | **§4 (scope STORE), §12–13, §39–40, §54** | **Ni le stock ni les commandes ne connaissent la boutique** |
| Rien à garder aujourd'hui | §14, §41 (retours) | Le service est un squelette vide |
| Contredit une décision déjà prise | §55 (« plusieurs Seller organisations ») | À trancher |

---

## 1. Le blocage principal : le scope STORE n'est pas applicable

C'est l'idée structurante du cahier — `ORDER_MANAGER sur store_001`,
`INVENTORY_MANAGER sur store_002` (§4), et le scénario E2E §54 tout entier. Or un
rôle par boutique n'a de sens que si la ressource sur laquelle il porte est
rattachable à une boutique. Vérification, service par service :

| Service | Rattachement à une boutique | Verdict |
|---|---|---|
| **catalog** | `Product.StoreId` (Guid?, **nullable**), `ProductOffer.StoreId` (non nullable) | Applicable, avec une réserve grave |
| **inventory** | **Aucun.** `FulfillmentLocation` n'a que `OwnerId` (le vendeur) ; `InventoryItem` n'a que `LocationId` | **Inapplicable** |
| **order** | **Aucun.** `OrderLine` porte `SellerId` et `ShipFromLocationId`, jamais de boutique. `OrderSellerShare` = `(SellerId, quantité, montant)` | **Inapplicable** |

Conséquence directe et non contournable :

> **Le scénario §54 ne peut pas être écrit.** « Store A → peut confirmer commande /
> Store B → ne peut pas ajuster stock » suppose qu'une commande et un stock
> appartiennent à une boutique. Aucun des deux ne le sait.

Et la réserve sur catalog est sérieuse. La migration
`20260813000000_RepriseStoresFromSellers.cs` a peuplé `product_offers.StoreId`
**avec l'identifiant du vendeur**, et crée la boutique de reprise avec
`StoreId = SellerId` pour que les offres restent justes. Son propre commentaire
avertit : « la coïncidence ne vaut que pour la première boutique de chaque vendeur
existant […] Rien dans le code ne doit jamais déduire l'un de l'autre. » Un contrôle
d'accès par boutique posé sur ces données donnerait, pour tout vendeur existant, une
boutique unique portant tout — c'est-à-dire exactement le scope SELLER, déguisé.

**Les trois issues, par coût croissant :**

1. **Livrer le scope SELLER seul d'abord**, et le scope STORE ensuite, service par
   service, à mesure que chacun apprend la boutique. Le modèle de données (§21–23)
   prévoit déjà `store_memberships` : on peut le construire dès le départ et ne
   l'appliquer qu'où il mord.
2. **Ajouter `StoreId` à `OrderLine`** au moment où la commande est composée — le
   panier connaît l'offre, l'offre connaît la boutique. Puis le remonter dans
   `OrderSellerShare`, ce qui touche wallet, notification et seller-service, tous
   consommateurs de cet événement.
3. **Rattacher les lieux d'expédition aux boutiques** dans inventory. Aujourd'hui la
   relation existe dans l'autre sens seulement (`Store.FulfillmentLocationId`), et
   rien n'empêche deux boutiques de pointer le même lieu — donc le sens inverse
   n'est pas déductible.

Le cahier ne mentionne aucun de ces trois travaux.

---

## 2. Les quatre socles supposés acquis, et qui n'existent pas

### 2.1 Redis n'est pas branché — et le cache mémoire rend §53 faux

`ICacheService` (`shared/common/HBA.Shared.Application/Abstractions/ICacheService.cs`)
existe et est correct. Son implémentation `DistributedCacheService` s'appuie sur
`IDistributedCache`. Mais `DependencyInjection.cs` l. 134-151 fait
`AddDistributedMemoryCache()` en annonçant en commentaire que « le Bootstrap remplace
l'`IDistributedCache` par Redis » — **ce remplacement n'existe nulle part.**
`AddStackExchangeRedisCache` : zéro occurrence dans le dépôt. Le seul Redis
réellement branché est celui de delivery-service, pour la géolocalisation des
livreurs, et il passe par `IConnectionMultiplexer`, pas par `ICacheService`.

Donc le cache de permissions est **un cache mémoire, par instance**. Et l'invalidation
par Kafka décrite en §31 et §50 ne fonctionne pas dans ce cadre : dans un groupe de
consommateurs, **une seule instance reçoit l'événement** et vide son propre
dictionnaire. Les autres répliques continuent de servir les droits périmés jusqu'au
TTL.

> Le scénario **§53 (« suspension immédiate »)** échoue donc sur N−1 instances.
> C'est précisément le cas que le cahier tient à garantir, et c'est le cas qui, en
> production, laisse un employé révoqué agir pendant cinq minutes.

Trois options : brancher réellement Redis (un lot en soi, mais qui profite aussi à
catalog, cart, review et seller) ; ou ne pas mettre les permissions en cache et payer
l'appel gRPC ; ou un cache mémoire avec un topic Kafka **par instance** (groupe de
consommateurs unique par réplique) — faisable, mais c'est un mécanisme que le dépôt
n'a nulle part.

### 2.2 L'audit n'existe pas, et sa partie difficile est hors de seller-service

Aucune table, aucune interface, aucun écrivain : `audit_log`, `AuditLog`,
`IAuditWriter`, `AuditEntry` → **zéro occurrence** dans tout le dépôt. Les 28 fichiers
qui contiennent le mot « audit » n'en parlent qu'en commentaire, souvent pour signaler
qu'un journal serait vide.

Créer `seller_member_audit_logs` et l'API §45 est facile. **Ce qui ne l'est pas, c'est
l'alimenter.** L'exemple d'enregistrement du cahier (§34) est une action de
**commande** (`ORDER_STATUS_CHANGED`, ressource `SELLER_ORDER`), et §35 exige d'auditer
l'ajustement de stock, la publication produit, l'approbation de retour. Or :

- **order-service n'enregistre aucun acteur.** `Order` n'a ni `ActorId`, ni
  `PerformedBy`, ni `UpdatedBy` — grep confirmé à zéro. Le fichier d'endpoints le dit
  lui-même : sur un remboursement, le motif en texte libre est « la seule trace de qui
  a décidé quoi ».
- **inventory-service non plus.** Un `AdjustStock` n'est imputable à personne en base.
- Le seul acteur persisté du périmètre marketplace est `ProductReview.ReviewedBy` dans
  catalog — et il concerne l'administrateur, pas le vendeur.

Le §40 le demande explicitement (« Chaque mutation stocke : actorUserId, actorMemberId,
actorStoreId »). C'est **une modification de schéma et de signature sur toutes les
mutations d'order et d'inventory**, pas un ajout dans seller-service. Le porteur existe
déjà côté transport (`HbaActor` dans `HbaRequestContext`, ambient) mais n'est jamais
persisté : c'est le bon point de départ, ce n'est pas le travail.

### 2.3 Le step-up (§37) n'a aucun socle, alors que le MFA, lui, existe

Bonne nouvelle : identity-service a **deux** mécanismes MFA complets — TOTP par
application (`ITotpService`, `POST /me/mfa/setup|confirm|disable`) et OTP par SMS/email
(agrégat `MfaChallenge`, code haché, 5 tentatives, 10 minutes).

Mais le step-up demandé n'est pas le MFA : c'est « cette action exige que
l'authentification soit **récente** ». Or `amr`, `acr`, `auth_time`, `reauth`,
`step-up` → **zéro occurrence dans tout le dépôt**. Le JWT
(`JwtTokenGenerator.cs`) n'embarque aucun de ces éléments, donc rien, côté
seller-service, ne peut savoir depuis combien de temps l'utilisateur s'est authentifié.

C'est un **lot identity-service** : ajouter `auth_time` (et idéalement `amr`) au jeton,
plus un endpoint de re-vérification qui rafraîchisse la marque. Petit, mais préalable
obligatoire à §16, §17 et §37.

### 2.4 Il n'existe aucun précédent d'invitation par email — mais toutes les pièces sont là

`Invit` sur tout le dépôt : 8 fichiers, **aucun ne concerne l'invitation d'une
personne**. Le seul rattachement existant (`RestaurantStaff` via `HireStaffCommand`)
prend un `UserId` déjà connu, sans passer par identity.

En revanche les pièces nécessaires existent toutes :
`IIdentityModuleApi.GetUserByEmailAsync` **et** `rpc GetUserByEmail` (l'adresse voyage
dans le corps du message, jamais dans une URL — c'est documenté au proto),
`POST /auth/register` pour l'invité sans compte, et notification-service pour l'envoi.
Le workflow §9 est donc constructible ; il est simplement inédit ici, et le premier de
son genre.

---

## 3. Ce que le cahier demande et qui n'a rien à garder

**§14 et §41 — permissions de retour.** `RETURN_VIEW`, `RETURN_APPROVE`,
`RETURN_REJECT`, `RETURN_CONFIRM_RECEIVED`, `RETURN_INSPECT`, `RETURN_DISPUTE_VIEW`.
Or `return-refund-service` est un **squelette vide** : quatre csproj, un `Program.cs` de
18 lignes qui n'expose que les sondes de santé, un README qui dit « aucune entité, aucun
cas d'usage, aucun endpoint métier ». Le service n'est ni dans `HBA.sln`, ni dans
`docker-compose.dev.yml`. `class ReturnRequest` → zéro occurrence dans le dépôt.

Il existe des **contrats** (`ReturnRequestSummary`) et **deux consommateurs enregistrés**
(wallet, notification) pour des événements que **personne ne publie**.

Ces six permissions peuvent être déclarées au catalogue — c'est même souhaitable, cela
coûte une ligne — mais aucune route ne les portera avant que le service ne soit écrit.
À dire dans le commit plutôt qu'à découvrir en recette.

**§41 — `REVIEW_REPLY`, en revanche, referme un vrai trou.** La réponse du vendeur à un
avis existe entièrement (`Review.SellerReply`, `ReplyToReviewCommand`,
`POST /reviews/{id}/reply`) — et son handler **ne lit aucune identité**. Pas de
`ClaimsPrincipal`, aucune vérification que l'appelant est le vendeur de l'avis :
n'importe quel utilisateur authentifié peut répondre au nom de n'importe quel vendeur.
C'est le gain le moins cher de tout le cahier.

---

## 4. La contradiction à trancher : §55

Le cahier pose, en règle non négociable :

> « Un User peut appartenir à plusieurs boutiques et **potentiellement plusieurs Seller
> organisations**. »

`PLAN-SELLER-FIN.md` avait tranché l'inverse (**option A** : un compte, un vendeur), et
`PLAN-MEMBRES.md` construit dessus. Ce n'est pas un détail de modélisation, parce que
cinq services résolvent aujourd'hui le vendeur **depuis le seul jeton** :
`GetSellerByUserIdAsync` / `CurrentSellerIdAsync`, sur ~72 routes.

Avec plusieurs organisations, **cette fonction devient indécidable** — et le cahier en
est conscient, puisqu'il pose en face : « les droits sont évalués avec sellerId +
storeId + userId » et « ne jamais faire confiance au sellerId/storeId fourni par le
client ». Les deux règles ne sont contradictoires qu'en apparence : *ne pas faire
confiance* veut dire *vérifier*, pas *refuser de recevoir*. La cible est donc :

- le client **désigne** le vendeur (paramètre de route, en-tête, ou déduit de la
  ressource visée) ;
- le serveur **vérifie** l'appartenance et la permission avant tout traitement.

C'est propre, et c'est ce que dit `HasPermission(user_id, seller_id, store_id,
permission)`. Mais c'est **une réécriture de la résolution sur les ~72 routes**, pas un
élargissement. Le coût n'est pas le même selon la réponse :

| | Option A (un compte, un vendeur) | Option B (§55) |
|---|---|---|
| Résolution | `GetSellerByUserIdAsync` élargi aux membres | Réécrite : le vendeur vient de la requête, vérifié |
| Routes touchées | Les gardes seulement | Gardes **et** signatures d'API |
| Clients (app, BFF) | Inchangés | Doivent désigner le vendeur à chaque appel |
| Le jour où B devient nécessaire | Tout est à refaire | Déjà fait |

`CheckMerchantCapability(user_id, seller_id, capability)` — déjà dessiné avec les deux
identifiants dans `PLAN-MEMBRES.md` — est compatible avec les deux. La question porte
sur la résolution, pas sur le contrat.

---

## 5. Ce qu'il ne faut pas suivre littéralement

- **§48, l'arborescence `HBA.SellerService/`.** Le service s'appelle
  `services/marketplace/seller-service/` et ses assemblages `HBA.Merchants.*`. Suivre
  §48 à la lettre créerait un second service en parallèle du vrai.
- **§24–25, le préfixe `/api/v1/seller/…`.** Les routes existantes sont
  `/api/v1/merchants/…`. La décision D1 tient : on garde le nommage du code et on aligne
  la spécification, pas l'inverse.
- **§46, les 20 codes d'erreur.** Le dépôt applique l'enveloppe §25 avec **cinq** codes
  normalisés, le code fin vivant dans `error.details[field="reason"]` (décision D16).
  Ces 20 valeurs sont donc des *reasons*, pas des `error.code`. Le §47 du cahier est
  d'ailleurs déjà presque conforme — il lui manque `meta.requestId`.
- **§18 + §23, les rôles personnalisés en base.** C'est réalisable et cohérent, mais
  c'est un modèle **différent** de `RestaurantStaff` (enum hiérarchique, rang comparé par
  l'ordinal). On y perd le contrôle du compilateur et la comparaison de rang en SQL ; on
  y gagne la règle §36 « un membre ne peut attribuer que ce qu'il est autorisé à
  administrer », qui est en réalité **plus solide** qu'un rang ordinal. C'est un choix
  défendable — mais alors le dépôt aura deux modèles de rôle par entité, et il faut
  l'assumer explicitement plutôt que le subir.

---

## 6. Ce que le cahier apporte, et qu'il faut garder

Sans réserve :

- **§2, l'encadré contre `isEmployee` sur `User`.** Exactement juste, et
  `UserRoleAssignment` (identity) ne porte aujourd'hui **aucun champ de portée** : ni
  vendeur, ni boutique. La séparation identité / appartenance est bien la bonne ligne.
- **§7, le hash du token d'invitation, l'expiration, l'usage unique.**
- **§36 en entier**, en particulier « sellerId/storeId provenant du body ne constitue
  jamais une preuve d'autorisation » et « le dernier OWNER ne peut pas être supprimé sans
  transfert ».
- **§55, « refus par défaut si le contexte d'autorisation est incomplet ».** C'est, dit
  autrement, la règle qui structure `PLAN-MEMBRES.md` : ne rien ouvrir avant que la
  vérification n'existe.
- **§17, `PAYOUT_CONFIGURE` réservé à OWNER.** Identique au point que le plan signalait
  comme celui à ne pas rater : c'est la route qui détourne les virements.
- **§51, la liste de tests.** Elle est bonne, et la moitié est écrivable dès le lot A.

---

## 7. Effet sur le découpage

`PLAN-MEMBRES.md` reste valable dans son ordre — A domaine, B rôle par événement,
C capacité, D les appelants, E notifications — mais le cahier ajoute **trois lots
préalables ou parallèles qu'il ne chiffre pas** :

| | Lot ajouté | Bloque |
|---|---|---|
| **0a** | Brancher Redis sur `ICacheService` | §31, §50, §53 |
| **0b** | `auth_time` / `amr` dans le JWT + endpoint de re-vérification (identity) | §16, §17, §37 |
| **0c** | Propager et persister l'acteur dans order et inventory | §34, §35, §40, §45 |

Et **un arbitrage à rendre avant d'écrire la première ligne** : option A ou option B
(§4 ci-dessus). Il change la nature du lot D, pas seulement sa taille.

Le scope STORE, lui, ne se règle pas par un lot préalable : il se règle en le livrant
**après** le scope SELLER, service par service, à mesure que chacun apprend la boutique.
