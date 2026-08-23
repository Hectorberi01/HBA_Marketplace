# SAGA MEMBRE VENDEUR — parcours reconstruits depuis le code

Périmètre : `services/marketplace/seller-service` (module `Members`),
`services/common/identity-service` (attribution du rôle `Seller`),
`services/common/notification-service` (courriel d'invitation),
et les cinq services appelants qui consomment `IMerchantAccessApi`
(catalog, inventory, order, review, payment/financial) — plus return-refund,
qui ne le consomme pas.

Tous les chemins sont relatifs à la racine du dépôt.

---

## 0. Les pièces du mécanisme

| Concept | Fichier | Ce qu'il porte |
|---|---|---|
| `SellerMember` | `Domain/Members/SellerMember.cs` | Appartenance d'un compte à UN vendeur. `MemberStatus{Invited,Active,Suspended,Revoked,Left}` (`:27-35`). `CanAct => Status == Active` (`:231`). `IsOwner => rôles contiennent OWNER` (`:237`). |
| `StoreMembership` | `SellerMember.cs:100-146` | Affectation à une boutique, avec ses propres rôles et son propre statut. |
| `SellerRole` | `Domain/Members/SellerRole.cs` | 9 rôles système à identifiants fixes (`:383-508`) + rôles personnalisés (`Custom`, `:128-160`). |
| `MerchantPermission` | `Domain/Members/MerchantPermission.cs:35-145` | 57 valeurs, projetées en table par `MerchantPermissions.Catalogue` (`:201-297`), avec risque et drapeau `OwnerOnly`. |
| `MemberActor` | `SellerMember.cs:878-1080` | L'acteur résolu : `Permissions` (union), `SellerLevelPermissions` (socle), `PermissionsByStore`. |
| `MemberAccess.For` | `SellerMember.cs:1082-1112` | Le **seul** endroit qui compose membre + rôles. |
| `SellerInvitation` | `Domain/Members/SellerInvitation.cs` | `InvitationStatus{Pending,Accepted,Declined,Expired,Revoked}`, durée 7 jours (`:86`), empreinte SHA-256 du jeton (`:103,122`). |

---

# 1. Le parcours nominal

## 1.1 — Le propriétaire naît avec le dossier

```
Point d'entrée: POST /api/v1/merchants · MapAuthenticatedGroup
                Api/Endpoints/MerchantEndpoints.cs:94, 515-521
États: (rien) → SellerMember{Status=Active, rôles=[OWNER]}
       Application/Sellers/Commands/RegisterSeller/RegisterSellerCommandHandler.cs:78-79
       Domain/Members/SellerMember.cs:248-256 (SellerMember.Owner)
Ce qui déclenche la suite: SellerRegisteredIntegrationEvent → GrantSellerRoleHandler (claim `Seller`)
Trace d'audit: OUI (SellersDbContext.KeepsAuditTrail = true)
Statut: COHERENT
```

L'appartenance `OWNER` est créée **dans la même transaction** que le dossier. Sans elle,
le propriétaire serait le seul compte de la plateforme sans droit sur sa propre boutique —
puisque toutes les routes d'équipe sont gardées par la résolution d'appartenance et non par
la colonne `Seller.UserId`.

## 1.2 — Invitation d'un membre

```
Point d'entrée: POST /api/v1/merchants/{sellerId}/members/invitations · MapSellerGroup + AllowIdempotency
                MerchantEndpoints.cs:224, 857-865
Garde: AUCUNE dans l'endpoint — délibéré (MerchantEndpoints.cs:199-218).
       La garde est MEMBER_INVITE dans la commande : MemberCommands.cs:173-177
       + délégation : SellerInvitation.Create → MemberActor.EnsureCanAssign (SellerMember.cs:1032-1080)
États: (rien) → SellerInvitation{Pending}, ExpiresOnUtc = now + 7 j (SellerInvitation.cs:86)
Ce qui déclenche la suite: SellerMemberInvitedIntegrationEvent (publié AVANT SaveChanges,
       donc dans la même transaction — MemberCommands.cs:304-309, 319-342)
       → SendSellerInvitationEmailHandler (notification-service/…/MemberEmailHandlers.cs:31-73)
Trace d'audit: OUI
Statut: COHERENT
```

Ce qui est fait correctement, et qui est rare :

- Le jeton est **32 octets de `RandomNumberGenerator`**, encodé base64url, et seule son
  empreinte SHA-256 est stockée (`Infrastructure/Security/InvitationTokens.cs:33-52`).
- Le jeton en clair n'entre dans **aucun** événement (`MemberCommands.cs:19-30`) et n'est
  **pas journalisé** (`MemberEmailHandlers.cs:63-70`) ; il n'est rendu qu'à l'appelant, une fois.
- Une seule invitation vivante par adresse et par vendeur (`MemberCommands.cs:181-191`) :
  deux jetons concurrents donneraient deux jeux de rôles selon le lien ouvert en premier.
- Le nom de la boutique voyage dans le courriel, parce qu'une invitation anonyme est
  indiscernable d'un hameçonnage (`MemberCommands.cs:311-316`).
- Le renvoi (`/invitations/{id}/resend`) **rejoue la délégation** : les rôles promis sont
  relus et confrontés à ce que détient le relanceur *maintenant*
  (`MemberCommands.cs:262-279` ; `SellerInvitation.Refresh` → `EnsureGouvernable` +
  `EnsureCanAssign`, `SellerInvitation.cs:365-445, 451-462`). Sans cela, `MEMBER_INVITE`
  seul aurait suffi à ressusciter une invitation `SELLER_ADMIN`.

## 1.3 — Création du compte d'identité, puis acceptation

```
Point d'entrée: POST /api/v1/merchants/invitations/accept · MapAuthenticatedGroup (PAS MapSellerGroup)
                MerchantEndpoints.cs:320-323, 888-893
Corps: { token } — AUCUN sellerId, AUCUNE adresse (MemberCommands.cs:48-59)
États: Invitation{Pending} → {Accepted} ; (rien) → SellerMember{Active}
       SellerInvitation.cs:254-301 ; SellerMember.FromInvitation (SellerMember.cs:345-365)
Ce qui déclenche la suite: SellerMemberJoinedDomainEvent → SellerMemberJoinedIntegrationEvent
       → GrantSellerRoleToMemberHandler (identity) qui pose la claim `Seller`
         common/identity-service/…/BusinessRoleGrantHandlers.cs
Trace d'audit: OUI
Statut: PARTIAL
```

L'invité n'a pas encore le rôle `Seller` : c'est pourquoi cette route est **la seule route
d'équipe hors de `MapSellerGroup`** — la poser dans le groupe vendeur rendrait l'acceptation
structurellement impossible (`MerchantEndpoints.cs:306-319`). L'exception est bornée : le
jeton *est* le secret, et l'adresse est relue chez identity.

Le **compte d'identité n'est pas créé par ce parcours** : on invite une adresse, et l'invité
« se connecte si le compte existe, le crée sinon » — le lien du courriel pointe vers le
parcours d'inscription standard (`AccountLinkBuilder.SellerInvitation`,
`MemberEmailHandlers.cs:20-27`). Il n'y a donc aucun couplage entre merchant et identity
pour la création de compte, ce qui est le bon découpage.

