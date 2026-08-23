# Reprise des données — passage aux révisions produit

La migration `20260818141400_AddProductConditionDefectsProductConditionsProductRevisions`
**contient déjà la reprise**. Il n'y a rien à coller : ce document explique ce
qu'elle fait, ce qu'elle ne fait pas, et comment vérifier qu'elle a bien fait ce
qu'elle dit.

---

## Le corps de cette migration a été réécrit à la main

`dotnet ef` a produit le bon schéma — et un `Up` qui aurait détruit les données.
L'instantané (`CatalogDbContextModelSnapshot`) reste **exactement** celui d'EF ;
seul l'ordre et le choix des opérations ont changé. Le schéma final est identique,
donc le prochain `migrations add` verra un modèle cohérent.

Trois défauts dans la version générée, du moins grave au pire :

1. **Les `DropColumn` venaient avant la création des tables.** Nom, description,
   catégorie et marque disparaissaient avant qu'aucune ligne n'ait pu être
   déménagée.

2. `CreatedOnUtc` était **renommé** en `UpdatedAtUtc`. Passe encore.

3. **`CategoryId` était renommé en `CurrentRevisionId`, et `BrandId` en `StoreId`.**

Le troisième point est celui qui compte. EF apparie par **type** : une colonne
`uuid?` qui disparaît et une autre `uuid?` qui apparaît lui ressemblent à un
renommage. Après application, chaque produit aurait porté l'identifiant de sa
**catégorie** dans `CurrentRevisionId` — pointant une révision inexistante — et
celui de sa **marque** dans `StoreId`.

Ces deux valeurs sont des uuid parfaitement formés. Aucune contrainte ne les
refuse, aucun journal ne s'en plaint. `Product.CurrentRevision` aurait levé au
premier chargement, et l'on aurait cherché le défaut dans le dépôt, pas dans la
migration.

**La leçon** : une migration générée sur un déplacement de colonnes se relit
ligne à ligne. `RenameColumn` est le mot à chercher.

---

## Ce que la migration fait

| Étape | Quoi |
|---|---|
| 1 | Ajoute les nouvelles colonnes de `products`, **toutes nullables** |
| 2 | Crée `product_revisions`, `product_conditions`, `product_condition_defects` |
| 3 | Déménage : une révision v1 par produit, une condition « Neuf » par révision, raccroche les identifiants, renomme `Active` → `Published` |
| 4 | Passe `CurrentRevisionId`, `CreatedAtUtc` et `UpdatedAtUtc` en NOT NULL |
| 5 | **Seulement alors** supprime les anciennes colonnes |
| 6 | Crée les index |

**L'étape 4 est un garde-fou, pas une formalité.** Si la reprise laisse une
ligne derrière elle, ces trois `ALTER` échouent et toute la migration est annulée.
Mieux vaut une migration qui refuse d'aboutir qu'une base à moitié reprise que
personne ne remarque.

`gen_random_uuid()` demande **PostgreSQL 13 ou plus** (fonction native depuis
cette version, extension `pgcrypto` avant).

---

## Ce qu'elle ne fait pas

- **`StoreId` reste NULL.** Aucune valeur n'est déductible : rattacher au hasard
  une fiche à l'une des boutiques du vendeur serait une erreur qui survivrait à ce
  document. La garde est dans le domaine — `SubmitForReview` refuse une fiche sans
  boutique — donc ces fiches restent lisibles et modifiables, et **ne peuvent plus
  avancer** tant que personne ne les rattache. C'est un travail à planifier.

- **Le prix de référence vaut 1 F** sur toutes les fiches reprises. Le domaine
  exige `basePrice > 0` (§23) : un 0 aurait rendu chaque fiche impossible à
  modifier. 1 F est faux aussi, mais visiblement faux. Le vrai prix n'est pas
  perdu — il vit dans `product_offers`, resté la source du prix transactionnel
  (décision D12).

- **Tout est en `Physical` et en `New`.** Aucune donnée antérieure ne dit le
  contraire, et rien ne permet de le deviner.

---

## Vérification après application

```sql
-- Autant de révisions que de produits, et une condition par révision.
SELECT (SELECT count(*) FROM catalog.products)           AS produits,
       (SELECT count(*) FROM catalog.product_revisions)  AS revisions,
       (SELECT count(*) FROM catalog.product_conditions) AS conditions;

-- Aucun produit orphelin de sa révision courante. Doit valoir 0.
SELECT count(*) FROM catalog.products p
LEFT JOIN catalog.product_revisions r ON r."Id" = p."CurrentRevisionId"
WHERE r."Id" IS NULL;

-- Aucune révision publiée sans produit publié, et réciproquement. Doit valoir 0.
SELECT count(*) FROM catalog.products p
WHERE (p."Status" = 'Published') <> (p."PublishedRevisionId" IS NOT NULL);

-- Plus aucun « Active ».
SELECT DISTINCT "Status" FROM catalog.products;

-- Les fiches à rattacher à une boutique.
SELECT count(*) FROM catalog.products WHERE "StoreId" IS NULL;
```

Puis, sur une base **neuve**, la vraie vérification :

```bash
./scripts/dev-up.sh --fresh
python3 scripts/check-migrations.py catalog-service   # doit passer
```

---

## Le retour en arrière

`Down` recopie dans `products` le contenu de la révision **courante** — donc pas
forcément celui d'avant la migration, si un vendeur a édité entre-temps — et
remappe les statuts vers les trois anciens.

Sont perdus sans retour : les révisions antérieures, les conditions commerciales,
les défauts déclarés, les prix de référence et l'historique de validation. Un
`Down` qui ne le dirait pas serait pire qu'un `Down` absent : on le lancerait en
croyant revenir en arrière.
