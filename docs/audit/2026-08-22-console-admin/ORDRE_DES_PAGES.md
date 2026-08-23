# Console Admin — ordre d'implémentation des pages

*22/08/2026. Établi en lisant les groupes d'endpoints et les routes de la passerelle,
pas les intentions. Chaque classement ci-dessous renvoie à des routes vérifiées.*

---

## Le critère qui décide de tout : la route de LISTE

Un écran d'administration commence par une question — « qu'est-ce qui attend une
décision ? ». Sans route de liste, il n'y a pas d'écran : il y a un formulaire où l'on
colle un identifiant qu'on a trouvé ailleurs.

C'est ce qui sépare les dix-sept pages « à écrire » en deux moitiés très inégales, et
**quatre d'entre elles n'ont pas cette route**. Les classer avec les autres aurait donné
un planning faux d'un facteur dix.

| | Pages | Ce que ça coûte |
|---|---|---|
| **Amont complet** | 13 | un lot d'interface |
| **Amont amputé** | 4 | une route serveur, puis l'écran |
| **Aucun amont** | 8 | une extraction de service |

---

## RANG 1 — À faire d'abord : l'argent qui sort — ✅ **rang terminé le 22/08**

### 1. Retraits — ✅ **fait le 22/08**

**Amont complet, et plus riche que prévu — deux files distinctes.**

```
GET  /api/wallet/withdrawals/pending          vendeurs et livreurs
GET  /api/wallet/withdrawals/processing
POST /api/wallet/withdrawals/{id}/approve
POST /api/wallet/withdrawals/{id}/reject

GET  /api/wallet/customer-withdrawals/pending  clients
POST /api/wallet/customer-withdrawals/{id}/paid
POST /api/wallet/customer-withdrawals/{id}/reject
```

**Pourquoi en premier.** C'est le seul écran où l'absence coûte de l'argent *tous les
jours* : un retrait non traité est un vendeur qui ne peut pas payer son fournisseur.
C'est aussi le seul geste de toute la console qui déplace des fonds vers l'extérieur —
donc celui qui justifie le plus la ré-authentification déjà câblée au lot A3.

**Écran livré avec trois onglets** — vendeurs & livreurs en attente, virements en cours (lecture seule), clients — et les quatre gestes en rouge derrière une ré-authentification.

**Deux files, pas une.** Les mélanger dans un seul tableau ferait approuver un retrait
client avec le geste d'un retrait vendeur : ce ne sont pas les mêmes routes, ni les mêmes
états (`approve`/`reject` d'un côté, `paid`/`reject` de l'autre).

### 2. Paiements — ✅ **fait le 22/08**

```
GET  /api/payments            liste
GET  /api/payments/stats
POST /api/payments/{id}/capture | /fail | /refund
```

**Pourquoi ici.** `stats` donne à l'écran une tête sans travail supplémentaire, et
`capture`/`fail` sont les gestes de rattrapage quand un prestataire Mobile Money laisse un
paiement en suspens — ce qui bloque une commande côté client.

**Écran livré** : quatre chiffres de tête tirés de `stats`, liste paginée avec recherche et filtre de statut, trois gestes de rattrapage dont chacun ne s'active que sur les états où le domaine l'accepte.

`refund` existe **aussi** ici, en plus du parcours de retour. Deux chemins vers le même
geste : l'écran doit dire lequel il emprunte, sinon deux administrateurs rembourseront
deux fois.

### 3. Produits — ✅ **fait le 22/08**

```
GET  /api/v1/catalog/admin/products            paginé, recherche, statut, tri
GET  /api/v1/catalog/admin/products/reviews    la file de validation
GET  /api/v1/catalog/admin/products/{id}/review
POST /api/v1/catalog/admin/products/{id}/approve | /reject | /suspend | /restore
```

**Écran livré** : deux onglets — file de validation (par défaut) et catalogue complet avec recherche et filtre sur les huit statuts — et quatre gestes dont chacun ne s'active que sur les états acceptés par `ProductStatusTransitions`.

**Pourquoi ici.** C'est la file la plus volumineuse de la plateforme, et la seule qui
grossisse toute seule : chaque vendeur qui publie ajoute une fiche à valider. Elle a un
point d'entrée dédié (`products/reviews`) — l'amont a été pensé pour cet écran.

---

## RANG 2 — Ce qui débloque un parcours — ✅ **rang terminé le 22/08**

### 4. Commandes — ✅ **fait le 22/08**

```
GET  /api/admin/orders
POST /api/admin/orders/{id}/review/refund | /review/resume
POST /api/admin/food/orders/{id}/review/refund | /review/resume
```

`review/resume` débloque une commande retenue par une revue anti-fraude. Sans écran,
elle reste retenue.

