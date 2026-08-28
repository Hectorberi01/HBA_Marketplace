# Inventaire — services déployables et bases de données

*Relevé du dépôt, pas de mémoire. Les chiffres viennent de `docker-compose.dev.yml`
(la seule source à jour du découpage), de `apps/api-gateway/.../appsettings.json`
pour les routes, et des `*.csproj` pour les modules co-hébergés.*

**24 conteneurs** — 23 services .NET plus la passerelle.
**14 bases** pour 23 services : trois d'entre elles sont partagées.
**19 clusters de passerelle, 55 routes** — et quatre services sans aucune surface publique.

---

## 1. Ce que « un service » veut dire ici, et les deux pièges

**UN DOSSIER DE SERVICE N'EST PAS UN CONTENEUR.** Quatre dossiers de
`services/common/` n'ont pas de `Dockerfile` : ce sont des modules embarqués dans
le processus d'un voisin. Les compter comme déployables donnerait 27 conteneurs
au lieu de 23, et ferait chercher quatre images qui n'existent pas.

| Module | Hébergé par | Conséquence |
|---|---|---|
| `billing-service` | `payment-service` | pas d'image, pas de Deployment, pas d'adresse propre |
| `wallet-service` | `payment-service` | idem |
| `recommendation-service` | `review-service` | idem |
| `wishlist-service` | `review-service` | idem |

**UNE BASE N'APPARTIENT PAS À UN SERVICE, MAIS À UNE FAMILLE.** Le §10 dit
« database ownership per service » ; la réalité est autre, et c'est elle qui
compte au moment de créer les bases — voir le tableau 3.

---

## 2. Les 24 conteneurs, par domaine

### `common` — 7 services

| Conteneur | Base | Cluster passerelle | Préfixes publics |
|---|---|---|---|
| `identity-service` | `hba_identity` | Identity | `/api/auth`, `/api/identity`, `/api/v1/auth` |
| `user-service` | `hba_user` | User | `/api/users`, `/api/geo`, `/api/v1/users` |
| `media-service` | `hba_media` | Media | `/api/media`, `/api/v1/media` |
| `notification-service` | `hba_communication` | Communication | `/api/notifications` |
| `payment-service` | `hba_financial` | Financial | `/api/payments`, `/api/wallet`, `/api/financial/*` |
| `promotion-service` | `hba_promotion` | Promotion | `/api/v1/promotions`, `/api/v1/merchant/promotions` |
| `review-service` | `hba_engagement` | Engagement | `/api/reviews`, `/api/recommendations`, `/api/wishlist` |

### `marketplace` — 6 services

| Conteneur | Base | Cluster passerelle | Préfixes publics |
|---|---|---|---|
| `catalog-service` | `hba_catalog` | Catalog | `/api/catalog`, `/api/v1/catalog` |
| `cart-service` | `hba_commerce` | Commerce | `/api/cart` |
| `inventory-service` | `hba_inventory` | Inventory | `/api/inventory` |
| `order-service` | `hba_order` | Order | `/api/orders`, `/api/admin/orders`, `/api/sellers/{id}/orders` |
| `seller-service` | `hba_merchant` | Merchant | `/api/merchants`, `/api/v1/merchants` |
| `return-refund-service` | `hba_commerce` | ReturnRefund | `/api/v1/{admin,seller,marketplace}/returns` |

### `delivery` — 7 services

| Conteneur | Base | Cluster passerelle | Préfixes publics |
|---|---|---|---|
| `delivery-service` | `hba_delivery` | Delivery | `/api/delivery` |
| `delivery-pricing-service` | `hba_delivery` | DeliveryPricing | `/api/v1/delivery-pricing`, `/api/v1/admin/delivery-pricing` |
| `driver-service` | `hba_delivery` | Drivers | `/api/v1/drivers`, `/api/v1/admin/drivers` |
| `dispatch-service` | `hba_delivery` | — | **aucune** |
| `proof-of-delivery-service` | `hba_delivery` | — | **aucune** |
| `route-service` | `hba_delivery` | — | **aucune** |
| `tracking-service` | `hba_delivery` | — | **aucune** |

### `food` — 3 services

| Conteneur | Base | Cluster passerelle | Préfixes publics |
|---|---|---|---|
| `restaurant-service` | `hba_food` | Food | `/api/food` |
| `food-cart-service` | `hba_food` | FoodCart | `/api/food/cart` |
| `food-order-service` | `hba_food` | FoodOrder | `/api/food/orders`, `/api/food/restaurant/orders`, `/api/admin/food/orders` |

