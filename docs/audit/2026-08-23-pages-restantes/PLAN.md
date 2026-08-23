# Plan d'exécution — les écrans restants

**23 août 2026.** 18 sections prêtes sur 28. Ce document séquence les dix qui restent.

## Le constat qui gouverne le plan

**Il ne reste presque pas de travail d'écran.** Sur les dix sections, **une seule** peut être
écrite aujourd'hui sans toucher au serveur. Les autres attendent soit une route, soit un
service entier, soit une décision de retrait.

Le plan est donc ordonné par **ce qui débloque le plus par unité d'effort**, et non par
l'ordre du panneau.

| Nature du reste | Sections |
|---|---|
| Écran écrivable tout de suite | Boutiques (1) |
| Une petite route serveur d'abord | Recommandations, Factures (2) |
| Une ligne de configuration, et **aucun écran** | Monitoring (1) |
| Un service à construire — hors périmètre console | Analytics, Marketing, Taxes, Bannières, Fraude, Notifications (6) |
| À trancher : implémenter ou retirer | `return-policies` (0 section, 1 route qui ment) |

---

## LOT 1 — L'observabilité — ✅ **fait le 23/08** (aucun écran)

**Pourquoi en premier :** c'est le seul élément de cette liste dont l'absence se paie
*pendant un incident*, c'est-à-dire au moment où l'on ne peut rien réparer.

`OPENTELEMETRY__ENDPOINT` n'est posé que dans `gateway.env` — un fichier sur quatorze. Les
autres services calculent `hasEndpoint = false` et n'exportent **rien**, silencieusement.
Prometheus, Grafana, Loki et le collecteur OTLP tournent et reçoivent les données d'un seul
processus.

**Travail — ✅ fait le 23/08**

**Ce lot prévoyait un `infra/docker/env/commun.env`. La vérification a montré que
c'était le mauvais véhicule**, et le plan est corrigé ici plutôt que laissé en deux
versions. Deux raisons :

- les `env_file` sont déclarés `required: false` ; un service dont le fichier a été retiré
  serait reparti muet, c'est-à-dire le défaut qu'on corrige ;
- le bloc `environment:` est, selon ce fichier lui-même, l'endroit de « tout ce dont les
  services ont besoin ».

Ce qui a été fait :

1. `OpenTelemetry__Endpoint: ${OTEL_ENDPOINT:-http://otel-collector:4317}` dans le bloc
   `environment:` des **treize services** de `compose.services.yml` **et de la passerelle** —
   quatorze processus. Le `.env` du répertoire garde le dernier mot par `OTEL_ENDPOINT`,
   pour viser un collecteur distant sans toucher aux composes.
2. Commentaire de `prometheus.yml` corrigé : il affirmait que « les métriques des quinze
   processus y arrivent ». Un encadré dit maintenant ce qui s'est passé, parce que le
   défaut se lit exactement comme la limite que ce même fichier décrivait déjà — « un
   service muet est indiscernable d'un service inactif ».
3. **Un troisième commentaire faux corrigé au passage** : `compose.services.yml` affirmait
   que les `env/*.env` « n'existent dans AUCUN dépôt » parce que « `.gitignore` ignore
   `*.env` ». Faux deux fois — `.gitignore` ignore `.env`, `.env.local` et `.env.*.local`,
   et `git ls-files infra/docker/env/` rend les quatorze fichiers. `required: false` reste
   juste, mais pour une autre raison.
4. `scripts/check-infra.py` refuse désormais un service de compose sans
   `OpenTelemetry__Endpoint` dans son `environment:` — **et le refus a été éprouvé** sur un
   compose factice à deux services, dont un muet. Un contrôle qu'on n'a pas vu échouer ne
   prouve rien.

**Ce que ce lot NE fait pas :** il ne crée pas d'écran « Monitoring ». Un lien vers Grafana
reste la bonne réponse — mais il ne l'était pas tant que Grafana était vide.

**Critère d'arrêt :** si les treize services n'apparaissent pas comme `service_name` distincts
dans Prometheus après redémarrage, ne pas empiler d'autres correctifs — le problème est
ailleurs (nom de ressource, réseau du collecteur), et il faut le trouver avant.

