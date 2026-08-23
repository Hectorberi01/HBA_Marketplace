# Faisabilité — cahier « Marketplace Cart & Order »

Lecture du cahier contre le dépôt au 19 août 2026. Rien n'a été modifié pour
écrire ce document.

**Verdict court.** Faisable, mais ce n'est pas un lot : c'en est quatre, dont un
seul est gros. Environ 60 % du cahier décrit ce qui tourne déjà sous d'autres
noms ; 25 % sont des manques réels et bornés ; 15 % touchent à une décision
d'architecture qu'il faut trancher avant d'écrire une ligne.

---

## 1. Deux consignes du cahier qu'il ne faut PAS suivre

### 1.1 Les solutions `HBA.Marketplace.Cart/` et `HBA.Marketplace.Order/` (§14, §40)

Le cahier décrit deux arborescences neuves, chacune avec son `.sln`. Les suivre
créerait **deux services parallèles** à côté de ceux qui tournent :

| Le cahier écrit | Le dépôt a |
|---|---|
| `HBA.Marketplace.Cart.sln` | `services/marketplace/cart-service`, assemblages `HBA.Commerce.*`, schéma `cart` |
| `HBA.Marketplace.Order.sln` | `services/marketplace/order-service`, assemblages `HBA.Order.*` (namespaces `HBA.Orders.*`), schéma `ordering` |

Aucun assemblage `HBA.Marketplace.*` n'existe aujourd'hui. Le dépôt n'a que deux
solutions — `HBA.sln` et celle de la passerelle — et une solution par service
serait une rupture de convention, pas une amélioration.

Surtout : les deux services existants portent des **données de production** et
treize migrations pour order seul. Un service neuf au même rôle ne se substitue
pas, il coexiste — et c'est exactement le piège qu'on a refusé dans le cahier
membres V2 §48 (`HBA.SellerService/`).

**Décision proposée : on garde les emplacements, les assemblages et les schémas
existants. Le cahier est aligné sur le dépôt, pas l'inverse.** C'est la même
règle que la décision D1 du lot membres.

### 1.2 Le renommage implicite des routes

Le cahier écrit `/api/v1/marketplace/cart` et `/api/v1/marketplace/orders`. Le
dépôt sert `/api/commerce/cart` (public : `/api/cart` après réécriture par la
passerelle) et `/api/orders`.

À noter, parce que ça se décide une fois : **le versionnage des routes est déjà
incohérent dans le dépôt**. Catalog, merchants, media, promotions, payments et
users servent `/api/v1/…` ; cart, orders, inventory, financial, engagement,
deliveries et food n'ont pas de `/v1`. Ce cahier ne peut pas trancher pour les
treize services — il ne voit que deux d'entre eux. Aligner cart et order sur
`/v1` sans aligner le reste ajouterait une troisième convention.

**Proposition : ne rien renommer dans ce lot, et traiter le versionnage comme un
lot transverse à part.** Si tu veux l'inverse, il faut le décider maintenant : la
passerelle, les quatre BFF et les clients mobiles lisent ces chemins.

---

## 2. Le vrai morceau : `SellerOrder` n'existe pas

C'est le cœur du cahier (§16, §17, §19) et c'est **absent du dépôt**.

Aujourd'hui une commande multi-vendeur est représentée par :

- des `order_lines` portant chacune leur `SellerId` ;
- un calcul **à la volée et non persisté**, `Order.BuildSellerShares()`, qui
  regroupe par vendeur et ne sert qu'à remplir l'événement `OrderConfirmed` ;
- un **statut global unique** sur la commande.

Il n'existe donc ni état par vendeur, ni délai par vendeur, ni expédition par
vendeur. Un vendeur qui a préparé sa part ne peut pas le dire.

Ce que le cahier demande en plus : un agrégat `SellerOrder` avec ses **treize
statuts**, son propre cycle, sa propre livraison, et un `MarketplaceOrder` qui
agrège — `PARTIALLY_SHIPPED`, `PARTIALLY_DELIVERED`, `PARTIALLY_CANCELLED`.

Coût honnête : c'est le lot. Nouvelle entité, nouvelle table, migration de
**reprise** pour les commandes existantes (une `SellerOrder` par groupe de
lignes), réécriture de la machine à états, et propagation dans les six
consommateurs Kafka d'order-service.

