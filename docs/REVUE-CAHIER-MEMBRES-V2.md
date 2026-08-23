# Revue de la V2 — « Seller Members & RBAC »

Delta par rapport à `FAISABILITE-CAHIER-MEMBRES.md` (V1). Vérifié dans le dépôt au
19 août 2026.

---

## Verdict

**La V2 est constructible.** Les cinq points bloquants de la V1 sont traités, et
traités correctement — pas contournés :

| Blocage V1 | Traitement V2 |
|---|---|
| Scope STORE inapplicable (inventory, order) | §3 + §18 + §19 : phasage SELLER → STORE, avec les deux migrations nommées |
| Redis pas branché | §4, lot 0a, avec le critère d'acceptation qui va bien |
| Aucun audit, acteur non persisté | §6 + §21, lot 0c, et la reconnaissance que le travail est **dans les services métier** |
| Step-up sans socle | §5, lot 0b : `auth_time` / `amr` / endpoint de re-vérification côté identity |
| Contradiction option A / option B | §7 : option B assumée, avec §23 pour la migration des ~72 routes |

Et les corrections de forme sont justes : §26 étend le service existant, §13 garde
`/api/v1/merchants`, §16 remet les 20 valeurs dans `details.reason` derrière l'enveloppe
normalisée. `enforcementStatus: "PREPARED"` (§8) est une bonne idée en soi : la donnée
existe et se dit non appliquée, plutôt que d'être absente puis rétro-ajoutée.

**Restent cinq points à corriger — dont un qui fait échouer la Phase SELLER telle
qu'elle est planifiée.**

---

## 1. Le lot manquant : personne ne donne le rôle `Seller` au membre

C'est le point sérieux. Le plan §22 n'a aucun lot pour cela, et sans lui **aucun test
d'acceptation §24 ne peut passer**.

Vérification :

```csharp
// shared/common/HBA.Shared.Hosting/Http/ApiAuthorization.cs:155-157
public static RouteGroupBuilder MapSellerGroup(this IEndpointRouteBuilder app, string prefix)
    => app.MapGroup(prefix)
        .RequireAuthorization(policy => policy.RequireRole(SellerRole, AdminRole, ModeratorRole));
```

Le groupe filtre **uniquement sur la claim de rôle du jeton**. Et le rôle n'est accordé
qu'au propriétaire :

```csharp
// identity-service/.../BusinessRoleGrantHandlers.cs:139-150
public sealed class GrantSellerRoleHandler : IIntegrationEventHandler<SellerRegisteredIntegrationEvent>
    => _grant.GrantAsync(e.UserId, "Seller", "inscription vendeur", ct);
```

Donc un membre invité, activé, avec ses rôles et ses permissions correctement écrits en
base, **est refoulé par le routage avant d'atteindre le moindre handler**. Le RBAC des
lots A, B et C ne s'exécute jamais. Le test « Order Manager peut confirmer une commande
du Seller » (§24) échoue à l'étage du groupe de routes, pas à l'étage de l'autorisation.

**Le lot manquant est petit** — un consommateur identity sur `seller.member.joined` qui
appelle `BusinessRoleGrant.GrantAsync(userId, "Seller", …)`, exactement la forme des
trois handlers existants (`GrantSellerRoleHandler`, `GrantFoodPartnerRoleHandler`,
`GrantDriverRoleHandler`). Mais il doit figurer au plan, **entre A et C**, et son
symétrique sur `seller.member.revoked` aussi.

Trois précisions à porter dans ce lot :

- **`BusinessRoleGrant` ne lève jamais** (`BusinessRoleGrantHandlers.cs:79-84, 99-104`) :
  un utilisateur inconnu produit un `LogError` et un retour silencieux. Le flux
  d'invitation doit donc garantir que le compte identity existe **avant** de publier
  `seller.member.joined`, sans quoi le membre restera dehors sans qu'aucune erreur ne
  remonte.
- **La révocation n'est pas symétrique** : avant de retirer le rôle `Seller`, vérifier
  que le compte n'est pas propriétaire d'un autre dossier — sinon on enferme dehors un
  vendeur qui était par ailleurs comptable chez un confrère. Sous l'option B (§7),
  ce cas devient courant, pas théorique.
- **Le trou jumeau côté food** est déjà documenté verbatim dans
  `GrantFoodPartnerRoleHandler` : seul `OwnerUserId` reçoit le rôle, « un cuisinier ou un
  caissier n'en obtient aucun, et l'écran de cuisine — qui est fait POUR eux — leur reste
  fermé ». Ce lot lui donne son gabarit.

---

## 2. §10 contredit §11 : le scope SELLER « temporaire » **est** l'escalade

§10 attribue en scope initial :