---

## LOT 2 — `return-policies` — ✅ **retirée le 23/08**

**Pourquoi si tôt :** c'est la seule chose du dépôt qui **ment activement**. La route est
relayée par la passerelle, répond 200 et 201, et ne persiste rien :

```csharp
group.MapGet("/", () => ApiResults.Ok<IReadOnlyList<ReturnPolicyDto>>([
    new("default", "2026.08.1", 14, true, true, true, true, 0m)   // écrit en dur
]));
group.MapPost("/", (UpsertReturnPolicyDto request) => ApiResults.Created(...));  // renvoie l'entrée
```

Deux lambdas sans `ISender`, sans dépôt, sans `DbContext`.

**Décision prise : retrait.** Fait le 23/08.

### Ce que le retrait a touché

1. `ReturnPolicyEndpoints.cs` → `_to_delete/2026-08-23-return-policies/`.
2. `app.MapReturnPolicyEndpoints()` retiré de `Program.cs`, avec l'encadré qui dit pourquoi.
3. `IReturnPolicyApplicationService` retiré — interface déclarant `ListAsync`/`UpsertAsync`,
   **jamais implémentée, jamais injectée**. Les lambdas faisaient le travail à la main, et le
   faisaient faux. Une interface sans implémentation n'est pas une intention documentée :
   c'est une promesse que la relecture prend pour un contrat existant.
4. Route `admin-return-policies` retirée de la passerelle — sans quoi elle aurait relayé vers
   un 404. L'explication a rejoint le `_lire` de `admin-returns` : même cluster, même sujet.
5. `ReturnPolicyDto` et `UpsertReturnPolicyDto` **conservés**, avec un encadré disant
   pourquoi : la forme a été relue, le lot qui rendra la politique configurable repartira de
   là.

### Ce que la vérification a trouvé et que l'audit avait sous-estimé

Le défaut ne se limitait pas aux deux routes. **Trois couches simulaient**, et la troisième
est dans le chemin réel :

- `ReturnPolicyRepository.GetApplicableSnapshotAsync` — celui que **`CreateReturnCommand`
  appelle vraiment** — rend un `PolicySnapshot` écrit en dur, sans toucher la base et **sans
  lire ses deux paramètres** (`productId`, `categoryId`) ;
- `ReturnPolicyCache`, rangé dans un dossier `Redis/`, est un `Dictionary` en mémoire ;
- `IReturnPolicyApplicationService`, déclaré et jamais écrit.

**Conséquence : toute la plateforme applique la même politique de retour**, quelle que soit
la catégorie ou le vendeur — fenêtre de 14 jours, preuve et inspection exigées, 0 % de frais
de remise en stock, retour à la charge du client pour `ChangedMind`, approbation automatique
pour `WrongItem` et `DamagedOnArrival`.

**Le retrait des routes ne corrige pas cela.** Il retire ce qui mentait ; la constante reste,
et elle est load-bearing. La rendre variable est un lot serveur à part entière — agrégat,
table, migration, résolution par portée — à chiffrer quand la politique devra varier par
catégorie ou par vendeur.

### Et la passerelle savait

Les métadonnées de la route retirée disaient déjà : « les deux points de terminaison de
return-policies sont des lambdas en dur rendant une liste littérale — aucun dépôt, aucune
commande derrière. La route est posée pour que la surface soit cohérente et que l'écart se
voie. »

Le pari n'a pas tenu, et c'est la leçon du lot : **l'écart ne se voyait que dans le fichier de
configuration de la passerelle**. Pour tout consommateur — y compris la console qu'on écrit —
la route répondait 200 et 201. Elle ne rendait pas la surface cohérente : elle la rendait
crédible.

---

## LOT 3 — Boutiques — ✅ **fait le 23/08** (aucun serveur)

**Le seul écran écrivable en l'état.** Vérifié : `GET /api/v1/merchants/{sellerId}/stores` et
`GET /{storeId}` existent sur `MapSellerGroup`, et ce groupe laisse entrer Admin et
Moderator. La gouvernance — `suspend`, `lift-suspension` — est sur `MapAdminGroup`.