**Deux univers, mêmes gestes** : marketplace et repas ont des routes distinctes. Un seul
écran avec un sélecteur d'univers, pas deux pages.

### 5. Livreurs — ✅ **fait le 22/08**

```
GET  /api/v1/admin/drivers          défaut : status=UnderReview, take=100
GET  /api/v1/admin/drivers/{id}
POST /api/v1/admin/drivers/{id}/verify | /reject | /suspend
```

Un livreur non vérifié ne peut pas prendre de course. C'est un goulot d'entrée, comme le
KYB vendeur — déjà traité au lot A3.

**Pas de pagination** : `take` plafonne à 100 et la réponse ne dit pas ce qui reste.
C'est la même limite que celle notée sur la tuile « Livreurs à vérifier » de l'accueil,
qui affiche `100+`.

### 6. Modération — ✅ **fait le 22/08**

```
GET  /api/food/admin/restaurants/pending
POST /api/food/admin/restaurants/{id}/approve | /reject | /suspend | /lift-suspension
```

**Moitié seulement, et c'est à savoir avant de commencer.** Les restaurants ont leur
file ; les **avis n'en ont pas**. On ne peut lire un avis que par produit ou par vendeur —
`GET /api/reviews/product/{id}`, `GET /api/reviews/seller/{id}`. Aucune route ne rend les
avis **signalés**, alors que `flag`, `reject` et `restore` existent.

Conséquence : l'écran ne sait pas montrer ce qui attend une modération d'avis. Il faut
soit s'en tenir aux restaurants, soit ajouter d'abord une route de file côté
engagement-service.

---

## RANG 3 — Gouvernance et référentiels

### 7. Rôles — ✅ **fait le 22/08**

```
GET    /api/identity/roles          liste
GET    /api/identity/roles/{id}
POST   /api/identity/roles
PUT    /api/identity/roles/{id}
PUT    /api/identity/roles/{id}/permissions
DELETE /api/identity/roles/{id}
```

**Écran livré.** Liste, création, renommage, remplacement des permissions et suppression — la suppression grisée sur les rôles système, seuls à la refuser.

**Il n'existe AUCUNE liste fermée de permissions** : `Permission.Create` valide une FORME, `^[a-z0-9_]+(\.[a-z0-9_]+)+$`, pas une valeur parmi N. L'écran accepte donc du texte libre et vérifie le motif — recopié du domaine — avant d'envoyer.

Amont complet. **À faire AVANT « Utilisateurs »** : assigner un rôle à quelqu'un suppose
de savoir quels rôles existent et ce qu'ils portent.

### 8. Marques — ✅ **fait le 23/08** · 9. Catégories — ✅ **fait le 23/08**

```
GET  /api/v1/catalog/brands                          liste publique, relayée
GET  /api/v1/catalog/admin/brands/requests           la file
POST /api/v1/catalog/admin/brands/requests/{id}/approve | /reject
POST /api/v1/catalog/admin/brands | /{id}/publish | /{id}/unpublish

GET  /api/v1/catalog/categories | /categories/{id} | /{id}/attributes
POST /api/v1/catalog/admin/categories | /{id}/publish | /{id}/unpublish
GET  /api/v1/catalog/admin/attributes
```

Les deux référentiels du catalogue. Une demande de marque en attente bloque la mise en
vente d'un produit — donc à traiter avant que la file « Produits » ne se remplisse pour
cette raison-là.

La liste des marques et celle des catégories sont sur le groupe **public** du catalogue,
pas sur son groupe admin. Les deux passent par la même route relayée ; il n'y a rien à
ouvrir, seulement à savoir où regarder.

**Écran « Marques » livré.** Deux listes côte à côte — la file des demandes des vendeurs et
le référentiel complet — parce que le geste utile les relie : approuver une demande, c'est
le plus souvent la **rattacher** à une marque existante (« samsumg » vers « Samsung »),
et non en créer une seconde. Le rattachement vise la marque sélectionnée dans le
référentiel ; le bouton porte son nom pour qu'on ne se trompe pas de cible.

Trois écarts relevés en écrivant l'écran, tous côté serveur, aucun corrigé ici :

1. **`DELETE /brands/{id}` supprime sans rien vérifier.** `DeleteBrandCommandHandler`
   charge la marque et appelle `Remove` : ni comptage de fiches, ni refus. Et
   `ProductRevisionConfiguration` déclare `BrandId` comme une simple propriété indexée,
   **sans clé étrangère** vers `brands` — la base ne s'y oppose pas non plus. Les révisions
   qui portaient l'identifiant continuent de le porter, vers une ligne disparue. Rien ne
   casse bruyamment : le filtre par marque de la vitrine cesse de les trouver. L'écran
   exige motif écrit **et** ré-authentification ; il ne peut pas faire mieux, aucune route
   ne permet de demander « combien de produits citent cette marque ».
