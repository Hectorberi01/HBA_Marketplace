# Plan découpé — les membres d'équipe et leurs capacités

Établi le 19 août 2026, après `444f540`. Dernier lot du §10.3 pour seller-service.

Décisions déjà prises (`PLAN-SELLER-FIN.md`) : **option A — un compte, un vendeur**
(un utilisateur est propriétaire OU membre d'exactement un vendeur), et un
`MemberRole` **énuméré** plutôt qu'un `permissions_json` libre.

---

## Ce que la vérification a changé au plan

### 1. Il existe déjà un modèle complet à copier, et il est bien fait

`RestaurantStaff`, dans food/restaurant-service, est le SEUL rôle par entité du
dépôt — et il a tout ce qu'on s'apprêtait à réinventer :

- `StaffRole` énuméré **avec hiérarchie par l'ordre des valeurs**, et l'invariant
  écrit noir sur blanc : « un employé ne peut agir que sur quelqu'un de
  STRICTEMENT PLUS BAS que lui. NE JAMAIS RÉORDONNER NI INSÉRER AU MILIEU. »
- `FoodPermission` par-dessus, avec `DefaultsFor(role)` et des dérogations
  nominatives — parce que « les rôles fournissent des permissions par défaut, mais
  le modèle doit permettre des permissions fines ».
- La forme de persistance : `HasConversion<int>` (et **pas** `string` — la
  comparaison de rang doit fonctionner en SQL), index unique
  `(RestaurantId, UserId)`, index simple sur `UserId` pour la résolution
  jeton → établissement, et `UsePostgresRowVersion()` pour la garde du « dernier
  propriétaire ».
- Les invariants d'agrégat : le fondateur est intouchable, un départ **désactive**
  au lieu de supprimer, et **chaque mutation exige un acteur** — « il n'existe
  aucune surcharge sans acteur. Le compilateur refuse l'appel non gardé. »
- Et jusqu'au transport : `rpc GetStaffMembership` existe, avec son
  `DenyUnlessStaffAsync(permission)` côté endpoints.

**Conséquence : ce lot n'est pas une conception, c'est une transposition.** Suivre
cette forme plutôt qu'en inventer une seconde évite d'avoir deux modèles de rôle
par entité dans le même dépôt, qui divergeraient à la première correction.

### 2. …avec UN piège documenté à ne pas recopier

`FoodContracts.cs` : « UN COMPTE = AU PLUS UN ÉTABLISSEMENT, AUJOURD'HUI.
`GetStaffMembershipAsync` rend une appartenance UNIQUE. Le jour où un même compte
travaillera dans deux restaurants, ce contrat devra rendre une liste. »

C'est exactement le piège de `GetSellerByUserIdAsync`. **Donc :
`CheckMerchantCapability(user_id, seller_id, capability)` prend les DEUX
identifiants** et ne résout jamais une appartenance depuis le seul utilisateur. Le
jour où l'option B deviendra nécessaire, le contrat n'aura pas à changer.

### 3. Le chiffrage réel : 5 points d'appel, ~72 routes

| Service | Point d'appel | Routes | Nature |
|---|---|---|---|
| seller | `DenyUnlessOwnSellerAsync` | 21 | garde |
| catalog | `CurrentSellerIdAsync` → 9 usages | ~30 | garde **et cadrage de données** |
| inventory | `CurrentSellerIdAsync` → 4 usages | 12 | garde **et cadrage** |
| payment | `DenyUnlessOwnSellerAsync` | 8 | garde |
| order | garde en ligne | 1 | garde |

Plus le RPC `GetSellerByUser` et trois doubles de test.

**Les gardes et le cadrage ne se traitent pas pareil.** Une garde répond
« autorisé ou non ». Un cadrage écrit le `sellerId` dans une commande —
`CreateOfferCommand`, `CreateProductCommand`, `CreateLocationCommand`, la liste
« mes produits », l'intersection avec « mes lieux ». Là, un membre doit produire
des données rattachées au vendeur, pas à lui-même : le cadrage marche déjà, à
condition que la résolution rende le bon vendeur.

### 4. Le danger central, et ce qui dicte l'ordre des lots

Avec l'option A, la voie la plus courte est de faire répondre
`GetSellerByUserIdAsync` **aussi pour les membres**. Les ~72 routes continuent alors
de fonctionner sans qu'on touche une ligne.

**Et chaque membre reçoit instantanément les pleins pouvoirs de son propriétaire,
sur cinq services, sans que rien ne le signale** — y compris
`PUT /{sellerId}/payout-account`, « la route la plus rentable du service pour qui
la trouvait », qui fixe le numéro Mobile Money où partent les gains.

D'où la règle qui structure tout ce plan :

> **`GetSellerByUserIdAsync` ne doit pas résoudre les membres avant que la
> vérification de capacité ne soit en place — et jamais sur un service qui ne
> l'applique pas encore.**

C'est aussi pourquoi le lot A livre des membres qui ne peuvent RIEN faire, et
pourquoi c'est volontaire.

---

## Lot A — le domaine et la table

**Périmètre : seller-service seul. Aucun autre service ne bouge.**

`SellerMember` : `SellerId`, `UserId`, `MemberRole`, `IsActive`, `AddedBy`,
`AddedOnUtc`. Transposition de `RestaurantStaff`, y compris :

- `MemberRole` énuméré et **hiérarchique** — `Owner = 0`, `Manager = 1`,
  `Accountant = 2` —, persisté en `int`, avec l'avertissement de non-réordonnancement.
- Index unique `(SellerId, UserId)`, index simple sur `UserId`, `UsePostgresRowVersion()`.
- Mutations **exigeant un acteur**, sans surcharge sans acteur.
- Un départ **désactive**, il ne supprime pas.

Invariants à tenir et à tester :

- le propriétaire ne peut pas se retirer lui-même ;
- un membre ne peut agir que sur un rang strictement inférieur au sien ;
- retirer un membre ne ferme pas son compte utilisateur ;
- le dernier propriétaire est intouchable.

Routes de gestion, réservées au propriétaire, sous les gardes actuelles (qui
fonctionnent, puisque seul le propriétaire est résolu à ce stade) :
`GET/POST /merchants/{sellerId}/members`, `PUT .../{memberId}/role`,
`DELETE .../{memberId}`.

**À la fin de ce lot, un membre existe en base et ne peut RIEN faire.** Il ne
porte pas le rôle `Seller`, donc il est refoulé par `MapSellerGroup` avant tout
handler — 403 au corps vide, sans `error.code` ni `meta.requestId`. C'est
inconfortable et c'est le bon état intermédiaire : rien n'est ouvert avant que la
capacité n'existe.

**Livrable autonome. Le dépôt reste vert et le comportement ne change pour personne.**

---

## Lot B — le rôle `Seller` pour les membres

**Périmètre : seller-service (deux événements) + identity-service (deux handlers).**

`SellerMemberAddedIntegrationEvent` et `SellerMemberRemovedIntegrationEvent`,
consommés par identity qui appelle `BusinessRoleGrant.GrantAsync` — le même chemin
que `SellerRegisteredIntegrationEvent` → `GrantSellerRoleHandler`.

**C'est exactement le trou déjà documenté côté restaurant**, dans
`GrantFoodPartnerRoleHandler` :

> « CONSÉQUENCE À CONNAÎTRE : LE PERSONNEL N'EST PAS COUVERT. Seul
> `OwnerUserId` reçoit le rôle. Un cuisinier ou un caissier ajouté par le §8 n'en
> obtient aucun, et l'écran de cuisine — qui est fait POUR eux — leur reste fermé.
> Le combler demande un événement « membre ajouté » que food-service ne publie pas
> encore. »

Le refermer ici donne le gabarit pour l'y refermer aussi. À signaler dans le
commit : c'est un lot food à part, mais il devient trivial après celui-ci.

**Le retrait n'est PAS symétrique.** Retirer le rôle `Seller` à un membre sorti
est correct sous l'option A (un compte, un vendeur) — mais il faut vérifier que le
compte n'est pas lui-même propriétaire d'un autre dossier avant de révoquer, sans
quoi on enferme dehors un vendeur qui était par ailleurs comptable chez un
confrère. `BusinessRoleGrant` est idempotent et ne lève pas ; la révocation doit
l'être aussi.

**Et l'invalidation de cache.** `sellers:by-user:{userId}` a un TTL de dix
minutes. Un membre ajouté puis immédiatement résolu tomberait sur une entrée
négative en cache. À invalider à chaque mutation d'appartenance — c'est le genre
d'oubli qui produit un « ça marche au bout de dix minutes » que personne ne relie
à un cache.

**À la fin de ce lot, un membre franchit `MapSellerGroup` et est refusé par
chaque garde d'appartenance** — avec, cette fois, un corps d'erreur lisible. C'est
un progrès de diagnostic, pas encore une capacité.

**Livrable autonome.**

---

## Lot C — la capacité, et seller-service en premier

**Périmètre : le proto merchant, `ISellerModuleApi`, et les 21 routes de seller-service.**

`CheckMerchantCapability(user_id, seller_id, capability)` au proto et dans
`ISellerModuleApi` — **les deux identifiants, jamais l'utilisateur seul** (voir le
piège §2).