**Ce que ça débloque, et qui compte au-delà de ce cahier :** l'audit du service
vendeur a relevé que `ORDER_CONFIRM`, `ORDER_REJECT`, `ORDER_MARK_PREPARING`,
`ORDER_MARK_READY` et `ORDER_CANCEL` sont déclarées au catalogue des permissions
et **ne gardent aucune route** — parce que ces routes n'existent pas.
`ORDER_MANAGER` est défini autour de gestes impossibles. Les six routes de §24
sont précisément ce qui rend ce rôle réel.

Corollaire à ne pas manquer : `CreateDeliveryOnOrderConfirmedHandler` **refuse
aujourd'hui les commandes multi-lieux** et les met en arbitrage. Avec des
`SellerOrder`, chaque part a son lieu d'expédition — le refus disparaît, et le
multi-vendeur devient livrable. C'est le gain le plus tangible du lot.

---

## 3. Ce qui manque vraiment, et qui est borné

### 3.1 `POST /api/orders` n'est pas idempotent — défaut vivant

Le cahier l'exige (§36, §49). Le dépôt ne l'a pas :

- aucun code d'idempotence dans order-service (deux occurrences du mot, toutes
  deux en commentaire) ;
- **aucune contrainte d'unicité sur `CartId`** dans `orders`.

Deux `POST /api/orders` concurrents sur le même panier créent **deux commandes et
deux paiements**. Ce n'est pas théorique : c'est ce que produit un double-clic ou
un rejeu réseau depuis un téléphone en 3G.

Le remède existe déjà dans le dépôt — `RequireIdempotency()`
(`IdempotencyEndpointFilter`) avec son magasin EF, utilisé par payment-service,
user-service et promotion-service. **Correctif de quelques lignes, et il ne
dépend d'aucun autre point de ce cahier.** À faire en premier, séparément.

### 3.2 `OrderItem` n'a pas de snapshot lisible

Le cahier insiste (§21) : *« si Catalog change le nom, l'image ou le prix demain,
l'ancienne commande doit toujours refléter ce qui a été acheté »*.

Le dépôt fige bien les **prix** — `UnitBasePrice`, `SellerDiscount`,
`PlatformDiscount`, `FinalUnitPrice`. Mais il n'a **ni `ProductName`, ni
`VariantName`, ni `ImageUrl`**. Une commande relue après suppression d'un produit
ne montre que des GUID et un SKU. L'historique client est déjà illisible pour ces
cas-là.

Trois colonnes, une migration, un remplissage à la création. Petit lot, valeur
immédiate.

### 3.3 Panier invité et fusion — entièrement absents

Le cahier les demande (§3, §5 `sessionId`, `POST /cart/merge`). Le dépôt exige un
jeton sur **toutes** les routes du panier, et `Cart.Create` refuse un `BuyerId`
vide.

Ce n'est pas un ajout de champ : c'est un changement de modèle d'identité, avec
sa politique d'expiration, sa fusion (que faire des doublons, des prix qui ont
bougé, des coupons déjà posés), et une surface anonyme à protéger contre l'abus.
**Lot à part entière, et le seul du cahier qui soit vraiment optionnel** — on
peut livrer tout le reste sans lui.

### 3.4 Les coupons sont un décor

Le champ existe, les routes existent, le handler appelle `ValidateCouponAsync` —
mais cart-service enregistre `NeutralPricingModuleApi`, dont la validation
renvoie **toujours** invalide, et **aucun client gRPC vers promotion-service n'y
est câblé**.

Plus gênant : promotion-service expose `ReserveCoupon`, `CommitCoupon` et
`ReleaseCoupon`, documentés comme idempotents par `cart_id` — et **personne ne
les appelle**. La saga de checkout n'a donc **aucune compensation de coupon** :
une commande annulée après paiement ne rend pas le coupon.

Câblage réel + les trois appels dans la saga. Lot moyen, indépendant du reste.

### 3.5 Historique de statut

Le cahier veut une trace `fromStatus → toStatus` avec acteur et motif (§34).

Le dépôt a un **journal d'audit générique** depuis le lot 0c
(`ordering.audit_entries` : qui a muté quoi, quand, avec quel `correlationId`) —
mais il ne retient **pas les valeurs**, donc pas la transition. Il retient
« quelqu'un a modifié cette commande », pas « de CONFIRMED vers PREPARING ».

Deux options : étendre le journal générique aux transitions de statut, ou une
table dédiée comme le demande le cahier. La seconde est plus lisible et se
requête ; la première ne se réinvente pas à chaque service. **À trancher.**

