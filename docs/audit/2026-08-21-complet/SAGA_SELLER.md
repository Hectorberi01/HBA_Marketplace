# SAGA VENDEUR — parcours reconstruits depuis le code

Périmètre : `services/marketplace/{seller,catalog,inventory,order,return-refund}-service`,
`services/common/{payment,wallet,billing,identity,review,notification}-service`,
`shared/common/HBA.Shared.Hosting`, `shared/contracts/HBA.Merchants.Contracts.Grpc`.

Tous les chemins sont relatifs à la racine du dépôt. Les numéros de ligne renvoient
à l'état lu (`/root/audit-src`).

---

## Rappel du mécanisme d'autorisation (il gouverne les six parcours)

| Élément | Fichier | Ce qu'il fait réellement |
|---|---|---|
| `MapSellerGroup` | `shared/common/HBA.Shared.Hosting/Http/ApiAuthorization.cs:155-157` | `RequireRole("Seller","Admin","Moderator")`. **Ne dit pas QUEL vendeur.** Première barrière seulement. |
| `MapAdminGroup` | `…/ApiAuthorization.cs:60-62` | `RequireRole("Admin","Moderator")`. |
| `MemberAccessResolver.ResolveAsync` | `seller-service/…/Application/Members/MemberAccessResolver.cs:48-74` | `(sellerId, userId)` → `MemberActor` ; refuse si pas membre (`sellers.member.not_a_member`) ou membre non actif (`sellers.member.not_active`). Garde interne à seller-service. |
| `MerchantAccessApi.GetAccessAsync` / `HasCapabilityAsync` | `seller-service/…/Infrastructure/Public/MerchantAccessApi.cs:45-86` | `userId` → `MerchantAccess` (SellerId, permissions, boutiques). Compare `acces.SellerId != sellerId` avant toute capacité. **C'est le seul point de cache.** |
| Cache | `…/Application/SellersCacheKeys.cs:65-85` + `…/Infrastructure/Persistence/SellersDbContext.cs:159-255` | `sellers:access:{userId}`, TTL **2 min**, Redis (`shared/common/HBA.Shared.Infrastructure/DependencyInjection.cs:186-201`), évincé dans le même `SaveChangesAsync` que toute mutation de `SellerMember`. |
| Step-up | `shared/common/HBA.Shared.Hosting/Http/StepUpAuthentication.cs:63,88-99` | claim OIDC `auth_time`, fenêtre 5 min, claim absent = refus. |
| Journal d'audit | `shared/common/HBA.Shared.Infrastructure/Persistence/ModuleDbContext.cs:61,143-210` | `KeepsAuditTrail` : **vrai uniquement** dans `SellersDbContext.cs:84`, `ReturnRefundDbContext.cs:28`, `MealOrderingDbContext.cs:45`. **Faux** pour catalog, inventory, order, wallet. |

---

# A. Intégration d'un vendeur

## A.1 — Le compte utilisateur devient vendeur

```
Point d'entrée: POST /api/v1/merchants  ·  MapAuthenticatedGroup (aucun rôle exigé)
                seller-service/src/HBA.Merchants.Api/Endpoints/MerchantEndpoints.cs:92-94, 515-521
États: (aucun dossier) → Seller{Status=Pending, KybStatus=NotStarted}
       Domain/Sellers/Seller.cs:20-33, 128-149 ; Enums.cs:4-23
Ce qui déclenche la suite: SellerRegisteredDomainEvent → SellerRegisteredIntegrationEvent
       → GrantSellerRoleHandler (identity) qui pose la claim `Seller`
         common/identity-service/…/Users/EventHandlers/BusinessRoleGrantHandlers.cs
Trace d'audit: OUI — SellersDbContext.KeepsAuditTrail=true, acteur = jeton
Statut: COHERENT
```

Contrôles réels dans `Application/Sellers/Commands/RegisterSeller/RegisterSellerCommandHandler.cs:37-84` :
compte existant (`IIdentityModuleApi.GetUserAsync`), **e-mail vérifié obligatoire** (`:44-47`),
un seul dossier par compte (`:49-52`), nom de boutique unique (`:54-57`).
Le handler crée aussi, dans la même transaction, l'appartenance `OWNER`
(`:78-79`, `SellerMember.Owner`) — sans quoi le propriétaire n'aurait aucun droit sur son
propre dossier.

L'exception d'ouverture est justifiée et bornée : `POST /` et `GET /me` sont les deux
seules routes hors `MapSellerGroup`, et aucune ne lit d'identifiant de vendeur dans l'URL
(`MerchantEndpoints.cs:75-94`).

**Défaut** : `RegisterSellerRequest` accepte `CommissionRate` **depuis le corps de la requête**
(`MerchantEndpoints.cs:519-521, 948`) ; `Seller.Register` ne la contrôle qu'entre 0 et 1
(`Seller.cs:141-144`). Un vendeur s'inscrit donc à `commissionRate: 0`.
La colonne est morte (`Seller.cs:56-86`, jamais relue, la vraie commission vient du
moteur de règles de Billing) — le dommage est aujourd'hui nul, mais un champ financier
accepté du client sans garde est exactement le type de champ qu'on rebranche par erreur.
`[MEDIUM] champ financier accepté du client — MerchantEndpoints.cs:519-520`

## A.2 — KYB : dépôt, soumission, décision