La capacité vient du croisement **rôle du membre × état du vendeur** : vendre exige
un KYB vérifié, pas seulement un rôle. C'est ce croisement qui fait la valeur du
RPC — `ValidateSeller` répond « ce vendeur est-il en règle », jamais « ce compte
a-t-il le droit de faire CECI ».

Capacités proposées, à confronter aux 21 routes existantes :
`ManageProfile`, `ManageKyb`, `ManagePayout`, `ManageStores`, `ManageMembers`,
`Sell`, `ViewOrders`, `ViewFinance`.

**Le point à ne pas rater de ce lot** : `PUT /{sellerId}/payout-account` reste
`Owner` SEUL. Un `Manager` qui pourrait repointer le compte de retrait annulerait
tout ce que la garde de propriété du lot 3 avait fermé — c'est la route qui
détourne les virements.

**Et c'est ici, pas avant, que `GetSellerByUserIdAsync` peut commencer à
résoudre les membres** — sur seller-service uniquement, puisque c'est le seul
service à appliquer la capacité à ce stade. Le contrat gRPC continuera de ne
résoudre que le propriétaire pour les quatre autres jusqu'à leur lot.

Si cette dernière contrainte s'avère trop coûteuse à exprimer, le repli est de
livrer C et D ensemble par service. **Ce qu'il ne faut PAS faire est d'élargir la
résolution globalement en attendant.**