### `apps` — 1

| Conteneur | Base | Rôle |
|---|---|---|
| `gateway` | *aucune* | YARP. Seul objet portant un Ingress. Pas de `DbContext`. |

**QUATRE SERVICES N'ONT AUCUNE ROUTE PUBLIQUE** — `dispatch`, `proof-of-delivery`,
`route`, `tracking`. Ils ne sont joints qu'en gRPC, depuis l'intérieur. Ce ne sont
donc ni des oublis de passerelle, ni des services à exposer : chercher pourquoi
`/api/dispatch` répond 404 serait chercher une route qui n'a jamais eu à exister.

---

## 3. Les 14 bases — ce que le script SQL crée

**TROIS BASES SONT PARTAGÉES, ET C'EST LE POINT À CONNAÎTRE AVANT DE LES CRÉER.**
Chaque service y pose son propre schéma ; ils cohabitent sans se voir. Mais ils
partagent le **rôle**, donc les identifiants — un service compromis de la famille
`delivery` lit les tables des six autres.

| Base | Services | Rôle |
|---|---|---|
| `hba_delivery` | **7** — delivery, delivery-pricing, dispatch, driver, proof-of-delivery, route, tracking | `hba_delivery` |
| `hba_food` | **3** — restaurant, food-cart, food-order | `hba_food` |
| `hba_commerce` | **2** — cart, return-refund | `hba_commerce` |
| `hba_identity` | identity | `hba_identity` |
| `hba_user` | user | `hba_user` |
| `hba_media` | media | `hba_media` |
| `hba_communication` | notification | `hba_communication` |
| `hba_financial` | payment *(+ billing, wallet)* | `hba_financial` |
| `hba_promotion` | promotion | `hba_promotion` |
| `hba_engagement` | review *(+ recommendation, wishlist)* | `hba_engagement` |
| `hba_catalog` | catalog | `hba_catalog` |
| `hba_inventory` | inventory | `hba_inventory` |
| `hba_order` | order | `hba_order` |
| `hba_merchant` | seller | `hba_merchant` |

**LES NOMS DE BASE NE SUIVENT PAS LES NOMS DE SERVICE, ET C'EST DÉLIBÉRÉ.**
`payment-service` écrit dans `financial`, `review-service` dans `engagement`,
`notification-service` dans `communication`, `seller-service` dans `merchant`,
`cart-service` dans `commerce`. Ces noms viennent du découpage d'origine, que les
migrations EF ciblent encore : les aligner imposerait de toutes les réécrire, pour
un gain cosmétique.

Création : `scripts/db/creer-bases.sql` (ou `creer-bases.sh`). Les deux créent
aussi `hba_delivery` et `hba_food`, dont les services arrivent au second lot —
une base vide ne coûte rien.

---

## 4. Ce qui est déployé au premier lot, et ce qui ne l'est pas

| Lot | Domaines | Conteneurs | Bases nécessaires |
|---|---|---|---|
| **1 — en cours** | common + marketplace + passerelle | **14** | 12 des 14 |
| **2 — à venir** | delivery + food | **10** | `hba_delivery`, `hba_food` |

**CE QUE LE LOT 1 LAISSE VISIBLEMENT CASSÉ.** La passerelle déclare les adresses
des dix services absents en `[Required, Url]` : elle démarre normalement — la
validation regarde la forme de l'adresse, pas son existence — et **chaque route
vers eux répond 502**. Livraison, suivi, tarification de course et tout le
parcours restauration sont hors périmètre. Ce n'est pas une panne à chercher.

---

## 5. Ce que cet inventaire ne dit pas

- **Il ne mesure pas la charge.** Sept services sur une base ne dit rien du volume
  qu'elle recevra ; `hba_delivery` porte sept processus mais peut être la moins
  sollicitée.
- **Il ne dit pas quel service appelle quel autre.** Le graphe gRPC vit dans
  `check-di.py` et dans les `ServicesOptions` de chaque hôte.
- **Il ne relève pas les schémas.** Chaque base en porte un ou plusieurs, posés
  par les migrations EF. Aucun script de ce dépôt ne les crée.
- **Il ne vaut que pour `docker-compose.dev.yml`.** C'était déjà vrai quand
  `infra/docker/compose.services.yml` décrivait, à côté, le découpage en 13
  services d'avant le redécoupage. Ce fichier a été retiré du dépôt
  (`_to_delete/2026-08-26-pile-compose-serveur/`) : il n'était lancé par
  personne, et il donnait une seconde réponse, fausse, à la même question.
