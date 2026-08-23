# Audit des BFF — ce qui reste à implémenter, par BFF

*22/08/2026. Établi en lisant le code, pas les documents : chaque affirmation ci-dessous
renvoie à un fichier et une ligne vérifiés. Les quantités sont comptées, pas estimées.*

---

## Ce qu'on appelle « BFF » ici, exactement

Depuis **D38**, il n'y a plus de service BFF : la passerelle EST le BFF. Elle fait deux
choses de nature différente, et les confondre fausse tout le reste de cet audit.

| | Quoi | Volume |
|---|---|---|
| **Agrégation** | `Controllers/Bff/*` — plusieurs services interrogés, une réponse composée, criticité déclarée par dépendance | **5 façades, 13 points d'entrée** |
| **Relais** | `ReverseProxy:Routes` — la requête part telle quelle vers un service | **54 routes, 19 grappes** |

**Un écran « non migré » ne demande donc pas forcément du travail de BFF.** Trois cas
se ressemblent et ne coûtent pas la même chose :

1. le module n'existe nulle part → **extraction de service** (des semaines) ;
2. le module existe et n'est pas relayé → **une route dans `appsettings.json`** (des minutes) ;
3. les données existent, éparpillées → **un agrégateur BFF** (des jours).

---

## Vue d'ensemble

| Façade BFF | Points d'entrée | Client | État |
|---|---|---|---|
| `api/v1/bff/client/express` | 2 | mobile **Client** | branchée |
| `api/v1/bff/client/food` | 2 | mobile **Client** | branchée |
| `api/v1/bff/merchant` | 2 | mobile **Seller-portal** | branchée, **incomplète** |
| `api/v1/bff/restaurant` | 2 | mobile **Seller-portal** | branchée |
| `api/v1/bff/driver` | 5 | **aucun** | écrite, testée, **jamais appelée** |
| `api/bff/client/{express,food}` | 2 | anciens binaires | `[Obsolete]`, retrait après bascule |

---

## Premier constat : quatre des treize « modules manquants » existent déjà

Les deux applications Flutter tiennent chacune un inventaire de ce qui leur manque
(`lib/src/core/network/not_migrated.dart`) — un très bon dispositif : il s'affiche à
l'écran au lieu de dormir dans un `TODO`, et `grep -rn NotMigrated` donne la liste sans
refaire l'enquête.

**Mais l'inventaire a pris du retard sur le serveur.** Sur treize entrées, quatre décrivent
une absence qui n'existe plus :

| Module déclaré manquant | Réalité vérifiée | Reste à faire |
|---|---|---|
| `returns` (**Client**) | `CustomerReturnsEndpoints` : **6 routes** sous `/api/v1/marketplace/returns`, relayées par `returns-customer` | quelques lignes de **Dart** |
| `returns` (**Seller**) | `SellerReturnsEndpoints` : **8 routes** sous `/api/v1/seller/returns`, relayées par `returns-seller` | quelques lignes de **Dart** |
| `geo` (**Client**) | `GET /api/geo/benin`, **anonyme**, relayé par la route `geo` vers la grappe `User` | quelques lignes de **Dart** |
| `appUpdate` (**Client**) | `AppVersionController` + section `AppVersions.client` **configurée** | quelques lignes de **Dart** |

Le commentaire du Client dit encore : *« Le module Returns n'a pas été extrait du
monolithe : ni agrégat, ni route. »* Le service `return-refund-service` est déployé, ses
quatre familles de routes sont relayées, et le lot 3.2 a traité *« le parcours de
remboursement, de bout en bout »*.

C'est **exactement le motif qui revient depuis le début de cette remédiation** : un texte
qui décrit un état que le code a quitté. Ici il coûte cher — il fait passer pour « des
semaines d'extraction » ce qui est une demi-journée de Dart, et il le fait dans le seul
document que quelqu'un consultera pour planifier.

**Premier geste recommandé, avant tout développement : purger ces quatre entrées.**

---

## BFF par BFF

### 1. `api/v1/bff/client/express` — application Client (colis)

**Existe :** `GET home` (accueil agrégé), `GET products/{id}` (fiche produit).
Les sections personnelles se taisent d'elles-mêmes quand l'appel est anonyme — c'est la
criticité déclarée qui fait le travail, pas un filtre conditionnel.

**Reste, côté serveur :**