```
Point d'entrée: POST   /{sellerId}/kyb/documents        · KYB_MANAGE   · MerchantEndpoints.cs:116, 575-580
                DELETE /{sellerId}/kyb/documents/{id}   · KYB_MANAGE   · :117, 587-592
                POST   /{sellerId}/kyb/submit           · KYB_MANAGE   · :139, 594-599
                POST   /{sellerId}/kyb/approve          · MapAdminGroup· :166, 601-602
                POST   /{sellerId}/kyb/reject           · MapAdminGroup· :167, 604-606
États: NotStarted → InReview → Verified | Rejected → (nouveau dépôt) → InReview
       Domain/Sellers/Seller.cs:181-234 (Add), 255-286 (Submit), 297-341 (Remove),
                                 344-360 (Approve), 382-423 (Reject)
Ce qui déclenche la suite: SellerKybSubmitted / SellerKybVerified / SellerKybRejected
                           + KybDocumentRemoved → media-service (effacement du fichier)
Trace d'audit: OUI (schéma sellers)
Statut: PARTIAL
```

Ce qui est correct et vérifié dans le code :

- La bascule automatique en `InReview` au premier dépôt est **conservée et documentée
  comme dépréciée** (`Seller.cs:205-231`) ; `SubmitKyb` existe et est idempotent
  (`Seller.cs:255-286`).
- Retirer la dernière pièce ramène un dossier `InReview` à `NotStarted` (`Seller.cs:321-324`) :
  pas de dossier vide dans la file d'administration.
- `RejectKyb` exige qu'il y ait eu quelque chose à examiner (`Seller.cs:390-398`),
  conserve le motif (`KybRejectionReason`, `:400-401`) et l'efface au dépôt suivant
  (`:200-203`) — le chemin de correction existe donc réellement.
- `ApproveKyb` refuse un dossier sans pièce (`Seller.cs:346-349`).
- La suppression d'une pièce émet `KybDocumentRemovedDomainEvent` pour que le fichier
  soit effacé chez media (`Seller.cs:326-338`), et `MarkForDeletion` en émet un **par pièce**
  (`Seller.cs:670-693`).

Défauts :

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| A-1 | **CRITICAL** | **Le rejet du KYB d'un vendeur actif ne retire rien de la vente.** `RejectKyb` appelle `Suspend(...)` sur un vendeur `Active` (`Seller.cs:413-420`) et l'encadré affirme « la suspension emprunte le chemin bâti pour l'exploitation : événement, **retrait du catalogue**, motif lisible sur chaque fiche » (`Seller.cs:405-411`). C'est faux : `SellerSuspendedIntegrationEvent` n'a **qu'un seul consommateur dans tout le dépôt**, et c'est une notification. Aucun handler dans catalog. Le vendeur reste `Suspended` en base et continue de vendre. | `Seller.cs:405-420` ; `Contracts/IntegrationEvents/SellerIntegrationEvents.cs:58` ; seul consommateur : `common/notification-service/…/SellerLifecycleNotificationHandlers.cs:86` ; `catalog-service/…/CatalogModuleInstaller.cs:259-260` n'enregistre que `SellerClosed` et `SellerDeleted` |
| A-2 | **HIGH** | **`SellerKybSubmittedIntegrationEvent` n'a aucun consommateur.** L'encadré de `SubmitKyb` motive son idempotence par « réémettre relancerait une notification à l'administrateur » (`Seller.cs:247-252`) : cette notification n'existe pas. La file d'administration ne se remplit que si un humain ouvre `GET /api/v1/merchants?kybStatus=InReview`. Idem pour `SellerKybApprovedIntegrationEvent`. | `grep -r "IIntegrationEventHandler<SellerKybSubmittedIntegrationEvent>"` → 0 ; idem `SellerKybApproved` ; contrats déclarés `SellerIntegrationEvents.cs:145,167` |
| A-3 | **MEDIUM** | **L'appartenance du média n'est vérifiée nulle part.** `Seller.AddKybDocument` documente explicitement que le contrôle « est de l'appelant » (`Seller.cs:188-193`), mais `AddKybDocumentCommandHandler` ne fait aucun appel à media-service. Un `mediaId` quelconque est accepté. | `Seller.cs:188-194` ; `Application/Sellers/Commands/AddKybDocument/AddKybDocumentCommandHandler.cs` (aucune référence à un client média) |

## A.3 — Coordonnées de reversement puis activation

```
Point d'entrée: PUT  /{sellerId}/payout-account · PAYOUT_CONFIGURE (Critical, OwnerOnly)
                                                · MerchantEndpoints.cs:115, 567-573
                POST /{sellerId}/activate       · MapAdminGroup · :168, 608-609
États: Pending + KybStatus=Verified + PayoutAccount≠null → Active
       Seller.cs:432-447
Ce qui déclenche la suite: SellerActivatedDomainEvent → SellerActivatedIntegrationEvent
Trace d'audit: OUI
Statut: PARTIAL
```

`Activate()` exige KYB validé **et** compte de reversement (`Seller.cs:434-442`).
`PAYOUT_CONFIGURE` est `Critical` + `OwnerOnly` (`Domain/Members/MerchantPermission.cs:277`),
donc soumise au step-up (`MerchantEndpoints.cs:465-468`) — la route la plus rentable pour un
attaquant est la mieux gardée. C'est cohérent.

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| A-4 | **MEDIUM** | `SellerActivatedIntegrationEvent` n'est consommé que par notification-service. Les encadrés de `Seller.cs:100-113` et `:536-547` justifient l'existence de `SuspendedFromStatus` par « un consommateur qui attend l'activation pour **ouvrir un portefeuille** ou autoriser la mise en vente ne voyait jamais ce vendeur ». Ce consommateur n'existe pas : le portefeuille vendeur est créé paresseusement au premier crédit (`wallet-service/…/Wallets/WalletMutations.cs:263-280`). Le correctif est réel, sa justification est périmée. | seul handler : `common/notification-service/…/SellerActivatedNotificationHandler.cs` |
| A-5 | **HIGH** | **`BANK_ACCOUNT_UPDATE` ne garde aucune route.** Déclarée `Critical`+`OwnerOnly` (`MerchantPermission.cs:279`), attribuable, affichée — et sans effet : c'est `PAYOUT_CONFIGURE` qui couvre aussi `PayoutProvider.BankAccount` (`Enums.cs:49`). Un vendeur croit restreindre la modification du compte bancaire ; il ne restreint rien. | `grep -rn "BankAccountUpdate\|BANK_ACCOUNT_UPDATE"` → uniquement `MerchantPermission.cs` |