2. **`Brand.Archive()` n'est appelé par aucun endpoint.** « Dépublier » ramène à `Pending`,
   pas à `Archived`. Il n'existe donc aujourd'hui aucun moyen de retirer une marque du
   référentiel autrement qu'en la supprimant — ce qui n'est pas la même chose.
3. **Le slug ne suit pas le nom.** Calculé à la création, il sert de clé d'unicité :
   renommer « Samsng » en « Samsung » laisse le slug « samsng ». L'écran l'affiche en
   lecture seule pour que personne ne croie le corriger en corrigeant le nom.

**Ce que l'écran ne montre pas :** l'historique des demandes tranchées.
`ListPendingAsync` filtre sur `Status == Pending` ; les champs `RejectionReason`,
`BrandId` et `ReviewedAtUtc` du contrat arrivent donc toujours nuls. Il faudrait une
route de liste acceptant un statut — elle n'existe pas. Et le vendeur n'est identifié que
par son `SellerId` : `BrandRequestSummary` ne porte pas le nom de la boutique, et rien ne
joint les deux services côté serveur. Le GUID est affiché tel quel plutôt qu'un libellé
inventé.

**Écran « Catégories » livré.** L'arbre à gauche — reconstruit localement en triant sur le
chemin matérialisé, ce qui donne un parcours en profondeur sans code récursif — et à
droite l'identité de la catégorie **plus son schéma d'attributs exigés**, qui est la
partie qui compte.

**Ce n'est pas un écran de présentation.** Publier une catégorie décide de sa présence
en vitrine ; rattacher un attribut **requis** décide qu'une fiche sans cet attribut sera
refusée à la soumission — `ChangeProductStatusCommandHandler` appelle
`ValidationDesAttributs.Valider` au passage en `PendingReview`. L'effet est immédiat et
vaut aussi pour les **brouillons déjà commencés**, qui buteront dessus sans préavis.
L'écran demande donc un motif écrit avant de cocher « requis ».

**Deux mécanismes portent le mot « schéma », et ils ne sont pas reliés.** La colonne
`attribute_schema` (`jsonb`) est stockée et rendue au contrat ; la validation, elle, lit
la **table** `category_attributes` via `ListByCategoryAsync`. Modifier le JSON ne change
rien à ce qu'un vendeur doit renseigner. L'écran affiche les deux, en le disant.

Trois écarts serveur relevés, aucun corrigé ici :

1. **Renommer coupe la branche.** `Category.Update` recalcule `Slug` et `Path` de la ligne
   modifiée et pas ceux de ses descendants — le domaine le documente lui-même :
   « les chemins des descendants ne sont pas répercutés (évolution future) ». Or
   `ListDescendantsAsync` cherche `Path.StartsWith(parent + "/")`. Après un renommage, une
   publication en cascade rend `affected = 1` et ne touche plus aucun enfant, **sans erreur
   ni message**. L'écran compare chaque chemin à celui de son parent déclaré, affiche un
   bandeau et une pastille sur les lignes concernées, et prévient avant d'enregistrer un
   renommage sur un nœud qui porte une branche. Il ne peut pas réparer : aucune route ne
   réécrit un chemin.
2. **`DELETE /categories/{id}` ne compte ni les enfants ni les fiches**, et `ParentId` n'a
   pas de clé étrangère. Supprimer un nœud intermédiaire laisse la branche entière avec des
   parents inexistants. L'écran **refuse** d'envoyer quand la catégorie a des enfants connus
   — c'est une règle du client, pas du serveur — et exige motif plus ré-authentification
   pour une feuille. Il reste aveugle aux produits : aucune route ne permet de les compter.
3. **`Category.Archive()` n'est appelé par aucun endpoint**, et `Publish()`/`Unpublish()`
   refusent tous deux l'état `Archived`. Une catégorie archivée ne peut donc plus rien
   faire ; cet état n'est atteignable que par un import ou une écriture directe.

**Un piège de type au passage :** `attribute_schema` est un `jsonb` et **aucun validateur
ne le regarde** — ni `CreateCategoryCommandValidator` ni `UpdateCategoryCommandValidator`.
Un JSON mal formé est refusé par PostgreSQL, donc en **500**. L'écran valide la forme avec
`JsonDocument.Parse` avant d'envoyer.

### 10. Tarification — ✅ **fait le 23/08** · 11. Commissions — ⛔ **bloqué, passerelle**

