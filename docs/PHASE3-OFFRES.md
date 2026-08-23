# Phase 3 — Les offres

> Établi le 16 août 2026, après relevé exhaustif des deux dépôts.
> **Ce document corrige le périmètre annoncé par `AUDIT-APP-VENDEUR.md` et par la
> tâche #165.** Les chiffres ci-dessous sont comptés, pas estimés.

---

## 1. La correction : il ne faut pas extraire le module Product

L'audit disait « extraire Products/Offers — 33 fichiers ». C'est faux, et le faire
serait un dommage durable.

**`catalog-service` porte DÉJÀ l'agrégat `Product`.** La comparaison ligne à ligne
entre `HBA.Catalog.Domain.Products.Product` et `HBA.Products.Domain.Products.Product`
donne **13 propriétés communes sur 13** — `SellerId`, `CategoryId`, `BrandId`,
`Name`, `Slug`, `Description`, `ProductGroupId`, `Attributes`, `Tags`, `Status`,
`CreatedOnUtc`, `Variants`, médias. Mêmes signatures de `Create(...)`, même
mécanique de slug pré-dédupliqué — **avec le même commentaire mot pour mot**.

L'explication est simple : `catalog-service` **est** l'extraction de
`Modules/Catalog` du monolithe, fichier pour fichier. `Modules/Product` est une
**réécriture postérieure du même agrégat**, restée dans le monolithe.

Extraire tout `Modules/Product` produirait donc : deux services détenant la fiche
produit, deux tables `products`, deux `product_variants`, deux
`ProductCreatedIntegrationEvent`, deux sources de vérité — dont une, `hba_catalog`,
déjà peuplée et migrée trois fois.

## 2. Ce que le code a déjà décidé

Trois faits, antérieurs à cette décision, désignent **catalog-service** comme le
détenteur des offres :

1. **`shared/proto/catalog/v1/catalog.proto`** déclare les quatre RPC d'offre
   (`GetOffer`, `GetOffers`, `ListPurchasableOffers`, `ListOffersBySku`) sur le
   service **`CatalogApi`**, lignes 28-31 — et le `message OfferSummary` avec ses
   prix. Il n'existe aucun `service OffersApi` dans tout le dépôt.
2. **`ProductsGrpcClient`** résout son adresse depuis **`Services:Catalog`**
   (`ProductsGrpc.cs:220`).
3. **Rien n'attend un `products-service`** : aucune base `hba_products`, aucune
   entrée dans `docker-compose.dev.yml`, aucune clé `Services:Products` dans les
   trois fichiers de configuration.

Créer un service séparé demanderait de réécrire le proto, le client, les
enregistrements DI de commerce-service et communication-service, d'ajouter une
base, une entrée compose et une clé de configuration partout. Greffer les offres
dans catalog-service ne demande que d'implémenter **quatre `override`** dans
`CatalogGrpcService`.

## 3. Le vrai périmètre

Sur les 33 fichiers de `Modules/Product/` :

| | Fichiers | |
|---|---:|---|
| Purement **OFFRE** | **5** | ≈ 900 lignes — à reprendre |
| **Mixtes**, séparables par frontière de classe | **15** | à découper |
| Purement **PRODUIT** | 12 | **à NE PAS reprendre** — doublon |
| Technique | 1 | — |