| Manque | Nature | Poids |
|---|---|---|
| `shop` — vitrine publique d'une boutique | `GET /api/v1/merchants/{sellerId}` existe mais sous `MapSellerGroup` : **un acheteur ne peut pas la lire**. Il faut un point d'entrée anonyme, pas un service | agrégateur + route |
| `loyalty` — points de fidélité | **aucun service, aucun agrégat** | extraction |
| `content` — aide, FAQ, éditorial | **aucun service** | extraction |
| `disputes` — litiges et fil de discussion | **aucun service** | extraction |

**Reste, côté application :** brancher `returns`, `geo`, `appUpdate` (voir plus haut).

---

### 2. `api/v1/bff/client/food` — application Client (repas)

**Existe :** `GET home`, `GET restaurants/{id}`. Panier et commandes repas passent par le
relais (`/api/food/cart`, `/api/food/orders`).

**Reste :** rien de déclaré. Aucune entrée `NotMigrated` ne vise l'univers Food côté
client. C'est la façade la plus complète des cinq.

---

### 3. `api/v1/bff/merchant` — application Seller-portal la plus incomplète

**Existe :** `GET activities` (la liste des activités du marchand), `GET stores/{id}/dashboard`.

**Reste — et c'est du travail de BFF pur, aucune extraction :**

| Manque | Pourquoi ça bloque | Nature |
|---|---|---|
| `merchantConsolidated` | `activities` rend la **liste** des activités, pas leurs **totaux**. Personne n'additionne boutiques et restaurants : les vues consolidées de `/home` et `/orders` n'ont pas d'amont | **agrégateur** |
| `analytics` | La série temporelle des ventes était calculée par le BFF du monolithe. Ni service HBA ni route ne la rend | **agrégateur + requête à écrire** |

**C'est le meilleur rapport valeur/effort de tout cet audit.** Les données existent déjà
dans les services ; il manque l'addition. Deux écrans du vendeur — dont son écran d'accueil
— affichent aujourd'hui un `NotMigratedScreen` pour cette seule raison.

**Aussi à noter :** `sellerOrders` est branché (`GET /api/sellers/{sellerId}/orders`, ajouté
à `ReverseProxy:Routes` par VEN3), mais **sans pagination, ni filtre, ni période**. C'est
une limite, pas une absence — elle deviendra un problème au premier vendeur à trois cents
commandes.

---

### 4. `api/v1/bff/restaurant` — application Seller-portal (volet restaurant)

**Existe :** `GET restaurants/{id}/dashboard`, `GET restaurants/{id}/kitchen`.
Douze routes d'édition de carte sont ouvertes dans `FoodEndpoints`, dont
`PUT .../items/{id}/availability` avec ses trois états.

**Reste : rien qui soit un oubli.** Cinq commandes restent délibérément fermées —
`MoveCategory`, `MoveMenuItem`, `SetMenuWindow`, `SetMenuItemImage`, retrait d'options.
Elles **existent et sont testées** ; aucun écran ne les demande, et le dépôt applique ici sa
propre règle : *« une route sans appelant est une surface d'attaque entretenue pour rien »*.
Le jour où un écran en a besoin, c'est une ligne.

---

### 5. `api/v1/bff/driver` — cinq points d'entrée, zéro appelant

`GET dashboard`, `GET missions`, `GET missions/{id}`, `GET earnings`, `GET profile` —
agrégés depuis `IDeliveryClient` et `IFinancialClient`, avec criticité déclarée, et couverts
par `DriverHandlerTests`.

**Aucun client ne les appelle.** `clients/mobile/Driver/README.md` : *« Squelette et cinq
écrans. **Toutes les données sont simulées. Aucun appel réseau, aucune persistance.** »* Et
`clients/driver-app/` ne contient qu'un README.

C'est la même catégorie que les **21 RPC morts** du lot 9.1 — avec une aggravation : une
suite de tests d'intégration verte les fait **paraître** branchés.

**Ce qui reste n'est donc pas du serveur, c'est l'application.** Le chemin d'écriture du
livreur existe déjà et est relayé (`/api/delivery/**`) :

`online` · `offline` · `break` · `position` · `accept` · `decline` · `arrived-pickup` ·
`picked-up` · `in-transit` · `arrived-dropoff` · `delivered` — **onze actions**, plus
l'inscription, les véhicules, les documents et le dossier de vérification côté
`driver-service` (relayés par `drivers-me`).

Autrement dit : **le serveur du livreur est prêt, le client est une maquette.** Les neuf
dossiers de `features/` (auth, onboarding, dashboard, missions, mission, earnings, history,
account, support) sont à câbler sur des routes qui répondent déjà.

---

### 6. Console vendeur Next.js — 0 % migrée