```
GET   /api/v1/admin/delivery-pricing/rules
POST  /api/v1/admin/delivery-pricing/rules | /{id}/activate | /{id}/deactivate
PATCH /api/v1/admin/delivery-pricing/rules/{id}

GET  /api/financial/commissions | /commissions/compute
POST /api/financial/commissions | /{id}/deactivate | /{id}/reactivate
PUT  /api/financial/commissions/{id}
```

Deux écrans de règles, rarement touchés, mais dont chaque modification change le prix payé
par tout le monde.

#### Tarification — écran livré

La liste marque **la règle qui gagne**, et c'est l'information centrale de la page. Trois
choses que le service ne dit nulle part, et que l'écran dit :

1. **Une seule règle tarife toute la plateforme.** `CreateQuoteAsync` filtre sur
   `Status == "ACTIVE"` et la fenêtre `ActiveFrom`/`ActiveTo`, trie par `Priority`
   décroissante et prend la première. **`Scope`, `ServiceLevel` et `VehicleType`
   n'apparaissent pas dans ce filtre** : ils sont stockés, rendus au contrat, affichés — et
   n'entrent pas dans le choix. Une règle « EXPRESS · priorité 200 » tarife donc aussi les
   courses standard, et une règle « MOTORBIKE » tarife les voitures. C'est l'écart le plus
   coûteux du lot : on croit régler un segment, on reprend toute la grille.
2. **Désactiver la dernière règle éligible casse le passage de commande.** La requête finit
   par `FirstAsync`, pas `FirstOrDefaultAsync` : sans règle, elle **lève**, et tout devis
   répond 500 — donc plus aucune commande marketplace ni repas, le devis se relisant chez
   delivery-pricing avant la création de la course. Le semis de secours ne rattrape pas :
   `EnsureSeedAsync` n'insère « Cotonou standard » que si la table est **vide**, pas si les
   règles existent et sont toutes inactives. L'écran refuse ce geste — garde du client, le
   serveur l'accepte sans rien dire.
3. **Un minimum supérieur au plafond fait lever chaque devis.**
   `Math.Clamp(subtotal, MinFee, MaxFee)` jette `ArgumentException` quand `min > max`. Il
   n'existe **aucun validateur** sur `PricingRuleRequest` : le couple est accepté et stocké.
   L'écran refuse de l'enregistrer, comme il refuse une date de fin antérieure au début et
   un multiplicateur négatif — trois refus que le serveur n'oppose pas.

À priorité égale, `OrderByDescending(Priority)` sans départage laisse la base rendre l'une
ou l'autre : l'écran signale les priorités en doublon parmi les règles éligibles.

**L'aperçu de prix est un calcul local, et il est étiqueté comme tel.** Aucune route ne
simule : `POST /quotes` crée un devis **réel**, persisté et publié en événement. Les cinq
lignes de `PricingPolicy` sont donc recopiées côté client pour montrer l'effet d'un réglage
avant de le facturer. Duplication assumée, et son risque est écrit dans le code : si le
service change sa formule, l'aperçu ment jusqu'à ce que quelqu'un le remarque.

Le `PATCH` est un **remplacement complet** malgré son verbe : `UpdateRuleAsync`
reconstruit l'enregistrement depuis la requête. Un plafond effacé **retire** le plafond, et
`ActiveFrom` n'étant pas nullable, un corps sans ce champ vaudrait `0001-01-01`, accepté
sans broncher. L'écran envoie toujours les treize champs. `Status`, lui, n'est pas dans la
requête : il ne se change que par `activate`/`deactivate`.

#### Commissions — bloqué, et pas là où on l'attendait

**Le service est complet ; la passerelle n'y mène pas.** billing-service expose sept routes
sous `/api/financial/commissions` — liste, `compute`, création, mise à jour, désactivation,
réactivation, suppression — et `appsettings.json` de la passerelle n'a **aucune entrée**
vers ce préfixe. Ses seules routes `financial` sont `settlements`, `wallets` et `payments`.
La console ne parle qu'à la passerelle : vue d'ici, tout répond 404.

**Et ajouter la ligne manquante ne suffit pas.** `commissions.MapGet("/", …)` n'a **pas** de
`.RequireAdmin()`, contrairement aux cinq écritures voisines. La liste porte les règles de
portée `Seller`, c'est-à-dire le **taux négocié vendeur par vendeur** — exactement la donnée
que le commentaire de `ComputeCommissionAsync` décrit comme la fuite qu'il vient de
refermer : « tout inscrit calculait la commission d'un concurrent […] la donnée sur laquelle
on décide de casser un prix ». Relayer la liste telle quelle rouvrirait cette fuite par une
autre porte.

**Deux corrections en amont, dans cet ordre :**

1. `.RequireAdmin()` sur `GET /api/financial/commissions` ;
2. une route de passerelle vers ce préfixe.

