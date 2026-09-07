# Lot 1 — le service analytics est écrit

Commit `c925da8`, 83 fichiers, 27ᵉ service du dépôt :
`services/common/analytics-service/`. Il agrège les événements en roll-ups
journaliers et sert les **deux familles constructibles avec les contrats
d'aujourd'hui** — ventes vendeur, activité plateforme.

**Je prends l'historique au jour du déploiement**, comme recommandé au §4 du
document de conception. Un rattrapage se rajoute plus tard par un import
ponctuel ; rien dans ce lot ne le rend plus difficile.

---

## 1. Trois tables, et leur clé est leur définition

```
seller_daily     (SellerId, Day, Currency, Kind)  → OrdersCount, ItemsCount, Revenue
platform_daily   (Day, Kind, Currency)            → OrdersCount, ItemsCount, Gmv
signup_daily     (Day, Kind)                      → Count
```

**La devise est dans la clé.** Additionner des francs CFA et des euros dans une
colonne « chiffre d'affaires » produit un nombre qui n'est pas une somme
d'argent. Il n'y a qu'une devise en production aujourd'hui — c'est justement
pourquoi il faut la poser maintenant : le jour où il y en aura deux, une table
sans devise serait déjà fausse et personne ne saurait quelles lignes réparer.

**La nature aussi**, ce qui rend « part marchandise / repas » gratuite. En
colonnes — deux compteurs par ligne — chaque nature nouvelle coûterait une
migration ; en clé, elle crée une ligne et rien d'autre.

Aucune clé étrangère ne sort du schéma : `SellerId` désigne un vendeur d'une
AUTRE base. Ces tables ne contraignent rien, elles comptent — c'est aussi ce qui
les rend reconstructibles.

---

## 2. Trois chiffres sont incomplets, et le code le dit

C'est la partie qui compte le plus, parce qu'un graphe faux est plus coûteux
qu'un graphe absent.

| Chiffre | Ce qu'il vaut vraiment |
|---|---|
| `Revenue` (vendeur) | La **part vendeur**, commission NON déduite. Un vendeur qui le compare à son relevé de versement trouvera un écart — l'écart est exactement la commission. C'est du chiffre d'affaires, pas du gain net. |
| `Gmv` (plateforme) | Un **volume marchand**. `OrderConfirmed` ne porte pas le total payé par l'acheteur, seulement la répartition par vendeur : ni frais de livraison, ni commission — et **zéro pour une commande de repas**, qui n'a aucune part vendeur. `OrdersCount` et `ItemsCount`, eux, sont exacts. |
| Inscriptions | **Acheteurs + vendeurs ne s'additionnent pas.** Un vendeur s'inscrit d'abord comme utilisateur : il est dans les deux séries. Le DTO ne rend donc aucun total « comptes créés ». |

Le premier se lève avec le lot 2 (`GrandTotal` optionnel sur `OrderConfirmed`,
même famille que les trois champs déjà identifiés). Les deux autres sont des
propriétés du modèle métier, pas des défauts.

---

## 3. Les routes

```
GET /api/sellers/{sellerId}/analytics/sales      ?from&to&currency
GET /api/admin/analytics/activity                ?from&to&currency
GET /api/admin/analytics/signups                 ?from&to
```

Fenêtre par défaut : 30 jours. Plafond : **366 jours** — une année bissextile
complète doit passer, sinon « l'an dernier » échoue une année sur quatre.
Les jours sans vente rendent un **zéro, pas un trou** : une courbe à laquelle il
manque des points relie le 3 au 7 par une droite et donne à lire une activité
continue là où il n'y en a eu aucune.

**Le vendeur est désigné par l'URL, pas par le jeton** — comme
`/api/sellers/{id}/orders`. Un membre d'équipe agit pour le compte de son
vendeur et n'a pas de dossier vendeur à son nom ; résoudre depuis le jeton
fermerait la console à toute l'équipe sauf au propriétaire.

### L'autorisation ne fabrique pas de seconde vérité

