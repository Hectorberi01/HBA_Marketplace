# Ce qui reste pour câbler l'app vendeur à 100 %

Audit du 17 août 2026, après les phases 0 à 3.

## Comment cet audit a été fait, et ce qu'il vaut

J'ai extrait mécaniquement les **101 routes distinctes** appelées par l'application
(102 appels `dio.*` dans 24 fichiers), résolu chaque préfixe via `AppConfig` et les
constantes locales, puis croisé le résultat avec les routes exposées par les treize
services et avec les **29 entrées YARP** de la passerelle.

**Le croisement automatique m'a menti une fois, et il faut le savoir.**
Mon extracteur attribuait tous les `group.MapPost(...)` d'un fichier au *dernier*
groupe déclaré. `IdentityEndpoints.cs` réutilise quatre fois le nom de variable
`group` (auth, account, users, roles) : six routes de compte ressortaient donc
« absentes » alors qu'elles existent. Les conclusions ci-dessous ont été vérifiées
une par une à la main. Un audit qui ne se relit pas ne vaut pas mieux qu'un silence.

## Ce qui est réellement branché

**Aucun préfixe appelé par l'application n'est orphelin côté passerelle.** Les seize
bases d'`AppConfig` — `auth`, `identity`, `users`, `merchants`, `catalog`,
`inventory`, `geo`, `orders`, `food`, `delivery`, `wallet`, `payments`, `reviews`,
`notifications`, `media`, plus les deux BFF — ont toutes une route YARP.

`/api/sellers/{sellerId}/orders` existe désormais des deux côtés (entrée
`seller-orders`, ordre 10, et `OrderEndpoints.cs:111`) : **la tâche #203 est
close**. Le `{**catch-all}` accepte un reste vide, donc l'appel sans segment
supplémentaire passe.

Fonctionnellement complets et éprouvés : authentification et session, activités,
tableaux de bord marchand et restaurant, catalogue produit (les douze routes),
déclinaisons, médias, stock, lieux d'expédition, mises en vente (les neuf routes de
la phase 3), carte Food (les douze routes), cuisine, messagerie, boutique et KYB,
portefeuille et retraits, avis, géographie.

---

# Ce qui reste, par ordre de coût croissant

## 1. Un point non tranché, à vérifier avant tout le reste

**Le stock ne s'enregistre pas à la création de produit.** Dernière capture : le
résumé annonçait 10 unités, la fiche affiche « Aucun stock enregistré pour ce SKU ».

Dans `_publier`, `createItem` passe *après* `offers.create` pour chaque
déclinaison. Si la création d'offre échoue, le stock n'est jamais tenté. Or la
lecture des offres levait au même moment (le `p.Id.Value` non traduisible, corrigé
depuis) — impossible de savoir si l'offre existe.

**À faire d'abord**, après reconstruction de catalog-service :

```
curl -s "localhost:8080/api/catalog/seller/stores/$STORE/offers" -H "Authorization: Bearer $TOKEN" | jq .
```

Une liste vide oriente vers `CreateOfferCommand` ; une liste peuplée vers
`InventoryApi.createItem`. C'est un aiguillage, pas une supposition — et il
conditionne le reste du travail sur l'assistant.

## 2. Sept écrans sans amont — dont trois qui ne sont pas du travail

`pendingModules` compte huit entrées (contre onze avant les phases 2 et 3, qui ont
retiré `sellerInventoryWrite`, `offers` et `imageProcessing`).

### Modules jamais extraits du monolithe — travail lourd, hors app

| Module | Écran | Nature |
|---|---|---|
| `shipping` | `/shipments` | Module non extrait. Aucune route d'expédition. |
| `returns` | `/returns` | Module non extrait. |
| `disputes` | `/dispute/:id` | Module non extrait. |

Ces trois-là ne se règlent pas côté Flutter : il faut extraire les modules. Ce
n'est pas de l'ordre de l'app vendeur, et prétendre le contraire donnerait un
faux plan.

### Décisions, pas des manques

| Module | Écran | Pourquoi ce n'est pas à faire |
|---|---|---|
| `reviewReport` | Signalement d'avis | **Décision, pas oubli.** Un vendeur ne doit pas pouvoir faire retirer un avis négatif qui le concerne. La modération dispose de `/flag`, `/reject`, `/restore` ; il n'existe volontairement aucun `/report` vendeur. |
| `analytics` | `/analytics` | Aucun service d'analytique. L'écran existe hors routeur. Voir §5. |
| `merchantConsolidated` | Tableau de bord global | Le BFF sert un tableau **par boutique**, jamais consolidé. C'est cohérent avec le modèle multi-activités : la vue consolidée demanderait un agrégateur qui n'existe pas. |

### Travail réel et borné

| Module | Écran | Coût |
|---|---|---|
| `sellerStatement` | Finances / relevé | Projection à écrire dans financial-service. **Tâche #228.** |
| `appUpdate` | Porte de version | Route de version minimale à exposer. **Tâche #228.** C'était un **bloquant App Store 5.1.1(v)** : une application qui s'auto-bloque sans pouvoir dire quelle version installer. |

## 3. Deux routes mortes dans le routeur (#221)

`/analytics` et `/messages` sont déclarées et atteignables, sans amont. Un écran
routé qui lève à l'ouverture est pire qu'un écran absent : le vendeur y arrive par
la navigation, et rien ne lui dit que ce n'est pas sa faute.