Aucune des deux n'est un geste de console, et c'est pourquoi l'écran n'a pas été écrit :
une page qui suppose une route ouverte à la va-vite serait la vraie faute de ce lot.

Le moteur, lui, est sain et mérite d'être noté : résolution `Seller > Category > Global` par
`Priority => (int)Scope`, départage par `EffectiveFromUtc` décroissante, et **repli sur le
taux par défaut plutôt que sur zéro** — un handler antérieur recopiait le résolveur et
rendait `0` quand rien ne matchait, si bien que l'aperçu annonçait « commission : 0 » pendant
que la comptabilisation prélevait 10 %. Cette copie a été supprimée ; `ComputeCommissionQuery`
délègue désormais au moteur. `AppliedRuleId` nul signifie « aucune règle, taux par défaut »,
et c'est ce que l'écran devra afficher le jour où il existera.

---

## RANG 4 — Lecture et pilotage

### 12. Settlement · 13. Portefeuille · 14. Stock — ✅ **les trois faites le 23/08**

```
GET  /api/financial/settlements | /{id} | /sellers/{id}/statement
POST /api/financial/settlements | /{id}/cancel
POST /api/financial/settlements/{batch}/payouts/{id}/paid | /failed

GET  /api/wallet/platform | /platform/transactions
GET  /api/wallet/sellers/{id} | /drivers/{id}

GET  /api/inventory/low-stock | /locations
```

Trois écrans de **lecture**. « Amont complet » était vrai du service et faux de la
passerelle sur le premier des trois — voir ci-dessous.

#### 12. Reversements

⛔ **Les quatre écritures ne passent pas la passerelle, et c'est délibéré.** La route
`settlements` déclare `Methods: [GET, HEAD, OPTIONS]`, avec ce motif dans ses métadonnées :
« le lancement d'un règlement vit sous /api/financial/settlements dans un groupe
MapAdminGroup voisin ; une route sans restriction de méthode l'exposerait au proxy. Le
service refuserait, mais on ne compte pas là-dessus. » Lancer un lot, l'annuler, marquer un
versement payé ou échoué s'exécutent donc **depuis le réseau interne**.

C'est justifié : marquer un versement payé **débite le vendeur sans retour possible** — le
déclarer ensuite échoué est refusé, puisque du point de vue du système l'argent est parti
(ISSUE-015). L'écran **nomme** ces quatre gestes plutôt que d'afficher des boutons qui
rendraient 404.

Ce qu'il apporte au-delà de la liste :

- **La somme des nets face au total annoncé du lot.** Les deux viennent du même agrégat :
  s'ils divergent, ce n'est pas un décalage de période, c'est une donnée abîmée — et rien
  d'autre ne le dirait.
- **« payé sans référence opérateur ».** `MarkPayoutPaidAsync` prend un `ProviderReference` :
  un versement marqué payé sans référence a été déclaré à la main, sans trace côté
  prestataire. C'est le seul état de la liste qui mérite qu'on s'arrête.
- **Le relevé du vendeur sur la période, à la demande** — et l'écran ne calcule
  **aucune** différence avec le versement, volontairement : le relevé filtre les gains sur
  `CreatedAtUtc`, le lot sur `ReleasedAtUtc` avec le statut `Released`. Deux axes de temps.
  Un écart est normal, et un « delta à justifier » enverrait chercher une erreur inexistante.

#### 13. Portefeuille

**Quatre poches, et l'écran ne les additionne pas.** Commissions est un revenu, frais
opérateur une dépense, livraison un encaissement, remboursements une sortie. Un total
mélangerait les sens et personne ne pourrait dire qu'il est faux.

**`take` est obligatoire sur les routes wallet** : `ListPlatformWalletTransactionsAsync(int take, …)`
déclare un `int` nu, si bien que la valeur par défaut de
`ListPlatformWalletTransactionsQuery(int Take = 50)` **n'est jamais atteinte depuis HTTP** —
la liaison échoue avant, sur un paramètre requis absent. À comparer avec
`LowStockAsync(int? take, …)` d'inventory-service, qui accepte l'absence. Deux services,
deux conventions.

**La consultation d'un compte se fait par GUID, faute de route de liste** : ni les
portefeuilles vendeurs ni ceux des livreurs n'en ont une. L'identifiant se copie depuis
Vendeurs, Livreurs, ou une ligne de reversement. L'accès passe parce que
`DenyUnlessOwnSellerAsync` et `DenyUnlessOwnDriverAsync` court-circuitent sur `Admin` /
`Moderator` avant même de chercher un dossier.

Le sens d'une écriture vient de `Direction`, **pas du signe du montant** : `Amount` est
toujours positif. Un relevé qui afficherait le montant sans le sens ferait tout
s'additionner.