**Pourquoi ça manque :** suspendre une **boutique** n'est pas suspendre un **vendeur**. Un
vendeur peut en tenir plusieurs, et le domaine le dit : « `SuspendStoreCommand` ne porte
volontairement PAS de SellerId, son handler emprunte le chemin sans contrôle de propriété :
le domaine la déclare décision d'admin ». Aujourd'hui la console agit sur le vendeur
entier — c'est-à-dire trop.

**Forme :** un panneau **dans la page Vendeurs**, pas une entrée de menu. La liste des
boutiques dépend d'un vendeur sélectionné ; une section autonome exigerait de recoller un
sélecteur de vendeur déjà présent à côté.

**Contenu :** liste des boutiques du vendeur sélectionné (nom, statut, commune), et pour
chacune : suspendre / lever la suspension, avec mot de passe — c'est une sanction.

**Ce que l'écran dit :** `Closed` est la fermeture décidée par le VENDEUR — congés,
travaux, saison — et elle lui appartient ; `Suspended` est celle de la PLATEFORME, et « le
vendeur ne peut pas la rouvrir lui-même, sinon la sanction ne durerait que le temps d'un
clic ». **« Lever » n'est donc offert que sur une suspension**, jamais sur une fermeture :
un bouton dessus laisserait croire que la plateforme peut rouvrir une boutique que son
gérant a fermée.

### Livré

Panneau greffé dans la page Vendeurs, sous les gestes de gouvernance. Sélectionner un
vendeur charge ses boutiques ; la liste se **vide avant** de recharger, sinon celles du
vendeur précédent restent affichées sous le nom du nouveau le temps d'un aller-retour — ce
qui suffit pour suspendre la mauvaise.

Trois distinctions que l'écran rend visibles, et qu'aucune autre vue ne portait :

1. **« ouverte » ≠ « en vente ».** `IsSelling` répond « ses offres sont-elles achetables EN
   CE MOMENT » : une boutique ouverte hors de ses horaires ne vend pas. Afficher le seul
   statut ferait chercher une panne là où il n'y a qu'un jeudi soir.
2. **Le motif de suspension est exigé** alors que `ReasonRequest(string? Reason)` l'accepte
   vide. Il atterrit dans `StatusReason`, que la vitrine publique n'expose pas, et c'est la
   seule trace de la raison d'une sanction. Sans lui, on retrouve un mois plus tard une
   boutique fermée sans savoir pourquoi ni si on peut la rouvrir.
3. **« aucun lieu d'expédition rattaché »** en alerte. Le lieu vit dans Inventory et n'est
   ici qu'un identifiant ; son absence signifie que rien ne peut être enlevé chez cette
   boutique — un dossier incomplet qui ne se voit pas depuis la fiche vendeur.

**Une garde qui n'est pas la même qu'ailleurs.** Le `DenyUnlessOwnSellerAsync` de
merchant-service court-circuite sur `Admin` **seulement**, là où celui de financial-service
laisse aussi passer `Moderator`. Un modérateur recevra donc 403 sur la lecture des
boutiques. L'échec du panneau est isolé dans son propre message : il n'efface pas la
confirmation d'un geste de gouvernance qui vient d'aboutir.

---

## LOT 4 — Factures — ✅ **fait le 23/08** (fusionné avec le LOT 5)

Bloquée **deux fois**, et l'ordre compte.

1. **Écrire `ListInvoicesQuery`** dans billing-service — paginée, filtrable par statut et par
   période, avec comptage par statut. Le dépôt a déjà `ListBySellerAsync` ; il manque la
   liste plateforme.
2. **La monter sur `invoices` avec `.RequireAdmin()`** — la liste porte le chiffre d'affaires
   commissionné vendeur par vendeur.
3. **Puis seulement** ajouter la route de passerelle vers `/api/financial/invoices`.
4. Écran : liste + fiche, et les quatre gestes existants (créer, ajouter une ligne, émettre,
   marquer payée).

