# Catalog Service — ce qui reste à implémenter

Audit du 18 août 2026, après le lot 1 (cycle de vie, révisions, condition
commerciale). Confronté au cahier `HBAExpress_Catalog_Service_DotNET9`.

---

## D'abord : un défaut ouvert, à fermer avant tout le reste

**Les trois routes produit publiques servent du contenu que personne n'a validé.**

```
GET /api/catalog/products              AllowAnonymous
GET /api/catalog/products/{id}         AllowAnonymous
GET /api/catalog/sellers/{id}/products AllowAnonymous
```

Toutes les trois projettent `ProductMapping.ToSellerSummary`, c'est-à-dire la
**révision courante** — celle que le vendeur édite. Et `GET /products` est câblée
sur `ListAllProductsQuery`, documentée dans son propre fichier comme « page de
produits pour la **console admin** » : sans paramètre `status`, elle ne filtre
rien.

Concrètement, un visiteur anonyme obtient aujourd'hui :

- les fiches en **brouillon**, en **attente de validation**, **rejetées** et
  **suspendues** ;
- pour une fiche publiée, la version **en cours de relecture** plutôt que la
  version approuvée ;
- le champ `facets`, qui donne la répartition du catalogue par statut.

Le §17 dit l'inverse en une phrase : « Elle ne doit retourner que la révision
publiée des produits PUBLISHED. »

**Le lot 1 a aggravé ce défaut sans le créer.** Avant les révisions, la fiche
n'avait qu'un seul jeu de champs : servir un brouillon exposait une fiche
incomplète. Maintenant qu'une fiche publiée porte une version non relue en
parallèle, la même route peut montrer au public un texte qu'un administrateur
vient précisément de refuser.

`ProductMapping.ToPublicSummary` existe déjà et rend `null` hors `Published`. Il
manque les requêtes qui l'utilisent.

---

## Où on en est

| Section | État |
|---|---|
| §5 Statuts, §4 règle absolue | **fait** — 8 statuts, liste blanche, double garde à la publication |
| §6, §8 Révisions | **fait** — révision publiée stable pendant une validation |
| §9 Condition commerciale | **fait** — 3 incohérences refusées |
| §20 Tables | **8 sur 19**, 2 sous un autre nom |
| §19 Événements | **8 sur 10** |
| §14 API vendeur | **partielle** |
| §16 API admin | **absente** |
| §17 API publique | **partielle, et fautive** (voir ci-dessus) |
| §22 RBAC, §24 codes, §25 pagination | **écarts structurels** |
| §29 Observabilité | **health checks seulement** |
| §28 Tests | **unitaires seulement** |

---

## §20 — Les tables

| Table du cahier | État |
|---|---|
| products | ✅ |
| product_revisions | ✅ |
| product_conditions | ✅ |
| product_condition_defects | ✅ |
| product_variants | ✅ |
| categories | ✅ |
| brands | ✅ |
| product_offers *(hors cahier)* | ✅ — porte le prix transactionnel (D12) |
| product_images | existe sous le nom **`product_media`** |
| outbox_events | existe sous le nom **`outbox_messages`** (code partagé) |
| product_prices | colonnes plates dans `product_revisions` — écart assumé, encadré dans `ProductRevisionConfiguration` |
| product_variant_attributes | colonne `jsonb` sur `product_variants` |
| attribute_definitions | ❌ |
| category_attributes | ❌ |
| brand_requests | ❌ |
| product_reviews | ❌ |
| product_review_reasons | ❌ |
| product_specification_groups | ❌ |
| product_specifications | ❌ |
| consumer_inbox | ❌ **non appliquée dans catalog** |

**`consumer_inbox` mérite une ligne à part.** La configuration existe dans
`shared/`, six services l'appliquent — pas catalog. Or catalog **consomme** deux
événements (`SellerClosed`, `SellerDeleted`) sans aucune protection contre le
rejeu. Kafka garantit « au moins une fois » : un redémarrage au mauvais moment
rejoue la fermeture d'un vendeur et dépublie une seconde fois des fiches déjà
dépubliées. Ici l'opération est idempotente par chance, pas par conception — la
prochaine ne le sera pas.

