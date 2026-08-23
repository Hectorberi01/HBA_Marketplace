# Audit de l'application vendeur — et le chemin vers 100 % de raccordement

> Établi le 16 août 2026, par croisement mécanique du code Flutter, des routes YARP,
> des `*Endpoints.cs` des treize services et du monolithe encore en place.
> Aucun chiffre de ce document n'est estimé : tous sont comptés.

---

## 1. Où en est-on, exactement

**65 écrans, feuilles et assistants.**

| État | Nombre | Ce que cela veut dire |
|---|---:|---|
| **RÉELS** | 34 | L'écran parle à la passerelle, et la route existe |
| **NOT_MIGRATED** | 16 | L'écran affiche « bientôt disponible » — rien ne part |
| **MIXTES** | 6 | Une partie répond, une autre lève. Les plus trompeurs |
| *LOCAL* | *9* | *Aiguillages, textes légaux, réglages — sans amont par nature* |

Sur les 94 appels HTTP sortants de l'application, **aucun ne tombe en 404, 405 ni sur
une route manquante**. Le câblage est juste. Ce qui manque, ce sont des capacités
serveur — et deux défauts d'ergonomie qui coûtent plus cher que leur taille.

---

## 2. Les trois constats qui comptent

### 2.1 L'application s'ouvre sur deux écrans morts

`selectedActivityIdProvider` vaut `null` au démarrage et **le choix n'est pas
persisté** (`selected_activity.dart:46`). Or :

- `/home` sans activité choisie → `GlobalDashboardScreen` → `NotMigrated('merchantConsolidated')`
- `/orders` sans activité choisie → `GlobalOrdersScreen` → `NotMigrated('merchantConsolidated')`

Un partenaire qui ouvre l'application tombe donc **systématiquement** sur
« bientôt disponible », sur les deux premiers onglets, avant d'avoir compris qu'il
doit passer par le troisième pour choisir une activité.

C'est le défaut le plus visible de toute l'application, et il ne coûte pas une
ligne de serveur : il suffit de retenir la dernière activité, ou de sélectionner
la première quand il n'y en a qu'une — ce qui est le cas de quatre comptes
d'amorçage sur cinq.

### 2.2 `offers` n'est pas un écran manquant, c'est le cœur du catalogue

Le module Products/Offers n'a jamais été extrait du monolithe. Conséquences en
chaîne, toutes vérifiées :

- **six écrans mixtes sur six** ont cette cause ;
- l'aperçu produit **ne montre jamais le prix acheteur** — ce qu'il est pourtant
  censé prévisualiser (`product_preview_screen.dart:30`) ;
- l'assistant de création crée un produit **invendable** : ni offre, ni stock ;
- son rollback est incomplet — `offersApi.delete` lève aussi
  (`product_wizard_sheet.dart:253`) ;
- et surtout, hors périmètre vendeur : `ProductsGrpc.cs:175-192` **lève
  `NotSupportedException`**, donc `AddItemToCartCommandHandler` ne peut pas
  résoudre un prix. **Aucun article ne peut entrer dans un panier client.**

C'est le chemin critique de toute la plateforme, pas seulement de l'app vendeur.

### 2.3 Six écrans sont inatteignables, dont deux entièrement câblés

| Écran | Cause |
|---|---|
| `ProductsScreen` (`/products`) | Dans la coquille, mais absent des cinq onglets. Son unique `push` vient de `onboarding_checklist.dart:96`, widget **sans aucun importeur** |
| `ProductWizardSheet` | Ouvert par `ProductsScreen` seulement |
| `ProductCreateSheet` | **Zéro appelant** dans tout le dépôt |
| `OrdersScreen` | Ni routée, ni instanciée — pourtant câblée et paginée |
| `FinanceScreen` | Import volontairement retiré du routeur |
| `AnalyticsScreen`, `DisputeScreen` | Aucun `push` |
| `ConversationsScreen` / `ChatScreen` | **Aucun geste d'interface** — uniquement par notification push |

Et l'écran de cuisine n'existe pas : `kitchenBoardProvider` et tout `KitchenApi`
(accepter, refuser, en préparation, prêt) **n'ont aucun consommateur**, alors que
la route BFF `GET /api/v1/bff/restaurant/restaurants/{id}/kitchen` répond.

---

## 3. Les treize manques, classés par nature