#### 14. Stock

**Les deux routes se complètent, et l'écran fait la jointure.** `low-stock` rend des
articles qui ne portent qu'un `LocationId` ; `locations` rend les lieux avec commune,
quartier et téléphone. Une alerte réduite à un GUID d'entrepôt ne se traite pas : il faut
savoir où aller. Si les lieux manquent, les alertes restent affichées avec leur GUID — une
alerte mal étiquetée vaut mieux qu'une alerte cachée.

**C'est une alerte, pas un inventaire, et le serveur s'en assure.**
`ListLowStockQueryHandler` plafonne à 200 quoi qu'on demande : « le plafond est posé ICI,
dans l'application, et non laissé au client : un `take` venu de la requête ne doit jamais
pouvoir rouvrir le balayage complet que ce lot ferme. » Aucune route ne rend le stock
complet de la plateforme. L'écran le dit plutôt que de le laisser découvrir.

**« Rupture » porte sur le disponible, pas sur le physique.** Un article peut avoir des
cartons en entrepôt et zéro vendable : la différence est ce que des commandes en cours ont
réservé. Les deux situations se règlent autrement — réapprovisionner d'un côté, débloquer
une commande de l'autre — et l'écran les distingue par deux pastilles.

---

## RANG 5 — ✅ **fait le 23/08** : trois routes serveur, trois écrans

**Trois des quatre blocages sont levés.** Le quatrième n'était pas celui qu'on croyait.

| Page | Diagnostic initial | Ce qu'il en était |
|---|---|---|
| **Utilisateurs** | aucune liste | ✅ la **requête existait déjà**, entièrement écrite — il manquait UNE route |
| **Remboursements** | aucune liste | ✅ route + requête + dépôt écrits |
| **Modération (avis)** | aucune file | ✅ route + requête + dépôt écrits |
| **Factures** | aucune liste | ⛔ **la passerelle ne mène pas à `/api/financial/invoices`** — même mur que Commissions |

### Utilisateurs — la requête était là, personne ne l'appelait

`ListUsersQuery`, son gestionnaire et `UserRepository.ListPagedAsync` existaient depuis le
début : **recherche, filtre de statut, tri, pagination et comptage par statut**, tout était
écrit. Aucune route ne montait l'ensemble. C'était du code mort — jamais exécuté, donc
jamais éprouvé.

Ce que son absence coûtait : les cinq gestes d'administration sont **tous adressés par
GUID**. Sans liste, il fallait connaître l'identifiant d'un compte pour le suspendre —
c'est-à-dire interroger la base à la main.

Ajouté : `GET /api/identity/users`, rendu en `ApiResults.Page` (qui préserve les facettes
dans `meta` et porte le `requestId`).

**La recherche ne porte que sur le prénom et le nom.** `UserRepository.ListPagedAsync`
l'explique : « ILike uniquement sur des colonnes string simples : Email/PhoneNumber sont des
value objects convertis, non traduisibles. » C'est une limite réelle, et c'est précisément
la façon dont on cherche un compte en support — par son e-mail. **La console devra le dire
à l'écran** plutôt que de laisser croire à une recherche globale qui ne trouve rien. Rendre
l'e-mail cherchable est le prochain petit lot serveur sur ce service.

### Remboursements — `GET /api/v1/admin/returns`

Ajouté : `ListForAdminAsync` au dépôt, `ListAdminReturnsQuery`, et la route sur le groupe
admin existant.

**Pas de « file des litiges » codée en dur.** `ReturnStatus` compte seize états ; décider
dans le serveur lesquels pressent y figerait un jugement d'exploitation. La route rend tous
les dossiers, filtrables par statut, avec le compte de chaque statut dans `meta.facets` —
c'est l'écran qui met en avant ce qui doit l'être.

🐛 **Défaut voisin corrigé au passage.** `GetCustomerReturnsQueryHandler` et
`GetSellerReturnsQueryHandler` construisaient leur `PagedResult` avec **`items.Count` en
guise de total** — c'est-à-dire la taille de la page. `TotalPages` en déduisait toujours
UNE page : un client ayant plus de vingt retours n'en voyait que vingt, sans rien qui
indique qu'il en existait d'autres. Deux `Count` ajoutés au dépôt, deux totaux exacts.

### Modération (avis) — `GET /api/engagement/reviews/moderation`

`flag`, `reject` et `restore` étaient montés depuis le début sur le groupe admin, **adressés
par identifiant d'avis**. Rien ne disait quels avis attendent : `ListByProductAsync` ne rend
que le publié, `ListBySellerAsync` demande un vendeur. Un avis signalé restait donc `Flagged`
jusqu'à ce que quelqu'un tombe dessus — c'est-à-dire jamais. La modération existait sur le
papier et pas dans les faits.