Le service consomme `SellerRegistered`, qui porte `(SellerId, UserId)` : il
**pourrait** tenir sa propre correspondance et se passer de gRPC. Je ne l'ai pas
fait. Ce serait une seconde source de vérité sur l'autorisation, aveugle aux
membres d'équipe, aux révocations et aux transferts de propriété — et elle ne se
tromperait qu'au moment où ça compte. C'est `IMerchantAccessApi`, comme
order-service, financial et engagement.

### Une nouvelle permission, et une que je n'ai pas créée

`SELLER_ANALYTICS_VIEW` est ajoutée **avec la route qu'elle garde**, dans le même
commit : le contrôle `permissions` refuse une permission au catalogue qu'aucune
route n'exige. Elle est séparée de `FINANCE_VIEW` — celle-là ouvre le
portefeuille et les versements, c'est-à-dire de l'argent qu'on peut sortir ;
celle-ci n'ouvre que des courbes. `OWNER` et `SELLER_ADMIN` la portent
automatiquement ; aucun rôle métier ne l'a encore.

**`PLATFORM_ANALYTICS_VIEW` n'existe pas, contrairement au document de
conception.** Le catalogue `MerchantPermission` décrit ce qu'un membre d'une
équipe VENDEUR peut faire chez SON vendeur ; y déclarer un droit sur les
chiffres de la plateforme entière aurait mis dans le catalogue vendeur une
permission qu'aucun vendeur ne doit jamais porter. Le back-office est gouverné
par le rôle Admin, comme les vingt et une autres surfaces `/api/admin/*`.

---

## 4. Une inbox, pas d'outbox

**L'inbox est plus critique ici que partout ailleurs.** Les trois gestionnaires
INCRÉMENTENT ; rien dans une ligne de roll-up ne peut s'apercevoir d'un rejeu.
Ailleurs une garde d'état rattrape parfois l'absence d'inbox — un panier déjà
clôturé, une commande déjà confirmée. Ici, aucune : un rebalancement de partition
doublerait le chiffre d'affaires d'une journée, et personne ne saurait le
corriger.

**L'outbox est absente** parce que le service ne publie rien. Poser une table
`outbox_messages` qu'aucun code ne remplit ferait croire, à qui la trouve, que
quelque chose devrait en sortir.

Conséquence déplacée, et elle vaut d'être connue : `InboxCleanupService` est
enregistré par `InboxAnalytics` et non par `AjouterLOutboxLocale`. Ce raccourci
tient tant que tout service qui consomme publie aussi — celui-ci serait resté
sans purge.

### Les sujets, et celui qu'on se trompe

```
OrderConfirmed    order-service     → service.order.v1
SellerRegistered  seller-service    → service.merchant.v1     ← « merchant », pas « seller »
UserRegistered    identity-service  → service.identity.v1
```

Le dossier s'appelle `seller-service`, l'espace de noms `HBA.Merchants.*`, le
sujet porte le DOMAINE. S'abonner à `service.seller.v1` ne produirait aucune
erreur : seulement une courbe d'inscriptions vendeur plate à zéro.

---

## 5. Ce qui a été touché ailleurs

| Fichier | Pourquoi |
|---|---|
| `HbaTopics` | `analytics-service → analytics`. Il ne publie rien, mais sans l'entrée le contrôle `kafka-topics` le prend pour un producteur hors catalogue — son marqueur de publication (`IntegrationEvent`) apparaît dans ses gestionnaires de CONSOMMATION. |
| `AutorisationsGrpc` | L'hôte et ses huit RPC marchand. |
| `MerchantPermission` + `MerchantCapabilities` | `SELLER_ANALYTICS_VIEW`, des deux côtés (un test les tient synchrones). |
| `ServicesOptions` + `appsettings.json` | **Les cinq endroits d'un coup** : adresse, propriété, branche `Resolve`, `ServiceKeys` + `All`, cluster + deux routes. Le fichier raconte déjà deux fois ce qui arrive quand on n'en fait que quelques-uns. |
| `docker-compose.dev.yml` | Le bloc du service, et `SERVICES__ANALYTICS` côté passerelle. |
| `001-create-databases.sql` | `hba_analytics`. `Database.Migrate()` crée la base absente en dev — c'est ce qui avait masqué l'oubli de `hba_promotion` pendant des semaines. |
| `ComposeProd.Bases` | Pour que `docker-compose.prod.yml` soit engendré avec une base. |