## A.4 — Création et ouverture de boutique

```
Point d'entrée: POST /api/v1/merchants/{sellerId}/stores            · STORE_CREATE     · MerchantEndpoints.cs:176, 659-665
                PUT  …/stores/{storeId}/location                    · STORE_UPDATE     · :180, 720-725
                POST …/stores/{storeId}/open                        · STORE_OPEN_CLOSE · :182, 734-737
                POST …/stores/{storeId}/close                       · STORE_OPEN_CLOSE · :183, 739-744
                POST …/stores/{storeId}/suspend | lift-suspension   · MapAdminGroup    · :194-197
États: (rien) → Store{Draft} → Open ⇄ Closed ; Open|Closed → Suspended → Closed
       Domain/Stores/Store.cs:85-113, 207-237, 240-253, 256-273, 284-302 ; StorePrimitives.cs:18-40
Ce qui déclenche la suite: StoreCreated / StoreOpened / StoreClosed / StoreSuspended (domaine + intégration)
Trace d'audit: OUI
Statut: PARTIAL
```

Points corrects : la création exige un vendeur **`Active`** (`Application/Stores/StoreCommands.cs:91-101`) —
un vendeur suspendu ne peut donc pas contourner sa sanction en ouvrant un magasin neuf ;
l'ouverture exige un lieu d'expédition rattaché (`Store.cs:224-229`) ; une boutique
**suspendue par la plateforme ne se rouvre pas** depuis l'espace vendeur (`Store.cs:214-219`) ;
et les neuf routes `/stores/{storeId}/…` passent le `storeId` à la garde, qui bascule alors
sur `HasInStore` au lieu de `Has` (`MerchantEndpoints.cs:428-435`, `SellerMember.cs:934-970`).

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| A-6 | **HIGH** | **Fermer ou suspendre une boutique n'arrête aucune vente.** `StoreClosedIntegrationEvent`, `StoreOpenedIntegrationEvent`, `StoreSuspendedIntegrationEvent`, `StoreSuspensionLiftedIntegrationEvent` et `StoreCreatedDomainEvent` n'ont **aucun consommateur** dans le dépôt. `Store.IsSelling` (`Store.cs:83`) n'est lu que pour l'affichage (BFF `apps/api-gateway/…/GetProductDetailHandler.cs:157`, `GetMerchantDashboardHandler.cs:107`) ; ni `PlaceOrder`, ni le panier, ni la Buy Box ne le consultent. Une boutique fermée pour congés — ou suspendue par la plateforme — continue d'encaisser. | `grep -rn "IIntegrationEventHandler<StoreClosedIntegrationEvent>"` → 0 (idem les quatre autres) ; `grep -rn "IsSelling"` : aucune occurrence hors mappage/affichage |

---

# B. Création d'un produit

```
Point d'entrée: POST /api/v1/catalog/seller/products              · PRODUCT_CREATE            · catalog-service/…/CatalogEndpoints.cs:255, 1110-1191
                PUT  …/products/{id}                              · PRODUCT_UPDATE            · :256, 1194-1213
                POST …/products/{id}/status                       · dépend de la cible        · :257, 1215-1217
                POST /api/v1/catalog/admin/products/{id}/approve  · MapAdminGroup             · :161, 931-941
                POST …/admin/products/{id}/reject                 · MapAdminGroup             · :162, 943-954
                POST …/admin/products/{id}/suspend | restore      · MapAdminGroup             · :163-164, 956-962
États produit: Draft → PendingReview → Approved → Published ⇄ Unpublished
               PendingReview → Rejected → Draft (correction) ; Approved|Published → Suspended → Approved
               Domain/Products/ProductStatus.cs (énumération + liste blanche)
États révision: Draft → PendingReview → Approved → Published → Superseded ; → Rejected → Draft
               Domain/Products/ProductRevision.cs:27-46
Ce qui déclenche la suite: ProductSubmittedForReview / ProductApproved / ProductRejected /
                           ProductPublished / ProductUnpublished / ProductSuspended (domaine)
Trace d'audit: NON — CatalogDbContext n'active pas KeepsAuditTrail
Statut: PARTIAL
```

### Réponses explicites aux quatre questions

**1. Peut-on passer de DRAFT à PUBLISHED sans revue ? Non.**
La liste blanche `ProductStatusTransitions.IsAllowed` (`ProductStatus.cs`) n'admet
`Published` qu'en cible de deux paires : `(Approved, Published)` et `(Unpublished, Published)`.
`(Draft, Published)` n'existe pas. Et `Product.Publish` (`Product.cs:534-606`) pose **deux**
gardes indépendantes : la révision courante doit être `Approved`, **et** la transition
produit doit être autorisée. La première seule laisserait republier un produit suspendu ;
la seconde seule laisserait publier une révision non relue. La permission
`PRODUCT_PUBLISH` (`MerchantPermission.cs:210`) est explicitement documentée comme
« autorise à publier une fiche DÉJÀ approuvée, rien de plus ». **Vérifié : la règle tient.**

