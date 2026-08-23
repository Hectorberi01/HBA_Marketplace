# Reorganisation du depot vers le design systeme

Date : 17 aout 2026 · Commit de reference avant travaux : `1c76054`

Le depot suit desormais l'arborescence du document **« HBAExpress Backend —
Structure du Dossier (Monorepo) »**. Ce fichier dit ce qui a bouge, pourquoi, et
ce qui reste a faire.

---

## 1. Ce qui a change dans la forme

### `src/` disparait comme prefixe

`apps/`, `services/` et `shared/` sont maintenant a la racine. La bonne nouvelle
est que **les references relatives entre projets n'ont pas change de profondeur** :
un projet passait de `src/services/common/x/src/HBA.X.Api` a
`services/common/x/src/HBA.X.Api`, et `shared/` est remonte du meme cran. Les
`..\..\..\..\..\shared\...` des `.csproj` restent donc exacts. Ce n'est pas de la
chance : c'est ce qui a rendu le deplacement sur.

En revanche les chemins **absolus depuis la racine** — contextes Docker,
`docker-compose`, workflows CI, scripts — ont tous ete reecrits.

### `infrastructure/` se separe en deux

`src/infrastructure/` melangeait trois choses de nature differente : la
passerelle (du code applicatif), les manifests Kubernetes (du deploiement) et les
configurations Docker/observabilite (de l'environnement). Elles sont separees :

| Avant | Apres | Nature |
|---|---|---|
| `src/infrastructure/gateway/bff/` | `apps/api-gateway/` | code applicatif |
| `src/infrastructure/k8s/` | `k8s/` | deploiement |
| `src/infrastructure/docker/` | `infra/docker/` | environnement |
| `src/infrastructure/observability/` | `infra/observability/` | environnement |
| `infrastructure/rembg/` | `infra/rembg/` | environnement |

### `clients/` accueille les interfaces

L'image ne montre que le backend, et `apps/` y contient exclusivement la
passerelle et les trois BFF. Les interfaces utilisateur auraient donc du cohabiter
avec des hotes .NET sous le meme nom — un dossier `apps/` ou `seller-portal`
(Next.js) et `seller-bff` (ASP.NET) se ressemblent au point de s'appeler presque
pareil sans rien partager. Elles sont donc dans `clients/`.

`src/apps/marchant-portal` a ete corrige en `clients/merchant-portal` au passage.

---

## 2. Ce qui a change dans les noms

Les dossiers portent maintenant le vocabulaire du design systeme.

| Dossier avant | Dossier apres | Domaine |
|---|---|---|
| `common/communication-service` | `common/notification-service` | commun |
| `common/order-service` | `marketplace/order-service` | marketplace |
| `common/commerce-service` | `marketplace/cart-service` | marketplace |
| `marketplace/merchant-service` | `marketplace/seller-service` | marketplace |
| `food/food-service` | `food/restaurant-service` | food |

**Seuls les dossiers ont ete renommes.** Les noms de service a l'execution
— ceux de `docker-compose.dev.yml`, les hotes DNS entre conteneurs, les cibles
Prometheus — sont inchanges : `merchant-service` repond toujours a
`http://merchant-service:8080`. Les renommer touche les `.env`, les `appsettings`
et les manifests k8s, c'est-a-dire la configuration d'execution, pas
l'arborescence. C'est un chantier separe, volontairement pas melange a celui-ci.

---

## 3. Les services eclates

Trois services contenaient plusieurs modules deja isoles en projets distincts.
Le design systeme leur donne une boite chacun ; ils ont donc leur dossier.

### `financial-service` → 3 services

| Projets | Nouveau dossier |
|---|---|
| `HBA.Financial.Payments.*` **+ `HBA.Financial.Api`** | `common/payment-service/` |
| `HBA.Financial.Wallet.*` | `common/wallet-service/` |
| `HBA.Financial.Billing.*` | `common/billing-service/` |

### `engagement-service` → 3 services

| Projets | Nouveau dossier |
|---|---|
| `HBA.Engagement.Reviews.*` **+ `HBA.Engagement.Api`** | `common/review-service/` |
| `HBA.Engagement.Recommendations.*` | `common/recommendation-service/` |
| `HBA.Engagement.Wishlist.*` | `common/wishlist-service/` |

**L'hote API n'a pas ete duplique, et c'est la limite de cet eclatement.**
`HBA.Financial.Api` heberge toujours Payments, Wallet ET Billing dans un seul
processus ; il vit dans `payment-service/` et reference les deux autres dossiers.
Idem pour `HBA.Engagement.Api` depuis `review-service/`. L'arborescence annonce
donc six services la ou tournent deux processus. Donner un hote propre a chacun
suppose de separer le `DbContext`, les migrations et l'enregistrement DI — du
code, pas du deplacement.

`billing-service`, `recommendation-service` et `wishlist-service` n'ont pas de
boite dans le diagramme mais existent dans le code : ils sont conserves plutot
qu'inventes ou perdus.

**`review-service` est en `common/`, pas en `marketplace/`.** Le diagramme montre
deux boites « Review Service », une par domaine ; `HBA.Engagement.Reviews` note
aussi bien un produit qu'un restaurant. Une seule implementation, placee la ou
les deux domaines peuvent l'atteindre.

---

## 4. Les services crees en squelette

17 dossiers du diagramme n'avaient aucun equivalent en projet. Ils existent
maintenant avec `Domain/`, `Application/`, `Infrastructure/`, `Api/`, un
`Dockerfile` et un `README.md` qui indique **ou est le code aujourd'hui**.

| Domaine | Services crees |
|---|---|
| commun | `promotion-service`, ~~`file-service`~~ |
| marketplace | `return-refund-service` |
| food | `menu-service`, `food-cart-service`, `food-order-service`, `kitchen-service`, `availability-service` |
| delivery | `dispatch-service`, `driver-service`, `tracking-service`, `route-service`, `proof-of-delivery-service`, `delivery-pricing-service` |
| apps | `client-bff`, `seller-bff`, `driver-bff` |

> **`file-service` a ete retire depuis** (18 aout 2026, voir `docs/DECISIONS.md`
> D8). Son README annoncait qu'il absorberait « la partie stockage brut » de
> media-service — or media-service avait justement CONSOLIDE cinq implementations
> S3 eparpillees. L'arborescence ne correspond donc plus exactement au diagramme
> sur ce point, et c'est le diagramme qui a tort.

Ces squelettes compilent — les projets sont valides et l'hote repond sur
`/health/live` — mais ne rendent aucun service. Deux consequences assumees :

1. **Ils ne sont pas dans `HBA.sln`.** Soixante-huit projets vides dans la
   solution allongent chaque build sans rien apporter.
2. **Ils ne sont pas dans `docker-compose.dev.yml`.** Un conteneur qu'un autre
   attend au demarrage et qui ne repond a rien bloque tout l'environnement sans
   que le message d'erreur designe le coupable.

Le code de la plupart existe deja, mais comme des **dossiers de namespace dans un
projet unique** — `HBA.Food.Domain/{Restaurants, Menus, Orders, Stations, Staff}`,
`HBA.Deliveries.Domain/{Deliveries, Dispatch, Drivers, Pricing}`. Les extraire
suppose de decouper un `DbContext`, des migrations et une transaction. C'est la
raison pour laquelle l'arborescence est en place et le code ne l'est pas encore :
la premiere est verifiable en une commande, le second se prouve service par
service.

---

## 5. Ce qui a ete verifie

Aucun `dotnet` n'est installe sur la machine : **la solution n'a pas ete
compilee**. La verification est statique, et elle porte sur les chemins :

| Controle | Resultat |
|---|---|
| Projets references par `HBA.sln` qui existent | 118 / 118 |
| `ProjectReference` resolues | 0 cassee |
| `Protobuf` / `Compile` / `Content` resolus | 23 verifies, 0 cassee |
| `COPY` et `dotnet restore/publish` des Dockerfiles | 0 cassee |
| `dockerfile:` de `docker-compose.dev.yml` | 0 cassee |
| Chemins obsoletes dans `k8s/`, `infra/`, `scripts/`, `.github/` | 0 |

Les occurrences restantes de `merchant-service`, `food-service` ou
`financial-service` dans les `.csproj` sont **dans des commentaires** — la prose
d'origine du depot, laissee intacte.

**A faire avant de considerer la migration terminee :**

```bash
make restore   # dotnet restore HBA.sln
make build     # dotnet build HBA.sln
make up        # docker compose -f docker-compose.dev.yml up -d
```

---

## 6. Suites

1. `dotnet restore` puis `dotnet build` sur une machine equipee du SDK .NET 9.
2. Aligner les **noms d'execution** (compose, DNS, Prometheus) sur les noms de
   dossier — chantier de configuration, a faire d'un bloc.
3. Donner un hote API propre a `wallet-service` et `billing-service`, puis a
   `recommendation-service` et `wishlist-service`.
4. Extraire les squelettes un par un, dans cet ordre de risque croissant :
   `driver-service` et `dispatch-service` (agregats deja distincts), puis
   `menu-service`, puis `food-order-service` (transactions partagees).
5. Vider `_to_delete/` — coquilles vides de `src/` et `infrastructure/`, laissees
   la parce que la suppression n'etait pas possible depuis l'outil de travail.