**Livrable autonome, et c'est le premier où un membre peut agir.**

---

## Lot D — les quatre services appelants, un par livraison

Dans cet ordre, du moins risqué au plus gros :

| # | Service | Routes | Pourquoi là |
|---|---|---|---|
| D1 | **order** | 1 | Une garde en ligne, une route. Le rodage du gabarit se fait ici, pas sur trente routes |
| D2 | **payment** | 8 | Gardes pures, aucun cadrage. `ViewFinance` ≠ `ManagePayout` : un comptable lit le relevé, il ne demande pas un retrait |
| D3 | **inventory** | 12 | Garde **et** cadrage — `CreateLocation` remplace l'`OwnerId` du corps, `MesLieux` intersecte les résultats |
| D4 | **catalog** | ~30 | Le plus gros, et le plus subtil : sept usages de cadrage écrivent le `sellerId` dans une commande |

Chaque livraison : remplacer la résolution locale par la capacité, migrer les
tests d'autorisation du service, puis seulement ouvrir la résolution des membres
pour ce service.

**Deux gardes de payment concernent les LIVREURS** (`DenyUnlessOwnDriverAsync`)
et n'ont rien à voir avec ce lot. Ne pas les emporter par symétrie.

---

## Lot E — ce que le backend ne suffit pas à rendre utile

**Les notifications.** `SellerOrderNotificationHandler` fait une « TRADUCTION
SellerId → UserId » explicite et pousse vers le compte du PROPRIÉTAIRE. Un membre
ayant `ViewOrders` ne recevra donc aucune notification de commande — il verra les
commandes s'il ouvre l'écran, jamais qu'il y en a une nouvelle. C'est le même trou
que le rôle, un étage plus haut.

**L'application vendeur.** L'écran de gestion d'équipe lui appartient, et n'est pas
chiffré ici.

---

## Ordre, et ce que chaque étape laisse derrière

| # | Lot | État à la fin |
|---|---|---|
| **A** | Domaine + table + routes de gestion | Les membres existent, ne peuvent rien. 403 vide |
| **B** | Rôle `Seller` par événement | Les membres entrent, sont refusés avec un motif lisible |
| **C** | Capacité + seller-service | Un membre agit — sur seller-service seulement |
| **D1–D4** | order, payment, inventory, catalog | Un membre agit partout, selon son rôle |
| **E** | Notifications, app vendeur | Un membre est prévenu, et sait où cliquer |

---

## Ce que ce plan ne couvre pas

- **L'option B** (un compte, plusieurs vendeurs). `CheckMerchantCapability` est
  dessiné pour ne pas avoir à changer le jour où elle arrivera, mais
  `GetSellerByUserIdAsync` devrait alors devenir `ListSellersForUserAsync`, et
  chaque appelant dire POUR QUEL vendeur il agit. Rien dans le dépôt ne justifie ce
  coût aujourd'hui.
- **Le trou jumeau côté food** — le personnel de restaurant sans rôle. Le lot B lui
  donne son gabarit ; il reste à écrire.
- **Aucune estimation de durée.** Le seul chiffre fiable de ce document est le
  nombre de routes.