**L'ordre 2 avant 3 n'est pas négociable.** Relayer avant de garder exposerait la donnée à
tout compte authentifié — c'est la fuite que `ComputeCommissionAsync` vient de refermer sur
la donnée voisine.

---

## LOT 5 — Commissions — ✅ **fait le 23/08** (fusionné avec le LOT 4)

1. `.RequireAdmin()` sur `GET /api/financial/commissions` — la route de liste n'en a pas,
   contrairement à ses cinq voisines en écriture, et elle rend les règles de portée `Seller`,
   c'est-à-dire les taux négociés.
2. Route de passerelle vers le préfixe.
3. Écran : liste des règles, création, modification, activation/désactivation, **et l'aperçu
   `compute`** — qui existe côté serveur et délègue au vrai moteur. C'est ce qui distingue une
   page de réglage d'un formulaire à l'aveugle, et Tarification a dû s'en passer.

Ce lot peut être fusionné avec le LOT 4 : même service, même mur, même correction de
passerelle. **Les faire ensemble économise la moitié du coût.**

### Ce qui a été fait, dans cet ordre

1. **`.RequireAdmin()` sur `commissions.MapGet("/")`** — seule route de son groupe sans garde,
   et elle rend les règles de portée `Seller`, c'est-à-dire les taux négociés.
2. **`IInvoiceRepository.ListForAdminAsync`** — page, total et comptes par statut. Les comptes
   et le total sont calculés **sans** `Include(i => i.Lines)` ; les inclusions ne portent que
   sur la page.
3. **`ListInvoicesQuery` + son gestionnaire**, statut illisible ignoré plutôt que refusé —
   même choix que les listes voisines d'identity et de return-refund.
4. **`invoices.MapGet("/", ListInvoicesAsync).RequireAdmin()`**.
5. **Puis** les deux entrées de passerelle : `financial-commissions` et `financial-invoices`
   (ordre 9, cluster `Financial`). La passerelle est à **55 routes**, `check-gateway.py` propre.
6. Écrans : `CommissionsViewModel` / `CommissionsView`, `FacturesViewModel` / `FacturesView`.
   Les deux entrées de menu sont passées de `AEcrire` à `Prete`.

### Ce que ces deux écrans ne couvrent pas

- **Le détail d'une facture n'existe nulle part.** `InvoiceMapper.ToSummary` laisse tomber les
  `InvoiceLine`, et `GetInvoiceQuery` rend ce même résumé : une ligne ajoutée n'est jamais
  relue, seul le total change. L'écran l'annonce **avant** l'ajout et le répète après. Le
  corriger élargirait un contrat que les clients vendeur consomment déjà — décision distincte.
- **« Marquer payée » n'encaisse rien.** `MarkPaid` change le statut ; aucun mouvement de
  portefeuille, aucun rapprochement. La référence demandée à la saisie **ne part pas** : la
  route ne transporte aucun corps. L'écran le dit plutôt que de laisser croire.
- **Le taux par défaut** appliqué quand aucune règle ne correspond vit dans la configuration
  du service financier, pas dans la grille. L'aperçu signale le cas ; il ne le règle pas.
- **`min > max` reste accepté par le serveur.** `ComputeCommission` applique plancher puis
  plafond sans `Math.Clamp` : le réglage donne alors toujours le maximum. La console le refuse
  avant l'envoi ; le domaine, lui, ne le refuse pas.
- **Le périmètre d'une règle n'est pas modifiable** — `UpdateCommissionRuleCommand` ne reprend
  ni `Scope` ni `TargetId`. Les deux champs sont donc désactivés hors création.

---

## LOT 6 — Recommandations — ✅ **fait le 23/08**

`POST /api/engagement/recommendations` existe sur le groupe admin et persiste réellement. Le
commentaire du service dit l'enjeu : « écrire une recommandation, c'est écrire la page
d'accueil ».

**Il manque une route de liste** — exactement la situation des avis avant le lot précédent,
et la correction est la même : une requête paginée, `ApiResults.Page`, montée sur le groupe
admin sous un chemin qui ne collisionne pas avec le groupe authentifié voisin.