**Trancher, ne pas laisser en l'état** : soit les retirer du routeur, soit leur
donner l'état « pas encore disponible » que les sept autres portent déjà.

## 4. Une lacune fonctionnelle Food (#227)

**Aucune route ne rend les commandes en attente d'acceptation.** L'écran de cuisine
créé en phase 0 affiche trois seaux — mais le ticket n'existe qu'*après*
acceptation. Le restaurateur ne peut donc pas voir ce qu'il doit accepter.

Le seul `pending` exposé est `moderation.MapGet("/restaurants/pending")` :
l'approbation d'établissements par un admin, sans rapport.

Accept/reject existent (`/orders/{id}/accept`, `/reject`, gardés depuis VEN5-a).
**Il manque uniquement la lecture** : une route qui liste les commandes reçues et
non encore acceptées, plus la bande correspondante dans `kitchen_screen.dart`.
C'est le manque le plus visible pour un restaurateur en service.

## 5. Un écran absent (#214)

`VEN6` — les routes de cartes et de sections Food existent toutes ; **l'écran de
gestion des cartes manque**. Le restaurateur peut créer des plats mais pas
organiser ses menus.

## 6. Trois failles ouvertes, et l'excuse a disparu

| # | Défaut | Pourquoi ça compte |
|---|---|---|
| **#179** | IDOR sur les **douze routes produit** de catalog-service. Un acheteur qui connaît un identifiant — rendu par la vitrine publique — peut renommer, dépublier, supprimer un produit, réécrire ses déclinaisons. `CreateProductAsync` prend toujours `SellerId` **dans le corps**. | `DenyUnlessOwnerAsync` existe désormais dans ce même fichier et donne le gabarit exact. Ce n'est plus une impossibilité, c'est un travail. |
| **#229** | Les **lectures** d'inventory restent transverses. | L'écriture a été fermée en phase 2 ; la lecture est restée ouverte. L'excuse a disparu, pas la fuite. |
| **#230** | Une déclinaison de catalog-service **ne peut pas être désactivée**. | `ListByVariantAsync` archive les offres d'une variante désactivée — le chemin existe, l'action manque. |

## 7. Deux dettes de contrat

**#164 — trois interfaces `*ModuleApi` en double.** `HBA.Products.Contracts` est un
vestige : il porte un `ProductSummary` qui fait doublon avec celui de
catalog-service, et son nom désigne un module qui ne sera jamais extrait. Le
replier n'était pas le travail de la phase 3, mais c'est un travail.

**#17 — `FirstName`/`LastName` sur `UserSummary`**, avec dix-sept appelants à
reprendre.

## 8. Ce qu'aucun test ne couvre

**#215 — les douze routes Food de VEN5-b n'ont aucun test de frontière.** Elles ont
été ouvertes avec une garde d'appartenance ; rien ne vérifie que cette garde tient.
Le même trou existe pour les neuf routes d'offres de la phase 3.

**#195 — le garde-fou CI** doit désormais couvrir aussi :
- le balayage du corps inféré sur `DELETE`/`GET` (deux services l'ont violé) ;
- les préfixes d'environnement contre les `SectionName` réels. C'est exactement
  ce qui a fait tourner media-service en mémoire pendant tout le développement :
  `OBJECTSTORAGE__*` ne liait rien, la section s'appelle `Media:Storage`. Le
  service avertissait au démarrage ; personne ne lisait la ligne. Un test qui
  croise les clés de compose avec les `SectionName` du code aurait attrapé ça en
  une seconde.

---

# Trois enseignements de cette session, qui valent au-delà des tâches

**Un contrat se vérifie contre son consommateur, jamais contre son ancêtre.**
`OfferDto` a été écrit en transposant le contrat du monolithe. Il lui manquait six
champs que l'écran lisait déjà, et il portait un désaccord de fond : la remise était
exprimée en prix acheteur là où toute l'application saisit du net vendeur.

**Un substitut silencieux coûte plus cher qu'une panne.** `InMemoryObjectStorage`
s'est installé parce que quatre variables d'environnement ne liaient rien. Les
photos partaient dans un dictionnaire. L'avertissement existait, au démarrage, dans
les journaux.

**Ce que la production ne peut pas révéler, le développement doit le révéler.**
La signature S3 omettait le port (`Uri.Host` au lieu de `Uri.Authority`). Contre R2
en HTTPS sur 443, les deux chaînes sont identiques et le défaut est invisible. Seul
MinIO sur le port 9000 pouvait le montrer — argument décisif contre les substituts
en mémoire en développement.

---

# Ordre de travail proposé

1. **Trancher le §1** — offre créée ou non. Un `curl`, et le reste s'éclaire.
2. **#227** — les commandes en attente d'acceptation. Le manque le plus visible.
3. **#179** — l'IDOR produit. Le plus grave, et le gabarit existe.
4. **#221** — les deux routes mortes. Quelques lignes.
5. **#228** — relevé et porte de version. Le bloquant App Store en fait partie.
6. **#214** — l'écran des cartes.
7. **#229 / #230** — lectures inventory, désactivation de déclinaison.
8. **#215 / #195** — les tests de frontière et le garde-fou CI.

`shipping`, `returns` et `disputes` restent hors périmètre : ce sont des extractions
de modules, pas du câblage d'application.