**2. Chemin de correction après rejet ? Oui.**
`REJECTED → DRAFT` est dans la liste blanche, et `Product.UpdateContenu` la déclenche
automatiquement quand la révision courante est rejetée (`Product.cs:280-315` :
`courante.MarquerCorrigee()` puis `ChangerStatut(Draft)` si la fiche n'a jamais été publiée).
Sans cela `SubmitForReview` aurait demandé `Rejected → PendingReview`, que la liste refuse.

**3. Y a-t-il des révisions ? Oui, et c'est un second axe réel.**
`ProductRevision` porte `RevisionStatus` indépendamment de `ProductStatus`
(`ProductRevision.cs:6-46`). Une fiche `Published` dont la révision courante est
`PendingReview` reste servie aux acheteurs : `SubmitForReview`, `Approve`, `Reject` ne
touchent au statut produit **que si `PublishedRevisionId is null`**
(`Product.cs:437-445, 462-470, 495-503`). `Publish` marque l'ancienne `Superseded`.

**4. L'acteur de chaque transition est-il enregistré ? Non, à moitié.**

| Transition | Acteur enregistré ? | Preuve |
|---|---|---|
| Create / Update | non | `CreateProductCommand`/`UpdateProductCommand` ne portent aucun `actorId` (`CatalogEndpoints.cs:1174-1190, 1197-1212`) |
| Submit for review | **non** | `ChangeProductStatusCommand(id, request.Status)` — deux arguments (`CatalogEndpoints.cs:1216`) ; `Product.SubmitForReview(nowUtc)` n'a pas de paramètre acteur (`Product.cs:384`) |
| Publish / Unpublish | **non** | même commande ; `Product.Publish(nowUtc)` (`:534`), `Product.Unpublish()` (`:612`) |
| Approve | **oui** | `ApproveProductCommand(id, reviewerId, comment)` (`CatalogEndpoints.cs:934-940`) → `Product.Approve(reviewedBy, …)` (`Product.cs:452,476`) |
| Reject | **oui** | `RejectProductCommand(id, reviewerId, …)` (`:946-953`) → `Product.Reject(reviewedBy, …)` (`Product.cs:484,507`) |
| Suspend / Restore | **non** | `SuspendProductCommand(id, reason)` / `RestoreProductCommand(id)` (`CatalogEndpoints.cs:956-962`) |
| Filet d'infrastructure | **absent** | `CatalogDbContext` n'active pas `KeepsAuditTrail` (`ModuleDbContext.cs:61` ; les trois seuls contextes qui l'activent sont sellers, return_refund, food-order) |

Défauts :

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| B-1 | **HIGH** | **Aucun acteur n'est enregistré pour les gestes vendeur du catalogue, et aucun journal d'infrastructure ne rattrape.** « Qui a dépublié ce produit », « qui a soumis cette révision », « quel administrateur l'a suspendu » sont sans réponse. Le module inverse est pourtant écrit et actif dans seller-service. | `CatalogEndpoints.cs:1215-1217, 956-962` ; `ModuleDbContext.cs:61,143-210` ; `grep -rn KeepsAuditTrail` |
| B-2 | **MEDIUM** | **`ListAuditEntriesQuery` affirme un journal qui n'existe pas.** L'encadré écrit « Le lot 0c a posé un journal par schéma : catalog, inventory, order et celui-ci ». Trois des quatre n'ont pas `KeepsAuditTrail`. Un commentaire qui certifie une garde absente fait passer la relecture. | `seller-service/…/Application/Members/AuditQueries.cs:29-33` vs `grep -rn KeepsAuditTrail` |
| B-3 | **MEDIUM** | La garde produit lit le `SellerId` **par le cache** de `ICatalogModuleApi.GetProductAsync`. C'est justifié tant qu'aucun transfert de propriété n'existe (l'encadré le dit, `CatalogEndpoints.cs:428-436`) — à retenir comme condition de sûreté d'une future fonctionnalité, pas comme défaut actuel. | `CatalogEndpoints.cs:439-460` |

Le cadrage par boutique est **réel** dans catalog (contrairement à inventory et order) :
`DenyUnlessProductOwnerAsync` appelle `acces.CanInStore(produit.StoreId, capacite)`
(`CatalogEndpoints.cs:489-492`), et la création refuse un `storeId` absent quand l'appelant
est un membre cadré (`:1146-1155`) — sans quoi une fiche naîtrait avec `StoreId = null` et
annulerait le cadrage pour toute sa vie.

---

# C. Stock

```
Point d'entrée: POST /api/inventory/locations                    · STOCK_LOCATION_MANAGE · inventory-service/…/InventoryEndpoints.cs:156, 382-419
                PUT  /api/inventory/locations/{id}/address       · STOCK_LOCATION_MANAGE · :157, 418-429
                POST /api/inventory/items                        · INVENTORY_ADJUST      · :159, 576-583
                POST /api/inventory/items/{id}/receive           · INVENTORY_ADJUST      · :160, 585-589
                POST /api/inventory/items/{id}/adjust            · INVENTORY_ADJUST      · :161, 591-595
                PUT  /api/inventory/items/{id}/reorder-threshold · INVENTORY_ADJUST      · :162, 597-601
                POST /api/inventory/reservations{,/release,/confirm} · MapAdminGroup     · :108-110
États: InventoryItem{OnHand, Reserved (somme des réservations), Available = OnHand − Reserved}
       Domain/Stock/InventoryItem.cs:37-76
Ce qui déclenche la suite: InventoryItemCreated / StockReserved / StockDepleted / StockReplenished
Trace d'audit: NON (InventoryDbContext n'active pas KeepsAuditTrail) ; aucun journal métier non plus
Statut: BROKEN
```

### Réponses explicites aux trois questions

**« Les mouvements sont-ils tracés ? » — Non. Il n'existe aucun journal de mouvements.**
Recherche exhaustive : `grep -rin "stockmovement|stock_movement|mouvement"` sur
`services/marketplace/inventory-service` ne renvoie qu'**un commentaire**, celui de
`InventoryItem.StockVersion` (`InventoryItem.cs:43`), qui est un **compteur de verrou
optimiste** et non un journal — l'encadré le dit lui-même : « la valeur elle-même n'est lue
par personne » (`:63`). Aucune entité, aucune table, aucun endpoint. `Receive` et
`AdjustOnHand` n'émettent **aucun** événement décrivant le mouvement : seul
`StockReplenished` part, et uniquement sur la transition « épuisé → disponible »
(`InventoryItem.cs:123-127, 200-205`). Un ajustement de −40 sur un article qui en avait 100
ne laisse strictement rien derrière lui, ni ligne, ni acteur, ni motif.

**« Un ajustement peut-il rendre le stock négatif ? » — Non.**
`AdjustOnHand` refuse `OnHand + delta < Reserved` (`InventoryItem.cs:188-191`), et
`Reserved ≥ 0` par construction, donc `OnHand` reste positif. `Create` refuse `onHand < 0`
(`:92-95`), `Receive` refuse une quantité nulle ou négative (`:108-111`).
**Vérifié : la garde tient.**

**« Stock réservé et stock disponible sont-ils distincts ? » — Oui, structurellement.**
`Reserved` est la somme des `StockReservation` enfants (`InventoryItem.cs:71`),
`Available = OnHand − Reserved` (`:74`). `ConfirmReservation` décrémente `OnHand` et solde
les réservations (`:171-183`) ; `ReleaseReservation` ne touche pas `OnHand` (`:163-168`).
Le verrou optimiste est **effectif** grâce à `Touch()` appelé par toute mutation, y compris
celles qui ne modifient que des lignes enfants (`:83, 146-150, 166`) — sans quoi deux
réservations concurrentes du dernier article passeraient toutes deux.

Défauts :

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| C-1 | **CRITICAL** | **Une réservation expirée n'est jamais libérée.** `StockReservation.ExpiresAtUtc` est écrite puis **plus jamais lue** : aucun `BackgroundService`/`IHostedService` dans tout `inventory-service`. `Reserved` somme toutes les réservations sans filtrer l'expiration. Tout panier abandonné entre `MarkAwaitingPayment` et un paiement qui n'arrive pas immobilise du stock définitivement. | `Domain/Stock/StockReservation.cs:20,25` ; `InventoryItem.cs:71` ; `grep -rln "BackgroundService\|IHostedService" services/marketplace/inventory-service` → **vide** |
| C-2 | **HIGH** | **Aucune traçabilité des mouvements.** Ni journal métier, ni `KeepsAuditTrail`. Un ajustement destructif est indistinguable d'une vente. `STOCK_MOVEMENT_VIEW` (`MerchantPermission.cs:70,222`) est déclarée, attribuée à `INVENTORY_MANAGER` et `STORE_ADMIN` (`SellerRole.cs:449-457, 434-433`), affichée dans l'écran des droits — et **ne garde rien** faute de route. | `grep -rin "mouvement\|movement"` sur inventory → 1 commentaire ; `ModuleDbContext.cs:61` |
| C-3 | **HIGH** | **Le transfert entre lieux n'existe pas.** `grep -rin "transfer\|transfert"` sur `inventory-service` → **aucune occurrence**. Aucune méthode `Transfer` sur `InventoryItem`, aucune route. `INVENTORY_TRANSFER` (`MerchantPermission.cs:69,221`) est déclarée `Sensitive`, portée par `INVENTORY_MANAGER` et `STORE_ADMIN`, et la description du rôle promet « Stocks, ajustements, **transferts** ». | `SellerRole.cs:450` (libellé) vs absence totale d'implémentation |
| C-4 | **MEDIUM** | **Aucun cadrage par boutique sur le stock, et c'est structurel.** `DenyUnlessOwnerAsync` appelle `acces.Can(capacite)` et non `CanInStore` (`InventoryEndpoints.cs:271-279`). `FulfillmentLocation` porte un `OwnerId` (= `SellerId`) et **aucun** `StoreId` : un gestionnaire de stock de la boutique A ajuste le stock de la boutique B. C'est assumé et documenté (`MerchantPermission.cs:382-397`), et compensé par le refus D27 (`MemberCommands.cs:794-844`) — mais l'exposition existe dès que le rôle est donné au niveau vendeur. | `InventoryEndpoints.cs:271-279` ; `Domain/Locations/FulfillmentLocation.cs` (aucun StoreId) |

Ce qui est bien fait, à ne pas défaire : la chaîne de propriété `jeton → userId → sellerId →
location.OwnerId → items du lieu` est appliquée sur les sept écritures
(`InventoryEndpoints.cs:213-320`), le cas `OwnerId is null` (entrepôt de plateforme) est un
**refus explicite** (`:275-279`), les deux lectures par SKU **filtrent** au lieu de refuser et
exigent quand même `INVENTORY_VIEW` (`:520-550`), et `POST /locations` **ignore** l'`OwnerId`
du corps pour un vendeur (`:382-402`).

---

# D. Commandes vendeur

## D.1 — Point crucial : l'agrégat `SellerOrder` n'existe pas

**Réponse : il n'existe pas. Le vendeur n'a qu'une vue filtrée de la commande globale.**

Preuves, dans l'ordre de force :

1. **Aucun fichier.** Le domaine d'`order-service` contient exactement :
   `Order.cs`, `OrderLine.cs`, `OrderLineKind.cs`, `OrderLineOption.cs`, `OrderIds.cs`,
   `IOrderRepository.cs`, `Events/OrderDomainEvents.cs`. Aucun `SellerOrder`, aucun
   `Fulfillment`, aucun `Shipment`.
   (`find services/marketplace/order-service/src/HBA.Order.Domain -name "*.cs"`)
2. **Le champ existe dans le contrat et vaut `null` en dur.** `OrderReturnContext.SellerOrderId`
   est déclaré (`shared/contracts/HBA.Ordering.Contracts/OrderingContracts.cs:85`), transporté
   par le proto (`shared/proto/order/v1/order.proto:83`) — et l'implémentation écrit
   `SellerOrderId: null` **littéralement** :
   `services/marketplace/order-service/src/HBA.Order.Infrastructure/Public/OrderingModuleApi.cs:66`.
   Le contrat est une coquille.
3. **Un seul statut, global.** `OrderStatus` (`Domain/Orders/OrderIds.cs:15-24` + valeur
   `UnderReview`) est porté par l'agrégat `Order` entier. `OrderLine` ne porte qu'un
   `SellerId`, jamais un statut (`Domain/Orders/OrderLine.cs`).
4. **La répartition vendeur n'est qu'un calcul de lecture.** `Order.BuildSellerShares()`
   (`Order.cs:355-386`) regroupe les lignes par `SellerId` pour l'événement
   `OrderConfirmed` — elle ne crée aucune entité et n'est jamais persistée.

## D.2 — Ce que le vendeur peut réellement faire

```
Point d'entrée: GET /api/sellers/{sellerId}/orders · MapSellerGroup + ORDER_VIEW
                order-service/src/HBA.Order.Api/Endpoints/OrderEndpoints.cs:119-120, 257-304
États: aucun — c'est une lecture
Ce qui déclenche la suite: RIEN
Trace d'audit: NON
Statut: BROKEN
```

**C'est la seule route vendeur du service.** Il n'existe ni confirmation, ni préparation,
ni « prêt », ni rejet, ni annulation vendeur. Le commentaire du groupe l'écrit noir sur
blanc : « c'est ici que **viendront** les routes de confirmation et de préparation
qu'`ORDER_MANAGER` attend » (`OrderEndpoints.cs:117-118`).

La transition `Paid → Confirmed` est déclenchée par la saga de paiement/réservation
(`Order.Confirm()`, `Order.cs:339-352`), pas par un vendeur.
`Confirmed → Delivered` vient de `MarkOrderDeliveredOnDeliveryCompletedHandler`.
Le vendeur n'a aucune prise sur le cycle de vie.

**« Si l'état est global, une confirmation par un vendeur affecte-t-elle les autres
vendeurs de la même commande ? »** — La question ne se pose pas encore, faute de route ;
mais la structure garantit qu'elle se posera : `Order.Confirm()` pose `Status = Confirmed`
pour la commande **entière** et émet un unique `OrderConfirmedDomainEvent` porteur de
**toutes** les parts vendeur (`Order.cs:339-352`). Exposer ce geste à un vendeur, en l'état,
confirmerait la commande au nom de tous les autres. C'est un piège de conception armé.

Défauts :

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| D-1 | **CRITICAL** | **Cinq permissions de commande ne gardent aucune route : `ORDER_CONFIRM`, `ORDER_REJECT`, `ORDER_MARK_PREPARING`, `ORDER_MARK_READY`, `ORDER_CANCEL`.** Elles sont déclarées, attribuées par défaut à `ORDER_MANAGER` et `STORE_ADMIN`, affichées comme cochées dans l'écran des rôles — et le vendeur n'a aucun moyen de traiter une commande. Le rôle « Commandes et préparation » ne permet que de lire. | `MerchantPermission.cs:86-90, 231-235` ; `SellerRole.cs:462-473` ; `OrderEndpoints.cs:119-120` (une seule route) |
| D-2 | **HIGH** | **Fuite inter-vendeur sur le carnet de commandes.** `ListOrdersBySellerQueryHandler` charge les commandes du vendeur puis les rend via `OrderMapper.ToSummary`, qui sérialise **toutes** les lignes — y compris celles des autres vendeurs (leur `SellerId`, `OfferId`, `Sku`, prix unitaires, remises) — plus l'adresse de livraison complète de l'acheteur : destinataire, téléphone, commune, quartier, point de repère, **latitude/longitude**. Sur une commande multi-vendeurs, chaque vendeur lit le carnet de ses concurrents. | `Application/Orders/Queries/OrderQueries.cs:66-78` ; `Application/Orders/OrderMapper.cs:9-43` (aucun filtre sur `SellerId`) |
| D-3 | **MEDIUM** | Le vendeur ne peut pas lire **une** commande : `GET /api/orders/{id}` filtre sur `order.BuyerId != requesterId` (`OrderQueries.cs:88-95`). Il n'a que la liste complète, non paginée (`ListOrdersBySellerQuery` n'a ni page ni filtre de statut, `OrderQueries.cs:36`). | idem |
| D-4 | **MEDIUM** | Le cadrage par boutique est impossible sur les commandes et documenté comme tel : `OrderLine` ne remonte pas la boutique de l'offre, une commande peut mêler plusieurs boutiques (`MerchantPermission.cs:389-392`). La garde utilise `acces.Can(...)` (`OrderEndpoints.cs:297`). | idem |