| Rôle | Scope initial | Scope cible |
|---|---|---|
| STORE_ADMIN | **SELLER temporaire ou préparé** | STORE |
| CATALOG_MANAGER | **SELLER** puis STORE Catalog | STORE |
| INVENTORY_MANAGER | **SELLER** | STORE après migration |
| ORDER_MANAGER | **SELLER** | STORE après migration |

§11 pose : « STORE_ADMIN ne peut pas obtenir des permissions SELLER/OWNER par
modification de requête. »

Les deux disent le contraire. §11 interdit d'**obtenir** le scope SELLER par une requête
forgée ; §10 le **donne d'office** pendant toute la Phase 1. Le résultat pour
l'utilisateur est le même : un « administrateur de boutique » a pouvoir sur toute
l'organisation. La différence est qu'ici c'est la spécification qui l'accorde, pas
l'attaquant qui l'arrache.

**Ce n'est acceptable que sous une condition, qui est vérifiable en données : tant que le
vendeur n'a qu'une seule boutique.** C'est le cas de tous les vendeurs existants — la
migration de reprise `20260813000000_RepriseStoresFromSellers.cs` en a créé exactement
une par vendeur. Dès qu'un vendeur en ouvre une seconde, un ORDER_MANAGER recruté pour la
boutique B agit sur la boutique A, et le §54 de la V1 devient un trou plutôt qu'un test.

Deux façons de refermer, à trancher :