Puis un écran : ce qui est mis en avant, par produit et par utilisateur, avec l'upsert.

**Priorité basse** tant que les recommandations sont calculées automatiquement : personne
n'attend cet écran aujourd'hui.

### Ce qui a été fait

1. **`IRecommendationRepository.ListAsync`** — page, total, compte par type, le compte calculé
   avant le filtre. Les entités sont **matérialisées et non projetées** : `RecommendedProductIds`
   est un accesseur sur un champ que la configuration EF ignore explicitement (`builder.Ignore`),
   seul `_recommendedProductIds` est mappé, sur une colonne `uuid[]`. Un `Select` qui toucherait
   la propriété échouerait à la traduction.
2. **`ListRecommendationsQuery` + son gestionnaire**, type illisible ignoré plutôt que refusé —
   même choix que la modération des avis et les listes de facturation.
3. **`recommendationsAdmin.MapGet("/", …)`** sur le groupe **admin**, avec `ApiResults.Page`.
   Cette page dit quels produits la plateforme pousse et sur les fiches de qui : c'est la donnée
   que la garde d'écriture protège déjà, la relayer en lecture ouverte l'annulerait par l'autre bout.
4. **Aucune route de passerelle à ajouter.** `/api/recommendations` est relayée depuis un lot
   antérieur, réécrite vers `/api/engagement/recommendations`, et couvre tous les verbes — la
   passerelle reste à 55 routes.
5. Écran : `RecommandationsViewModel` / `RecommandationsView`, nouvelle entrée de menu
   `recommandations` dans « CONTENU & SUPERVISION », icône `Etincelles` (distincte d'`Etoile`,
   qui sert la modération des avis — une note d'acheteur n'est pas une mise en avant).

### Correction de passage : une raison fausse dans un commentaire que j'avais écrit

Le commentaire du groupe de modération des avis justifiait `/moderation` plutôt que `/` par une
collision de routage avec le groupe authentifié voisin. **C'est inexact** : celui-ci monte
`MapGet("/{id:guid}")` — un segment de plus — et `MapPost("/")` — un autre verbe. ASP.NET Core
les distingue tous les deux. Le chemin reste le bon pour une autre raison, qui tient :
`GET /api/engagement/reviews` se lirait « les avis » alors que la route rend une **file
d'arbitrage**. La raison a été corrigée sur place ; c'est elle qui aurait survécu au bogue.

### Ce que cet écran ne couvre pas

- **Rien ne supprime une recommandation.** Le dépôt n'expose qu'`AddAsync` et des lectures ;
  aucune route ne retire une clé. Une mise en avant posée reste posée jusqu'au remplacement
  suivant. Retirer demande aujourd'hui d'agir en base.
- **L'enregistrement REMPLACE.** `Refresh` réécrit la liste entière et le score sur la clé
  (type + contexte), et le serveur rend le même 201 que pour une création. L'écran demande un
  motif quand la clé existe déjà dans la liste — le motif ne part nulle part, il force à
  regarder ce qu'on efface.
- **La commande n'exige aucun contexte.** Sans produit ni utilisateur, une ligne est créée que
  les trois lectures adressées du service ne retrouveront jamais. L'écran l'interdit et
  signale par une pastille celles qui existent déjà ; le serveur, lui, l'accepte toujours.
- **Rien ne distingue une ligne écrite à la main d'une ligne calculée.** Le domaine ne garde
  pas cette information : un recalcul du moteur remplacera l'une comme l'autre, sans trace.
- **Les produits ne sont que des identifiants.** Le service de recommandation est un read model
  sans accès au catalogue ; afficher des noms demanderait un appel croisé par ligne.
- **Non corrigé, signalé sur place :** `GetProductRecommendationsQueryHandler` rend une absence
  sous la forme d'une recommandation vide portant `Guid.Empty` et `DateTime.MinValue`. Un client
  qui affiche la date écrit « calculé le 01/01/0001 ». Ces deux lectures sont consommées par les
  applications acheteur ; un 404 changerait leur chemin d'erreur. La console passe par la liste
  et ne rencontre jamais ce cas.