---

# E. Retours

```
Point d'entrée: GET  /api/v1/seller/returns                 · MapSellerGroup, sellerId en QUERY STRING
                GET  /api/v1/seller/returns/{id}            · MapSellerGroup
                POST …/{id}/approve | reject | inspection | refund-decision | shipment | receive
                return-refund-service/src/HBA.Marketplace.ReturnRefund.Api/Endpoints/SellerReturnsEndpoints.cs:14-23
États: Requested → EligibilityCheck → AwaitingApproval → Approved → AwaitingReturn →
       InReturnTransit → Received → InspectionPending → RefundPending → Refunded → Closed
       (+ Rejected, RejectedAfterInspection, Cancelled, Expired, ManualReview)
       Domain/Policies/ReturnStateMachine.cs ; Domain/Enums/ReturnEnums.cs:3-21
Ce qui déclenche la suite: ReturnApproved / RefundRequested / ReturnRefunded (domaine → outbox)
Trace d'audit: OUI, deux fois — ReturnStatusHistory{ActorId} (Domain/…/ReturnRequest.cs:322-339)
               et ReturnRefundDbContext.KeepsAuditTrail = true (:28)
Statut: BROKEN
```

La machine à états et la traçabilité sont les meilleures du domaine marketplace :
chaque transition passe par `MoveTo` qui vérifie `ReturnStateMachine.CanTransition` et
inscrit une ligne d'historique avec l'acteur (`ReturnRequest.cs:322-339`).
`ReturnInspection` porte aussi l'acteur (`:270`).