**`/moderation` et non `/`** : le groupe admin partage son préfixe avec le groupe
authentifié, qui monte déjà `MapGet("/{id:guid}")` et `MapPost("/")`. Un `MapGet("/")` ici
aurait été une seconde route sur le même chemin, arbitrée par l'ordre d'enregistrement —
sans erreur pour le signaler.

La file est triée **du plus ancien au plus récent** : une file de modération se traite par
le bas, à l'inverse des listes de vitrine.

**Il n'existe pas d'index sur `(Status, CreatedAtUtc)`.** L'index `(SellerId, Status)`
posé pour la note vendeur ne sert pas cette requête, qui n'a pas de vendeur. Ce n'est pas
un problème aujourd'hui ; ce le deviendra quand la table grossira.

### Factures — bloqué à la passerelle, pas au service

Le diagnostic initial était faux. Il ne manque pas seulement une liste : **`appsettings.json`
de la passerelle n'a aucune entrée vers `/api/financial/invoices`.** Ses seules routes
`financial` sont `settlements`, `wallets` et `payments`. Même mur que Commissions, et même
prudence à observer avant de le percer — une liste de factures plateforme expose le chiffre
d'affaires commissionné vendeur par vendeur, exactement la donnée que le service a fermée
sur ses lectures voisines.

Deux corrections en amont, dans cet ordre : une liste **admin-only** dans billing-service,
puis une route de passerelle. Aucune des deux n'est un geste de console.

### Les trois écrans