**`product_reviews` est citée par trois commentaires du code** comme « l'endroit
où vivent les motifs de rejet ». Elle n'existe pas. Un rejet ne conserve donc
aujourd'hui **aucun motif** : le vendeur apprend que sa fiche est refusée, jamais
pourquoi.

---

## §19 — Les événements

Huit sur dix, tous avec contrat, handler et enregistrement :
`submitted`, `approved`, `rejected`, `published`, `unpublished`, `suspended`,
`archived`, plus `restored` (ajouté, absent du cahier — sans lui, un consommateur
qui a masqué une fiche sur `suspended` n'apprend jamais qu'il peut la reprendre).

| Manquant | Pourquoi |
|---|---|
| `catalog.brand.requested` | l'agrégat « demande de marque » n'existe pas |
| `catalog.brand.approved` | idem |

Deux réserves sur l'existant :

- `catalog.product.created` **ne porte pas `[HbaEvent]`** : il part sur l'ancien
  sujet `service.catalog.v1`, pas sur `hba.<env>.catalog.product.v1`. Les sept
  autres l'ont. C'est le dernier événement produit du module resté sur l'ancien
  nommage.
- `ProductDeletedIntegrationEvent` existe **sans aucun handler ni
  enregistrement** : il n'est jamais publié.

---

## §14, §16, §17 — Les trois API

### Le préfixe

Le catalogue expose `/api/catalog/...`, le cahier demande `/api/v1/catalog/...`.
Le dépôt est mixte : media, user, identity et promotion utilisent `/api/v1/`, les
autres non. À trancher globalement, pas service par service.

### API vendeur (§14)

| Attendu | État |
|---|---|
| `POST /products` | ✅ |
| `PUT /products/{id}` | ✅ |
| `POST /products/{id}/variants` | ✅ |
| `POST /products/{id}/submit` | via `POST /products/{id}/status` `{"status":"PENDING_REVIEW"}` |
| `POST /products/{id}/publish` | idem |
| `POST /products/{id}/unpublish` | idem |
| `POST /products/{id}/archive` | idem |
| `POST /products/{id}/images` | `POST …/media` attend une **URL déjà déposée**, pas un envoi de fichier |
| `GET /products/{id}` | ❌ dans le groupe vendeur (seule la route publique existe) |
| `GET /products` | ❌ dans le groupe vendeur |

Les deux dernières lignes ne sont pas cosmétiques : **le vendeur n'a aujourd'hui
aucune route pour lire ses propres brouillons** autrement qu'en passant par la
vitrine anonyme. Fermer la fuite publique la lui retirerait — les deux corrections
vont ensemble.

### API admin (§16) — entièrement absente

| Attendu | État |
|---|---|
| `GET /products/reviews` (file de validation) | ❌ |
| `GET /products/{id}/review` | ❌ |
| `POST /products/{id}/approve` | ❌ |
| `POST /products/{id}/reject` | ❌ |
| `POST /products/{id}/suspend` | ❌ |
| `POST /products/{id}/restore` | ❌ |
| `POST /brands/requests/{id}/approve` | ❌ |

**Le domaine sait déjà tout faire** — `Product.Approve`, `Reject`, `Suspend`,
`Restore` existent et sont testés. **Aucune commande ni route ne les appelle.**

Conséquence directe, et c'est le blocage le plus visible du service :
`ChangeProductStatusCommandHandler` refuse les transitions d'administration en
renvoyant le vendeur vers « l'API admin » — qui n'existe pas. **Une fiche soumise
ne peut donc jamais être approuvée, et le parcours du §28 s'arrête à l'étape 4.**

Le groupe admin actuel ne contient que le référentiel : marques et catégories.

### API publique (§17)

| Attendu | État |
|---|---|
| `GET /categories` | ✅ (sans pagination) |
| `GET /brands` | ✅ (sans pagination) |
| `GET /products` | existe, **fautive** (voir le premier encadré) |
| `GET /products/{slug}` | ❌ — la recherche se fait par identifiant. Le slug existe en base et porte un index unique partiel sur les révisions publiées, mais aucune requête ne l'utilise |

Filtres du §17 attendus : `query, categoryId, brandId, sellerId, condition,
minPrice, maxPrice, attributes, rating, sort, page, pageSize`. Les seuls présents :
`search, status, sort, dir, page, pageSize`.