| Module | Nature | Ce qui manque exactement |
|---|---|---|
| `accountDeletion` | **ROUTE_HTTP** | `DeleteAccountCommand` complète, **zéro appelant**. YARP passe déjà |
| `consent` | **ROUTE_HTTP** | `AcceptTermsCommand` écrite, **zéro appelant**. YARP passe déjà |
| `appUpdate` | **ROUTE_HTTP** | Rien nulle part — mais rien à écrire au-delà d'une lecture de configuration |
| `sellerStatement` | **ROUTE_YARP** | Trois routes prêtes sous `MapAuthenticatedGroup`, avec garde de propriété. Aucune entrée `/api/financial/settlements` |
| `sellerInventoryWrite` | **AUTORISATION** | 12 écritures sous `MapAdminGroup` (`InventoryEndpoints.cs:72`) |
| `reviewReport` | **DÉCISION** | `/report` n'a jamais existé, dans aucun des deux dépôts |
| `offers` | **DOMAINE** | 33 fichiers dans le monolithe |
| `shipping` | **DOMAINE** | 38 fichiers. Trois services HBA attendent ses événements sans producteur |
| `returns` | **DOMAINE** | 25 fichiers. Même situation |
| `disputes` | **DOMAINE** | 24 fichiers. Pas même de contrat recopié |
| `analytics` | **DOMAINE** | Aucun stockage de métriques dans HBA |
| `merchantConsolidated` | **DOMAINE** | Les entrées amont manquent (stats commandes, stock bas) |
| `imageProcessing` | **DOMAINE** | `IImageProcessor` et ses implémentations non reprises |

---

## 4. Le plan, par coût croissant

### Phase 0 — Ce qui ne coûte rien et se voit tout de suite

Aucune ligne de serveur. À faire avant tout le reste, parce que ces défauts
donnent l'impression que l'application est plus cassée qu'elle ne l'est.

1. **Persister l'activité sélectionnée**, et la choisir d'office quand il n'y en a
   qu'une. Supprime les deux écrans morts du démarrage (§2.1).
2. **Rendre `ProductsScreen` atteignable** — réattacher `StartupChecklist`, ou
   ajouter l'entrée dans le menu Compte. Débloque au passage `ProductWizardSheet`.
3. **Donner une entrée à la messagerie.** Aujourd'hui, un vendeur ne peut pas lire
   ses conversations sans avoir reçu une notification.
4. **Trancher le sort des écrans morts** : `ProductCreateSheet` (zéro appelant),
   `OrdersScreen`, `FinanceScreen`, `AnalyticsScreen`. Ouvrir un chemin, ou
   supprimer le fichier. Les laisser compiler sans être atteints est le pire des
   deux.
5. **Écrire l'écran de cuisine** — la route BFF existe et répond ; c'est
   l'écran qui manque, et c'est le poste de travail quotidien d'un restaurateur.

### Phase 1 — Trois routes, quelques lignes chacune

6. **`accountDeletion`** — `MapDelete("/me", …)` dans `IdentityEndpoints` + un
   handler de trois lignes. La commande vérifie déjà le mot de passe, gère
   l'idempotence et anonymise. **Exigence App Store 5.1.1(v) : bloquant pour
   toute soumission.** À faire en premier de cette phase.
7. **`consent`** — `MapPost("/me/accept-terms", …)`, même service, même groupe.
   La lecture (`acceptedTermsVersion`) est déjà servie.
8. **`appUpdate`** — un endpoint anonyme adossé à la configuration, sur le modèle
   de `/api/geo/benin` qu'on vient d'ouvrir. Aucun domaine.
9. **`sellerStatement`** — une entrée YARP vers `/api/financial/settlements`,
   **puis** la réécriture de la projection : le service exige
   `periodStartUtc`/`periodEndUtc` non nullables là où l'app les envoie
   facultatifs, et les noms de champs diffèrent. Ouvrir la route est nécessaire,
   pas suffisant.

### Phase 2 — Rendre le stock au vendeur

10. **`sellerInventoryWrite`.** Les douze écritures vivent sous `MapAdminGroup`.
    **Ce n'est pas un déplacement de ligne** : inventory-service ne référence
    pas `HBA.Merchants.Contracts` et ne sait donc pas résoudre compte → vendeur.
    Les ouvrir sans cette dépendance créerait un IDOR en écriture sur le stock
    d'autrui. L'ordre est donc : ajouter la dépendance, poser le contrôle de
    propriété, puis seulement déplacer les routes.

    Débloque `_StockActionSheet`, `_CreateStockSheet`, et une moitié de
    `/locations`.

### Phase 3 — Extraire Products/Offers *(chemin critique)*