`clients/seller-portal/Web` vise `SELLER_BFF_URL`, dont le défaut est
`seller.marketplace-staging.hba-marketplace.fr` — **l'ancienne marketplace**, pas HBA
Express. Tous ses appels sont sous `/seller/*`.

Son README annonce : *« Le BFF Vendeur expose **112 routes sur 18 domaines**. Cette console
les couvre tous les quatorze exposés au vendeur »*, plus **un hub SignalR** authentifié par
`?access_token=`.

**La passerelle n'en sert aucune**, et n'expose aucun hub temps réel. Cette console n'est
pas « en retard de migration » : elle est branchée sur un autre produit. La migrer suppose
d'abord de décider si elle doit l'être — la question n'est pas technique.

---

## Quatre services existent et sont injoignables de l'extérieur

19 grappes pour 23 hôtes. Les quatre absents :

| Service | Surface publique | Verdict |
|---|---|---|
| `dispatch-service` | `/internal/v1/dispatch` uniquement | **normal** — service interne |
| `tracking-service` | `/internal/v1/tracking` uniquement | **normal** — service interne |
| `route-service` | **expose `/api/v1/routes`** | aucune grappe : injoignable |
| `proof-of-delivery-service` | **expose `/api/v1/proofs`** (authentifié : créer, présigner un média, soumettre, lire par course) | aucune grappe : injoignable |

Le cas de la preuve de livraison mérite d'être tranché plutôt que laissé : **il y a deux
implémentations de la preuve**. `delivery-service` porte la sienne dans son domaine
(`ProofOfDelivery`, `ProofPolicy`, `ProofOfDeliveryKind`), capturée à `delivered` — c'est
celle que le parcours livreur emprunte, et elle fonctionne. `proof-of-delivery-service` en
porte une seconde, avec stockage de médias et présignature, que personne ne peut atteindre.

Ce n'est **pas** bloquant pour le livreur, contrairement à ce qu'on pourrait craindre. Mais
l'une des deux est morte, et tant qu'on ne dit pas laquelle, la prochaine personne qui
travaillera la preuve choisira au hasard.

---

## Ce qui reste, classé par ce que ça coûte

### A. Quelques lignes de Dart — le serveur est prêt

1. `returns` côté **Client** (6 routes disponibles)
2. `returns` côté **Seller** (8 routes disponibles)
3. `geo` côté **Client** (route anonyme disponible)
4. `appUpdate` côté **Client** (contrôleur et configuration disponibles)
5. **Purger les quatre entrées correspondantes des deux `not_migrated.dart`**

### B. Agrégation BFF — les données existent, l'addition manque

6. `merchantConsolidated` — totaux boutiques + restaurants (débloque `/home` et `/orders` du vendeur)
7. `analytics` — série temporelle des ventes
8. `shop` — vitrine de boutique **anonyme** (les données sont là, l'autorisation ne l'est pas)

### C. Câblage d'application — le serveur est prêt, le client est une maquette

9. **Application livreur** : 9 dossiers de fonctionnalités à brancher sur 5 points d'entrée BFF + 11 actions relayées + 4 routes de dossier livreur

### D. Extraction de service — des semaines

10. `disputes` (Client **et** Seller)
11. `shipping` (Seller)
12. `loyalty` (Client)
13. `content` (Client)
14. `reviewReport` (Seller — aucun endpoint de signalement dans `review-service`)

### E. Décisions, pas développements

15. Console vendeur Next.js : la migre-t-on, ou reste-t-elle sur l'ancienne marketplace ?
16. Preuve de livraison : laquelle des deux implémentations survit ?
17. `route-service` : on le relaie, ou on retire son groupe public ?
18. Routes `[Obsolete]` `api/bff/client/*` : **aucun critère de retrait n'est enregistré**.
    `AppVersionController` et `AppVersions.MinSupportedBuild` fournissent pourtant
    exactement le levier qui permettrait de dire quand elles peuvent partir.

---

## Ce que cet audit ne couvre pas

- **Il ne juge pas la complétude fonctionnelle d'un écran branché.** Un écran qui appelle sa
  route peut n'en afficher qu'une partie ; seule l'absence d'amont est mesurée ici.
- **Il ne mesure pas la couverture de la console admin.** `clients/admin-portal` ne contient
  qu'un README annonçant une application **Avalonia C#** qui n'existe pas encore.
- **Il ne vérifie pas les applications legacy.** Ce que sert
  `seller.marketplace-staging.hba-marketplace.fr` est hors de ce dépôt.
- **Il ne mesure pas la charge.** `seller-orders` sans pagination est signalé parce que le
  dépôt le dit lui-même, pas parce qu'une mesure a été faite.