---

## Le transverse

### §25 Pagination

L'enveloppe `{success, data, pagination:{…}}` du cahier n'existe pas. Ce qui existe
dans `shared/` est `ApiEnvelope` — forme `{success, data, error, meta}` avec
`page/pageSize/total/hasNext`, **sans `totalPages` ni `hasPrevious`**.

Et le catalogue ne l'utilise **jamais** : toutes ses listes rendent l'objet nu
via `Results.Ok(items)`. Le corps de `GET /products` est un `PagedResult` brut.
Résultat : **succès à l'ancienne forme, erreurs à la nouvelle** — un client doit
gérer deux enveloppes selon que ça marche ou non.

### §24 Codes d'erreur

Le cahier liste 22 codes en SCREAMING_SNAKE (`PRODUCT_NOT_FOUND`,
`PRODUCT_NOT_APPROVED`…). Le domaine produit 60 codes pointés
(`catalog.product.not_approved`).

**La traduction est destructive.** `ApiResults.Problem` dérive le code sortant
du *type* d'erreur, pas de son code : huit valeurs possibles
(`VALIDATION_ERROR`, `CONFLICT`, `BUSINESS_RULE_VIOLATION`,
`CATALOG_SERVICE_NOT_FOUND`…). Le code fin survit dans
`error.details[0].message`, ce que personne ne lit.

Le §15 donne l'exemple exact du besoin : un client doit distinguer
`PRODUCT_NOT_APPROVED` de toute autre règle métier pour afficher le bon message.
Aujourd'hui il reçoit `BUSINESS_RULE_VIOLATION`, comme dix-sept autres cas.

### §22 RBAC

Le rôle `Seller` **existe comme constante et n'est exigé nulle part** ; il n'y a pas
de `MapSellerGroup`. Le groupe `/api/catalog/seller` n'exige qu'un jeton — donc
**tout compte authentifié, acheteur compris, y entre**.

La qualité de vendeur est vérifiée à chaud, par appel gRPC
(`ISellerModuleApi.GetSellerByUserIdAsync`), et l'appartenance de la fiche par
`DenyUnlessProductOwnerAsync`. Les douze routes produit la posent bien — les
bandeaux de commentaires qui annoncent la faille comme ouverte sont **périmés et
trompeurs**, à corriger.

Ce qui reste vrai : la permission `PUBLISH_APPROVED_PRODUCT` du §22 n'est portée
par aucune politique, et l'admin contourne les gardes d'appartenance par un
`IsInRole` écrit à la main plutôt que par les politiques partagées.

### §29 Observabilité

| Item | État |
|---|---|
| Health checks `/health/live`, `/health/ready` | ✅ |
| Rate limiting | filet global du socle + politiques de la passerelle ; aucune route catalogue ne déclare la sienne |
| OpenTelemetry | ❌ aucun paquet dans `HBA.Catalog.Api.csproj` |
| Serilog / logs JSON structurés | ❌ `ILogger` par défaut |
| OpenAPI / Swagger | ❌ les `.WithTags(...)` ne produisent aucun document |

Le service n'a **aucun `appsettings.json`** — toute sa configuration vient de
l'environnement.

### §26, §28 Tests

| Projet | État |
|---|---|
| `HBA.Catalog.UnitTests` | ✅ créé au lot 1 |
| `HBA.Catalog.AuthorizationTests` | ✅ *(hors nomenclature du cahier)* |
| `IntegrationTests` | ❌ |
| `ContractTests` | ❌ |
| `E2ETests` | ❌ — `tests/e2e/` ne contient qu'un README |

Le parcours E2E principal du §28 (créer → soumettre → 409 sur publish → approuver
→ publier → lecture publique) **ne peut pas être écrit aujourd'hui** : l'étape
« admin approuve » n'a pas de route.

---

## Plan proposé