---

## 4. Les noms de RPC du cahier — surtout du vocabulaire

La plupart existent sous un autre nom. Ceux-là ne coûtent rien :

| Cahier | Réalité |
|---|---|
| `CatalogService.GetSellableVariant` | `OfferApi.GetOffer` — le « sellable » est `OfferSummary.IsPurchasable` |
| `InventoryService.ConsumeReservation` | c'est `ConfirmReservation` qui décrémente le stock physique |
| `PaymentService.CreatePayment` | `InitiatePayment` |
| `PromotionService.ValidateCoupon` | `EvaluatePromotion` en gRPC |
| `SellerService.ValidateStores` | `ValidateSeller` — au niveau **vendeur**, un à la fois |
| `DeliveryService.GetDeliveryQuote` | `LookupQuote` |

**Ce dernier n'est pas un détail de nom.** `GetQuote` **écrit** un devis,
`LookupQuote` en **relit** un figé. La saga utilise délibérément le second, pour
que le prix affiché au client soit celui facturé. Renommer vers
« GetDeliveryQuote » rouvrirait une décision déjà prise.

Manquent réellement, et il faudra les écrire :

- `GetBatchSellableVariants` — le batch existe pour les offres (`GetOffers`) mais
  pas pour les variantes : **la notion de variante n'est pas exposée en gRPC**,
  le proto catalog ne parle que de `Product` et `Offer` ;
- `GetBatchAvailability` — n'existe pas ; le checkout preview d'un panier à dix
  lignes ferait donc dix allers-retours ;
- `EvaluateCartPromotions` — `EvaluatePromotion` prend **un seul code et un
  contexte agrégé**, sans lignes ni résultat par ligne.

---

## 5. Le point que le cahier ne voit pas : Food partage ces agrégats

Le cahier ne parle que de Marketplace. Or dans ce dépôt, **`Cart` et `Order`
servent aussi le parcours Food** : les deux agrégats portent un discriminant
`Kind` (`Goods` / `Food`), un `RestaurantId`, des options de menu, et des
invariants dédiés — un panier ne mélange jamais les deux, un panier Food ne porte
qu'un restaurant.

Créer un `HBA.Marketplace.Cart` séparé laisserait donc le parcours Food **sans
panier**, ou en imposerait un second. Le cahier ne mentionne nulle part ce qu'il
advient de Food.

C'est la conséquence la plus coûteuse du §1.1, et la raison principale de ne pas
le suivre : la séparation qu'il propose n'est pas une réorganisation de dossiers,
c'est un démembrement de deux agrégats partagés.

---

## 6. Découpage proposé

| Lot | Contenu | Dépendances |
|---|---|---|
| **1** | Idempotence sur `POST /api/orders` + unicité sur `CartId` | aucune |
| **2** | Snapshot lisible sur `OrderLine` (nom, variante, image) | aucune |
| **3** | `SellerOrder` : agrégat, table, reprise, machine à états, les six routes de §24, agrégation des statuts partiels | 2 |
| **4** | Livraison par `SellerOrder` — lève le refus multi-lieux | 3 |
| **5** | Coupons réels : câblage promotion, `Reserve`/`Commit`/`Release` dans la saga | aucune |
| **6** | Checkout preview + `GetBatchAvailability` + batch variantes | aucune |
| **7** | Panier invité, fusion, expiration | aucune — **optionnel** |
| **8** | Historique de transitions (si l'option « table dédiée » est retenue) | 3 |

Les lots 1 et 2 sont livrables tout de suite et ferment des défauts réels. Le lot
3 est le cahier lui-même ; le reste s'y raccroche ou vit à côté.

---

## 7. Trois décisions à prendre avant d'écrire

1. **Emplacements et noms.** On garde `cart-service`/`HBA.Commerce.*` et
   `order-service`/`HBA.Orders.*`, ou on suit le cahier et on crée deux services
   parallèles ? *(recommandation : on garde — voir §1.1 et §5)*
2. **Routes.** On laisse `/api/commerce/cart` et `/api/orders`, ou on migre vers
   `/api/v1/marketplace/…` ? *(recommandation : on laisse, et le versionnage
   devient un lot transverse)*
3. **Historique de statut.** Table dédiée `order_status_history`, ou extension du
   journal d'audit générique aux valeurs ? *(pas de recommandation ferme : la
   première est plus lisible, la seconde ne se réinvente pas par service)*