**Utilisateurs.** Liste paginée, onglets de statut portant le compte de **chaque** statut
(les facettes sont calculées avant le filtre, donc « Suspendus (3) » reste lisible pendant
qu'on regarde les actifs), panneau d'identité, suspension/réactivation et gestion des rôles.

L'écran affiche en clair **ce que la recherche ne sait pas faire** — ni e-mail ni téléphone —
parce qu'une liste vide se lit « ce compte n'existe pas », et que c'est faux.

Il distingue aussi **« vérifié » de « vérifié sur parole »** : `EmailVerifiedByAdminOnUtc`
existe précisément pour cela, et le contrat le demande — « "Oui" et "Oui, sur parole" ne
valent pas la même chose ».

Sur un compte `Deleted` — anonymisé à la demande de son titulaire — les deux boutons sont
grisés : le domaine ne fournit aucun chemin de retour, « les données d'origine n'existent
plus, il n'y a rien à restaurer ».

🐛 **Une affirmation corrigée avant d'être écrite dans l'écran.** J'allais afficher que
suspendre « ne révoque pas les sessions en cours ». Faux : `User.Suspend()` appelle
`RevokeAllRefreshTokens()`, et la passerelle interroge identity à **chaque** requête
authentifiée (`TokenRevocationMiddleware`), avec un cache de 30 s. Le jeton d'accès cesse
donc de passer en moins d'une minute. La seule réserve est réelle et vaut d'être dite :
ce contrôle **échoue ouvert** (D27), donc pendant une panne d'identity un compte suspendu
garde ses droits.

**Retours et remboursements.** Onglets par statut — seulement ceux qui ont des dossiers,
plus l'onglet actif même vidé, sinon le filtre disparaît sous la main de celui qui vient de
traiter le dernier dossier. `ManualReview` en tête : c'est le seul des seize états qui
attend explicitement un humain.

**Le nom de la route n'est pas le nom du geste.** `POST /{id}/override` envoie
`RejectReturnCommand` : ce geste **rejette**. Le bouton dit « Rejeter le dossier », pas
« Arbitrer » ni « Passer outre » — l'un et l'autre laisseraient croire à un déblocage.

**Asymétrie de l'API elle-même** : le filtre s'envoie en **nom** (`?status=ManualReview`,
lu par `Enum.TryParse`) et le statut revient en **numéro** — `ReturnRequestDto` porte les
énumérations telles quelles et aucun `JsonStringEnumConverter` n'est enregistré dans le
dépôt. Le client envoie un mot et relit un chiffre ; la table de correspondance est recopiée
de `ReturnEnums.cs` et le commentaire le date.

`ResolutionRequested` n'est **pas** affiché : le domaine dit lui-même qu'« aucune des cinq
valeurs n'est jamais posée ». Le montrer laisserait croire à une décision jamais prise, sur
l'écran même où on la prend.

**Modération des avis** — entrée distincte de « Modération », qui arbitre des restaurants.
Deux files, deux métiers, deux rythmes.

Trois gestes qui **réécrivent une réputation** : la note d'un produit et celle d'un vendeur
ne comptent que les avis `Published`. Rejeter sort de la moyenne, restaurer y remet —
d'où le mot de passe sur ces deux-là, et pas sur « signaler », qui ne retire rien de la
vitrine. Un avis **sans achat vérifié** porte une pastille : c'est le premier critère de
relecture.

**Rien n'enregistre qui a signalé un avis, ni pourquoi** : le domaine ne porte qu'un
statut. Un avis « signalé » attend donc une relecture sans motif joint — c'est le texte
lui-même qu'il faut juger, et l'écran le dit.

### État de la console

**21 sections prêtes sur 29** — Factures et Commissions se sont ajoutées le 23/08 (lots 4+5
du plan des pages restantes), puis **Recommandations**, qui n'existait dans aucun rang de cet
audit : la surface était réelle et sans écran, repérée seulement à l'audit des pages restantes.
Restent les **huit sections du rang 6**, qui demandent chacune un service, pas un écran.

Le mur des deux dernières était le même : billing-service exposait tout, la passerelle ne
menait à rien. Il a été franchi **dans cet ordre** — `.RequireAdmin()` sur
`GET /api/financial/commissions`, `ListInvoicesQuery` puis
`invoices.MapGet("/").RequireAdmin()`, et **seulement ensuite** les deux entrées de
passerelle. Relayer avant de garder aurait publié la grille tarifaire négociée de la
plateforme, vendeur par vendeur, pendant toute la durée d'un déploiement.

**Ce que ces deux écrans ne couvrent pas** : le détail d'une facture n'est rendu par aucune
route — `InvoiceMapper.ToSummary` laisse tomber les `InvoiceLine`, donc une ligne ajoutée
n'est jamais relue et seul le total bouge ; « marquer payée » ne fait que constater, sans
mouvement de portefeuille ni conservation de la référence saisie ; et le taux par défaut
appliqué faute de règle vit dans la configuration du service, pas dans la grille.

**Recommandations** n'avait aucun mur de passerelle : `/api/recommendations` est relayée
depuis un lot antérieur et couvre tous les verbes. Ce qui manquait était une **lecture
d'ensemble** — les trois lectures du service sont toutes adressées, par produit ou par
utilisateur, si bien qu'on écrivait la page d'accueil sans jamais pouvoir la relire. Même
situation que les avis avant la file de modération. La liste a été montée sur le groupe
**admin**, parce qu'elle dit quels produits la plateforme pousse et sur les fiches de qui.

**Ce que cet écran ne couvre pas** : rien ne SUPPRIME une recommandation, le dépôt n'exposant
qu'`AddAsync` et deux lectures ; l'enregistrement REMPLACE la liste entière de sa clé sans que
le serveur distingue création et écrasement ; et rien ne distingue une ligne écrite à la main
d'une ligne calculée — un recalcul du moteur remplacera l'une comme l'autre.

---

## RANG 6 — Aucun amont : ce n'est pas un écran, c'est un service

| Page | Ce qu'il faudrait construire |
|---|---|
| **Analytics** | l'agrégation était dans le BFF du monolithe ; ni service ni route ne la rend. Bloque aussi l'écran `analytics` de l'app vendeur |
| **Marketing** | promotion-service est **entièrement** vendeur. Une campagne plateforme n'a ni agrégat ni route |
| **Notifications** | `/api/notifications` est la surface du DESTINATAIRE. Aucun envoi de masse, aucun gabarit |
| **Taxes** | le mot n'apparaît nulle part dans les services |
| **Bannières** | aucun service de contenu éditorial — même manque que `content` côté app cliente |
| **Fraude** | aucun score, aucune règle |
| **Outbox** | table interne, délibérément non exposée : elle porte des charges utiles d'événements, donc des données personnelles. Demanderait une projection dédiée |
| **Monitoring** | Prometheus et Grafana tournent déjà. Un lien vers Grafana, pas un écran |

---

## Ce que cet audit ne dit pas

- **Il ne mesure pas la complexité d'un écran à amont égal.** « Produits » et « Stock » ont
  tous deux un amont complet ; le premier demandera trois fois plus de travail. Le
  classement suit la valeur et le blocage, pas la durée.
- **Il n'a pas éprouvé les réponses.** Les routes sont lues dans le code, pas appelées. Une
  route qui existe peut rendre une forme que l'écran n'attend pas — c'est exactement ce
  qui s'est produit avec `kybStatus=Pending`, valeur inexistante acceptée en silence.
- **Il ne traite pas la pagination.** Plusieurs listes plafonnent (`take=100` sur les
  livreurs) ou n'ont ni filtre ni période (`seller-orders`). Ce sont des limites, pas des
  absences — elles deviendront des sujets au premier volume réel.