11. **Le module Offers.** 33 fichiers, commandes déjà écrites
    (`CreateOfferCommand`, `ChangeOfferPriceCommand`, `ApplyOfferPromotionCommand`…
    — exactement la surface qu'appelle `offers_data.dart`).

    Ce que cela referme : les six écrans mixtes, l'aperçu produit, l'assistant de
    création de bout en bout, l'autre moitié de `/locations`, `OffersScreen` — et
    **le panier de l'application cliente**, aujourd'hui hors service.

    C'est la tâche #165, et elle conditionne plus de choses que tout le reste
    réuni.

### Phase 4 — Les chiffres

12. **`GET /api/sellers/{id}/orders/stats?from=&to=`** dans order-service. Le BFF
    recalcule aujourd'hui les chiffres du jour faute de mieux, et le dit
    (`MerchantDtos.cs:64-75`).
13. **`GET /api/inventory/owners/{id}/low-stock`**, scopé vendeur.
14. **`merchantConsolidated`** — l'agrégat BFF devient alors écrivable : il
    n'attend que ces deux entrées.
15. **`analytics`** — la série temporelle demande en plus une table de snapshots
    et un handler d'accumulation. C'est le seul de ce groupe qui soit du domaine
    neuf.

### Phase 5 — Les trois modules jamais extraits

16. **`shipping`** (38 fichiers) — à traiter en premier des trois : `financial-service`
    et `communication-service` **consomment déjà** ses événements, sans producteur.
    Le module de livraison n'en tient pas lieu : il n'a ni `Shipment`, ni
    `Carrier`, ni file d'expéditions par vendeur.
17. **`returns`** (25 fichiers) — même situation, `ReturnRefunded*` sans producteur.
18. **`disputes`** (24 fichiers) — le seul dont même le contrat n'a pas été recopié.

### Phase 6 — Décisions à prendre, pas travaux à faire

19. **`reviewReport`** — laisser fermé. Un vendeur ne doit pas pouvoir faire
    retirer un avis négatif qui le concerne. Le bouton a été retiré, pas grisé :
    c'est la bonne façon de porter une décision.
20. **`imageProcessing`** — détourage et fond blanc. media-service redimensionne,
    il ne retouche pas. Soit on recâble un prestataire, soit on assume que les
    photos partent telles quelles. La dégradation actuelle est déjà propre
    (l'échec est rattrapé, la photo d'origine reste envoyable) : ne rien faire est
    une option défendable.

---

## 5. Ce que « 100 % » veut dire, et ce qu'il ne veut pas dire

À l'issue de la **phase 3**, l'application vendeur est fonctionnellement complète
pour un commerçant : créer, vendre, encaisser, expédier via la course de
livraison, répondre aux avis, discuter. C'est le seuil qui compte.

Les phases 4 et 5 ajoutent le pilotage (chiffres, consolidation) et l'après-vente
lourd (expéditions, retours, litiges). Ce sont des besoins réels, mais aucun
n'empêche un vendeur de travailler.

Et une part de ces treize modules ne sera **jamais** raccordée, parce qu'elle ne
doit pas l'être : `reviewReport` est un refus assumé. Compter 13/13 comme objectif
serait se donner une cible fausse. La bonne cible est 12, dont 11 par du travail
et 1 par une décision.

---

## 6. Annexe — les écrans, un par un

### Entièrement raccordés (34)

Authentification (6) · Sélection et bascule d'activité (3) · Tableaux de bord
boutique et restaurant (2) · Commandes d'une activité et détail (3) · Produits :
liste, fiche, déclinaisons, modification (4) · Carte : liste, plat, assistant (3) ·
Finances et portefeuille (2) · Avis (1) · Messagerie (2) · Boutique : profil, KYB,
société, reversement (5) · Compte, profil, notifications, préférences (4).

### Entièrement en attente (16)

`ConsentScreen`, `UpdateRequiredScreen`, `GlobalDashboardScreen`,
`GlobalOrdersScreen`, `AnalyticsScreen`, `_StockActionSheet`, `_CreateStockSheet`,
`OffersScreen`, `ShippingLocationsScreen`, `_ChangePriceSheet`, `_DiscountSheet`,
`FinanceScreen`, `ShipmentsScreen`, `ReturnsScreen`, `DisputeScreen`,
`_DeleteAccountSheet`.

### Mixtes (6) — tous sur le catalogue, tous pour la même cause

`ProductDetailScreen`, `ProductPreviewScreen`, `ProductWizardScreen`,
`ProductWizardSheet`, `ProductCreateSheet`, `_CreateOfferSheet`.

`offers` + `sellerInventoryWrite` + `imageProcessing`. Les phases 2 et 3 les
referment toutes les six.
