# Vingt-trois processus, vingt-sept dossiers, en quatre groupes

```
services/
├── common/        ce que PLUSIEURS domaines appellent  (11 dossiers →  7 processus)
├── marketplace/   la vente d'articles                  ( 6 dossiers →  6 processus)
├── food/          la restauration                      ( 3 dossiers →  3 processus)
└── delivery/      l'acheminement                       ( 7 dossiers →  7 processus)
```

**UN DOSSIER N'EST PAS UN SERVICE, ET L'ÉCART EST DE QUATRE (lot 9.3).**

Ce titre annonçait « les treize services » et ce tableau « 8 / 3 / 1 / 1 ». Aucun
des cinq nombres n'était juste, et le dépôt avait beaucoup changé depuis. Surtout,
la ligne `common/` confondait deux choses différentes :

| Dossier | Ce qu'il est | Son processus |
|---|---|---|
| `billing-service/` | **module hébergé** | payment-service |
| `wallet-service/` | **module hébergé** | payment-service |
| `recommendation-service/` | **module hébergé** | review-service |
| `wishlist-service/` | **module hébergé** | review-service |

Ces quatre-là ont la forme d'un service — quatre projets, base, migrations, schéma
— et **ni `Program.cs`, ni `Dockerfile`, ni entrée dans `docker-compose.dev.yml`**.
Ils s'exécutent dans le processus de leur hôte, qui appelle leur
`ModuleInstaller` dans son `Program.cs`. Chacun porte désormais un README qui le
dit en tête.

**L'audit d'août les a comptés comme quatre services de plus** — c'est-à-dire deux
fois chacun : une fois comme service à déployer, une fois comme module déjà
fourni. C'est le défaut symétrique des bandeaux « SQUELETTE » du lot 0.5, et il se
corrige de la même façon : en l'écrivant là où on regarde.

**Le critère qui tranche est le `Program.cs`**, pas le suffixe `-service` du
dossier. Cinq dossiers de `delivery/` sont par ailleurs des **squelettes** — ils
ont un processus, mais leur README porte un bandeau qui dit ce qu'il rend
vraiment.

## Le critère de rattachement, et pourquoi il n'est pas « le nom du service »

Un service va dans `common/` **quand plusieurs domaines l'appellent** — pas quand
son nom paraît générique. Le rattachement de trois services s'est décidé en lisant
leur code, et il aurait été faux en lisant leur nom :

| Service | Paraît | Est |
|---|---|---|
| `commerce-service` | Marketplace | **Commun** — le panier porte deux natures de ligne, marchandise ET repas, et refuse de les mélanger |
| `order-service` | Marketplace | **Commun** — une ligne de commande porte sa nature ; c'est ce discriminant qui décide d'expédier ou d'envoyer en cuisine |
| `engagement-service` | Marketplace | **Commun** — les avis portent sur des produits ET sur des restaurants |

Le diagramme d'architecture les montre dédoublés (« Marketplace Cart » / « Food
Cart », « Marketplace Order » / « Food Order »). Ce n'est pas le découpage retenu :
HBA en a **un seul de chaque**, qui porte les deux natures. Les ranger sous
`marketplace/` ferait croire que le domaine Food ne s'en sert pas — et c'est
justement l'erreur que la lecture du code évite.

`financial-service` suit la même règle et le diagramme le confirme : il place
*Payment* et *Wallet* dans « SERVICES COMMUNS ».

## Ce que ce regroupement ne change PAS

Aucun code métier n'a bougé : ce sont des déplacements de dossiers et la réécriture
des chemins qui en dépendent. Le découpage interne d'un service — `Domain`,
`Application`, `Infrastructure`, `Api`, `Contracts` — est inchangé, et correspond
déjà couche pour couche à celui du diagramme (`domain/`, `application/`,
`infrastructure/`, `interfaces/`).

**La pile reste .NET 9, YARP, PostgreSQL, Kafka.** Le diagramme décrit une pile
Node/TypeScript — `main.ts`, `package.json`, Kong, MongoDB, Consul. Il documente une
intention d'architecture, pas la technologie employée ; les deux ne se confondent
pas.

---


Chaque dossier porte le même squelette une fois rempli : `api/`, `domain/`,
`application/`, `infrastructure/`, sa base, ses tests, son Dockerfile.

## Règles qui ne se négocient pas

1. **Une base par service.** Pas de schéma partagé, pas de jointure entre deux
   services. C'est la seule règle dont la violation ne se voit pas avant qu'il soit
   trop tard pour la corriger.
2. **On ne parle qu'aux contrats.** Synchrone par gRPC/HTTP pour une lecture qui
   bloque une décision, asynchrone par Kafka pour tout le reste.
3. **Ce qui connaît deux services n'appartient à aucun des deux.** Dans le
   monolithe, ces raccordements vivent dans `Marketplace.Api/Integration/` ; ils
   deviendront des consommateurs d'événements.

## Correspondance avec les 29 modules — TABLEAU DE CIBLE, PAS D'ÉTAT

**Les noms de la colonne « Service » ne sont plus ceux du dépôt.** Il n'existe
ni `merchant-service` (c'est `seller-service`), ni `food-service`
(`restaurant-service`), ni `commerce-service` (`cart-service`), ni
`financial-service` (`payment-service`), ni `engagement-service`
(`review-service`). Et `Wishlist` n'est pas dans le panier mais hébergé par
review-service.

Ce tableau décrit le découpage VISÉ au moment de l'extraction. Il est conservé
pour cette raison-là, pas comme description de l'existant — le relire comme un
état conduit à chercher des dossiers qui n'existent pas.

### Correspondance avec les 29 modules (cible d'origine)

| Service | Modules | Base |
|---|---|---|
| identity-service | Identity | PostgreSQL |
| user-service | User | PostgreSQL |
| merchant-service | Sellers | PostgreSQL |
| catalog-service | Catalog, Product, Search | PostgreSQL |
| inventory-service | Inventory | PostgreSQL |
| food-service | Food | PostgreSQL |
| commerce-service | Cart, Wishlist, Pricing, Loyalty, Marketing | PostgreSQL + Redis |
| order-service | Ordering, Returns, Disputes | PostgreSQL |
| delivery-service | Delivery, Shipping | PostgreSQL |
| financial-service | Payments, Wallet, Billing, Tax, Fraud | PostgreSQL |
| engagement-service | Reviews, Recommendations, Analytics | PostgreSQL + ClickHouse |
| communication-service | Notifications, Messaging | PostgreSQL |
| media-service | Media | PostgreSQL + S3/MinIO |