Et **rien de tout cela n'est protégé.**

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| E-1 | **CRITICAL** | **Aucune route vendeur du service de retours ne vérifie à qui appartient le dossier.** (a) `ListAsync(Guid sellerId, …)` : le `sellerId` est **lié depuis la query string**, jamais confronté au jeton — tout compte porteur du rôle `Seller` liste les retours de n'importe quel vendeur. (b) Les sept autres routes passent le `ReturnId` directement au handler ; aucun handler ne compare `request.SellerId` à l'appelant. Un vendeur **approuve, rejette, inspecte, réceptionne et décide le remboursement** sur le dossier d'un concurrent. Le service ne référence même pas `IMerchantAccessApi` (`grep` → 0 occurrence). | `SellerReturnsEndpoints.cs:14, 26-27, 33-52` ; `Application/Commands/ReturnLifecycleCommands.cs:34-47, 164-182` |
| E-2 | **HIGH** | **Les dix politiques d'autorisation du service sont du code mort.** `ReturnAuthorizationPolicies` déclare `return:approve`, `refund:decide`, `return:override`… ; `grep -rn "ReturnAuthorizationPolicies"` sur tout le dépôt ne renvoie **que le fichier de déclaration**. Aucun `RequireAuthorization` ne les cite. | `Api/Authorization/ReturnAuthorizationPolicies.cs:3-14` |
| E-3 | **HIGH** | **Les six permissions `RETURN_*` ne gardent rien.** `RETURN_VIEW`, `RETURN_APPROVE`, `RETURN_REJECT`, `RETURN_CONFIRM_RECEIVED`, `RETURN_INSPECT`, `RETURN_DISPUTE_VIEW` sont attribuées à `STORE_ADMIN` et `CUSTOMER_SUPPORT` et n'ont aucun effet. Le catalogue le documente comme provisoire (« `return-refund-service` est un squelette : quatre csproj, un Program.cs de dix-huit lignes, aucune entité ») — **ce commentaire est périmé** : le service a 55 fichiers, un agrégat complet et huit routes vendeur actives. | `MerchantPermission.cs:240-251` vs `find services/marketplace/return-refund-service -name "*.cs" \| wc -l` = 55 |
| E-4 | **HIGH** | **Le plafond de remboursement est autoréférentiel.** `DecideRefundCommandHandler` construit le `RefundBreakdown` **à partir du montant demandé** (`ReturnLifecycleCommands.cs:176`), si bien que le contrôle `requested.Amount > calculated.Amount` de `RefundCalculationPolicy` (`Domain/Policies/RefundCalculationPolicy.cs:15-19`) compare une valeur à elle-même et ne peut jamais échouer. Le seul plafond effectif est `EstimatedRefundAmount`, calculé à la création (`CreateReturnCommand.cs:82`). Le détail (frais de port, pénalités) n'est jamais vérifié. | `ReturnLifecycleCommands.cs:169-181` ; `RefundCalculationPolicy.cs:15-19` |