Contrôles réels de `AcceptInvitationCommand` (`MemberCommands.cs:378-460`) :
jeton haché avant recherche (`:387-388`) ; jeton inconnu et jeton révoqué rendent la même
erreur (`:390-396`) ; l'adresse du compte doit **correspondre exactement** à celle invitée
(`SellerInvitation.cs:289-294`) ; expiration vérifiée et **statut posé au passage**
(`:275-287`) ; l'invitation ne survit pas à son émetteur — si celui-ci a été révoqué ou
suspendu, l'acceptation est refusée (`MemberCommands.cs:422-430`) ; un rôle personnalisé
supprimé entre l'envoi et l'acceptation fait échouer plutôt qu'admettre avec moins de
droits que promis (`:437-442`).

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| M-1 | **MEDIUM** | **L'acceptation ne vérifie pas que l'adresse du compte est vérifiée.** `RegisterSellerCommandHandler` exige `user.EmailVerified` (`:44-47`) ; `AcceptInvitationCommand` lit `_identity.GetUserAsync` et n'en fait rien (`MemberCommands.cs:398-403`). Quelqu'un qui crée un compte avec l'adresse invitée sans confirmer la boîte entre dans l'équipe. Les deux portes du même vendeur n'ont pas la même exigence. | `MemberCommands.cs:398-403` vs `RegisterSellerCommandHandler.cs:44-47` |
| M-2 | **LOW** | `SellerInvitation.Decline` (`:303-322`) n'a **aucun appelant** : ni commande, ni route. Le statut `Declined` n'est jamais atteignable en production, et le message d'erreur `sellers.invitation.declined` (`:269`) est mort. | `grep -rn "\.Decline(" services/marketplace/seller-service` → 0 appelant |

## 1.4 — Attribution de rôles, de permissions, rattachement à une boutique

```
Point d'entrée: PUT    /{sellerId}/members/{memberId}/roles                · MEMBER_ASSIGN_ROLE  · MerchantEndpoints.cs:228, 895-902
                PUT    /{sellerId}/members/{memberId}/stores/{storeId}     · MEMBER_ASSIGN_STORE · :229, 904-911
                DELETE /{sellerId}/members/{memberId}/stores/{storeId}     · MEMBER_ASSIGN_STORE · :230, 913-919
                POST   /{sellerId}/members/{memberId}/suspend | activate   · MEMBER_SUSPEND      · :231-232
                DELETE /{sellerId}/members/{memberId}                      · MEMBER_REVOKE       · :233
                DELETE /{sellerId}/members/me                              · aucune permission   · :241, 840-845
                GET    /{sellerId}/roles · POST · PATCH · DELETE           · ROLE_VIEW/CREATE/UPDATE/DELETE · :246, 276-278
                GET    /api/v1/merchants/permissions                       · MapSellerGroup      · :301-304
                GET    /{sellerId}/members/audit                           · AUDIT_VIEW          · :296, 823-837
États: Invited/Active → Active ⇄ Suspended → Revoked | Left
       SellerMember.cs:380-431 (SetSellerRoles), 432-468 (AssignStore), 492-516 (Suspend),
                       518-555 (Reactivate), 557-582 (Revoke), 584-602 (Leave)
Ce qui déclenche la suite: SellerMemberRolesUpdated / StoreAssigned / StoreUnassigned /
       Suspended / Activated / Revoked (tous publiés — Application/Members/MemberEventHandlers.cs:29-190)
Trace d'audit: OUI, deux niveaux — AuditEntry par entité mutée (ModuleDbContext.cs:143-210)
       + journal lisible par le vendeur (GET /members/audit, AuditQueries.cs)
Statut: COHERENT
```