---

## 6. Le point faible de ce lot : la migration est écrite à la main

Pas de SDK .NET sur ce poste. Ni `20260907000000_InitialAnalytics` ni
`AnalyticsDbContextModelSnapshot` n'ont vu `dotnet ef`. Ils suivent la forme des
vingt-quatre autres, et **le seul fait établi est que le contrôle `migrations`
retrouve un `CreateTable` pour chaque `.ToTable(...)`** — ce qui ne dit rien des
colonnes.

La vérification existe, et elle est à faire **avant tout déploiement** :

```bash
./scripts/verifier-migrations.sh --contexte=AnalyticsDbContext
```

Elle demande une migration de plus et vérifie que son diff est VIDE. Un diff non
vide dira exactement ce que j'ai manqué.

### Ce que j'ai pu vérifier sans compilateur

`using HBA.*` tous résolus (la panne des 77 usings pendants) · aucun type
`Analytics` masqué par l'espace de noms (la panne CS0118 d'`Order`) ·
`ProjectReference` et `Protobuf` tous résolus · aucun `Version=` (NU1008) ·
accolades et indentation · fermeture transitive du `Dockerfile` · symétrie des
cinq endroits de la passerelle · `docker-compose.dev.yml` relu par un automate
qui reproduit celui des contrôles · producteur au catalogue · les quatre types du
snapshot existent · aucun `record *IntegrationEvent` redéclaré · aucun code de
permission en littéral · aucun paramètre `…Request` sur un `MapGet`.

**Ce que ça ne remplace pas :** aucune erreur de type, d'accessibilité ou de
surcharge ne peut sortir d'ici. C'est le huitième lot empilé sans compilateur.

---

## 7. Ce que ce lot ne couvre pas

- **Les manifests `k8s/`.** Je n'ai pas ajouté le service — aucun contrôle ne le
  dirait, et je préfère te le signaler plutôt que d'inventer une forme.
- **Le rôle métier.** Aucun rôle vendeur autre qu'`OWNER` et `SELLER_ADMIN` ne
  porte `SELLER_ANALYTICS_VIEW`. À trancher : un `FINANCE_MANAGER` ou un
  `ORDER_MANAGER` devrait-il voir les courbes ?
- **Le fuseau.** Les journées sont en UTC, partout comme le reste du dépôt. À
  Cotonou (UTC+1), une vente conclue à 00h30 locale est comptée la veille. Le
  geste pour changer est dans un seul fichier (`JourneeAnalytique`), mais il
  faudra RECALCULER l'historique — sinon la moitié de la série ne se compare pas
  à l'autre.
- **La rétention des roll-ups.** Une ligne par vendeur, par jour et par devise
  reste petite. Des séries horaires ne le seraient plus.
- **Les lots 2 à 4** : les trois champs optionnels, la santé opérationnelle, les
  produits et le stock.

## 8. Deux choses vues en passant

- **`docs/DEPLOIEMENT.md` et `docs/SOCLE-TRANSVERSE.md` sont apparus en
  suppression indexée** pendant que je travaillais — ce n'est pas moi. Ils sont
  restés hors de mon commit, avec tes fichiers en cours
  (`CommerceEndpoints.cs`, `CommerceGrpcService.cs`, `IdentityGrpcService.cs`)
  et les suppressions de `Claude outputs/`.
- **`scripts/kafka-topics.sh` n'existe pas.** Les sujets se créent donc au
  premier message, avec les réglages par défaut du courtier. Ce n'est pas un
  problème de ce lot, mais c'est un point à connaître avant la production —
  partitionnement et rétention ne sont réglés nulle part.