---

# F. Finances

```
Point d'entrée (payment-service, HBA.Financial.Api/Endpoints/FinancialEndpoints.cs):
  GET  /api/financial/wallets/sellers/{sellerId}                · WALLET_VIEW        · :141, 473-486
  GET  /api/financial/wallets/sellers/{sellerId}/transactions   · WALLET_VIEW        · :142, 488-497
  GET  /api/financial/wallets/sellers/{sellerId}/withdrawals    · PAYOUT_VIEW        · :143, 499-517
  POST /api/financial/wallets/sellers/{sellerId}/withdrawals    · WITHDRAWAL_REQUEST · :144, 519-530   (Critical → step-up)
  GET  /api/financial/settlements/sellers/{sellerId}/statement  · FINANCE_VIEW       · :172, 574-581
  GET  …/statement/lines                                        · FINANCE_VIEW       · :173, 583-584
  GET  /api/financial/settlements/sellers/{sellerId}/payouts    · PAYOUT_VIEW        · :174, 586-590
  GET  /api/financial/invoices/seller/{sellerId}                · FINANCE_VIEW       · :134, 453
  POST /api/financial/settlements{,/{id}/cancel,…/paid}         · MapAdminGroup      · :195-199
  PUT  /api/v1/merchants/{sellerId}/payout-account              · PAYOUT_CONFIGURE   · MerchantEndpoints.cs:115 (Critical + OwnerOnly → step-up)
États: SellerWallet{Available, Pending} ; Withdrawal{Requested → Approved|Rejected → Paid|Failed}
Ce qui déclenche la suite: OrderConfirmed → AccrueEarningsOnOrderConfirmedHandler ;
                           OrderDelivered → ReleaseEarningsOnOrderDeliveredHandler ;
                           OrderCancelled / ReturnRefunded → reprise des gains
Trace d'audit: partielle — WalletTransaction (livre de comptes) oui ; KeepsAuditTrail non
Statut: COHERENT (avec réserves)
```