1. **Les rôles à vocation STORE ne sont pas attribuables en Phase 1.** Seuls OWNER,
   SELLER_ADMIN et FINANCE_MANAGER existent. C'est cohérent avec `enforcementStatus:
   "PREPARED"` : le membre peut être *affecté* à une boutique, la permission n'est pas
   *accordée*.
2. **Ils sont attribuables, mais refusés dès que le vendeur a ≥ 2 boutiques.** Un garde
   explicite plutôt qu'une règle tacite, avec le `reason` qui va bien.

Ne pas laisser ce point implicite : c'est celui qu'on découvre le jour où un vendeur
ouvre sa deuxième boutique, c'est-à-dire le jour où le multi-boutique sert enfin.

---

## 3. §18 — `OrderLine.StoreId NOT NULL` efface la distinction que §25 exige

§18 écrit `+ StoreId UUID NOT NULL (pour nouvelles commandes)`, et §25 exige en test que
« les commandes historiques sans StoreId suivent une politique legacy explicite et non
une déduction SellerId=StoreId ».

Techniquement, `NOT NULL` est possible : `order_lines` n'a **aucune donnée de migration
ni de seed** dans le dépôt (`InsertData` → zéro occurrence ; aucun script de `scripts/`
ne l'alimente), et le précédent exact existe —
`20260819000000_LigneDeCommandeTypee.cs:52-58` ajoute `RestaurantId` en
`nullable: false, defaultValue: Guid.Empty`.

Mais c'est justement ce qui pose problème : une colonne `NOT NULL` avec une valeur
sentinelle rend les lignes historiques **indiscernables** des lignes nouvelles. La
politique legacy de §25 n'a alors plus de critère sur lequel s'appuyer.

`Guid?` nullable dit la vérité : `null` signifie « écrite avant le scope boutique », et
c'est exactement la clé dont la politique legacy a besoin. Même choix que
`Product.StoreId`, qui est nullable pour la même raison et le documente.

---

## 4. §19 — la table `store_fulfillment_locations` se greffe sur un champ que personne ne lit

§19 propose `store_fulfillment_locations(store_id, fulfillment_location_id, is_default,
status)` puis la chaîne `InventoryItem → LocationId → Store → Seller → Member`.

Trois constats avant de l'écrire :

- **`Store.FulfillmentLocationId` n'a aucun index unique** (`StoreConfiguration.cs:27` :
  `builder.Property(s => s.FulfillmentLocationId);` et rien d'autre ; le seul index de la
  table est sur `SellerId`). Deux boutiques peuvent pointer le même lieu, et rien dans
  `Store.AttachFulfillmentLocation` ne le vérifie. §19 le pressent (« si un lieu est
  partagé […] la politique doit être explicite et testée ») — c'est déjà le cas
  aujourd'hui, ce n'est pas une hypothèse.
- **Personne ne consomme ce champ hors seller-service.** Il traverse le contrat et le fil
  gRPC (`StoreSummary`, `MerchantsGrpc.cs:183-185`), et zéro appelant en lit la valeur.
  Son unique usage métier est interne : `Store.Open()` refuse d'ouvrir sans lui.
- **Ce qui circule réellement dans les commandes est `ShipFromLocationId`**, porté par
  l'**offre**, recopié dans le panier puis dans la ligne de commande — un chemin
  totalement indépendant de `Store.FulfillmentLocationId`. Si §18 fige `StoreId` depuis
  l'offre au checkout, ce chemin-là devient la source, et §19 sert à autre chose (le
  cadrage du stock), pas au rattachement des commandes.

**Et la V2 ne dit pas quel service possède la table.** Dans seller-service, inventory
doit faire un appel gRPC synchrone à chaque autorisation de stock ; dans
inventory-service, inventory apprend la notion de boutique. Les deux se défendent, mais
c'est un arbitrage, et il change le coût de F2.

---

## 5. §22 — `REVIEW_REPLY` n'a pas à attendre le lot D2

§17 dit « trou d'autorisation existant à faible coût de correction », et §22 le place en
**D2, derrière 0a, A, B et C**. Ces deux affirmations ne tiennent pas ensemble.

L'état actuel, cité du fichier lui-même
(`EngagementEndpoints.cs:98-113`) : « CETTE ROUTE RESTE OUVERTE À TOUT INSCRIT […]
n'importe qui peut faire dire n'importe quoi à un vendeur sous son propre avis ». Elle est
sur `MapAuthenticatedGroup` — **un acheteur peut l'appeler**. Le handler ne lit aucune
claim ; le command n'a pas de champ d'appelant.

Or la correction minimale n'a besoin d'aucun RBAC : `Review.SellerId` existe déjà
(`Review.cs:38`, posé à la création depuis la ligne de commande), et
`GetSellerByUserIdAsync` répond aujourd'hui pour le propriétaire. Le décompte réel :
~15 lignes de C# sur 5 fichiers, dont **deux `.csproj`** — car review-service ne référence
pas `HBA.Merchants.Contracts` et n'enregistre que le client gRPC Order. C'est la
dépendance nouvelle qui coûte, pas le code.

**Donc : corriger maintenant, en propriétaire seul, avant le lot A. Passer à
`HasPermission(…, REVIEW_REPLY)` en D2.** La dépendance de projet est de toute façon à
créer, autant qu'elle serve d'abord à fermer le trou.

---

## 6. Deux détails qui mordront

- **§7, la séquence serveur, oublie le contournement administrateur.** `MapSellerGroup`
  laisse passer `Admin` et `Moderator` autant que `Seller`, et toutes les gardes
  d'appartenance existantes commencent par `if (IsAdmin(user)) return null`
  (`MerchantEndpoints.cs:228`, `CatalogEndpoints.cs:346`). Si `HasPermission` devient le
  seul chemin sans reproduire ce raccourci, **les administrateurs perdent l'accès à tout
  le back-office vendeur** — et cela se verra en recette, pas en revue.
- **§4 ne mentionne pas le cache existant `sellers:by-user:{userId}`** (TTL 10 min,
  `SellerModuleApi.cs`, avec mémorisation des absences). Un membre ajouté puis
  immédiatement résolu tombe sur une entrée négative en cache. À invalider à chaque
  mutation d'appartenance, au même titre que `seller-access:*`.

---

## 7. Plan §22 corrigé

| Lot | Contenu | Dépend de |
|---|---|---|
| **0a** | Redis distribué sur `ICacheService` | — |
| **0b** | `auth_time` / `amr` + `POST /auth/reauthenticate` (identity) | — |
| **0c** | Propager et persister l'acteur (order, inventory, catalog) | — |
| **0d** | **`REVIEW_REPLY` : fermeture immédiate, propriétaire seul** | — |
| **A** | `SellerMember`, `Invitation`, `Role`, `Permission`, migrations | — |
| **B** | Outbox + événements d'appartenance + notifications d'invitation | A |
| **B′** | **Octroi et révocation du rôle `Seller` au membre (identity)** | B |
| **C** | `SellerAccess` gRPC + `CheckMerchantCapability` + cache Redis | 0a + A + **B′** |
| **D1** | Enforcement SELLER sur seller, catalog, order, inventory | C |
| **D2** | `REVIEW_REPLY` par `HasPermission` (remplace 0d) | C |
| **E** | UI HBAExpress Pro — membres, rôles, audit | A, D1 |
| **F1** | `StoreId` (nullable) sur `OrderLine` + `OrderSellerShare` | D1 |
| **F2** | Rattachement explicite boutique ↔ lieu — **propriétaire de la table à trancher** | D1 |
| **F3** | Cohérence Store/Offer sur les données de reprise | D1 |
| **G** | Enforcement STORE, service par service | F1/F2/F3 |
| **H** | Step-up sur les actions financières | 0b + C |

Deux ajouts (**0d**, **B′**), un changement de dépendance (**C** attend **B′**), une
nullabilité (**F1**), un arbitrage à rendre (**F2**), et le point §10/§11 à trancher avant
d'écrire le premier rôle.

Le reste de la V2 tient. On peut commencer.