Les cinq fichiers d'offre : `Offers/ProductOffer.cs` (380 l.),
`Offers/OfferStatus.cs` (104 l.), `Offers/Events/OfferDomainEvents.cs` (38 l.),
`Application/Offers/OfferCommands.cs` (227 l., 10 commandes),
`Application/Products/StoreCatalogCommands.cs` (158 l. — malgré son nom, il
n'agit que sur les offres).

**La coupe est nette, et ce n'est pas une chance.** Les quinze fichiers mixtes le
sont par *juxtaposition de classes déclarées*, pas par entrelacement :
`IProductOfferRepository:56`, `ProductOfferConfiguration:191`,
`ProductOfferRepository:88`, `OfferPricingSettings:95`, `OfferQueryHandler:168`,
`OfferSummary:39`. Aucune ne touche l'agrégat `Product` — **l'offre le référence
par identifiant**, ce que le domaine justifie explicitement.

## 4. Ce que cela débloque

- **Le panier de l'application cliente.** `AddItemToCartCommandHandler:72` appelle
  `GetOfferAsync`, qui **lève** aujourd'hui (`ProductsGrpc.cs:178`). Le handler
  rattrape en `cart.catalog_unavailable` : **aucun article ne peut entrer dans un
  panier**. C'est la conséquence la plus lourde, et elle est hors app vendeur.
- **Une exception non rattrapée** : `SellerLifecycleNotificationHandlers:267`
  appelle `ListOffersBySkuAsync` sans `try` — l'exception remonte dans un handler
  d'événement d'intégration.
- Côté vendeur : les six écrans mixtes, l'aperçu produit (qui ne montre jamais le
  prix acheteur), `OffersScreen`, et l'assistant de création de bout en bout.
- `OfferId` est déjà une **colonne obligatoire** dans commerce, order et financial :
  la donnée est attendue en base par trois services extraits.

## 5. Le plan

### 3.1 — Domaine
Reprendre les trois fichiers `Offers/` dans `HBA.Catalog.Domain/Offers/`.
Ne rien reprendre de `Products/`, `Variants/`, `Images/`, `Attributes/`.

### 3.2 — Persistance
`ProductOfferConfiguration` (extrait de `ProductsConfigurations.cs:191`) et
`ProductOfferRepository` (`ProductsRepositories.cs:88`) dans catalog-service. Une
migration ajoutant `product_offers` au schéma `catalog` — **le DDL exact est déjà
écrit** dans `20260811191545_InitialProducts.cs:54`, index compris
(`ux_product_offers_store_variant:233`).

### 3.3 — Application
`OfferCommands.cs` (10 commandes), `StoreCatalogCommands.cs`, la moitié offre de
`ProductQueries.cs` (`OfferDto:139`, `ListProductOffersQuery:164`,
`ListStoreOffersQuery:166`), et `IOfferPricingSettings`.

### 3.4 — gRPC : les quatre `override`
Implémenter `GetOffer`, `GetOffers`, `ListPurchasableOffers`, `ListOffersBySku`
dans `CatalogGrpcService`, et **supprimer les quatre `NotSupportedException`**.
C'est l'étape qui remet le panier client en service.

### 3.5 — HTTP
Les routes vendeur sous `/api/catalog/seller/offers`, dans le groupe déjà gardé.
Le monolithe les servait depuis son BFF vendeur ; côté HBA, la garde de
propriété doit être posée comme pour le stock (VEN11) — `ISellerModuleApi` est
déjà référencé par catalog-service.

### 3.6 — Application vendeur
Rebrancher `offers_data.dart` (11 méthodes `NotMigrated`), retirer `offers` de
`pendingModules`, et rétablir la bande « Mises en vente » de la fiche produit.

### 3.7 — Vérification
Le seul test qui compte : **un article entre dans un panier client**. Tant que
`AddItemToCartCommandHandler` retombe en `cart.catalog_unavailable`, la phase
n'est pas finie, quel que soit l'état des écrans vendeur.

---

## 6. Dette adjacente, à ne pas confondre avec cette phase

- **`AttributeSchema.cs`** (340 l.) et son lecteur : la colonne
  `Category.AttributeSchema` existe déjà en base et le contrat l'expose déjà ; il
  manque le *validateur*. Enrichit l'agrégat existant — ne crée pas de service.
- **`ProductStatus`** : 6 valeurs et des transitions dans le monolithe, 3 dans
  catalog-service. À trancher si la modération de fiches est voulue.

Ces deux points concernent le PRODUIT, pas l'offre. Les traiter ici brouillerait
une coupe qui est actuellement nette.