C'est le parcours le mieux gardé. Chaque route porte `DenyUnlessOwnSellerAsync`
(`FinancialEndpoints.cs:675-745`) qui, dans l'ordre : laisse passer l'administration,
résout l'appartenance par `IMerchantAccessApi`, compare `acces.SellerId` au `sellerId` de
l'URL, exige la capacité, **puis** applique le step-up si la capacité est `Critical`
(`:738-743`). Les trois écritures de règlement (lancer un lot, marquer payé, annuler) sont
dans `MapAdminGroup` (`:195-199`).

La demande de retrait fige la destination au moment de la demande, et ne la relit pas à
l'approbation (`wallet-service/…/Wallets/WalletCommands.cs:129-140`) — sans cela, modifier le
compte entre la demande et la validation détournerait le virement. Le compte de versement
est lu par un RPC dédié et **sans cache** (`:100`), ce qui est le bon arbitrage pour de
l'argent.

Ré-authentification pour les actions sensibles — **oui, et le mécanisme est solide** :
`auth_time` OIDC recopié par le rafraîchissement de jeton (donc un client qui rafraîchit
en boucle ne reste pas éternellement « frais »), fenêtre de 5 minutes non configurable par
service, claim absent = refus, `auth_time` dans le futur = refus au-delà d'une minute de
dérive (`StepUpAuthentication.cs:63, 88-99`). Les capacités concernées :
`PAYOUT_CONFIGURE`, `WITHDRAWAL_REQUEST`, `BANK_ACCOUNT_UPDATE`, `SELLER_CLOSE`,
`OWNERSHIP_TRANSFER`, `SECURITY_POLICY_UPDATE` (`MerchantPermission.cs:277-294, 356-358`).

| # | Sévérité | Constat | Preuve |
|---|---|---|---|
| F-1 | **HIGH** | **Le taux de commission affiché au vendeur peut être faux.** `SellerMapper.ToSummary` sert un `effectiveCommissionRate` alimenté par `IPlatformPricing.CommissionRate` (`SellersModuleInstaller.cs:64` — lu dans la configuration), c'est-à-dire le **défaut** de la plateforme, pas la règle du vendeur, qui vit dans le moteur de règles de Billing (`billing-service/…/Commissions/CommissionResolver.cs`) et que la comptabilisation des gains, elle, interroge bien (`wallet-service/…/AccrueEarningsOnOrderConfirmedHandler.cs:197`). Le pont manque : `financial.proto:14` déclare `ComputeCommission`, et `FinancialGrpcService` **ne l'implémente pas** (aucun `override ComputeCommission` dans le fichier) — l'appel rendrait `UNIMPLEMENTED`. Dès qu'un administrateur crée une règle « Seller » à 5 %, l'argent prélève 5 % et l'écran vendeur annonce toujours le défaut. | `Domain/Sellers/Seller.cs:56-86` ; `Application/Sellers/SellerMapper.cs:21-33` ; `Infrastructure/SellersModuleInstaller.cs:64` ; `shared/proto/financial/v1/financial.proto:14` vs `common/payment-service/…/GrpcServices/FinancialGrpcService.cs` |
| F-2 | **MEDIUM** | **La libération des gains par expédition ne se déclenche jamais.** `ReleaseSellerEarningsOnShipmentDeliveredHandler` est enregistré (`wallet-service/…/SettlementModuleInstaller.cs:133`) et consomme `ShipmentDeliveredIntegrationEvent` — que **personne ne publie** : `IShippingModuleApi` n'a aucune implémentation dans le dépôt, il n'existe pas de shipping-service. Le seul chemin actif est `ReleaseEarningsOnOrderDeliveredHandler`, au niveau de la commande entière : dans une commande multi-vendeurs, aucun vendeur n'est réglé tant que **tout** n'est pas livré. | `grep -rn "ShipmentDeliveredIntegrationEvent"` : consommateurs seulement ; `shared/contracts/HBA.Shipping.Contracts/IShippingModuleApi.cs` sans implémentation |
| F-3 | **MEDIUM** | `wallet-service` et `billing-service` n'ont **aucun projet `.Api`** : ils sont hébergés par `payment-service`. `apps/seller-bff` est un stub de 18 lignes (deux routes de santé). Toute la surface financière vendeur passe donc par un service dont ce n'est pas le nom, ce qui rend la matrice de routes illisible depuis l'arborescence. | `ls services/common/{wallet,billing}-service/src` ; `apps/seller-bff/src/HBA.SellerBff.Api/Program.cs` (18 lignes) |

---

## Récapitulatif des statuts

| Parcours | Statut | Raison en une ligne |
|---|---|---|
| A — Intégration vendeur | **PARTIAL** | Machine à états solide ; le rejet KYB et la suspension ne retirent rien de la vente, et la file d'administration n'est alimentée par aucun événement. |
| B — Création de produit | **PARTIAL** | Double machine à états (produit + révision) correcte et infranchissable ; l'acteur des gestes vendeur n'est enregistré nulle part. |
| C — Stock | **BROKEN** | Négatif impossible et réservé/disponible bien séparés, mais aucun journal de mouvements, aucun transfert, et les réservations expirées ne sont jamais libérées. |
| D — Commandes vendeur | **BROKEN** | Pas d'agrégat `SellerOrder` (`SellerOrderId: null` en dur) ; une seule route, en lecture, qui fuit les lignes des autres vendeurs et l'adresse GPS de l'acheteur. |
| E — Retours | **BROKEN** | Machine à états et historique d'acteur excellents ; zéro autorisation — un vendeur décide le remboursement du dossier d'un concurrent. |
| F — Finances | **COHERENT** | Appartenance + capacité + step-up sur chaque route ; réserves sur le taux de commission affiché et sur la libération par expédition. |