---

## Hors périmètre : les six qui demandent un service

Ces sections ne sont pas des écrans à écrire. Les inscrire dans un plan d'écrans donnerait
une fausse idée du reste à faire.

| Section | Ce qu'il faudrait construire | Ordre de grandeur |
|---|---|---|
| **Analytics** | Une série temporelle — rien ne groupe par période sur aucun agrégat. Les agrégats ponctuels existent déjà (stats paiement, files, facettes). | Plusieurs jours, et une décision d'architecture : agrégat de lecture ou calcul à la volée |
| **Marketing** | Un agrégat de campagne plateforme — budget, ciblage, arbitrage entre vendeurs. promotion-service n'a que du vendeur. | Un service |
| **Notifications** | Une surface d'exploitation : envoi de masse, gabarits, vue de livraison. Les quatre groupes actuels sont ceux du destinataire. | Un service |
| **Bannières** | Un service de contenu éditorial. L'application cliente déclare le même manque sous `content`. | Un service |
| **Fraude** | Un moteur de score et de règles. Le seul signal existant est `ProcessingWithdrawalView.Anomaly`, déjà affiché par Retraits. | Un service |
| **Taxes** | Un agrégat `TaxRule` et sa résolution. Deux commentaires de `CommissionCommands.cs` le décrivent **comme existant** — il n'existe pas. | Un service |

**Décision à prendre sur le panneau lui-même :** ces six entrées affichent aujourd'hui un
texte expliquant l'absence. C'est utile une fois, pénible ensuite. Deux options — les
regrouper sous une seule entrée « Non couvert » listant les six, ou les retirer et laisser le
document d'audit porter l'information. Je pencherais pour la première : l'écran est le seul
endroit que quelqu'un rouvre.

---

## Séquence recommandée

```
LOT 1  observabilité          ✅ fait   aucun écran, débloque tous les incidents
LOT 2  return-policies        ✅ fait   retirée ; la constante de politique reste
LOT 3  Boutiques              ✅ fait   panneau dans Vendeurs, pas une entrée de menu
LOT 4+5 Factures+Commissions  ✅ fait   fusionnés ; garde AVANT passerelle
LOT 6  Recommandations        ✅ fait   liste + écran ; aucune passerelle à toucher
──────
puis   corriger les 3 textes faux du panneau (Fraude, Taxes, Analytics)
       et étendre check-config-and-guards.py aux *Command cités sans exister
```

**Les six lots sont faits.** La console est à **21 sections prêtes sur 29** ; les huit
restantes sont le rang 6 de l'audit précédent, et aucune n'est un écran à écrire — ce sont
six services à décider, à chiffrer et à prioriser sur autre chose que la complétude d'un menu.

Il reste deux dettes nommées ci-dessus, toutes deux hors lot : **corriger les trois textes
faux du panneau** (Fraude, Taxes, Analytics — un panneau qui décrit faux vaut moins qu'un
panneau vide) et **étendre `check-config-and-guards.py`** aux noms en `*Command` /
`*CommandHandler` cités sans exister, qui aurait attrapé `UpdateTaxRuleCommand` tout seul.

---

## Deux garde-fous pour la suite

**Ne pas relayer avant de garder.** Les deux blocages de passerelle du lot 4+5 portaient la
même donnée sensible — le taux et le chiffre d'affaires par vendeur — et ont été levés dans
cet ordre : garde d'abord, route ensuite. L'inverse ne se voit pas à l'exécution : rien
n'échoue, la donnée est simplement lisible par tout compte authentifié le temps du
déploiement. Le lot 6 n'a rien eu à ouvrir, et c'est ce qu'il fallait vérifier avant d'y
toucher plutôt qu'après.

**Ne pas construire sur une route qui n'a jamais tourné.** Ce chantier a rencontré trois fois
le même défaut : `ListUsersQuery` écrite et jamais montée, `Brand.Archive()` sans appelant,
`return-policies` qui simule. Avant d'écrire un écran sur une route, vérifier qu'un appelant
existe — ou l'appeler soi-même une fois.