| Lot | Contenu | Pourquoi dans cet ordre |
|---|---|---|
| **2** | Fermer la fuite publique + API publique du §17 (`GET /products` filtrée sur `Published`, `GET /products/{slug}`, filtres) + routes de lecture vendeur | C'est un défaut ouvert, pas une fonctionnalité manquante. Et les deux vont ensemble : filtrer la vitrine retire au vendeur son seul accès à ses brouillons |
| **3** | API admin §16 + `product_reviews` / `product_review_reasons` | Débloque le parcours entier. Sans lui, rien de ce qui a été construit au lot 1 ne peut être exercé de bout en bout |
| **4** | `attribute_definitions`, `category_attributes` (§10), `brand_requests` + les 2 événements manquants | Le formulaire vendeur du §13 (étape 8, « caractéristiques dynamiques de catégorie ») en dépend |
| **5** | Spécifications §12, table des attributs de variante, envoi d'images §14 | Complète la fiche produit |
| **6** | Enveloppe §25, codes d'erreur §24, `consumer_inbox` + idempotence, rôle `Seller` | Transverse. À faire d'un coup, sur tout le service |
| **7** | OpenTelemetry, Serilog, OpenAPI, tests intégration / contrat / E2E | Ce qui se vérifie en production |

**Deux décisions à prendre avant le lot 6**, parce qu'elles dépassent le
catalogue :

1. **Le préfixe `/api/v1/`** — quatre services l'utilisent, neuf non. Le trancher
   service par service produira des clients qui devront retenir lequel est lequel.
2. **Le format des codes d'erreur** — passer aux 22 codes du §24 touche
   `ApiResults` dans `shared/`, donc les quatorze services.

---

## Écarts assumés au lot 5

Deux points du lot 5 ont été traités autrement que ce que le plan laissait
attendre. Ils sont notés ici pour qu'on ne les reprenne pas par erreur comme des
oublis.

### 1. Les attributs de variante restent en `jsonb`

Le plan disait « table des attributs de variante ». Ils sont restés dans la
colonne `jsonb` de `product_variants`, comme `product_prices`.

La raison est la même que celle qui a fait choisir DEUX TABLES pour les
spécifications : **l'ordre**. Une fiche technique s'affiche dans un ordre voulu
par le vendeur, groupe par groupe — un `jsonb` ne le garantit pas, PostgreSQL
réordonne les clés. Les attributs de variante, eux, ne s'affichent pas : ils
servent à IDENTIFIER une variante (« Couleur=Noir, Taille=128 Go ») et sont
toujours lus en bloc, jamais parcourus. Leur ordre n'a aucun sens visible.

Le seul gain d'une table séparée serait de pouvoir filtrer la vitrine par valeur
d'attribut de variante. Ce besoin n'existe pas au §17, et le jour où il
apparaîtra, un index GIN sur le `jsonb` le couvrira sans migration de données.
Créer la table maintenant, c'est deux jointures de plus sur le chemin de lecture
le plus chaud du service, pour une requête que personne n'écrit.

### 2. La disponibilité Inventory ne figure pas sur la fiche publique

Le §17 montre un champ de disponibilité sur la fiche produit. Il n'y est pas.

Le rendre demanderait, pour CHAQUE produit d'une page de résultats, un appel à
inventory-service. Sur une page de vingt produits, c'est vingt allers-retours
gRPC synchrones avant de pouvoir répondre — le N+1 classique, invisible sur un
jeu de test de trois produits et fatal en production. `IInventoryModuleApi`
n'expose pas aujourd'hui de RPC par lot.

C'est donc **un manque de contrat, pas un oubli de câblage** : il faut d'abord
ajouter un `GetAvailabilityForProductsAsync(IReadOnlyList<Guid>)` côté Inventory.
Tant qu'il n'existe pas, mieux vaut une fiche sans disponibilité qu'une vitrine
qui s'effondre sous sa propre page de résultats.

---

## Ce que cet audit n'a pas vérifié

- **Rien n'a été exécuté contre une base réelle.** La migration de reprise n'a pas
  encore été rejouée à froid (`dev-up.sh --fresh`).
- Les performances des requêtes (les index sont posés, aucun plan d'exécution n'a
  été lu).
- Le comportement du cache après le passage aux révisions : l'invalidation a été
  étendue à `ProductRevision`, jamais observée en fonctionnement.

Une fausse alerte levée puis écartée pendant cet audit, notée ici pour qu'elle ne
revienne pas : l'instantané EF paraissait ne pas contenir les trois nouvelles
tables. Il les contient bien — c'était une copie de travail désynchronisée qui
mentait, pas le dépôt.