**Il n'existe pas d'attribution de permission à un membre.** Les permissions ne s'attachent
qu'à des **rôles** ; un membre porte des rôles, au niveau vendeur et/ou par boutique.
C'est explicite (`MerchantPermission.cs:7-19`) et c'est structurellement plus sûr : il n'y a
pas de chemin « permission directe » à garder en plus.

Le verrou du dernier propriétaire est pris **avant** le décompte, et c'est un verrou
consultatif PostgreSQL sur le vendeur, pas un `xmin` — parce que révoquer deux propriétaires
écrit **deux lignes différentes** et ne produit donc aucun conflit optimiste
(`MemberCommands.cs:652-690`, encadré). C'est un raisonnement juste et rare.

---

# 2. Parcours par rôle : chacun a-t-il des routes, et est-il refusé ailleurs ?

Rôles système : `SellerRole.cs:383-508`. Capacités exigées par les services :
`Contracts/MerchantCapabilities.cs`.

| Rôle | Routes réellement accessibles | Refus réels ailleurs | Verdict |
|---|---|---|---|
| **CATALOG_MANAGER** (`SellerRole.cs:434-448`) | catalog : `GET/POST /seller/products`, `PUT /products/{id}`, `POST /products/{id}/status`, variantes, médias, `POST /offers`, `PUT /offers/{id}/price`… — 20+ routes, toutes gardées par `PRODUCT_*`/`OFFER_*` et **cadrées par boutique** (`CatalogEndpoints.cs:439-506, 510-553`) | inventory 403 (`INVENTORY_*` absentes), order 403 (`ORDER_VIEW` absente), financial 403, membres 403 | **COHERENT** — sauf retours (voir §3) |
| **INVENTORY_MANAGER** (`:449-457`) | inventory : les 7 écritures + `GET /items/{id}`, `GET /items/sku/{sku}`, `POST /items/by-locations` (`InventoryEndpoints.cs:155-162`) ; catalog en lecture via `PRODUCT_VIEW` | catalog en écriture 403, order 403, financial 403 | **PARTIAL** — `INVENTORY_TRANSFER` et `STOCK_MOVEMENT_VIEW` n'ont **aucune route** (§3, M-8) ; aucun cadrage boutique (`InventoryEndpoints.cs:271-279` utilise `Can`, pas `CanInStore`) |
| **ORDER_MANAGER** (`:461-473`) | order : **une seule** route, `GET /api/sellers/{sellerId}/orders` (`OrderEndpoints.cs:119-120`) ; inventory et catalog en lecture | inventory en écriture 403 (pas d'`INVENTORY_ADJUST` — conforme au §24), financial 403, membres 403 | **BROKEN** — `ORDER_CONFIRM`, `ORDER_REJECT`, `ORDER_MARK_PREPARING`, `ORDER_MARK_READY` ne gardent rien : le rôle « Commandes et préparation » ne peut que lire |
| **CUSTOMER_SUPPORT** (`:475-486`) | review : `POST /api/engagement/reviews/{id}/reply`, gardée par `REVIEW_REPLY` via `HasCapabilityAsync` (`review-service/…/ReplyToReviewCommand.cs:84-88`) ; order en lecture | catalog en écriture 403, inventory 403, financial 403 | **PARTIAL** — `REVIEW_VIEW` ne garde **aucune** route (`grep` : la constante n'apparaît que dans `MerchantCapabilities.cs:79`) ; les 6 `RETURN_*` non plus |
| **FINANCE_MANAGER** (`:493-503`) | financial : portefeuille, transactions, versements, relevé, lignes de relevé, factures — 7 routes gardées par `FINANCE_VIEW`/`WALLET_VIEW`/`PAYOUT_VIEW` (`FinancialEndpoints.cs:134,141-143,172-174`) ; `GET /members/audit` par `AUDIT_VIEW` ; order en lecture | catalog 403, inventory 403, membres en écriture 403 ; **pas de `WITHDRAWAL_REQUEST` par défaut** (`SellerRole.cs:487-492`, choix explicite) | **COHERENT** |
| **STORE_ADMIN** (`:432-433`) | tout le périmètre boutique | `MEMBER_INVITE/SUSPEND/REVOKE` absents → 403 sur la composition d'équipe ; `PAYOUT_CONFIGURE`/`SELLER_CLOSE` `OwnerOnly` → jamais portables | **COHERENT** |
| **SELLER_ADMIN** (`:406-411`) | tout sauf `OwnerOnly` (`MerchantPermissions.All.Where(p => !p.IsOwnerOnly())`) | ne peut pas repointer le compte de versement, ni fermer le dossier, ni transférer la propriété | **COHERENT** |
| **EMPLOYEE** (`:501-508`) | trois lectures : `ORDER_VIEW`, `PRODUCT_VIEW`, `INVENTORY_VIEW` | tout le reste 403 | **COHERENT** |

---

# 3. Les six scénarios, preuve de code à l'appui

## 3.1 — Un membre rattaché à la boutique A peut-il agir sur la boutique B ?

**Réponse : cela dépend du service, et l'écart est documenté.**

Le mécanisme : `MemberActor.HasInStore(storeId, permission)` ne retient que le **socle
vendeur** plus les rôles de **la boutique visée** ; une boutique inconnue du dictionnaire
retombe sur le socle et **jamais** sur l'union (`SellerMember.cs:963-973`). Le socle est
**transporté**, jamais recalculé par intersection — l'encadré explique pourquoi
l'intersection serait un trou (`SellerMember.cs:1096-1111`).

| Service | Appel réel | Cadré ? |
|---|---|---|
| seller-service (`/stores/{storeId}/…`) | `acteur.HasInStore(boutique, capacite)` quand la route nomme une boutique (`MerchantEndpoints.cs:428-435`) | **OUI** |
| catalog (produits) | `acces.CanInStore(produit.StoreId, capacite)` (`CatalogEndpoints.cs:489-492`) ; création : `storeId` **obligatoire** pour un membre cadré (`:1146-1160`) | **OUI** |
| catalog (offres) | `acces.CanInStore(offer.StoreId, capacite)`, `StoreId` non nullable (`CatalogEndpoints.cs:528-537`) | **OUI** |
| inventory | `acces.Can(capacite)` — **pas** `CanInStore` (`InventoryEndpoints.cs:271-279`) | **NON** |
| order | `acces.Can(MerchantCapabilities.OrderView)` (`OrderEndpoints.cs:297`) | **NON** |
| review | `HasCapabilityAsync(..., storeId: null, ...)` → `CanInStore(null,…)` = `Can` | **NON** (un avis ne porte pas de boutique) |
| return-refund | rien du tout | **NON** |

Le garde-fou compensatoire existe et fonctionne : `EnsurePorteeBoutiqueAsync`
(`MemberCommands.cs:794-844`) **refuse** d'attacher à une boutique un rôle portant une
permission non cadrable (`MerchantPermissions.StoreScoped`, `MerchantPermission.cs:405-418`)
dès que le vendeur a **plus d'une** boutique, et le refus **nomme la permission fautive**.
Le pendant est dans `CreateStoreCommand` : ouvrir une deuxième boutique est refusé tant
qu'un membre actif porte un rôle non cloisonnable (`Application/Stores/StoreCommands.cs:103-140`).

`[MEDIUM]` Ce filet ne couvre **que** les rôles attribués **via une affectation boutique**
(`MemberCommands.cs:797-814`). Un rôle donné **au niveau vendeur** — c'est-à-dire par
`PUT /members/{memberId}/roles`, qui ne fait volontairement aucun contrôle de portée
(`MemberCommands.cs:474-482`) — donne `INVENTORY_ADJUST` sur tous les entrepôts de toutes
les boutiques. C'est assumé (« un droit donné à ce niveau est un choix explicite ») mais
c'est le chemin le plus court pour élargir sans le vouloir.

## 3.2 — Un membre du vendeur A peut-il agir chez le vendeur B ?

**Réponse : non partout, sauf dans return-refund-service, où il le peut entièrement.**

Chaînes de refus vérifiées :

- `MemberAccessResolver.ResolveAsync(sellerId, userId)` interroge
  `GetMembershipAsync(sellerId, userId)` — le couple, pas l'un ou l'autre
  (`MemberAccessResolver.cs:51-56` ; `Infrastructure/Persistence/MemberRepositories.cs`).
- `MerchantAccessApi.HasCapabilityAsync` refuse d'emblée si `acces.SellerId != sellerId`,
  avec l'encadré du §36 : « le `sellerId` de l'appelant est vérifié, jamais accepté »
  (`MerchantAccessApi.cs:69-77`).
- Dans le domaine, `EnsureCanAdminister` compare `acteur.SellerId != SellerId` **avant tout
  autre contrôle** et rend « introuvable » plutôt qu'« interdit »
  (`SellerMember.cs:794-803`) ; `MuterAsync` refait le contrôle en amont
  (`MemberCommands.cs:638-646`) ; `EnsureGouvernable` fait de même pour les invitations
  (`SellerInvitation.cs:451-462`).
- `EnsureCanAssign` refuse un rôle personnalisé appartenant à un autre vendeur
  (`SellerMember.cs:1047-1051`).
- `AssignMemberStoreCommand` vérifie que la boutique appartient bien au vendeur visé
  (`MemberCommands.cs:490-499`).
- catalog : `produit.SellerId != acces.SellerId` → 404 (`CatalogEndpoints.cs:455-460`).
- inventory : `location.OwnerId != acces.SellerId` → 404, et `OwnerId is null` est un
  **refus explicite** (`InventoryEndpoints.cs:270-279`).
- order : `acces.SellerId != sellerId` → 403 (`OrderEndpoints.cs:288-295`).
- financial : idem, capacité **puis** step-up (`FinancialEndpoints.cs:675-745`).

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| M-3 | **CRITICAL** | **Dans return-refund-service, un membre du vendeur A agit sur les dossiers du vendeur B.** Le service ne référence même pas `IMerchantAccessApi` (`grep -rn "IMerchantAccessApi" services/marketplace/return-refund-service` → **0**). `ListAsync(Guid sellerId, …)` lie le `sellerId` **depuis la query string** ; les sept autres routes ne transportent que le `ReturnId` et aucun handler ne compare `request.SellerId` à l'appelant. Un `CATALOG_MANAGER` — qui ne porte aucune permission `RETURN_*` — approuve, rejette, inspecte et **décide le remboursement** du dossier d'un concurrent. Le seul filtre est la claim `Seller` de `MapSellerGroup`. | `Api/Endpoints/SellerReturnsEndpoints.cs:14, 26-27, 33-52` ; `Application/Commands/ReturnLifecycleCommands.cs:34-47, 164-182` |
| M-4 | **MEDIUM** | **Un compte ne peut appartenir qu'à une seule équipe.** `GetActiveMembershipByUserAsync(userId)` rend **une** appartenance (`MemberRepositories.cs:36-47`), la plus ancienne. La limite est documentée (`SellerMember.cs:1123-1132` : « le §55 vise plusieurs organisations par compte, et cette signature ne le permet pas »). Conséquence concrète : un comptable indépendant invité par deux commerçants n'accède qu'au premier — et le second lui envoie des invitations qui aboutissent en base sans lui ouvrir quoi que ce soit chez les cinq services appelants. | `MemberRepositories.cs:36-47` ; `MerchantAccessApi.cs:88-97` |

## 3.3 — La suspension d'un membre est-elle immédiate, ou un cache continue-t-il de l'autoriser ?

**Réponse : la suspension d'un MEMBRE est immédiate. Le retrait d'un droit à un RÔLE ne
l'est pas — il prend jusqu'à 2 minutes. Et la suspension d'un VENDEUR n'a aucun effet
sur l'autorisation.**

**Où est le cache** : un seul endroit, `MerchantAccessApi.GetAccessAsync`
(`Infrastructure/Public/MerchantAccessApi.cs:45-58`), clé `sellers:access:{userId}`
(`Application/SellersCacheKeys.cs:65`). Le client gRPC des cinq services appelants
**ne cache rien** — c'est écrit et motivé : « dans un groupe de consommateurs, une SEULE
réplique reçoit le message d'invalidation »
(`shared/contracts/HBA.Merchants.Contracts.Grpc/MerchantsGrpc.cs:472-484`).

**Quelle durée** : `MemberAccessTtl = 2 minutes` (`SellersCacheKeys.cs:85`) ;
cache négatif `MissTtl = 30 secondes` (`:42`). Support : Redis quand
`Redis:ConnectionString` est renseigné, sinon `AddDistributedMemoryCache` **par processus**
avec un avertissement explicite au démarrage
(`shared/common/HBA.Shared.Infrastructure/DependencyInjection.cs:172-201`).

**Qui l'invalide** : `SellersDbContext.SaveChangesAsync` collecte les clés **avant**
d'écrire et les évince **après** (`:159-169`) ; `CollectCacheKeysToEvict` ajoute
`sellers:access:{userId}` pour **toute** entrée `SellerMember` en `Added|Modified|Deleted`
(`:235-255`). Une suspension, une révocation, un départ et un changement de rôles d'un
membre passent donc tous par là.

Ce qui n'est **pas** évincé, et le trou que cela laisse :

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| M-5 | **HIGH** | **Retirer une permission d'un RÔLE met jusqu'à 2 minutes à mordre.** `CollectCacheKeysToEvict` n'inspecte que `Seller`, `SellerMember` et `KybDocument` (`SellersDbContext.cs:215-280`) — **jamais `SellerRole`**. `PATCH /roles/{roleId}` remplace la liste des permissions en bloc (`SellerRoleCommands.cs:241`) ; les entrées `sellers:access:*` de tous les porteurs restent servies jusqu'au TTL. Le défaut est **connu et assumé** (`SellersCacheKeys.cs:70-84` : « ce TTL est le délai maximal de propagation d'un changement de RÔLE »), et il n'est acceptable que parce que la révocation d'un membre, elle, est immédiate. Un vendeur qui découvre qu'un rôle est trop large et le corrige laisse deux minutes ouvertes ; il n'existe aucun moyen de forcer la coupure autrement qu'en suspendant chaque porteur. | `SellersDbContext.cs:199-280` (aucune boucle `Entries<SellerRole>()`) ; `SellersCacheKeys.cs:70-85` |
| M-6 | **CRITICAL** | **La suspension d'un VENDEUR n'affecte pas l'autorisation de son équipe.** `MerchantAccess` ne porte pas le statut du vendeur (`Contracts/MerchantAccess.cs`), `ResoudreAsync` ne lit jamais l'agrégat `Seller` (`MerchantAccessApi.cs:88-121`), et `HasCapabilityAsync` ne compare que l'appartenance et la capacité (`:60-86`). Un vendeur `Suspended` — y compris suspendu par un rejet de KYB (`Seller.cs:413-420`) — continue, lui et toute son équipe, de créer des produits, de les publier, d'ajuster son stock et de demander des retraits. Le seul geste qui vérifie `Status == Active` est la création d'une boutique (`Application/Stores/StoreCommands.cs:96-101`). | `MerchantAccessApi.cs:60-121` ; `Contracts/MerchantAccess.cs` ; `SellersDbContext.cs:215-221` (une mutation de `Seller` évince `sellers:seller:*` et `sellers:by-user:*`, jamais `sellers:access:*` — sans conséquence ici, puisque la clé ne contient pas le statut) |
| M-7 | **MEDIUM** | **Hors Redis, l'éviction est locale au processus.** `AddDistributedMemoryCache` est le repli par défaut (`DependencyInjection.cs:183`) ; la promesse « après suspension, toutes les répliques refusent sans attendre le TTL » (`SellersCacheKeys.cs:59-63`) est donc **conditionnelle à une configuration**. L'avertissement au démarrage existe (`DependencyInjection.cs:176-181`) mais rien n'empêche un déploiement de production sans `Redis:ConnectionString`. | `DependencyInjection.cs:172-201` |

Note : la claim `Seller` du jeton n'est pas révoquée à la suspension, et c'est **délibéré
et correct** — un jeton déjà émis franchit `MapSellerGroup`, puis se fait refuser par la
résolution d'appartenance avec un motif lisible (`sellers.member.not_active`,
`MemberAccessResolver.cs:65-69`). Révoquer les sessions déconnecterait le compte de toute
la plateforme, achats compris (`BusinessRoleGrantHandlers.cs`, encadré de `RevokeAsync`).

## 3.4 — Le dernier propriétaire peut-il être supprimé ou perdre son rôle ?

**Réponse : non, sur les quatre chemins. Mais la conséquence est un cul-de-sac.**

| Chemin | Garde | Preuve |
|---|---|---|
| `POST /members/{id}/suspend` | `EnsureNotLastOwner(estDernierProprietaire)` | `SellerMember.cs:500-504, 836-845` |
| `DELETE /members/{id}` (révocation) | idem | `SellerMember.cs:565-569` |
| `DELETE /members/me` (départ volontaire) | idem, décompte pris sous verrou | `SellerMember.cs:586-590` ; `MemberCommands.cs:567-575` |
| `PUT /members/{id}/roles` | `if (IsOwner && !roles.Any(r => r.Id == OwnerId))` → refus | `SellerMember.cs:394-400` |

Deux protections supplémentaires, réelles :

- **Le propriétaire n'est administrable que par un propriétaire** (`SellerMember.cs:818-828`) :
  un `SELLER_ADMIN`, qui porte pourtant `MEMBER_REVOKE`, ne peut pas révoquer le propriétaire
  et rester seul aux commandes. C'est l'escalade la plus courte du module, et elle est fermée.
- **Le décompte est sérialisé par un verrou consultatif PostgreSQL** pris **avant** la lecture
  (`MemberCommands.cs:652-690`). L'encadré explique pourquoi `xmin` ne suffisait pas :
  révoquer deux propriétaires écrit deux lignes distinctes, il n'y a aucun conflit optimiste
  à détecter, et le vendeur tombait à zéro propriétaire actif — état définitivement
  inadministrable puisque `EnsureCanAdminister` exige `acteur.IsOwner` pour toucher un
  propriétaire. Le raisonnement est correct.

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| M-8 | **HIGH** | **La propriété ne peut jamais être transférée, donc le rôle OWNER ne peut jamais quitter son porteur.** Les quatre gardes ci-dessus renvoient toutes vers « transférez la propriété d'abord » (`SellerMember.cs:396-399, 842-844`) et `EnsureCanAssign` verrouille explicitement le rôle OWNER (`SellerMember.cs:1054-1061`). Or `OWNERSHIP_TRANSFER` (`MerchantPermission.cs:142, 291`) **ne garde aucune route** : `grep -rn "OwnershipTransfer\|OWNERSHIP_TRANSFER"` ne renvoie que sa déclaration et `MerchantCapabilities`. Aucune commande, aucun endpoint. Un vendeur dont le propriétaire quitte l'entreprise, perd son compte ou décède n'a **aucun** chemin : le dossier reste administré par un compte inaccessible, et `SELLER_CLOSE` étant `OwnerOnly`, il ne peut même pas être fermé. | `grep -rn "OwnershipTransfer"` → `MerchantPermission.cs:142,291`, `MerchantCapabilities.cs` uniquement ; `SellerMember.cs:388-400, 1054-1061` |

## 3.5 — Un membre peut-il s'attribuer une permission qu'il n'a pas ?

**Réponse : non. C'est la partie la mieux construite du module.**

Quatre verrous indépendants :

1. **On ne s'administre pas soi-même.** `EnsureCanAdminister` refuse `acteur.Id == Id`
   (`SellerMember.cs:811-816`). Il n'existe aucune route où un membre modifie ses propres
   rôles.
2. **On ne donne que ce qu'on a.** `MemberActor.EnsureCanAssign` compare chaque permission
   du rôle visé au **périmètre exact** de l'acteur (`SellerMember.cs:1032-1080`), et l'encadré
   décrit précisément l'attaque fermée : « M est STORE_ADMIN sur la seule boutique A. Son
   union contient STORE_UPDATE. Il invite N en lui donnant STORE_ADMIN AU NIVEAU DU VENDEUR.
   […] M vient de fabriquer un compte qui administre une boutique sur laquelle il n'a
   lui-même aucun droit. » Le référentiel est donc `SellerLevelPermissions` pour une
   attribution vendeur, et `PermissionsByStore[B]` pour une attribution sur B.
3. **Les permissions `OwnerOnly` ne peuvent être portées par aucun rôle**, même personnalisé :
   `SellerRole.EnsureDelegatable` les refuse d'entrée (`SellerRole.cs:326-337`), et la table
   est la seule source du drapeau (`MerchantPermission.cs:196-199, 342-354`). Concernées :
   `PAYOUT_CONFIGURE`, `BANK_ACCOUNT_UPDATE`, `SELLER_CLOSE`, `SELLER_REACTIVATE`,
   `OWNERSHIP_TRANSFER`, `SECURITY_POLICY_UPDATE`.
4. **Le rôle OWNER ne s'attribue pas par ce chemin** (`SellerMember.cs:1054-1061`),
   et un rôle personnalisé d'un autre vendeur est « introuvable » (`:1046-1051`).

Un ajout à l'énumération sans ligne au catalogue fait **échouer le constructeur statique**,
donc le démarrage du service (`MerchantPermission.cs:310-331`) : l'oubli du drapeau
`OwnerOnly` sur une permission neuve est structurellement impossible.

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| M-9 | **MEDIUM** | **La création et la mise à jour d'un rôle comparent à l'UNION, l'attribution au SOCLE.** `CreateSellerRoleCommand` passe `acteur.Value.Permissions` (`SellerRoleCommands.cs:150`), `UpdateSellerRoleCommand` passe `acteur.Permissions` (`:241`), `EnsureDeletable` aussi (`:274`) — c'est-à-dire l'union tous périmètres confondus, exactement le référentiel que `EnsureCanAssign` a cessé d'utiliser pour la raison écrite dans son encadré. Un `STORE_ADMIN` de la boutique A peut donc **fabriquer** un rôle portant `STORE_UPDATE`. Il ne peut pas l'attribuer au niveau vendeur (le socle l'arrête) ni sur la boutique B (`PermissionsByStore[B]` l'arrête), donc l'escalade ne se referme pas aujourd'hui — mais les deux moitiés du module ne se règlent plus sur la même horloge, et c'est le genre d'écart qui devient exploitable au prochain chemin d'attribution ajouté. | `SellerRoleCommands.cs:150, 241, 274` vs `SellerMember.cs:1032-1043` |
| M-10 | **MEDIUM** | **`ROLE_ASSIGN` ne garde rien.** Déclarée `Sensitive` (`MerchantPermission.cs:115, 263`), elle est doublonnée par `MEMBER_ASSIGN_ROLE`, seule utilisée (`SellerMember.cs:382`). Elle figure au catalogue rendu par `GET /permissions`, se coche, et ne change rien. | `grep -rn "RoleAssign\|ROLE_ASSIGN"` → déclaration seule |

## 3.6 — Les actions critiques exigent-elles une ré-authentification ?

**Réponse : oui pour les deux routes de seller-service et la route de retrait, non ailleurs —
et les routes d'équipe ne passent par aucun contrôle de step-up.**

Le mécanisme est solide (`shared/common/HBA.Shared.Hosting/Http/StepUpAuthentication.cs`) :
claim OIDC `auth_time` recopié par le rafraîchissement de jeton depuis la session d'origine —
donc un client qui rafraîchit toutes les quatre minutes ne reste **pas** éternellement
« fraîchement authentifié » (`:20-27`) ; fenêtre de 5 minutes **non configurable par service**
(`:47-63`) ; claim absent = **refus** (`:29-34, 90-93`) ; `auth_time` dans le futur au-delà
d'une minute de dérive = refus (`:81-98`).

Couverture réelle :

| Capacité `Critical` | Route existante | Step-up appliqué ? |
|---|---|---|
| `PAYOUT_CONFIGURE` | `PUT /api/v1/merchants/{sellerId}/payout-account` | **OUI** — `MerchantEndpoints.cs:465-468`, après le contrôle de capacité (l'ordre est motivé : `:453-458`) |
| `SELLER_CLOSE` | `POST /api/v1/merchants/{sellerId}/close` | **OUI** — même garde |
| `WITHDRAWAL_REQUEST` | `POST /api/financial/wallets/sellers/{sellerId}/withdrawals` | **OUI** — `FinancialEndpoints.cs:738-743` |
| `BANK_ACCOUNT_UPDATE` | *aucune* | sans objet |
| `OWNERSHIP_TRANSFER` | *aucune* | sans objet |
| `SECURITY_POLICY_UPDATE` | *aucune* | sans objet |

Catalog et inventory appellent `MerchantCapabilities.RequiresStepUp` alors qu'aucune de
leurs capacités n'est critique — délibérément, pour que la garde ne diverge pas
(`CatalogEndpoints.cs:494-504` ; `InventoryEndpoints.cs:289-293`).

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| M-11 | **MEDIUM** | **Aucune route d'équipe ni de rôle ne traverse un contrôle de step-up.** Les treize routes `/members/…` et les quatre routes `/roles/…` sont volontairement hors de `DenyUnlessOwnSellerAsync` (`MerchantEndpoints.cs:199-218, 260-267`), qui est le seul endroit de seller-service où `HasRecentAuthentication()` est consulté. Leurs commandes contrôlent l'appartenance et la permission, jamais la fraîcheur d'authentification. Aucune permission d'équipe n'est aujourd'hui `Critical`, donc rien ne fuit — mais promouvoir `MEMBER_REVOKE` ou `ROLE_UPDATE` au rang critique n'aurait **aucun effet** sur ces routes, silencieusement, et c'est exactement le mode de défaillance que les encadrés de catalog et inventory décrivent pour justifier leur ligne inutile. | `MerchantEndpoints.cs:465-468` (unique occurrence) ; `MemberCommands.cs` et `SellerRoleCommands.cs` (aucune occurrence de `HasRecentAuthentication`) |
| M-12 | **LOW** | **`SECURITY_POLICY_UPDATE` est déclarée sans objet**, l'encadré le dit lui-même : « sans objet tant que `seller_security_policies` n'existe pas » (`MerchantPermission.cs:293-294`). Elle est portable et cochable. | idem |

---

# 4. Synthèse

| Étape | Statut |
|---|---|
| Propriétaire → membre OWNER du dossier | **COHERENT** |
| Invitation (jeton, empreinte, unicité, renvoi gardé) | **COHERENT** |
| Création du compte d'identité + acceptation | **PARTIAL** (adresse non vérifiée exigée à l'inscription vendeur mais pas à l'acceptation) |
| Membre ACTIF | **COHERENT** |
| Attribution de rôles | **COHERENT** |
| Attribution de permissions | **sans objet** — il n'existe pas d'attribution directe ; tout passe par les rôles, ce qui est plus sûr |
| Rattachement à une boutique | **PARTIAL** (le cadrage est réel dans catalog et seller-service, absent dans inventory, order, review et returns) |
| Parcours CATALOG_MANAGER | **COHERENT** |
| Parcours INVENTORY_MANAGER | **PARTIAL** (2 permissions sans route) |
| Parcours ORDER_MANAGER | **BROKEN** (4 permissions sans route ; lecture seule) |
| Parcours CUSTOMER_SUPPORT | **PARTIAL** (`REVIEW_VIEW` et les 6 `RETURN_*` sans garde) |
| Parcours FINANCE_MANAGER | **COHERENT** |

| Scénario | Réponse |
|---|---|
| Boutique A → boutique B | Refusé dans catalog et seller-service (`CanInStore`) ; **possible** dans inventory, order, review et returns (`Can` ou rien). |
| Vendeur A → vendeur B | Refusé partout **sauf return-refund-service**, qui n'a aucune autorisation. |
| Suspension d'un membre | **Immédiate** (éviction Redis dans la même transaction). Le retrait d'un droit à un **rôle** : jusqu'à 2 min. La suspension du **vendeur** : sans aucun effet. |
| Dernier propriétaire | **Ne peut ni être retiré ni perdre son rôle** — sur les quatre chemins, sous verrou. Mais la propriété ne peut jamais être transférée : le dossier devient inadministrable si le propriétaire disparaît. |
| Élévation de privilège | **Fermée** : pas d'auto-administration, `EnsureCanAssign` compare au périmètre exact, `OwnerOnly` inattribuable, OWNER verrouillé. Réserve : création/modification de rôle comparent à l'union, pas au socle. |
| Ré-authentification | **Oui** pour `PAYOUT_CONFIGURE`, `SELLER_CLOSE`, `WITHDRAWAL_REQUEST` ; les trois autres capacités critiques n'ont pas de route ; **aucune** route d'équipe ni de rôle ne la contrôle. |
