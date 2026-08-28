# Décisions d'architecture — HBAExpress Backend

Registre des arbitrages pris pendant la mise en conformité au cahier des charges v2.
Chaque décision dit ce qui a été choisi, contre quoi, et ce que ça coûte.

---

## D1 — `Seller` est conservé, la spec s'aligne sur le code

**Date** : 17 août 2026 · **Statut** : tranchée

Le §3 du cahier des charges nomme le service « Merchant Service » et ses tables
`merchants` / `outlets`. Le code l'appelle `Seller` / `Store` depuis l'origine.

**Décision : `Seller` est conservé**, et désigne indifféremment un vendeur marketplace
et un restaurant. C'est la spec qui sera mise à jour, pas le code.

**Pourquoi.** Renommer aurait touché 83 fichiers, les `.proto`, les noms de topics
Kafka et le schéma de base — pour un gain purement lexical. Surtout, `Seller` porte
déjà le sens voulu : le diagramme du design système montre bien un « Seller Service »
dans le domaine Marketplace, et un restaurant y est un vendeur comme un autre.

**Conséquences.**

- `SELLER_SERVICE_NOT_FOUND` remplace `MERCHANT_SERVICE_NOT_FOUND` dans les codes
  d'erreur normalisés. `ServiceCodes.Merchant` est devenu `ServiceCodes.Seller`.
- `seller-service` doit passer son code explicitement à `UseHbaService`, la déduction
  automatique donnant `SELLER` — ce qui est désormais correct, donc rien à passer.
- Le cahier des charges doit être corrigé au §3, §10.3 et dans les événements
  `merchant.created`, `merchant.kyc.*`, `outlet.status.changed`, qui deviennent
  `seller.*` et `store.*`.

**Ce qu'on perd.** La conformité littérale au document v2. Un lecteur qui compare
les deux trouvera un écart : c'est pourquoi il est écrit ici.

---

## D2 — Clé de `consumer_inbox` : `(event_id, consumer_name)`

**Date** : 17 août 2026 · **Statut** : tranchée par défaut, **à confirmer**

Le §19.5 présente `event_id` comme clé primaire de `consumer_inbox`. Le §11.3 exige
`unique(event_id, consumer_name)`. Les deux sont incompatibles.

**Décision : le couple**, conformément au §11.3.

**Pourquoi.** Avec `event_id` seul, un événement traité par le Notification Service
serait considéré comme déjà traité par le Wallet Service : le premier consumer servi
ferait taire tous les autres. La colonne `consumer_name` du §19.5 n'aurait d'ailleurs
aucun usage si elle ne participait pas à la clé.

**À confirmer avant la première migration** — après, le changement coûte une reprise
de données.

---

## D3 — Dépréciation des chemins REST plutôt que rupture

**Date** : 17 août 2026 · **Statut** : tranchée

Le passage de `/api/users` à `/api/v1/users` casse les applications mobiles déjà
installées, qui ne se mettent pas à jour à la demande.

**Décision** : la passerelle expose les deux. `users-v1` sert le nouveau chemin,
`users-legacy` réécrit l'ancien vers le nouveau. À retirer quand la télémétrie montre
que plus personne n'appelle l'ancien.

**Limite** : la coquille couvre les CHEMINS, pas la FORME du corps. Les réponses
passent sous l'enveloppe du §5 dans les deux cas.

---

## D4 — Le code plus riche que la spec : la spec s'aligne

**Date** : 17 août 2026 · **Statut** : ouverte

Plusieurs services modélisent plus que le cahier des charges ne demande : le paiement
distingue `CapturePayment` et `FailPayment` là où la spec ne connaît que
`GetPaymentStatus` ; des domaines entiers — messagerie, avis, recommandations,
wishlist, facturation, règles de commission — n'y figurent pas du tout.

**Position par défaut, tant que rien n'est tranché : ne rien supprimer.** La mise en
conformité ajoute et renomme ; elle ne retire pas de fonctionnalité au motif qu'un
document ne la mentionne pas.

---

## D5 — Migrations manquantes : `dotnet ef`, pas la main

**Date** : 18 août 2026 · **Statut** : tranchée

Douze tables sont configurées sans qu'aucune migration ne les crée : `mfa_challenges`,
`preferences`, `devices`, `notification_templates`, plus `consumer_inbox` et
`idempotency_keys` dans les quatre schémas qui les appliquent.

Le dépôt a des précédents de migrations écrites à la main, et le `Up`/`Down` n'est pas
la partie difficile — ce sont des `CreateTable` mécaniques.

**Le piège est le `ModelSnapshot`.** EF ne compare pas le modèle à la base, il le
compare au snapshot. Un snapshot écrit à la main qui diverge, même légèrement, de ce
qu'EF aurait produit ne casse rien tout de suite : c'est le PROCHAIN
`migrations add`, des semaines plus tard, qui régénère les mêmes tables ou sort un
diff fantôme. Le coût de l'erreur est différé et retombe sur quelqu'un sans le
contexte.

**Décision** : `scripts/db/add-missing-migrations.sh` (`make migrations`). Les quatre
projets ont une `IDesignTimeDbContextFactory` autonome, donc la génération tourne
hors ligne, sans docker ni postgres.

**Conséquence assumée** : ces migrations ne peuvent pas être produites depuis un
environnement sans SDK .NET. Le contrôle `check-migrations.py` reste rouge jusqu'à ce
que la commande soit passée — et c'est exactement ce qu'on lui demande de dire.

---

## D6 — Un contrôle statique doit prouver qu'il attrape encore

**Date** : 18 août 2026 · **Statut** : tranchée

Quatre des sept contrôles de `check-all.sh` ne vérifiaient plus rien depuis la
réorganisation en monorepo : trois pointaient un `src/services` disparu et levaient un
`FileNotFoundError` que le lanceur comptait comme un échec ordinaire ; le quatrième
lisait zéro fichier C# et signalait en conséquence les cent clés du compose comme
orphelines.

**Ce qui rend la panne dangereuse** : un contrôle mort est indiscernable d'un contrôle
vert tant qu'on ne lit que le compte final, et un contrôle qui crie pour rien finit
ignoré — c'est le seul échec qui compte.

**Décision** : tout contrôle réparé est validé par INJECTION DE FAUTE — on casse
volontairement ce qu'il surveille, on vérifie qu'il le voit, on restaure. C'est ainsi
qu'a été trouvé le point aveugle de `check-di` : il comptait un enregistrement porté
par un `*Installer.cs` que le `Program.cs` n'appelait pas.

**Corollaire** : un contrôle qui n'a rien à comparer doit le DIRE — comme
`check-event-consumers`, qui annonce « Monolithe introuvable » au lieu de rendre vert.

---

## D7 — Media et File ne sont dans aucun §10 du cahier des charges

**Date** : 18 août 2026 · **Statut** : ouverte

Le cahier des charges décrit SEIZE services, de `10.1 Identity` à
`10.16 Promotion`. **Ni Media Service ni File Service n'y figurent.** Le stockage
objet y est une capacité rattachée à d'autres services : « S3/MinIO pour documents
KYC » chez Merchant (§3), « S3/MinIO pour médias » chez Catalog (§3).

L'image du système de conception, elle, liste huit services communs dont Media et
File. Le dépôt a suivi l'image : media-service existe, est extrait, et son README
explique qu'il a consolidé cinq implémentations S3 dispersées — trois dans Catalog,
deux dans Sellers.

**Décision** : ne pas inventer un §10.17. Il n'y a pas de contrat à respecter pour
ces deux services, donc rien à quoi les « rendre conformes ». Ce qui leur est
appliqué est le SOCLE commun aux quatorze autres — enveloppe du §5, codes d'erreur
normalisés, nommage d'événements du §19, chemins `/api/v1` — et des tests.

**Conséquence** : les paragraphes cités dans les commentaires de media-service
(« §14 », « §16 », « §20 »…) renvoient à une numérotation qui n'est PAS celle du
cahier v2. Ils datent d'un document de conception antérieur, propre au service. Ne
pas les lire comme des références au cahier — c'est le seul endroit du dépôt où
`§` est ambigu.

**À trancher** : soit le cahier gagne un §10.17 et un §10.18, soit ces deux services
sont documentés hors cahier. Tant que rien n'est décidé, D4 s'applique : on n'enlève
rien.

---

## D8 — file-service n'est pas créé, et son squelette est retiré

**Date** : 18 août 2026 · **Statut** : tranchée

Le squelette `services/common/file-service` existait pour que l'arborescence
corresponde au diagramme du système de conception. Son README annonçait qu'il
absorberait « la partie stockage brut » de media-service.

**Trois faits ont décidé.**

1. **media-service a CONSOLIDÉ ce qu'on proposait de re-disperser.** Son README le
   dit : il a rassemblé cinq implémentations S3 éparpillées — trois dans Catalog,
   deux dans Sellers — derrière un seul port `IObjectStorage`. Extraire « le
   stockage brut » referait le chemin en sens inverse, un an plus tard.

2. **L'ordre « octets d'abord, ligne ensuite » est une garantie LOCALE.** Le
   domaine le documente comme portant : déposer les octets avant d'écrire la
   métadonnée évite une ligne « Uploaded » désignant un objet inexistant. Le
   compromis assumé est l'objet orphelin, que la purge de rétention ramasse.
   Séparer les deux services transforme cet ordre en garantie distribuée et ajoute
   le mode de panne inverse — une partition réseau entre la ligne et ses octets.

3. **Rien ne le référençait.** Aucun `.cs`, aucun `.json`, aucun compose, pas la
   solution. Les deux seules mentions étaient documentaires.

**Décision** : les quatre projets vides et leur `Dockerfile` partent dans
`_to_delete/`. Le pointeur du README de `proof-of-delivery-service` est corrigé
vers media-service — les preuves de livraison y sont déjà `MediaType.DeliveryProof`,
en visibilité `Restricted` et 180 jours de rétention.

**Conséquence assumée** : l'arborescence ne correspond plus au diagramme sur ce
point. C'est le diagramme qui a tort, et `docs/REORGANISATION.md` le dit désormais.

**Ce qui ferait revenir la décision** : un besoin que media ne couvre pas —
téléversement en plusieurs morceaux avec reprise, presigned upload (le fichier
précède la métadonnée), ou quarantaine antivirus. Aucun n'est demandé aujourd'hui,
et construire pour eux serait construire sur une hypothèse.

---

## D9 — Infrastructure : VPS OVH auto-hébergé, manifests Kustomize

**Date** : 18 août 2026 · **Statut** : tranchée

Le cahier Infrastructure (§20) demande du Terraform pour le réseau, le DNS, le
cluster et les services managés, et cite « Helm/Kustomize » sans choisir.

**Cible** : VPS OVH, Kubernetes auto-hébergé. Postgres, Kafka, Redis et le
stockage objet tournent DANS le cluster.

**Conséquence assumée, et elle est lourde** : le §18 — snapshots, WAL/PITR,
rétention 30 jours, test de restauration mensuel — n'est fourni par personne. Chez
un fournisseur managé, il vient avec la base ; ici il est à construire et à
éprouver. Tant que la restauration n'a pas été testée, il n'y a pas de sauvegarde,
seulement des fichiers.

**Manifests** : **Kustomize**, pas Helm. L'arborescence du §3 (`base/` +
`overlays/dev,staging,prod`) est déjà du Kustomize pur : des patches YAML sur une
base YAML, sans langage de template. Sur vingt-six services qui se ressemblent, un
diff lisible vaut mieux qu'un chart paramétré — et `kubectl` comme Argo CD le
lisent nativement.

Les briques tierces (cert-manager, kube-prometheus-stack, un opérateur Kafka)
resteront installables par Helm : les empaqueter en Kustomize serait réécrire le
travail de leurs auteurs.

---

## D10 — Les images non-root avant les manifests, pas avec eux

**Date** : 18 août 2026 · **Statut** : tranchée

L'audit du 18 août a trouvé **huit** Dockerfiles sur trente qui tournaient en
root : catalog, order, cart, inventory, seller, payment, review, delivery. Les
vingt-deux autres posaient déjà `USER $APP_UID`.

Rien ne le signalait : une image root démarre, sert, et passe tous les tests.

**Décision** : corriger les huit AVANT d'écrire le moindre manifeste. Le §7 impose
un `SecurityContext` non-root ; posé sur une image root, il fait planter le pod au
premier déploiement — et le diagnostic partirait alors du manifeste, qui est
correct, au lieu de l'image, qui ne l'est pas.

**Ce qui est en jeu** : en root, une exécution de code arbitraire dans le processus
devient une exécution root dans le conteneur.

Les deux ports du service — 8080 (REST) et 9090 (gRPC) — sont au-dessus de 1024,
donc le passage non-root n'oblige à rien changer d'autre.

---

## D11 — Terraform et Ansible livrés sans preuve d'exécution, avec un contrôle qui le dit

**Date** : 18 août 2026 · **Statut** : tranchée

`infra/terraform/` et `infra/ansible/` sont écrits, câblés et versionnés. Ils
n'ont **jamais tourné** : sans identifiants OVH, ni `terraform plan` ni
`ansible-playbook` ne sont possibles depuis ce dépôt.

**Décision** : les livrer quand même, et rendre l'absence de preuve *visible dans
l'outillage* plutôt que dans une note qu'on ne relit pas.

`scripts/check-infra.py` est le substitut, neuvième contrôle bloquant de
`check-all.sh`. Il vérifie ce qui se vérifie sans fournisseur : le HCL se parse,
chaque `module { source }` existe, chaque argument passé correspond à une
`variable` déclarée, chaque variable sans défaut est fournie, chaque `var.X` est
déclaré dans son dossier, chaque environnement a un backend distant et une clé
d'état qui lui est propre, chaque rôle Ansible nommé existe, chaque `notify:`
désigne un handler réel, chaque `template: src:` existe. Et il **termine en
disant ce qu'il n'a pas vérifié**.

**Pourquoi ce dernier point compte** : un contrôle vert qui ne dit pas sa portée
se lit comme une garantie. Sept injections de faute ont validé chaque règle (D6) —
argument mal orthographié, variable manquante, clé d'état partagée entre les deux
environnements, `notify` vers un handler inexistant, groupe `hosts:` absent,
template manquant, `--disable-network-policy` réellement posé.

Deux défauts trouvés en écrivant ce contrôle, et qui n'auraient rien signalé
autrement :

- **`--disable-network-policy`** : j'avais écrit dans `k8s/base/policies/` que
  k3s n'applique pas les NetworkPolicies. C'est faux — k3s embarque le contrôleur
  de kube-router, activé par défaut. L'affirmation aurait conduit à installer
  Calico sans raison, ou pire, à croire les politiques inertes et cesser de les
  écrire. Corrigée, et le contrôle refuse désormais le drapeau qui les
  désactiverait vraiment.
- **`make migrate`** appelait un script inexistant puis une option refusée : la
  cible échouait systématiquement, et l'on en concluait un problème de migrations.

**Écart assumé, à fermer avant la production** : le plan de contrôle k3s n'est pas
redondé — un seul serveur, même à trois nœuds. Encadré dans
`infra/ansible/roles/k3s-serveur/` et listé dans `docs/DEPLOIEMENT.md`, §4.4.

---

## D12 — ProductOffer reste la source du prix transactionnel

**Date** : 18 août 2026 · **Statut** : tranchée (par HECTOR)

Le cahier Catalog met le prix sur la révision produit et sur la variante (§8, §11).
L'existant le met sur `ProductOffer` — agrégat séparé portant commission, frais
fournisseur et prix acheteur — que cart-service consomme en gRPC
(`ListPurchasableOffers`, `ListOffersBySku`). Les deux modèles ne peuvent pas
coexister sans que l'un devienne la vraie source.

**Décision** : `ProductOffer` reste ce que l'acheteur paie. Le `basePrice` /
`compareAtPrice` de la révision est un prix de **référence vendeur** : la base à
partir de laquelle une offre est créée, et la valeur qui, modifiée, exige une
nouvelle validation (§6).

**Ce que cela évite** : réécrire `AddItemToCartCommandHandler` et quatre RPC, et
surtout décider où vont la commission et les frais fournisseur — dont le cahier
Catalog ne parle pas du tout.

**Le piège qui reste** : un affichage public qui lirait `basePrice` montrerait un
prix **hors commission**, donc inférieur à ce qui sera facturé, et l'écart ne se
verrait qu'au panier. L'encadré en tête de `ProductPricing` le dit à l'endroit où
la faute s'écrirait.

---

## D13 — BIGINT pour les nouvelles tables catalog seulement

**Date** : 18 août 2026 · **Statut** : tranchée (par HECTOR)

Le §21 impose des montants XOF en entier. L'existant stocke des `decimal` en
`numeric(18,2)` via le VO `Money` partagé par tout le dépôt.

**Décision** : `bigint` pour la tarification des révisions ; `product_offers`
reste en `numeric(18,2)`.

**Pourquoi pas partout** : convertir `product_offers` toucherait `Money`, donc
onze services, et les RPC gRPC qui rendent les montants en chaîne. Le gain serait
théorique — le XOF n'a pas de subdivision, `decimal` ne perd rien.

**Le prix** : deux conventions dans le même schéma. Elles ne se rencontrent qu'à
un endroit, et il est encadré dans `ProductPricing`.

---

## D14 — Les champs descriptifs quittent `products`, et `product.Name` disparaît

**Date** : 18 août 2026 · **Statut** : tranchée

Le §6 exige qu'une fiche publiée reste servie pendant qu'une nouvelle version est
en validation. Cela demande deux jeux de champs — celui que le vendeur édite,
celui que l'acheteur voit.

**Décision** : nom, slug, description, catégorie, marque, prix, condition,
attributs et mots-clés quittent `products` pour `product_revisions`. `Product`
n'expose **plus** `Name` : il expose `CurrentRevision` et `PublishedRevision`.

**Pourquoi pas une lecture traversante `product.Name`** — la solution qui n'aurait
cassé aucun appelant : elle aurait dû choisir une révision pour tout le monde. La
vue vendeur et la vue acheteur diffèrent précisément pendant une validation,
c'est-à-dire au moment où l'erreur coûte le plus cher : montrer au public un texte
que personne n'a relu. Le compilateur pose maintenant la question à chaque appel.

**Deux machines à états, assumées.** `ProductStatus` (§5) décrit le produit,
`RevisionStatus` la version. Un produit `PUBLISHED` peut avoir une révision
`PENDING_REVIEW` — c'est tout l'objet du §6. Une seule énumération aurait retiré
la fiche de la vente à chaque correction de faute de frappe. C'est `Product` qui
les fait avancer ensemble, jamais l'appelant.

**Deux défauts trouvés en chemin** :

- `ProductStatusChangedIntegrationEvent` était publié sur Kafka à **chaque**
  changement de statut et **n'avait aucun consommateur** dans tout le dépôt.
  Retiré, remplacé par les huit événements du §19, qui portent enfin de quoi agir
  (le vendeur, la révision, le relecteur).
- `shared/contracts/HBA.Catalog.Contracts.Grpc/ProductsGrpc.cs` comparait le
  statut à la chaîne littérale `"Active"` pour décider de la visibilité. Le
  renommage `Active → Published` aurait rendu **invisible chaque produit en
  vente**, sans exception ni journal. C'est ce genre de ligne qui justifie
  l'encadré en tête de `ProductStatus`.

**Reprise des données** : `services/marketplace/catalog-service/MIGRATION-REVISIONS.md`.
Le `StoreId` des fiches antérieures reste NULL — aucune valeur n'est déductible, et
la garde de soumission les empêche d'avancer tant que personne ne les rattache.

---

## D15 — Le préfixe `/api/v1/` : service par service, avec coquille de dépréciation

**Date** : 18 août 2026 · **Statut** : tranchée

Quatre services servaient déjà `/api/v1/` (identity, media, promotions, users),
onze non. Trancher service par service produit des clients qui doivent retenir
lequel est lequel ; trancher les onze d'un coup produit un diff qui touche onze
services et la passerelle, à tester en entier avant de reprendre le catalogue.

**Décision** : migration **service par service**, en commençant par catalog
(cinquième aligné). Chaque migration est un couple indissociable :

1. le service renomme sa racine en `/api/v1/<service>` ;
2. la passerelle garde une **coquille de dépréciation** sur l'ancien chemin, avec
   un `Transform.PathPattern` qui réécrit vers le nouveau.

**Pourquoi la coquille n'est pas facultative.** Sans elle, toutes les applications
déjà installées reçoivent 404 sur la surface entière du service, à la seconde du
déploiement — et une application mobile ne se met pas à jour à la demande. Pour le
catalogue, cela signifie la vitrine entière.

**Le piège que ce couple cache**, et qui a motivé un test : laisser l'ancien chemin
ROUTÉ sans réécriture donne exactement la même panne, en silence. La passerelle
transmet `/api/catalog/...` à un service qui ne sert plus que `/api/v1/catalog/...`,
le cluster répond, et le test des quinze préfixes publics reste **vert** puisque le
préfixe est bien routé. `RoutingTests.Chaque_coquille_de_depreciation_reecrit_vers_
le_chemin_versionne` exige donc un `PathPattern` sur toute route nommée `*-legacy`.

**Deux coquilles par service, pas une** : la séparation lecture/écriture
(anonyme + limiteur `read` sur les GET, authentifié + `write` ailleurs) doit être
dupliquée à l'identique. Une coquille unique en `Authenticated` fermerait la
vitrine anonyme aux anciens clients — c'est-à-dire à tous, le jour du déploiement.

**Retrait** : quand la télémétrie montre que plus personne n'appelle l'ancien chemin.

---

## D16 — L'enveloppe §25 est adoptée ; les codes d'erreur restent dérivés du type

**Date** : 18 août 2026 · **Statut** : tranchée

`shared/ApiResults` enveloppait déjà toutes les **erreurs** et normalisait leur
code depuis `ErrorType` (`VALIDATION_ERROR`, `CONFLICT`, `BUSINESS_RULE_VIOLATION`…),
en reportant le code fin du domaine dans `details.reason`. Catalog, lui, rendait
ses **succès** nus : 59 `Results.Ok`, zéro `ApiResults.Ok` — l'état que
`ApiResults.cs` décrit lui-même comme « le pire des deux mondes », le client devant
tester le status code avant de savoir comment lire le corps.

**Décision** : envelopper les succès de catalog ; **ne pas** élargir `shared` aux
22 codes du §24 pour l'instant. Élargir la table changerait la réponse d'erreur des
quatorze services d'un coup, et tout client qui teste un code exact devrait être
livré au même instant.

**Ce qui a été ajouté à `shared`, et pourquoi c'était nécessaire** :

- `ApiResults.Page(PagedResult<T>)` et `ApiMeta.Facets`. `PagedResult` porte des
  facettes — la répartition du catalogue par statut. Envelopper avec la surcharge
  `(items, page, pageSize, total)` compile, rend une réponse d'apparence correcte
  et **jette les facettes en silence** : le graphe de la console d'administration
  serait devenu vide, et l'on aurait cherché la cause dans la requête.
- `ApiResults.NotFound(serviceCode)` et `Unauthorized()`. Un `Results.NotFound()`
  nu rend un corps **vide** : pas de `error.code`, et surtout pas de
  `meta.requestId`. La réponse dont on a le plus besoin de savoir d'où elle vient
  était la seule qui ne le disait pas.

**Idempotence : `AllowIdempotency`, pas `Require`.** Le §25 rend l'en-tête
obligatoire sur les créations. Posé aujourd'hui, il refuserait en 400 chaque appel
de création des applications installées — aucune ne l'envoie. Ce qu'on échange :
un double POST peut créer deux fiches, dommage visible et réparable, sur des routes
qui ne débitent ni ne commandent. Les clients migrent un par un ; le passage à
`RequireIdempotency()` sera d'une ligne par route.

**Rôle `Seller` (§22)** : la surface vendeur du catalogue exige désormais
`Seller`, `Admin` ou `Moderator`. Les deux derniers parce que
`DenyUnlessProductOwnerAsync` les laisse **déjà** passer délibérément, pour qu'un
modérateur puisse corriger la fiche d'un vendeur injoignable ; les exclure aurait
fermé au niveau du groupe un chemin que le handler ouvre trois lignes plus bas.

---

## D17 — L'observabilité vit dans le socle, et la pile démarre enfin

**Date** : 18 août 2026 · **Statut** : tranchée

Avant ce lot, **seule la passerelle** était instrumentée. Les quatorze services
n'émettaient ni trace, ni métrique, ni journal structuré : une trace commençait à
la passerelle, montrait le saut YARP, et s'arrêtait à la frontière du service.

**Décision** : `AddHbaTelemetry` est appelé par `AddHbaService`, donc posé sur les
quatorze services d'un coup. Même raisonnement que la `FallbackPolicy` : un
branchement à faire quatorze fois est un branchement qu'on oublie une fois — et le
service oublié est muet sans que rien ne le signale.

**Aucun paquet nouveau.** Les cinq paquets OpenTelemetry étaient déjà épinglés
pour la passerelle. Pour la base et gRPC, on s'abonne aux sources que Npgsql et
`Grpc.Net.Client` émettent nativement plutôt que d'épingler
`OpenTelemetry.Instrumentation.EntityFrameworkCore`, encore en bêta. On perd le
découpage par `DbContext` ; on garde le SQL et sa durée.

### Le défaut principal : l'outbox coupait toutes les traces

`KafkaIntegrationEventPublisher` posait un en-tête `traceparent` depuis l'origine,
à partir de `Activity.Current`. Cela n'a **jamais rien propagé** : l'outbox existe
précisément pour publier *plus tard*, depuis un service d'arrière-plan où
`Activity.Current` est nulle. Et le consommateur, lui, ne lisait pas cet en-tête.

Le pire des deux mondes : le code de propagation existait, avait l'air correct, et
ne propageait rien. Une commande passée produit huit effets asynchrones — stock
réservé, paiement, notification, course créée — dont aucun n'apparaissait sous la
trace de la commande.

La correction est en trois points : `outbox_messages.trace_parent` capture le
contexte **à l'écriture**, `OutboxProcessor` le rejoue **à la publication**, et le
consommateur reconstitue le parent depuis l'en-tête reçu.

### Trois erreurs dans une pile jamais démarrée

`infra/observability/` était écrit, relu, versionné — et **aucun compose ne le
lançait**. En le branchant, trois défauts sont apparus, tous invisibles tant que
rien ne tourne :

1. **Prometheus moissonnait treize points d'entrée inexistants.** Les services
   poussent leurs métriques en OTLP ; aucun n'expose `/metrics`. Treize jobs en
   échec permanent, zéro métrique, et un diagnostic qui aurait commencé par
   « l'instrumentation ne marche pas » alors qu'elle marchait. On moissonne
   désormais le collecteur, seul point d'entrée réel.
2. **Le pipeline de traces n'exportait que vers `debug`** — la sortie standard du
   collecteur. Les traces étaient reçues et imprimées, interrogeables nulle part.
   Tempo est ajouté comme backend.
3. **Les deux fichiers de provisionnement Grafana étaient intervertis** : les
   sources de données dans `provisioning/dashboards/`, le fournisseur de tableaux
   de bord hors de `provisioning/`. Grafana aurait démarré sans aucune source ni
   aucun tableau de bord, et la cause aurait été un nom de dossier.

---

## D18 — Pas de Serilog : journaux OpenTelemetry et console JSON

**Date** : 18 août 2026 · **Statut** : tranchée

Le plan de lot disait « Serilog ». Le dépôt n'en a aucun paquet : il utilise
`Microsoft.Extensions.Logging` partout, avec un `LoggingBehavior` MediatR.

**Décision** : pas de Serilog. On branche l'exportateur de **journaux**
OpenTelemetry et un formateur console JSON.

**Ce que cela donne, et que Serilog n'aurait pas donné de surcroît** : chaque
ligne porte le `traceId` et le `spanId` de l'activité courante. On passe d'une
trace lente à ses journaux d'un clic, au lieu de recouper des horodatages entre
quatorze flux. Aucun des milliers d'appels `_logger.LogInformation` existants
n'est réécrit.

**Ce que Serilog aurait coûté** : une seconde pile de journalisation à côté de
celle d'ASP.NET Core, sur quatorze services, de nouveaux paquets à tenir à jour —
et la corrélation avec les traces aurait quand même demandé un enricher OTel.

**`ClearProviders()` avant `AddJsonConsole()`** : sans lui, chaque ligne est écrite
deux fois — une en texte, une en JSON. Le volume double et les agents de collecte
comptent chaque erreur deux fois.

**Console JSON désactivée par défaut**, activée par le compose : imposer le JSON
partout rendrait `docker compose logs` illisible pendant le développement, et
c'est là qu'on lit le plus de journaux à l'œil.

### Corollaire : `make migrations` ne voyait pas les colonnes

`outbox_messages.trace_parent` est une **colonne**, sur les quatorze services.
`check-migrations.py` ne détecte que les **tables** manquantes : il aurait affiché
« ✓ Aucun service n'a de table configurée sans migration » et n'aurait rien
généré. Au premier démarrage, quatorze services seraient tombés sur
`42703: column o.trace_parent does not exist` — juste après une commande qui
venait d'annoncer que tout allait bien.

`add-missing-migrations.sh` a donc une **seconde passe** qui ne devine rien et
demande à EF : `dotnet ef migrations has-pending-model-changes`, sur tous les
projets Infrastructure, quelle que soit la nature de l'écart.

**L'injection de faute a trouvé un défaut dans cette passe elle-même.** Sa
première version cherchait le mot « pending » dans la sortie pour reconnaître un
écart. EF n'écrit pas ce mot — son message est « Changes have been made to the
model… ». Les quinze services étaient donc déclarés « contrôle impossible » alors
qu'ils avaient tous un vrai écart. Le test reconnaît désormais l'**échec
d'outillage**, pas l'écart : se tromper de ce côté fait échouer bruyamment le
`migrations add` qui suit, tandis que l'autre sens faisait sauter une migration
nécessaire en n'affichant qu'un avertissement.

---

## D19 — `ApproveReactivation` exige la demande préalable

**Date** : 19 août 2026 · **Statut** : tranchée

La méthode acceptait un compte simplement `Closed`, alors que son nom et son code
d'erreur — `no_reactivation_request` — affirmaient qu'une demande était requise.
Un test du lot 1 figeait l'écart sous le préfixe `Ecart_`, en attendant qu'on
tranche : ce n'était pas nécessairement faux, un administrateur pouvant vouloir
rouvrir un compte fermé par erreur.

**Décision** : c'est le **nom** qui avait raison. La garde n'accepte plus que
`PendingReactivation`. Le parcours est
`Closed → RequestReactivation → PendingReactivation → ApproveReactivation`.

**Pourquoi** : réactiver un compte est un geste que son titulaire doit avoir
demandé, et `RequestReactivation` existe pour porter cette volonté. Sans
l'exigence, un administrateur remettait en vente un commerçant qui avait fermé
boutique sans que rien ne trace qu'il souhaitait revenir — le geste ressemblait à
une réouverture consentie, il n'en était pas une.

**Ce qu'on perd, et qui est assumé** : rouvrir un compte fermé PAR ERREUR n'a plus
de chemin direct. Si l'exploitation en a besoin, ce sera un geste **distinct**,
nommé pour ce qu'il fait — pas une garde élargie en silence sous un nom qui dit
autre chose. Le principe général : **le code et son nom doivent dire la même
chose**, et quand ils divergent, on corrige celui des deux qui a tort plutôt que
de documenter l'écart.

---

## D20 — Les boutiques dans la fiche vendeur : projection HTTP, pas champ de contrat

**Date** : 19 août 2026 · **Statut** : tranchée

Le §10.3 montre les boutiques **imbriquées** dans `GET /merchants/{id}`. Le
service ne les rendait pas, et le client enchaînait un second appel à
`GET /{id}/stores` — deux allers-retours pour ouvrir un écran.

Mais `SellerSummary` traverse le **proto gRPC**, lu par catalog, inventory, order
et payment.

**Décision** : une projection HTTP séparée, `SellerDetail`, dans la couche
Application. Le contrat inter-services ne bouge pas.

**Pourquoi pas un champ de plus sur `SellerSummary`** : le mappeur gRPC n'aurait
aucune raison de le remplir, et les quatre appelants distants recevraient une
liste **vide**. Ils en concluraient que le vendeur n'a pas de boutique. C'est le
mode de panne silencieux déjà payé avec `CatalogClient` désérialisant une
enveloppe dans un objet aux champs nuls : rien ne lève, rien ne compile de
travers, la donnée est simplement fausse. Un champ n'existe que là où il est
réellement rempli.

**`SellerDetail : SellerSummary`, par héritage de record.** Le constructeur de
copie — `base(seller)` — reprend les quatorze champs sans en nommer un seul : une
recopie à la main aurait compilé aujourd'hui et divergé au premier champ ajouté.
L'héritage donne aussi la **forme JSON du §10.3** — champs du vendeur à plat,
`stores` à côté — là où un record enveloppant (`{ seller: {…}, stores: […] }`)
aurait cassé, en silence, tout client lisant `data.shopName`. Un test
d'intégration garde les deux moitiés : ce qui s'ajoute, et ce qui n'a pas bougé.

**Corollaire** : `GetSellerQuery` est supprimée. Une fois la route HTTP basculée,
plus personne ne l'envoyait — les appels inter-services passent par
`ISellerModuleApi`, jamais par MediatR. `SellerSummary` cesse d'être `sealed`,
avec l'unique dérivation documentée sur place.

---

## D21 — Le compte de reversement a son propre RPC, et il n'est pas mis en cache

**Date** : 19 août 2026 · **Statut** : tranchée · **Corrige** : `AUDIT-SELLER-RESTE.md` §1

`SellerSummary.Payout` existe sur le record C# ; le proto `merchant.v1` ne le
transporte pas. Le mappeur du client gRPC écrivait donc `Payout: null` en dur.
wallet-service, hébergé par payment-service, résout `ISellerModuleApi` sur ce
client — et lisait ce champ pour autoriser un retrait.

**Conséquence : aucun vendeur de la plateforme ne pouvait sortir son argent.**
Chaque demande était refusée par « Aucun compte de versement Mobile Money
configuré », message que le vendeur lisait avec son numéro MTN sous les yeux. Et
la validation administrative d'une demande existante ne se contentait pas de
refuser : elle partait dans `FailAndRefundAsync` — l'admin cliquait « approuver »
et la demande était **détruite**, sur un motif faux.

**Décision** : un RPC dédié, `GetSellerPayout`, et un type de retour
`SellerPayout(SellerExists, Account)`.

**Pourquoi pas un champ de plus sur `GetSeller`** : cette réponse-là est appelée
en boucle par la fiche produit mobile et **mise en cache dix minutes**. Y poser le
numéro Mobile Money du vendeur ferait circuler une coordonnée de paiement sur le
chemin le plus chaud de la plateforme, et la ferait vivre dans un cache de
lecture — alors qu'un seul appelant en a besoin, une fois par retrait.

**Et sans cache, délibérément.** Un nom de boutique périmé de dix minutes n'a
jamais fait de mal ; un numéro Mobile Money périmé, si : c'est l'argent envoyé à
l'ancien numéro d'un vendeur qui vient de corriger une faute de frappe. Un test
d'intégration garde cette propriété, précisément pour que personne ne remette ce
chemin dans le cache « par symétrie ».

**Trois cas, plus un seul `null`.** `SellerPayout` distingue « vendeur inconnu »,
« vendeur sans compte » et « voici le compte ». Les deux premiers rendaient le
même `null` que le champ perdu en transport : c'est cette confusion qui a rendu le
défaut illisible pour l'utilisateur comme pour le support. Un identifiant inconnu
est une erreur ; un compte manquant est une étape d'onboarding à terminer.

### Ce que cette décision ne règle PAS

Le mappeur gRPC invente encore **cinq** champs — `Rating`, `SalesCount`,
`KybDocuments`, `Metadata`, `KybRejectionReason` — et `StoreSummary.IsSelling`
compare le statut à `"Active"`, valeur qui n'existe pas dans `StoreStatus` : toute
boutique est fermée pour un appelant distant. Personne ne les lit **aujourd'hui**,
et c'est la seule raison pour laquelle ils ne sont pas dans ce lot.

La sortie définitive est de **séparer les contrats** — un `SellerSummary` local et
complet, un contrat distant qui ne porte que ce qui voyage — pour que le
compilateur interdise ce qu'un commentaire se contente d'avertir. Voir
`AUDIT-SELLER-RESTE.md` §5, étape 4.

---

## D22 — La propriété d'une pièce KYB se vérifie aux DEUX bouts

**Date** : 19 août 2026 · **Statut** : tranchée · **Corrige** : `AUDIT-SELLER-RESTE.md` §2

`Seller.AddKybDocument` refuse un `mediaId` vide et rien d'autre. Son encadré
délègue le reste « à l'appelant — la couche qui voit les deux — qui contrôle que
le média est de nature `SellerDocument` et qu'il appartient à CE vendeur ». Cette
couche était le handler ; il ne le faisait pas. La documentation renvoyait ensuite
au **BFF Vendeur**, dont le `Program.cs` annonce lui-même n'exposer aucun cas
d'usage.

**Une délégation vers un destinataire inexistant se lit comme une décision
d'architecture. C'est un trou.**

Deux exploitations, à la portée d'un vendeur inscrit : rattacher le média d'un
concurrent à son dossier puis s'en faire signer l'URL — c'est mot pour mot la
faille que le passage de `FileUrl` à `MediaId` devait fermer ; et rattacher puis
retirer, ce qui faisait supprimer le fichier chez media-service — une primitive de
**suppression arbitraire** contre les photos produit, les visuels de restaurant ou
le dossier KYB d'autrui.

**Décision** : le contrôle est posé aux deux bouts.

- **seller-service** interroge `IMediaModuleApi` avant de rattacher : le média
  doit exister, appartenir à `(Seller, sellerId)`, être de nature
  `SellerDocument`, et être `Ready`. Gabarit repris de
  `AddProductMediaCommandHandler`, qui applique la même chaîne depuis le lot média
  du catalogue.
- **media-service** vérifie ce qu'il s'apprête à détruire. Son handler de
  suppression portait déjà l'argument juste — passer par un ÉVÉNEMENT plutôt qu'un
  appel gRPC, « pour ne pas donner à merchant-service le droit de détruire des
  objets qu'il ne possède pas » — et l'annulait en supprimant sur parole. Le droit
  était donné, simplement par un autre chemin.

**Pourquoi les deux, et pas seulement l'émetteur** : seller-service n'est pas le
seul émetteur possible de ce type d'événement. Un consommateur qui détruit sur
parole redevient dangereux au premier émetteur suivant.

**Un refus côté média ne lève pas.** Trois rejeux d'un message qui ne réussira
jamais n'apportent rien : ce n'est pas une panne transitoire, c'est une demande
illégitime. Warning, et aucun fichier supprimé — un fichier non supprimé est un
incident bien moins grave qu'un fichier détruit à tort, et celui-là serait
irréversible.

**Convention posée au passage** : une pièce KYB est un média
`(OwnerType=Seller, OwnerId=sellerId, MediaType=SellerDocument)`. `OwnerId` est
l'identifiant du **vendeur**, pas celui du compte utilisateur — un compte peut
cesser d'être rattaché au dossier, le dossier reste.

### Ce que ça casse, et qui devait casser

`scripts/seed-accounts.sh` envoyait `mediaId: $(new_guid)` en le documentant :
« ni son existence ni son appartenance ne sont vérifiées […] en production, c'est
un trou ». Il téléverse désormais un vrai PDF minimal — les magic bytes sont lus,
un fichier vide serait refusé. Sans cela, le dossier resterait sans pièce, donc
jamais validé, donc jamais actif, et aucune boutique ne pourrait ouvrir.

Idem pour les tests d'intégration : `DeposerPieceAsync` déposait un GUID au
hasard. Le faux média de la suite est désormais **pilotable** — chaque test
choisit le propriétaire, la nature et l'état du fichier. Un faux qui dirait
toujours oui rendrait vert un service ayant reperdu son contrôle.

---

## D23 — Le lieu d'expédition se vérifie, et `IsSelling` voyage

**Date** : 19 août 2026 · **Statut** : tranchée · **Corrige** : `AUDIT-SELLER-RESTE.md` §3 et §4

### Le lieu d'expédition (§3)

Jumeau exact de la pièce KYB (D22), avec la même délégation dans le vide.
`AttachStoreLocationCommand` portait : « L'appartenance du lieu au vendeur n'est
pas vérifiée ici. […] Le contrôle est fait par l'appelant, qui voit les deux
modules — **voir la route du BFF Vendeur**. » Le BFF Vendeur annonce lui-même
n'exposer aucun cas d'usage.

N'importe quel GUID passait : le `SellerAddress` d'un concurrent, un
`PlatformWarehouse`, ou un identifiant inexistant. `Store.Open()` acceptait
ensuite la boutique, et l'identifiant partait vers delivery, qui bâtissait un
enlèvement coursier sur une adresse que le vendeur ne contrôle pas. **Le GUID
inexistant ne se manifestait qu'APRÈS le paiement de l'acheteur**, sur la jambe
coursier.

**Décision** : le handler interroge `IInventoryModuleApi.GetLocationAsync` et
exige `OwnerId == sellerId`.

**« Sellers ne connaît pas Inventory » n'était pas un argument** : le contrat
existait et transportait déjà `OwnerId`, il n'était simplement pas appelé.
Dépendre d'un contrat pour valider une entrée qu'on va persister n'est pas une
fuite de couche — c'est ce que fait déjà `RegisterSeller` avec Identity, et
`AddKybDocument` avec Media.

**Un entrepôt plateforme est refusé**, et c'est délibéré : son `OwnerId` est nul
par construction (FBP), et l'accepter rendrait la garde inopérante. Confier une
boutique à un entrepôt de la plateforme est une décision d'exploitation, pas un
geste de vendeur ; le jour où elle sera nécessaire, elle méritera sa propre route
d'administration, nommée pour ce qu'elle fait.

### `IsSelling` (§4)

Le mappeur du client gRPC calculait `IsSelling` en comparant `status` à
`"Active"` — valeur qui **n'existe pas** dans `StoreStatus` (Draft, Open, Closed,
Suspended). Toute boutique était donc fermée pour un appelant distant, en silence.

Le piège est réel : `"Active"` EST une valeur légitime… de `SellerStatus`. Le même
mappeur traite les deux vocabulaires, et l'un a été appliqué à l'autre.

**Décision** : `bool is_selling` au proto. Le serveur connaît la réponse —
`Store.IsSelling` — donc il l'envoie.

**Pourquoi pas corriger la chaîne** : remplacer `"Active"` par `"Open"` aurait
marché aujourd'hui et laissé un prédicat de DOMAINE recalculé côté client à partir
d'un statut sérialisé. Renommer une valeur d'énumération l'aurait cassé de
nouveau, en silence. C'est la même leçon que D21 : un champ n'est pas un
commentaire, il se transporte ou il n'existe pas.

**Un champ de moins dans la liste des inventés.** Il en reste cinq — `Rating`,
`SalesCount`, `KybDocuments`, `Metadata`, `KybRejectionReason` — et la sortie
définitive reste la séparation des contrats (`AUDIT-SELLER-RESTE.md` §5).

---

## D24 — Deux contrats : ce qui voyage, et ce qui reste

**Date** : 19 août 2026 · **Statut** : tranchée · **Corrige** : `AUDIT-SELLER-RESTE.md` §5

`SellerSummary` portait quatorze champs ; le proto `merchant.v1` en transporte
huit. Les six autres — `Rating`, `SalesCount`, `Payout`, `KybDocuments`,
`Metadata`, `KybRejectionReason` — recevaient du mappeur gRPC une valeur neutre
(`0`, `null`, liste vide) qu'aucun appelant distant ne pouvait distinguer d'une
vraie.

**Une interface, deux sémantiques.** Dans seller-service, `ISellerModuleApi`
rendait le vendeur ; ailleurs, un objet *en forme* de vendeur, dont l'argent et
les pièces d'identité avaient été remplacés par du plausible. Rien — ni le type,
ni la DI — ne distinguait les deux, et `WalletCommands` a été écrit contre la
première pour recevoir la seconde le jour où wallet a été replié dans
payment-service. Coût constaté : **aucun retrait vendeur possible sur la
plateforme** (D21).

**Décision** : `SellerSummary` porte exactement les huit champs du proto — c'est
sa définition. La vue riche vit dans `SellerDetail`, côté Application, et ne sort
jamais du service.

**Trois options avaient été posées** (audit §5). A — compléter le proto : le
contrat grossit, et le RIB comme les pièces d'identité circulent entre services
qui n'en ont pas l'usage. B — un RPC dédié par champ : fait pour le compte de
reversement (D21), disproportionné pour les cinq autres, que personne ne lit à
distance. **C — séparer les contrats**, retenue : c'est la seule qui fasse tenir
la règle par le COMPILATEUR plutôt que par un commentaire.

**Ce que la séparation répare au passage** : `SellerModuleApi.Map` était
positionnel, s'arrêtait à `Metadata` et laissait `KybRejectionReason` tomber sur
son défaut. Le même vendeur n'avait donc pas de motif de refus selon le chemin de
lecture. La divergence disparaît par construction — il n'y a plus de champ à
oublier.

**Et un gain de lecture non prévu** : `GetSellerAsync` n'a plus besoin de
`.Include(KybDocuments)`. Cette lecture est appelée en boucle par la fiche produit
mobile ; elle chargeait les références des pièces d'identité de chaque vendeur, à
chaque affichage, pour les jeter aussitôt.

### Le piège de `/merchants/me`, et pourquoi un test le garde

`GetSellerByUserQuery` rendait `SellerSummary`. La laisser suivre l'allègement
aurait fait disparaître les six champs de l'écran d'accueil de l'application
vendeur — **sans une erreur** : `SellerAccount`, côté passerelle, est un record
positionnel sans `[JsonPropertyName]`, les champs absents seraient devenus `0` et
`null` à la désérialisation, et le double de test de la passerelle construit ce
record directement, sans passer par du JSON. Rien n'aurait échoué.

Elle rend donc `SellerDetail`, et un test d'intégration énumère les six champs un
par un — une liste de compatibilité doit échouer sur celui qui disparaît, pas sur
un représentant.

**Règle qui en découle, écrite dans les deux fichiers** : ajouter un champ à
`SellerSummary`, c'est l'ajouter au proto dans le même geste. Sinon on rouvre le
trou à l'identique.

---

## D25 — La file de modération est paginée, filtrée, et allégée

**Date** : 19 août 2026 · **Statut** : tranchée · **Corrige** : `AUDIT-SELLER-RESTE.md` §6

`GET /api/v1/merchants` était l'unique entrée de la file de validation KYB. Elle
rendait **tous** les vendeurs, **avec toutes leurs pièces** (`ListAsync` faisait un
`.Include(KybDocuments)` sur la table entière), sans pagination et sans filtre —
pas même sur `KybStatus`, la seule chose qu'un modérateur cherche.

Et elle rendait le `SellerSummary` COMPLET : en un appel, le numéro Mobile Money de
chaque vendeur, son RCCM, son IFU, le téléphone de son gérant, et les références de
ses pièces d'identité.

**Décision** : `PagedResult<SellerListItem>`, avec `search`, `kybStatus`, `status`,
et des facettes par statut KYB.

**`SellerListItem` ne porte ni le compte de retrait, ni les pièces, ni les
informations légales, ni le taux de commission.** Le rôle administrateur reste
nécessaire ; il n'est plus l'excuse de la charge utile. Une console a le droit
d'afficher ces données — sur la fiche qu'un humain ouvre délibérément, pas dans un
listing qu'un écran charge au réveil. Le compte des pièces remplace leurs
références : c'est ce dont la file a besoin.

**Les facettes se comptent sur la recherche, PAS sur la page ni après le filtre de
statut.** Sur la page, la console dirait « 1 en revue » sur une file qui en
contient quarante. Après le filtre, la facette sélectionnée serait la seule non
nulle — et l'on annoncerait « aucun dossier en revue » au modérateur qui vient de
filtrer sur « vérifié ».

**Un filtre illisible est ignoré, pas refusé.** La console construit ces valeurs
depuis ses propres listes déroulantes : un 400 sur une faute de frappe
transformerait une colonne mal nommée en écran blanc, et l'on croirait la file en
panne. Un filtre inconnu ne restreint rien — le modérateur voit toute la file et
comprend aussitôt.

**`AsSplitQuery()` est obligatoire, pas une optimisation.** En requête unique,
`Skip`/`Take` s'appliquent aux lignes du JOIN : un vendeur portant trois pièces
consommerait trois places de la page, et une page de vingt en rendrait sept. Le
défaut ne se voit qu'avec des dossiers inégalement fournis — c'est-à-dire en
production, jamais sur un jeu de test où chacun a une pièce. L'ordre doit aussi
être TOTAL (`CreatedOnUtc` puis `Id`) : en requête scindée, deux inscriptions de la
même milliseconde feraient diverger les deux requêtes sur la frontière de page.

**La forme de la réponse ne change pas pour autant** : `data` reste un tableau, la
pagination vit dans `meta` (§25). Seul le contenu des lignes s'allège.

---

## D26 — La note et le compteur de ventes sont alimentés par événement

**Date** : 19 août 2026 · **Statut** : tranchée · **Corrige** : `AUDIT-SELLER-RESTE.md` §7

`Seller.Rating` et `Seller.SalesCount` étaient persistés, projetés, affichés — et
n'avaient **aucun alimenteur** : `Seller.UpdateRating` n'avait pas un seul appelant
dans le dépôt, et rien n'incrémentait `SalesCount`. Toutes les boutiques
affichaient `0` vente et `0/5`. Sur une place de marché, la preuve sociale sur
laquelle repose l'achat était constamment fausse, et fausse dans le sens qui
décourage.

**Décision** : dénormalisation sur `Seller`, alimentée par des événements
d'intégration. C'était le dessein déjà écrit dans `ReviewQueries.cs` — « recalculé
uniquement à la publication/au rejet d'un avis (par le module Sellers) […] la note
vendeur est ensuite persistée sur l'entité vendeur ». Seul le lien manquait.

### On POSE une valeur recalculée, on n'accumule jamais

C'est la règle que l'agrégat écrit déjà sur `SetSalesCount` : « poser le total
exact est idempotent, alors qu'incrémenter double-compterait si l'événement est
rejoué ». Kafka livre au moins une fois ; un compteur incrémental est faux dès le
premier rééquilibrage de partitions.

Conséquence directe sur les tests : chaque cas va par paire, et **la seconde valeur
est toujours plus petite que la première**. Un gestionnaire qui accumulerait
passerait le premier et échouerait le second.

### Un événement dédié plutôt qu'un rappel gRPC

`SellerRatingRecomputedIntegrationEvent(SellerId, Average, Count)` : review-service
recalcule depuis ses propres tables et publie le résultat.

**Pourquoi pas un rappel** — seller-service redemandant la moyenne à
review-service : `HBA.Engagement.Contracts.Grpc` ne contient qu'un `.csproj`. Le
proto y déclare bien `GetSellerRating`, mais aucune classe n'est écrite et
personne ne référence le projet. Construire une couche de transport entière pour un
seul appelant coûte plus cher que de porter trois nombres dans un événement.

**Et il ne porte QUE `SellerId`, délibérément.** `KafkaEventNaming.AggregateId`
choisit la clé de partition dans un ordre fixe où `ProductId` passe **avant**
`SellerId`. Y ajouter le produit ferait partitionner par produit : deux avis du
même vendeur sur deux produits partiraient sur deux partitions, arriveraient dans
un ordre quelconque, et la dernière moyenne écrite pourrait être la plus ancienne
calculée. Sans produit, la clé est le vendeur — ses recalculs se suivent dans
l'ordre, par construction. C'est pour cela que ce n'est pas un champ de plus sur
`ReviewPublished`, ce qui aurait été plus court à écrire.

`SellerId` est ajouté au passage à `ReviewPublished` et `ReviewRejected` — l'agrégat
l'avait en main depuis toujours, et son absence obligeait
`ReviewPublishedNotificationHandler` à résoudre `ProductId → SellerId` par un appel
gRPC au catalogue, un aller-retour par avis.

### Le rejet compte autant que la publication

`GetSellerRatingAsync` ne retient que les avis `Published`. Ne republier qu'à la
publication laisserait un vendeur porter indéfiniment la note d'un avis modéré — et
ce serait le seul endroit où le retrait d'un avis ne se verrait nulle part.

### Un défaut de contrat trouvé en chemin

`GetSellerSalesCountAsync` **ne filtrait pas sur le statut côté gRPC**, alors que
l'implémentation in-process ne compte que les commandes `Confirmed` ou `Delivered`.
Même interface, deux réponses : le nombre de ventes d'un vendeur aurait différé
selon que le lecteur vivait dans order-service ou ailleurs — et le plus faux des
deux est celui que voit l'acheteur. C'est le défaut que D24 vient de fermer sur
`SellerSummary`, présent aussi ici, sur une méthode que personne n'appelait encore.

### Ce que ce lot ne couvre pas

**Une annulation après confirmation ne déclenche aucun recalcul.**
`OrderCancelledIntegrationEvent` ne porte aucun vendeur — ni identifiant, ni parts —
et son producteur ne tient pas les lignes de la commande en main à ce moment-là. Le
compteur reste donc trop **haut** jusqu'à la prochaine vente confirmée du même
vendeur, qui le remet d'aplomb. Il n'est jamais faux par accumulation ; il est en
retard à la baisse. Refermer cela demande d'enrichir l'événement d'annulation côté
order-service : c'est un lot à part, nommé dans `AUDIT-SELLER-RESTE.md` §7.

**Et rien de tout cela n'est encore visible d'un acheteur** : la route publique de
boutique n'existe pas (`GetStoreShowcaseAsync` rend « non implémenté » en dur), et
`SellerPublicSummary` / `SellerDetail.ToPublic()` attendent toujours un appelant.
Portée retenue : les compteurs seulement. La vitrine est un lot distinct.

---

## D27 — La révocation de jeton se vérifie à la passerelle, et elle échoue ouverte

**Date** : 21 août 2026 · **Statut** : tranchée · **Corrige** : `docs/audit/2026-08-21-complet/IMPLEMENTATION_DEFECTS.md` ISSUE-022

`IdentityModuleApi.ValidateAccessTokenAsync` est écrite, complète, et compare le
`security_stamp` du jeton à celui du compte — exactement le contrôle qui permet de
refuser un jeton cryptographiquement valide mais métier-mort. Elle n'a **aucun
appelant**. Déconnexion, changement de mot de passe et suspension n'invalident donc
rien : le jeton reste valide jusqu'à son expiration naturelle, quinze minutes. Le
mécanisme de révocation existait et ne servait à rien.

**Décision** : le contrôle vit **à la passerelle**, et il **échoue ouvert**.

### Pourquoi la passerelle, et pas le socle partagé

Mettre le contrôle dans `AddHbaService` aurait semblé plus rigoureux — chaque
service se défend lui-même. Il aurait fallu `Services:Identity` dans quatorze
entrées de configuration, un client gRPC dans quatorze hôtes, et surtout : identity
serait devenue une **dépendance dure de chaque requête de la plateforme**. Une
latence sur identity serait devenue une latence sur tout.

Tout le trafic externe passe par YARP. Les appels de service à service, eux,
n'utilisent pas de jeton d'utilisateur : leur garde est l'intercepteur à clé
partagée. Le seul endroit où un jeton révoqué peut entrer est donc la passerelle —
un point de contrôle, un cache, un client.

**Corollaire à tenir** : le jour où un service devient joignable hors de la
passerelle, ce raisonnement tombe. C'est une contrainte de déploiement, pas une
opinion — la même que celle qui gouverne `OUTBOX_ENABLED`.

### Pourquoi ouvert plutôt que fermé

Le dépôt refuse de démarrer plutôt que de simuler, et c'est la bonne règle au
démarrage : une plateforme qui ne boote pas se répare en cinq minutes. Elle ne
s'applique pas ici. Fermer signifierait qu'une panne d'identity rend 401 à tout le
monde, paiements en cours compris : l'indisponibilité d'un service deviendrait
l'indisponibilité de la plateforme.

Ouvert, un compte suspendu conserve ses droits **pendant la panne**, borné par la
durée de vie du jeton. C'est-à-dire exactement le risque que l'on subit
aujourd'hui en permanence — mais réduit aux minutes d'une panne.

**L'échec doit être BRUYANT** : journal `Critical`, pas `Warning`. Un contrôle de
sécurité désactivé en silence est pire que son absence, parce que personne ne le
sait.

### Cache court, et clé par jeton

Le résultat est mémorisé par empreinte de jeton, quelques dizaines de secondes :
assez pour qu'une rafale de requêtes d'une même session ne produise qu'un appel,
assez bref pour que la révocation reste utile. Un cache long rendrait le contrôle
décoratif — ce qu'il était déjà.

---

## D28 — Une remise porte son financeur, et le vendeur ne supporte que les siennes

**Date** : 21 août 2026 · **Statut** : tranchée · **Corrige** : `docs/audit/2026-08-21-complet/IMPLEMENTATION_DEFECTS.md` ISSUE-052, ISSUE-033

`Promotion` porte un périmètre, un type, une valeur et un budget. **Rien qui dise
qui paie.** Le reste de la plateforme suppose pourtant la distinction :
`CartContracts.cs:33` porte `SellerDiscount` **et** `PlatformDiscount`, et wallet
calcule le gain du vendeur sur `UnitBasePrice - SellerDiscount`. Mais le seul
producteur écrit `SellerDiscount: 0m` en dur.

Conséquence si l'on branche promotion-service tel quel : **le vendeur supporte les
coupons de la plateforme**, silencieusement, via le calcul des gains. Le
prélèvement ne se découvre qu'au premier relevé contesté.

**Décision** : le modèle `Promotion` gagne un **financeur**, et la part vendeur se
calcule sur `UnitBasePrice - SellerDiscount` — jamais sur `FinalUnitPrice`.

### Ce que cela implique

- un champ de financement sur `Promotion`, avec sa migration ;
- la propagation jusqu'au panier : `SellerDiscount` et `PlatformDiscount` cessent
  d'être l'un une valeur en dur et l'autre le total ;
- le report en commande, puis le calcul des gains dans wallet.

**Cette décision débloque aussi un contrôle d'autorisation.** Sans propriétaire,
aucune garde d'appartenance n'était fondable sur `/api/v1/merchant/promotions` :
c'est pour cela que `RequireAdmin` y était contourné. Un financeur donne enfin la
question à poser — « cette promotion est-elle la vôtre ? ».

### Ce que cela ne dit pas

Le partage d'une remise **cofinancée** — moitié plateforme, moitié vendeur — n'est
pas tranché ici. Le champ doit permettre de l'exprimer plus tard sans migration
supplémentaire ; le calcul, lui, peut se contenter des deux cas purs tant que le
commerce ne demande pas le troisième.

---

## D29 — `SellerOrder` est construit

**Date** : 21 août 2026 · **Statut** : tranchée · **Corrige** : `docs/audit/2026-08-21-complet/IMPLEMENTATION_DEFECTS.md` ISSUE-027, ISSUE-026

L'agrégat n'existe pas. `OrderingModuleApi.cs:66` renvoie `SellerOrderId: null` en
dur. Conséquences en chaîne : les cinq permissions `ORDER_CONFIRM`,
`ORDER_REJECT`, `ORDER_MARK_PREPARING`, `ORDER_MARK_READY`, `ORDER_CANCEL` ne
gardent **aucune route** ; le rôle `ORDER_MANAGER` ne peut que lire ; le vendeur ne
peut ni confirmer, ni préparer, ni remettre au livreur.

**Décision** : le construire. C'est le seul point de l'audit qui demande de
construire un agrégat plutôt que de corriger du code, et il conditionne tout le
parcours vendeur.

### Pourquoi pas le report

Reporter aurait exigé de **retirer les cinq permissions** — un rôle qui promet une
autorité qu'il n'exerce pas est pire que son absence, et c'est précisément ce qui a
fait qu'un audit a mis des heures à établir que `ORDER_MANAGER` ne peut rien faire.
Retirer maintenant pour remettre plus tard coûte deux migrations de permissions et
une confusion de plus dans la documentation vendeur.

### Ce qu'il porte

Une commande multi-vendeurs se décompose en une commande par vendeur, avec son
propre état. « Confirmée » n'a de sens qu'à cette échelle-là : aujourd'hui le
statut est global, donc intraduisible en un geste que le vendeur puisse poser.

**Et il ferme un défaut de projection au passage.** La vue vendeur du carnet de
commandes met le frais de port à zéro (voir `OrderMapper.ToSellerSummary`) parce
qu'il est porté par la COMMANDE et qu'aucune règle ne permet de le répartir.
`SellerOrder` donne l'objet qui peut le porter.

---

## D30 — Le domaine livraison est fini ; les quatre squelettes food sont retirés

**Date** : 21 août 2026 · **Statut** : tranchée · **Corrige** : `docs/audit/2026-08-21-complet/IMPLEMENTATION_DEFECTS.md` D-3

Onze dossiers sont des maquettes en mémoire. Ils ne se valent pas.

**Décision** : finir `driver`, `dispatch`, `tracking`, `route` et
`proof-of-delivery` ; **retirer** `menu`, `availability`, `kitchen-prep` et
`food/review`.

### Pourquoi les cinq de la livraison sont indispensables

Sans eux, aucune livraison n'existe : `IDriverLocationCache.SetAsync` n'a aucun
appelant, donc **aucune course n'est jamais proposée à personne**, et la chaîne
livreur est rompue en trois points. Ce n'est pas une fonctionnalité manquante,
c'est le domaine entier qui est inerte. Le code de `delivery-service` est d'ailleurs
correct — ce sont ses amonts qui n'existent pas.

### Pourquoi les quatre du food partent

`restaurant-service` fait déjà le travail : il porte les menus, les articles, les
options, les tickets de cuisine. Les quatre squelettes en sont des doublons —
61 à 98 lignes chacun, un `ConcurrentDictionary` pour persistance, et **quatre
`.proto` qui sont des copies octet pour octet** d'un contrat qu'aucun d'eux ne sert.

Les retirer supprime quatre maquettes, quatre protos morts, et surtout quatre
sources d'ambiguïté sur « qui possède le menu ». Le jour où l'un d'eux mérite
d'exister — un vrai service de disponibilité de tables, par exemple — il sera écrit
à partir d'un besoin, pas d'un dossier vide.

**Les deux BFF vides (`seller-bff`, `driver-bff`) ne sont pas concernés** : ils
déclarent déjà leur état, et leur sort dépend du raccordement des BFF, pas de
celui-ci.

---

## D31 — Le bus est unifié sur le schéma existant ; le §19.2 reste la cible

**Date** : 21 août 2026 · **Statut** : tranchée · **Corrige** : ISSUE-001

Il y avait **trois nommages de sujets Kafka**, et aucun ne se parlait.

Les producteurs dérivaient leur sujet de `SERVICE_NAME` : `seller-service` publiait
sur `service.seller.v1`. Les consommateurs s'abonnaient à une liste de treize sujets
**écrite en dur** qui disait `service.merchant.v1`. Six domaines ne se croisaient
donc jamais — seller/merchant, cart/commerce, payment/financial, review/engagement,
notification/communication, restaurant/food. Vingt-trois sujets étaient publiés que
personne n'écoutait. Aucune erreur : un message part, il est acquitté, il n'arrive
nulle part.

Le troisième était `k8s/overlays/*/kafka-topics.yaml`, qui provisionnait
`hba.<env>.<domaine>.<agrégat>.v1` — le schéma du **§19.2**, qu'aucune ligne de code
ne connaissait.

### Ce qui a été tranché

**Unifier l'existant maintenant, finir le §19.2 plus tard.**

Une table unique — `HbaTopics` — traduit le nom du conteneur en domaine, et elle est
lue des DEUX côtés : le producteur y prend son sujet, le consommateur y prend sa
liste d'abonnement. `SubscribeTopics` est vide par défaut ; deux listes ne restent
jamais d'accord, une seule si.

### Pourquoi pas le §19.2 tout de suite

`HbaEventNaming`, `HbaEventAttribute` et `HbaEventEnvelope` **ne sont pas du code
mort par négligence** : ils implémentent le §19.2, 59 des 139 événements portent
déjà `[HbaEvent]`, et quatre suites de tests l'exigent. La migration a été
commencée, testée, puis abandonnée avant de raccorder le runtime.

Y aller directement demandait d'annoter 80 événements, de rebrancher publieur et
consommateur, de s'abonner par agrégat et de régénérer les manifestes — plusieurs
jours, sur une plateforme qui **n'avait encore jamais échangé un message de bout en
bout**. La faire marcher d'abord, la redécouper ensuite.

### Ce que ce choix coûte, et il faut le savoir

Un sujet par PRODUCTEUR, donc tout le monde reçoit tout et jette après
désérialisation. La rétention et le partitionnement se règlent par service, pas par
agrégat : **impossible de garder les paiements trente jours et les positions GPS une
heure**. Et `service.<domaine>.v1` ne porte pas l'environnement — un courtier
partagé entre deux environnements les mélangerait sans rien dire.

Ces trois limites sont exactement ce que le §19.2 corrige. Elles ne sont pas des
oublis : ce sont le prix, assumé, de la mise en marche.

### Ce qui empêche le retour du défaut

`scripts/check-kafka-topics.py` rapproche les **trois** sources — la table du code,
les `SERVICE_NAME` du compose et de k8s, les sujets provisionnés — et échoue à la
moindre divergence. C'est le seul moyen : le défaut d'origine n'était pas une faute
de frappe, c'était deux vérités qui avaient cessé de correspondre sans que rien ne
puisse le dire.

---

## D32 — Les contrats d'événements sont additifs ; une rupture crée un nouveau type

**Date** : 21 août 2026 · **Statut** : tranchée · **Corrige** : KAFKA §11 (lot 2.4)

`EventVersion` était écrit **`1` en dur** dans l'enveloppe et dans l'en-tête
`event-version`, et le consommateur ne le lisait **jamais**. Les 58 événements
annotés déclarent tous `Version = 1` : aucune version n'a donc jamais changé, et
la question était entièrement devant nous.

Ce qui se serait passé le jour d'une rupture : le producteur publie la nouvelle
forme, chaque ancien consommateur la désérialise **en silence** avec les champs
manquants à `null`, le gestionnaire écrit un effet faux, et la seule trace est un
span vert.

### Ce qui a été tranché

**On n'ajoute que des champs OPTIONNELS.** Une rupture — champ renommé, retiré, ou
champ obligatoire ajouté — crée un **nouveau type** d'événement (`OrderConfirmedV2`),
jamais une version 2 du même.

La raison est le décalage dans le temps : un événement est écrit dans l'outbox,
publié, puis relu par des services déployés à des dates différentes. Deux formes
d'un même type circulent alors ensemble, et rien ne les distingue. Un nouveau TYPE,
lui, est visible : l'ancien continue d'être servi, le nouveau est adopté service
par service.

### Ce que cela ferme, concrètement

`EventVersion` cesse de mentir : il vient de `[HbaEvent].Version`. Le consommateur
le **lit**, et refuse ce qu'il ne sait pas lire — journal `Critical`, effet annulé,
message acquitté. C'est un filet contre une entorse à la règle, pas un mécanisme de
migration.

**On acquitte malgré tout, et c'est un arbitrage.** Bloquer l'offset mettrait à
l'arrêt tous les autres événements du même producteur — paiement, commande,
livraison — pour un message qui viole une convention. La perte d'un message annoncée
en `Critical` est un moindre mal que l'arrêt d'une partition. C'est le même
arbitrage que l'abandon après trois tentatives.

### Ce qui empêche la règle d'être oubliée

`scripts/check-event-contracts.py` tient un instantané versionné des 136 contrats
(`docs/contrats-evenements.json`) et échoue sur toute rupture : champ retiré,
renommé, type changé, ou champ ajouté en `required`.

Il ne rend pas la rupture impossible — il la rend **visible**. Un changement voulu
met l'instantané à jour dans le même commit (`--accepter`), et le relecteur voit
exactement ce qui a bougé. C'est la seule protection possible : la question n'est
pas « à quoi ressemble ce contrat », qu'un compilateur sait, mais « en quoi a-t-il
changé depuis la dernière fois », qui demande une mémoire.

### Ce que cette décision ne dit pas

Elle ne présume pas du §19.2. Le jour où le sujet portera une version par agrégat
(`hba.<env>.<domaine>.<agrégat>.v<major>`, voir D31), la coexistence de deux
versions deviendra possible par le sujet. La règle additive restera néanmoins la
voie normale : elle ne coûte rien et n'oblige personne à migrer.


---

## D33 — Le client est remboursé sur SON portefeuille ; le virement Mobile Money est une seconde étape, manuelle

**Date** : 28 août 2026 · **Statut** : tranchée · **Corrige** : ISSUE-009, et le canal manquant du lot 3.2

### Le problème, tel qu'il se posait

FedaPay **n'expose aucune API de remboursement**. C'est un fait du prestataire, pas
une lacune du code : on ne peut pas rendre l'argent par le chemin qui l'a apporté.
MTN, Moov et PayPal sont dans le même cas dans ce dépôt. Résultat, avant cette
décision : un retour validé, un remboursement décidé, et un appel qui répondait
`Success: false` — le dossier escaladait en `ManualReview`, et le client n'était
**jamais** remboursé automatiquement.

Deux issues étaient possibles : refuser de démarrer en production tant qu'aucun PSP
local ne rembourse par API — ce qui aurait immobilisé la plateforme sur une
dépendance qui ne nous appartient pas — ou rendre l'argent par un autre chemin.

### Ce qui a été tranché

**Un remboursement client crédite le PORTEFEUILLE du client.** L'argent lui est
rendu immédiatement, à l'intérieur de la plateforme, et il peut le dépenser sur une
commande suivante sans rien demander à personne.

**Le virement vers son Mobile Money est une DEMANDE distincte.** Le client la
formule quand il veut ; un administrateur l'exécute chez le prestataire et la marque
payée, avec la référence du virement. C'est le même circuit que le retrait vendeur,
qui existe déjà et qui a déjà été éprouvé.

**Le remboursement PSP reste la voie normale quand le prestataire sait le faire.**
Le routage se fait sur `IPaymentGateway.SupportsRefund` : Stripe rend l'argent sur
la carte, ce qui est mieux pour le client — pas d'étape de retrait. Ce sont les
prestataires qui ne savent pas rembourser qui basculent sur le portefeuille.

### Pourquoi ce chemin plutôt qu'un autre

**Pourquoi le portefeuille et non le versement direct.** Le versement Mobile Money
existe (`IPayoutModuleApi`, utilisé par les retraits vendeur), et il aurait pu être
déclenché à l'instant du remboursement. Deux raisons de ne pas le faire. D'abord un
versement parti ne revient pas : le déclencher automatiquement sur un flux qui
comporte encore des arbitrages — retour litigieux, inspection contestée — c'est
transformer chaque erreur en perte sèche. Ensuite, le numéro Mobile Money du client
n'est porté par aucun contexte de retour : il faudrait le demander au moment le plus
mal choisi, celui où le client attend son argent.

**Pourquoi la validation reste manuelle.** Non par prudence de principe, mais parce
que c'est un point de contrôle des sorties d'argent, et que le même contrôle existe
déjà sur les retraits vendeur. Le rendre automatique se fera le jour où le volume
l'exigera — le canal de versement est déjà en place, il ne manquera qu'une décision.

**Pourquoi dans payment-service et non dans return-refund.** Trois flux remboursent
un client : le retour, l'annulation de commande, et le geste administratif direct.
Les trois passent par `RefundPaymentCommand`. Poser la règle là, c'est la poser une
fois pour les trois — la poser dans return-refund l'aurait laissée absente des deux
autres.

### Ce que cela ferme

Le garde-fou de démarrage introduit au lot 3.2 — refuser la production quand un
prestataire ne sait pas rembourser — **n'a plus lieu d'être** : le client est
désormais remboursé quoi qu'il arrive. Le drapeau `Payments:AllowGatewaysWithoutRefund`
disparaît avec lui. Ce qui reste, c'est un avertissement au démarrage disant par
quel chemin l'argent repart.

### Ce que cela ne ferme PAS

La plateforme porte désormais une **dette envers ses clients** : les soldes de
portefeuille sont de l'argent dû. Il n'existe aucun rapprochement entre le total des
soldes clients et la trésorerie réelle. C'est un rapport d'exploitation à écrire, et
il n'est pas dans ce lot.

Le client qui ne demande jamais son virement garde un solde indéfiniment. Aucune
règle de péremption n'est posée — et il ne faut pas en poser une sans avis
juridique : un solde de portefeuille est une créance.

---

## D34 — Le livreur appartient à delivery-service jusqu'à ce que driver-service sache écrire ; les services se parlent par contrat

**Date** : 3 septembre 2026 · **Statut** : tranchée · **Corrige** : ISSUE-069, ISSUE-070 · **Lot** : 5.4

Le domaine livraison était le seul du dépôt où des services se référençaient par
leur **domaine** et non par leur contrat. Ce n'était pas une entorse isolée : c'était
un **cycle**.

```
HBA.Delivery.Driver.Domain      ──▶ HBA.Delivery.Core.Domain
HBA.Delivery.Core.Application   ──▶ HBA.Delivery.Driver.Domain
HBA.Delivery.Core.Infrastructure──▶ HBA.Delivery.Driver.Domain + Dispatch.Domain
HBA.Delivery.Dispatch.Domain    ──▶ Core.Domain + Driver.Domain
HBA.Delivery.Pricing.Domain     ──▶ Core.Domain + Driver.Domain
```

Aucun de ces services ne pouvait être restauré, construit, versionné ni déployé
seul. Modifier une règle du domaine livreur recompilait le domaine livraison, et
inversement. Trois images embarquaient le code de plusieurs services.

### Ce qui causait réellement le cycle : des fichiers mal classés

Cinq fichiers, pas davantage. Ils vivaient dans le dossier d'un service et
déclaraient le namespace d'un autre :

| Fichier | Dossier | Namespace déclaré | Seuls appelants |
|---|---|---|---|
| `DeliveryDriver.cs` (agrégat `Driver`) | driver-service | `HBA.Deliveries.Domain.Drivers` | delivery-service |
| `IDriverRepository.cs` | driver-service | `HBA.Deliveries.Domain.Drivers` | delivery-service |
| `Capacity.cs` (`VehicleCapacity`) | driver-service | `HBA.Deliveries.Domain.Drivers` | delivery-service |
| `DriverDomainEvents.cs` | driver-service | `HBA.Deliveries.Domain.Drivers.Events` | delivery-service |
| `DispatchPolicy.cs` | dispatch-service | `HBA.Deliveries.Domain.Dispatch` | delivery-service |

Le dossier disait une chose, le code en disait une autre, et **c'est le dossier qui
mentait**. Ces cinq fichiers sont revenus dans `HBA.Delivery.Core.Domain`. Les dix
références de projet croisées sont tombées avec eux — sans qu'un seul `using` change,
puisque les namespaces sont restés identiques.

Les deux références de `delivery-pricing-service` étaient, elles, **entièrement
mortes** : la tarification travaille délibérément sur ses propres primitives
(`string? VehicleType`, `Guid DeliveryId`). Elle payait un couplage dont elle ne se
servait pas.

### Le propriétaire de l'agrégat `Driver` : delivery-service, et pour l'instant seulement

C'est le point qui mérite d'être discuté, parce qu'il déplaît.

À terme, le livreur appartient à **driver-service**. Ce n'est pas ce que dit ce lot.
Ce lot dit que le livreur appartient **aujourd'hui** à delivery-service, parce que
c'est la vérité du code et de la base :

* `delivery-service` déclare `DriverId`, configure la table `deliveries.drivers`,
  la migre, la lit, l'écrit, et publie `DriverVerified` ;
* `driver-service` ne l'a **jamais** lue. Sa maquette tient dans un
  `ConcurrentDictionary` avec des statuts en chaîne de caractères.

Déplacer une table de production vers un service **qui n'a pas de base** aurait
échangé un défaut de structure contre une perte de données. La table n'a donc pas
bougé d'un octet et **aucune migration n'accompagne ce lot**.

**Le transfert reste à faire, et il a un lieu : le lot 5.3.** Quand driver-service
saura persister, l'agrégat le rejoindra et delivery-service l'interrogera par
`shared/contracts/HBA.Drivers.Contracts` — ou par son contrat gRPC — comme
order-service interroge inventory-service. **Il ne faudra pas rétablir la référence
de projet** : une `ProjectReference` vers le domaine du voisin n'est pas un
raccourci, c'est le même déploiement.

### Deux déclarations mortes retirées

L'agrégat `Driver` était déclaré **trois fois** : la vraie (`DeliveryDriver.cs`), et
deux maquettes sans appelant — `DriverAggregate.cs` (`HBA.Drivers.Domain.Drivers`)
et `Entities/*` + `Enums/*` (`HBA.Delivery.Driver.Domain.*`). Les deux mortes sont
parties. C'est ce doublon qui faisait dire à `check-usings.py`, depuis douze
fichiers de delivery-service, que `DriverId` était « déclaré dans
`HBA.Drivers.Domain.Drivers` » : quatorze signalements, tous éteints.

### Ce que ce choix laisse ouvert, et il faut le savoir

**Il reste deux `DriverCandidate` dans le dépôt, délibérément.** Celui de
`HBA.Deliveries.Domain.Dispatch` porte un agrégat `Driver` complet et sert la boucle
de dispatch **interne** à delivery-service ; celui de
`HBA.Delivery.Dispatch.Domain.Entities` porte un identifiant et un score et
appartient à dispatch-service. Les fusionner recréerait exactement la dépendance
qu'on vient de couper. Quand dispatch-service prendra le dispatch en charge (lot
5.2), c'est le premier qui disparaîtra.

**La capacité de charge n'est plus partagée à la compilation.** L'encadré de
`VehicleCapacity` dit que ses seuils sont communs au dispatch et à la tarification.
Ce n'est plus vrai depuis que `delivery-pricing-service` ne référence plus ce
projet — et ce ne l'était déjà pas, puisque la référence était morte. Le risque
d'origine — *un devis qui promet ce que le dispatch refusera* — n'est donc pas fermé.
Il le sera quand la tarification lira les capacités par le contrat de
delivery-service. **En attendant, les deux jeux de seuils se rapprochent à la main.**

**Ce lot ne déplace aucune responsabilité d'exécution.** delivery-service pilote
toujours lui-même le dispatch et le cycle de vie du livreur. Il démêle le graphe de
compilation ; il ne construit pas les services amont.

### Ce qui empêche le retour du défaut

`scripts/check-dockerfiles.py` passe de **5 projets manquants à 0**. Ce contrôle est
le garde-fou réel : une `ProjectReference` croisée réapparue oblige à ajouter un
`COPY` du domaine d'un autre service dans un Dockerfile, et c'est exactement ce que
le script refuse. **Le réflexe à ne pas avoir est d'ajouter le `COPY` manquant** :
si un service doit copier le domaine d'un autre, c'est la référence qu'il faut
retirer, pas le `COPY` qu'il faut écrire.

---

## D35 — L'affectation d'une course est arbitrée par la BASE ; la politique de preuve est décidée par le DOMAINE

**Date** : 4 septembre 2026 · **Statut** : tranchée · **Corrige** : ISSUE-028, ISSUE-056, ISSUE-057, ISSUE-058 · **Lot** : 5.1 / 5.3

### Deux mécanismes pour l'affectation, parce qu'aucun ne suffit seul

`Delivery` était le dernier agrégat vivant sans jeton de concurrence, et rien en
base ne disait qu'un livreur ne porte qu'une course à la fois. Deux acceptations
concurrentes passaient toutes deux la garde de `AcceptByDriver` ; la seconde
écriture écrasait la première sans bruit, pendant que `DeliveryAcceptedDomainEvent`
était levé deux fois. Deux motos, deux rémunérations, un colis.

Deux mécanismes ont été posés, et **ils ne protègent pas la même chose** :

| | Ce qu'il arbitre | Ce qu'il ne voit pas |
|---|---|---|
| `xmin` (`UsePostgresRowVersion`) | Deux écritures sur la **même** course | Tout ce qui se passe sur une autre ligne |
| `ux_deliveries_engaged_driver` | Deux **courses différentes** qui veulent le même livreur | Deux écritures concurrentes sur une seule course |

C'est la confusion que `MemberConfiguration` et `ISellerUnitOfWork` détaillent
déjà : « le verrou optimiste couvre le quota » est faux, parce que `xmin` est un
jeton **par ligne**.

**Le jeton est ici RÉELLEMENT évalué**, contrairement au piège décrit par
`InventoryItem.StockVersion` : `AcceptByDriver` écrit trois colonnes de la ligne
parente — `Status`, `AssignedDriverId`, `AcceptedAtUtc` — donc EF émet bien un
`UPDATE deliveries … WHERE xmin = …`. Aucun compteur à la `StockVersion` n'est
nécessaire. **La règle à tenir** : le jour où une opération ne mutera qu'une
`DeliveryAssignment`, le verrou redeviendra décoratif sur ce chemin-là.

### L'index unique est PARTIEL, et l'index sec de l'audit aurait été faux

`UNIQUE ("AssignedDriverId")` sans filtre interdirait à un livreur d'avoir deux
courses **de toute son histoire**. Le filtre restreint la contrainte aux cinq états
**engagés** (`DriverAccepted` → `ArrivedAtDropoff`).

**Ce qu'il ne couvre pas** : `DriverAssigned` est volontairement hors du filtre —
le dispatch propose à plusieurs candidats, et une proposition n'est pas un
engagement. Et **le jour où HBAExpress autorisera le groupage, cet index devra
tomber** : il encode une décision d'exploitation, pas une loi de la nature.

### `ConcurrencyExceptionHandler` n'avait jamais existé

Quatre configurations et l'encadré de `UsePostgresRowVersion` annonçaient que le
conflit optimiste était « traduit en 409 par `ConcurrencyExceptionHandler` ». **Ce
type n'existait nulle part.** `DbUpdateConcurrencyException` dérive bien de
`DbUpdateException`, mais son exception interne n'est pas une `PostgresException`
— l'`UPDATE` n'a pas échoué, il a touché zéro ligne. Le filtre du bloc « doublon »
ne mordait pas, et **tout verrou optimiste du dépôt ressortait en 500**, ce qui
faisait réessayer les clients sur des écritures perdantes. Le bloc manquant est
désormais dans `ServiceExceptionMiddleware`, **avant** celui des doublons.

### La politique de preuve appartient au domaine, pas aux appelants

`RequiredProof` était persisté, projeté vers l'application livreur, et **renseigné
par personne** : la valeur par défaut du contrat était `"None"`, et `MarkDelivered`
ne demande rien quand la preuve vaut `None`. Toute course de la plateforme était
clôturable d'un geste.

Ce n'était pas une négligence ponctuelle : **c'était la valeur par défaut du
contrat**, donc n'importe quel troisième producteur l'aurait reproduite. Le
paramètre a été retiré du contrat, du proto (champ 7 `reserved`), de la commande et
de l'API. L'appelant **décrit** désormais — `DeclaredValue`, `IsCashOnDelivery` —
et `ProofPolicy`, dans le domaine, **conclut** :

1. paiement à la livraison → `Pin` ;
2. valeur ≥ 50 000 FCFA → `Pin` ;
3. tout le reste → `Photo`. **`None` n'est plus jamais produit.**

**Ce que la règle ne couvre pas** : elle ne produit jamais `Signature` (l'écran
livreur ne sait pas la capturer) ; la valeur déclarée n'est **pas vérifiée** ; le
seuil est en FCFA et **deviendra faux sans rien dire** si une course se règle dans
une autre monnaie ; et `DeclaredValue`/`IsCashOnDelivery` ne sont **pas persistés**
— la décision n'est pas re-dérivable depuis la ligne.

### Trois maquettes corrigées EN TANT QUE maquettes

`dispatch`, `proof-of-delivery` et `tracking` n'ont **ni `DbContext`, ni migration,
ni outbox drainée** (ISSUE-007). Leur état vit dans des `ConcurrentDictionary` de
processus.

Le choix a été de **corriger la logique dans la maquette**, pas d'inventer une
demi-persistance : décider en passant du schéma de ces services est le travail du
lot 5.2 (D30). **Conséquence à annoncer** : ces gardes sont réelles **dans** un
processus et **nulles entre deux** — elles disparaissent au redémarrage et ne sont
pas partagées entre réplicas. Elles existent pour que le défaut **ne survive pas** à
l'implémentation du service.

Le jeton de flux de tracking a été **retiré, pas réparé** : `trk_<guid>` n'était
vérifié nulle part, et ce service n'a aucun point de terminaison de flux à qui le
présenter. Un jeton qu'on ne vérifie pas est pire qu'aucun jeton — **il fait passer
la relecture**. Le réparer aurait voulu dire concevoir la fonction, pas corriger un
défaut.

### L'identité vient du jeton, jamais du corps — encore

`CreateProofRequest.DriverId` et `LocationBatchRequest.DriverId` étaient lus **dans
le corps**, sur des routes anonymes. C'est ISSUE-017/018, refermée à la vague 1 et
rouverte ici. Les deux champs ont disparu des corps de requête.

**Ce qui reste ouvert** : ni proof-of-delivery-service ni tracking-service ne
peuvent vérifier que le livreur du jeton est **celui affecté à la course** — les
affectations vivent dans `deliveries.deliveries` et aucun contrat ne les expose. Le
contrôle posé est l'**appartenance** (sa propre preuve, sa propre session), pas
l'**affectation**. Tracking s'en approche en refusant d'ouvrir une session
lui-même : seul le port interne le peut, et c'est delivery-service qui l'appelle.

**Et l'acheteur ne peut plus suivre sa course** : `latest` est réservé au livreur
suivi et à l'exploitation, faute pour tracking-service de savoir qui a commandé.
C'est un **manque assumé**, préféré à une position GPS en direct ouverte à tout
inscrit — l'état précédent.

### Le doublon qu'il faudra trancher

`Delivery.IssuedPin` + `FailedProofAttempts` (delivery-service, **persistés**) et
`ProofStore` + `OtpChallenge` (proof-of-delivery-service, **en mémoire**)
implémentent le **même** mécanisme deux fois. La question n'est pas « comment
persister le second » mais **lequel des deux garder**. Elle n'est pas tranchée ici.

---

## D36 — Le DOSSIER du livreur est à driver-service, la PROJECTION dispatchable reste à delivery-service ; sa position est écrite là où le dispatch la lit

**Date** : 5 septembre 2026 · **Statut** : tranchée · **Corrige** : ISSUE-029, ISSUE-030, ISSUE-007 (driver-service) · **Lot** : 5.2

### Le défaut n'était pas « un service incomplet », c'était un domaine inerte

`IDriverLocationCache.SetAsync` **n'avait aucun appelant**. `DispatchDeliveryCommandHandler`
lit ce cache pour trouver les livreurs proches du point de collecte ; il était donc
toujours vide, le dispatch concluait « aucun livreur disponible », réessayait cinq
fois et abandonnait la course. **Aucune course n'a jamais été proposée à personne**,
sur une plateforme dont le reste du code de livraison est correct (D30).

Et il y avait pire, en dessous : **`deliveries.drivers` n'avait aucun écrivain**.
`IDriverRepository.AddAsync` n'était appelé de nulle part, et le
`RegisterDriverCommandHandler` que cite `DriverConfiguration` n'a jamais existé.
Même le cache alimenté, `ListByIdsAsync` n'aurait rendu personne.

Enfin, `DriverStore` exposait un **`DefaultDriverId` codé en dur** sur lequel
opéraient les six routes `/api/v1/drivers/me*` : **tous les livreurs étaient le même
livreur** (ISSUE-029). Aucune de ces routes n'ouvrait le `ClaimsPrincipal` — le
service savait qui appelait et ne s'en servait pas.

### Deux objets, deux propriétaires — et la table n'a pas bougé

D34 avait laissé la question ouverte : l'agrégat `Driver` est chez delivery-service
« aujourd'hui seulement ». Ce lot **ne le déplace pas**, et ce n'est plus un
provisoire — c'est une décision :

| | `drivers.driver_accounts` (driver-service) | `deliveries.drivers` (delivery-service) |
|---|---|---|
| Répond à | « cette personne a-t-elle le DROIT de livrer ? » | « à qui puis-je proposer cette course, MAINTENANT ? » |
| Contient | inscription, pièces, véhicule déclaré, décision de vérification | disponibilité, dernière position, mission en cours, compteur |
| Rythme | rare, relu par un humain, conservé pour raisons légales | plusieurs fois par jour, lu à chaud par le dispatch |
| Écrivain | l'exploitation et le livreur | les transitions de course et la session de travail |

Fusionner les deux mettrait le scan d'un permis de conduire sur le chemin du
dispatch, et ferait dépendre l'affectation d'une course d'une table que
l'exploitation modifie à la main. **Aucune migration ne déplace `deliveries.drivers`.**

**Le lien est un événement** : `driver.dossier-verified`, publié par driver-service à
la vérification et consommé par `ProjectDriverOnDossierVerified`, qui crée la ligne
dispatchable. Il porte le nom, le téléphone et le véhicule parce que `Driver.Register`
les exige — un événement réduit aux identifiants obligerait le consommateur à
rappeler l'émetteur, et l'échec de cet appel laisserait un livreur vérifié que le
dispatch ignore.

**`Driver.Register` prend désormais un `DriverId` optionnel**, et c'est
indispensable : sans lui, la projection aurait tiré un second identifiant pour une
même personne. `DriverAccountView` expose ce `driverId` vers l'extérieur et
financial-service tient le portefeuille du livreur sous
`/api/financial/wallets/drivers/{driverId}` — deux identifiants auraient produit deux
portefeuilles, dont un seul se remplirait.

### La position est écrite là où le dispatch la lit, et nulle part ailleurs

Le seul appelant de `SetAsync` est `POST /api/deliveries/mine/position`, **chez
delivery-service**. Trois raisons, dans cet ordre :

1. `IDriverLocationCache` est un **port de ce module**. L'appeler depuis
   driver-service demanderait soit une `ProjectReference` vers ce domaine — le cycle
   que D34 vient de couper —, soit un appel gRPC de plus **toutes les cinq à quinze
   secondes par livreur**, sur le chemin le plus sensible à la latence.
2. La position n'a de sens que rapprochée de la **disponibilité** et de la **mission
   en cours**, qui vivent sur l'agrégat `Driver` de ce module. Trois décisions qui se
   prennent ensemble ou pas du tout.
3. **tracking-service ne convient pas** : c'est encore une maquette en mémoire (D35).
   Faire passer l'alimentation du dispatch par lui ferait dépendre l'attribution des
   courses d'un service qui perd son état à chaque redémarrage et ne le partage pas
   entre réplicas. Le suivi **client** reste son métier.

**La recopie en base est ÉPISODIQUE — cinq minutes.** Redis reçoit chaque
battement ; `Driver.LastKnownPosition` n'existe que pour survivre à un vidage du
cache. L'écrire à chaque battement serait exactement ce que l'encadré de
`IDriverLocationCache` interdit.

### Ce que ce découpage coûte, et il faut le savoir

* **Le livreur parle à DEUX services.** Son dossier est chez driver-service, sa
  session de travail chez delivery-service. Le prix est payé par le client mobile.
* **`DriverSuspendedIntegrationEvent` n'est consommé par personne.** Un livreur
  suspendu dans son dossier **continue de recevoir des propositions**. C'est le
  manque le plus sérieux laissé ouvert ; le geste qui le ferme est un second
  gestionnaire à côté de `ProjectDriverOnDossierVerified`.
* **Le lien est asynchrone.** Entre la vérification et le moment où le livreur peut
  prendre son service s'écoule le temps de l'outbox et du bus — indéfiniment si le
  drain est arrêté. Le livreur voit alors « aucun livreur rattaché à ce compte ».
* **Nom, téléphone et véhicule ne sont recopiés qu'une fois**, à la vérification. Les
  modifier ensuite dans le dossier ne met pas la projection à jour : le client
  appellerait un ancien numéro.
* **Deux énumérations de véhicule** cohabitent, une par service, et le véhicule
  voyage en texte. Une valeur ajoutée d'un seul côté retombe sur `Motorcycle` — c'est
  journalisé bruyamment, ce n'est pas rattrapé.
* **`POST /me/availability` et `GET /me/deliveries` ont disparu de driver-service**,
  et le groupe `/internal/v1/drivers` a été **retiré, pas déplacé** : il n'était
  protégé que par la politique de repli, donc tout compte authentifié pouvait lire le
  dossier d'un livreur et le rendre « occupé », c'est-à-dire l'exclure du dispatch.

### ISSUE-007, pour driver-service seulement

`AddOutboxProcessor<DriverDbContext>()` est posé **à l'endroit exact** où le lot 5.4
avait laissé son encadré. Les événements du module passent maintenant par
`drivers.outbox_messages`, écrits dans la transaction qui produit l'effet métier.

**Les quatre autres services de la livraison — dispatch, route, tracking, preuve —
restent des maquettes sans base ni outbox.** Ce lot ne les touche pas : décider de
leur schéma demande de décider de leur métier.

---

## D37bis — Le code OTP est livré par e-mail ET par SMS ; le fournisseur SMS reste à choisir

**Tranché par HECTOR le 22/08/2026.** ISSUE-062.

### Ce qui était en cause

`IssueOtpChallengeCommandHandler` générait le code, le hachait, le stockait, appliquait
le plafond de cinq tentatives — puis écrivait `_ = code;`. Le clair partait avec la pile.
Le commentaire juste au-dessus affirmait pourtant que « le code EN CLAIR ne sort pas d'ici
autrement que par le canal choisi » : il n'existait aucun canal, ni `ISmsSender`, ni
événement, ni consommateur.

Et la seconde moitié était pire : `verify-otp` rendait `(bool Verified, string Channel)`.
**Aucun jeton.** Même livré, le code n'aurait ouvert aucune session. L'endpoint était
décoratif de bout en bout.

### Les trois options, et pourquoi celle-ci

| Option | Ce qu'elle valait |
|---|---|
| E-mail seul | court — le patron existe et tourne — mais `SMS` est le canal PAR DÉFAUT, et un second facteur par e-mail sur un parcours mobile béninois a peu de sens |
| **E-mail + fournisseur SMS** | **retenue** : le vrai parcours |
| Retirer la route | honnête à court terme, mais il fallait aussi retirer 4 routes de passerelle et le limiteur `otp` |

### Ce qui est écrit, et ce qui ne l'est pas

Écrit : le contrat d'événement, la publication depuis identity (chiffrée, dans l'outbox,
**avant** `SaveChangesAsync`), le consommateur qui déchiffre au dernier moment et aiguille,
les deux gabarits, le port `ISmsSender`, l'adaptateur de développement, et l'émission des
jetons à la vérification.

**PAS d'adaptateur SMS de production, et c'est délibéré.** Choisir un agrégateur n'est
pas une décision technique : c'est un contrat commercial, un compte opérateur, une
facturation au message et une identité d'expéditeur à faire homologuer auprès des
opérateurs béninois. Écrire un adaptateur pour un fournisseur arbitraire aurait produit du
code plausible, jamais exécuté, impossible à vérifier — exactement ce que ce chantier
passe son temps à retirer.

Deux gardes de démarrage tiennent la place, et **refusent de démarrer** plutôt que de
laisser croire que les SMS partent :

1. `Notifications:Sms` renseigné sans adaptateur enregistré — quelqu'un a payé un compte
   et croit que ça marche ; retomber sur l'adaptateur de développement écrirait les codes
   dans la console d'un serveur.
2. Production sans `Notifications:Sms` — le canal par défaut n'atteindrait personne, et
   l'échec serait totalement silencieux.

### Ce lot a ouvert une faille de step-up, et l'a refermée

`StepUpAuthentication.HasRecentAuthentication` ne lisait QUE `auth_time`. Son propre
encadré annonçait depuis le premier jour : « ce compte a-t-il saisi son **MOT DE PASSE**
il y a moins de cinq minutes ». Il ne le vérifiait pas.

L'écart n'a jamais rien coûté **parce que tout chemin d'émission de jetons passait par un
mot de passe**. `verify-otp` est le premier qui n'en exige aucun. Sans correction, qui
recevait un SMS obtenait un jeton « fraîchement authentifié » et franchissait les six
gardes sensibles du dépôt — virement, compte bancaire, transfert de propriété vendeur,
mouvements de stock. **Une carte SIM aurait suffi à vider un portefeuille.**

Le prédicat exige désormais `pwd` dans l'`amr`, et refuse un `amr` absent (même
raisonnement fail-closed que pour un `auth_time` absent). Aucun chemin existant ne change :
tous portent `pwd`. Seule la session OTP est exclue des gestes sensibles, et son porteur
passe par `POST /auth/reauthenticate`, qui rejoue le mot de passe.

**La fabrique de test portait la même hypothèse muette** : `StepUpTests.Jeton(...)`
produisait un jeton sans `amr`, et les tests de fenêtre passaient quand même puisque le
prédicat n'en lisait aucune. Le défaut est passé à `pwd` ; l'absence se demande
explicitement, et a son propre test.

### Ce que la décision laisse ouvert

* **Le fournisseur SMS.** C'est le seul geste qui reste, et il n'est pas de code.
* **C'est une connexion SANS MOT DE PASSE.** Qui reçoit le code entre ; la sécurité du
  compte devient celle du canal.
* **Un mauvais code ne verrouille pas le compte** — seulement le défi. Cinquante défis
  successifs coûtent cinquante SMS et ne verrouillent rien. La seule limite est le
  limiteur `otp` de la passerelle, donc par IP.
* **Aucun repli si le canal échoue.** L'événement porte pourtant adresse ET numéro, pour
  que ce repli puisse se décider le jour où il sera voulu.

---

## D38 — La passerelle EST le BFF ; les trois squelettes `apps/*-bff` sont retirés

**Tranché par HECTOR le 22/08/2026.**

### Ce qui était en cause

`apps/client-bff`, `apps/seller-bff` et `apps/driver-bff` étaient **construits et
démarrés** par `docker-compose.dev.yml`, et joignables par personne :

* absents de `HBA.sln` — aucune intégration continue ne les compilait ;
* aucune route de passerelle ne visait leur préfixe `/api/v1/client` ;
* `client-bff` portait 13 routes dont **9 rendaient 501**, et son `/home` était en dur ;
* `seller-bff` et `driver-bff` ne contenaient qu'une sonde de santé.

Pendant ce temps la passerelle portait son **propre** BFF, complet et couvert par six
suites de tests : `HBA.Gateway.Application/Bff/` — client express, client food, marchand,
livreur, restaurant.

La question n'était donc pas « finir les BFF » mais **lequel des deux EST le BFF**.

### Ce qui a été fait

Les trois dossiers sont dans `_to_delete/bff-D38/`, retirés de `docker-compose.dev.yml`,
`scripts/dev-up.sh`, `scripts/dev-doctor.sh` et du `README`. Aucun manifeste k8s ni aucune
CI ne les mentionnait — ils n'étaient nulle part ailleurs, ce qui est exactement le
problème qu'ils posaient.

`BFF_SERVICES` **reste déclaré, vide**, dans `dev-up.sh` : les profils `bff` et
`bff-only` démarrent donc la passerelle seule, ce qu'il faut pour travailler le BFF.
Supprimer le tableau ferait échouer ces deux profils sur une variable inconnue.

**Trouvé au passage** : `dev-up.sh` construisait encore les quatre squelettes food
retirés au lot 6.4, et `dev-doctor.sh` les diagnostiquait encore. Un diagnostic qui
interroge des services absents rapporte quatre indisponibilités permanentes — dans
lesquelles une cinquième, réelle, se perd.

### Ce que la décision laisse ouvert

* **`Bff:Screens` est toujours vide.** Les deux sections `client.express.home` et
  `client.food.home` sont des tableaux `[]` : l'écran d'accueil agrège **zéro source**.
  Retirer les squelettes n'a pas créé l'agrégation, cela a supprimé l'illusion qu'elle
  existait ailleurs.
* **Deux générations de contrôleurs cohabitent** dans la passerelle :
  `Controllers/Bff/Client*` en `/api/v1/bff/client/…` et `Controllers/Client/*BffController`
  en `/api/bff/client/…`, ce dernier déjà `[Obsolete]`. Les routes sont distinctes, donc
  rien ne casse — mais deux implémentations d'un même écran finissent par diverger. À
  retirer en vague 9, une fois les clients migrés sur `/api/v1/`.
* **Les dossiers sont dans `_to_delete/`, pas supprimés** — `device_bash` ne peut pas
  effacer sur cette machine.

---

## D39 — L'argent a TROIS représentations, pas deux ; la troisième est la seule qui mordait

**Lot 8.9. L'audit demandait « documenter, pas corriger à l'aveugle ». Le relevé a
trouvé une représentation de plus que celles qu'il nommait — et un défaut réel dans
celle-là.**

### Les trois représentations, et pourquoi chacune existe

| # | Représentation | Où | Pourquoi |
|---|---|---|---|
| 1 | `decimal` / `numeric(18,2)` (et `numeric(12,2)` pour les courses) | catalogue, panier, commandes, repas, paiements, portefeuille, facturation — **65 colonnes** | Type exact en base 10 : pas d'erreur de représentation sur 0,10. C'est la représentation par défaut du dépôt |
| 2 | `long` / `bigint` | `promotions` (`Value`, `Budget`, `BudgetConsumed`), `delivery_pricing` (`Subtotal`, `Discount`, `Total`) | **Le franc CFA n'a pas de sous-unité.** Un entier ferme la porte aux arrondis au lieu de les gérer. Choix argumenté dans `PromotionConfigurations` |
| 3 | **`string`** | **~50 champs monétaires des contrats gRPC** | protobuf n'a **pas** de type décimal. `double` serait pire — un binaire flottant ne représente pas 0,10 exactement. Le texte en `InvariantCulture` est le bon choix |

**La troisième n'était nommée nulle part.** L'audit comptait deux représentations ; il
en manquait celle qui traverse chaque frontière de service.

### Ce que chaque frontière fait, et ce qu'elle vaut

| Frontière | Conversion | Verdict |
|---|---|---|
| panier → promotions | `EnUnitesEntieres(decimal) → long`, arrondi **au plus proche** | **Correct, et déjà argumenté** : tronquer 4 999,99 ferait échouer une condition « panier ≥ 5 000 » pour un centime qui n'existe pas dans la devise |
| promotions → panier | `(decimal)long`, puis répartition par ligne au `Math.Floor` | **Correct** : l'arrondi vers le bas garantit que la somme des remises de ligne ne dépasse jamais la remise accordée |
| delivery_pricing → delivery | `int64 total` → `decimal? Total`, conversion implicite | **Correct** : 1 500 devient 1 500,00. **Aucun × 100 ni ÷ 100 nulle part dans le dépôt** — vérifié |
| tout service → tout service | `decimal` ⇄ `string` | **C'est ici que ça mordait** |

**Un balayage exhaustif confirme qu'aucune conversion ne multiplie ou ne divise par
cent.** Le risque que l'audit pressentait n'était pas là où il le cherchait.

### Le défaut réel : sept lecteurs sur huit rendaient ZÉRO sans rien dire

Huit fonctions du dépôt lisaient un montant venu du fil. **Sept s'écrivaient
`TryParse(…) ? valeur : 0m`.** Une seule refusait — celle de `FinancialGrpcService`,
côté paiement.

Et zéro n'est pas une valeur neutre pour de l'argent :

> Un champ `string` de protobuf 3 vaut la **chaîne vide** quand l'émetteur ne le pose
> pas. Il n'existe pas de « non renseigné ». Un chemin de code qui oublie une
> affectation, ou un producteur plus ancien qui ne connaît pas un champ ajouté par la
> règle additive **D32** : dans les deux cas le lecteur recevait `""` et lisait
> **zéro franc**.

**Le cas le plus cher est démontrable, pas théorique.** `ReturnLifecycleCommands`
calcule `plafondCommande = CapturedAmount − AlreadyRefundedAmount`. Un zéro silencieux
sur `AlreadyRefundedAmount` **remonte le plafond de remboursement** — c'est exactement
le défaut qu'**ISSUE-014** a fermé, et le commentaire de ce fichier-là le dit encore :
*« `AlreadyRefundedAmount: 0m` EN DUR : le plafond ignorait purement et simplement ce
qui avait déjà été rendu »*. La représentation en texte pouvait le rouvrir **sans
qu'une seule ligne de code fautive n'apparaisse nulle part**.

### La décision

**`MontantSurLeFil` — un seul endroit pour écrire et lire l'argent sur le fil.**

* `Ecrire(decimal)` — `InvariantCulture` obligatoire. Un conteneur en locale française
  écrirait « 1234,50 » avec un `ToString()` nu ; le lecteur, en invariant, y verrait
  123 450 ou rien. Les deux services compilent, aucun test ne change, et l'écart
  n'apparaît qu'en production sur une image dont quelqu'un a réglé la locale.
* `Lire(valeur, champ)` — **refuse** (`InvalidArgument`) au lieu de rendre zéro.
* `LireOuAbsent(valeur, champ)` — vide ⇒ `null` ; **présent mais illisible ⇒ refus**.
  Absent et cassé ne sont pas la même chose, et les confondre est précisément ce qui
  rendait zéro.
* `NumberStyles.Number`, **jamais `Any`** : `Any` accepte les symboles monétaires et la
  notation exponentielle — « 1E3 » deviendrait mille francs, « $12 » douze. Deux des
  huit lecteurs utilisaient `Any`, sans que personne n'ait choisi ces tolérances.

**Le nom du champ est rempli par le COMPILATEUR**, via `[CallerArgumentExpression]` :
le message porte « order.AlreadyRefundedAmount », plus précis qu'un littéral recopié, et
qui suit les renommages tout seul. Aucun des ~60 sites d'appel n'a eu à changer.

**`InvalidArgument` et non `Internal`** : l'émetteur a envoyé quelque chose
d'inexploitable, ce n'est pas une panne, et le disjoncteur du lot 8.8 ne doit pas le
compter — sans quoi un producteur fautif ferait couper l'accès à un service sain.

### La règle, pour la suite

1. **Un montant en base est `numeric` — sauf dans `promotions` et `delivery_pricing`**,
   qui sont en entier par décision, et le restent.
2. **Un montant sur le fil est un `string`**, écrit et lu par `MontantSurLeFil`. Jamais
   un `double`. Un `int64` reste acceptable **à l'intérieur** des deux modules entiers.
3. **Jamais de multiplication ni de division par 100.** Le franc est l'unité. Le jour où
   une devise à sous-unité entrera, ce sera une décision à prendre, pas une conversion à
   improviser — voir ci-dessous.

### Ce que cette décision NE règle pas

* **Une devise à sous-unité (EUR, USD) casserait la représentation n° 2.** `long`
  suppose que l'unité de compte est indivisible. Aujourd'hui `Currency` est un champ
  libre de trois lettres et **rien ne vérifie qu'il vaut XOF** : une campagne
  promotionnelle en euros stockerait « 10 » pour dix euros, et une remise de 10,50 €
  deviendrait 10 ou 11 selon l'arrondi. Ce n'est pas hypothétique le jour d'une
  ouverture régionale.
* **Le nombre de décimales n'est contraint nulle part.** `numeric(18,2)` accepte 0,01
  franc, qui n'existe pas. Aucune contrainte `CHECK` ne l'interdit ; les montants
  fractionnaires viennent des calculs de remise au prorata, et sont assumés.
* **`double` reste utilisé pour 26 colonnes** — coordonnées, distances, scores, facteur
  routier. **Aucune n'est de l'argent**, vérifié colonne par colonne.
* **Aucun contrôle automatique n'empêche un neuvième lecteur d'écrire
  `? valeur : 0m`.** La règle est écrite ici et dans l'en-tête de `MontantSurLeFil` ;
  elle n'est pas outillée.

### Trouvé en faisant ce relevé, et sans rapport avec l'argent

**`DeliveryApi.LookupQuote` n'a aucun corps de serveur** — c'est le CRITICAL n° 1 de
l'audit, toujours ouvert. Deux appelants s'en servent : `PlaceOrderCommandHandler` et
`PlaceMealOrderCommand`. **Aucune commande de repas ne peut être passée** : le devis y
est obligatoire, et sa relecture rend `UNIMPLEMENTED`, non rattrapé.

Et c'est **pire que ce que l'audit décrit** : `delivery-service` possède sa PROPRE table
`delivery_quotes` (en `numeric(12,2)`), qui n'est écrite par aucune route et lue par
aucune — `GetQuote` n'a pas non plus de corps de serveur, et il n'existe aucune route
HTTP de devis dans ce service. Le seul magasin de devis vivant est celui de
**delivery-pricing-service**, en `long`, et c'est bien lui que `CreateDeliveryCommand`
consomme. **Implémenter `LookupQuote` contre la table de `delivery-service` ne
marcherait donc pas non plus** : elle est vide.

Le geste attendu passe par une décision, pas par une correction mécanique :

* **(A)** `delivery-service` implémente `LookupQuote` en **relayant** vers
  delivery-pricing (et convertit `long → decimal` au passage, règle 3 ci-dessus) ;
  order-service et food-order-service ne changent pas ;
* **(B)** order-service et food-order-service interrogent **directement**
  delivery-pricing ; `delivery-service` perd ses deux RPC de devis et sa table morte.

**(B) supprime un saut réseau et une table fantôme ; (A) garde une seule façade de
livraison pour les appelants.** À trancher.

---

## D40 — Une valeur d'énumération inatteignable n'est pas du bruit : c'est une fonction déclarée et non construite

**Lot 9.2. L'instruction du plan était « les atteindre ou les retirer ». Le relevé montre
qu'il ne faut, presque partout, faire ni l'un ni l'autre — et il montre surtout que le
chiffre de 83 ne mesure pas ce qu'il prétend mesurer.**

### Trois mesures, dont deux fausses, et pourquoi

| Mesure | Résultat | Ce qui cloche |
|---|---:|---|
| « valeur jamais affectée » | **256** | Compte `MerchantPermission` 57/57. Or une permission ne s'affecte jamais : elle se **lit** d'une charge utile. Idem pour les motifs de retour, les types d'attribut, les moyens de paiement |
| « valeur jamais produite (hors comparaison) » | **78** | Proche des 83 de l'audit — mais compte encore `DeliverySource.HbaFood`, posé par `FoodOrderBridgeHandlers` **sous forme de chaîne** : `Source: "HbaFood"`. Toute énumération qui traverse un contrat en texte y échappe |
| « jamais produite **et** jamais citée en littéral » | **~12** | Le vrai compte, et il porte sur les **états d'agrégat** |

**La première mesure était la mienne, et elle partageait l'hypothèse fausse de ce qu'elle
mesurait** — le défaut que cette campagne rencontre pour la septième fois. Une énumération
d'ENTRÉE et une machine à ÉTATS ne se vérifient pas de la même façon.

### La décision : conserver, annoter, ne pas retirer

Les valeurs réellement inatteignables ne sont pas des restes : ce sont, presque toutes, des
**fonctions écrites dans le vocabulaire et jamais branchées**.

| Valeur | Ce que son absence signifie | Geste |
|---|---|---|
| `CartStatus.Abandoned`, `FoodCartStatus.Abandoned` | Aucun balayeur. Et depuis `ux_carts_active_buyer` (8.2), le panier actif d'un acheteur est le sien **pour toujours** | annotée |
| `PromotionStatus.Expired` | Rien ne compare `ValidUntil` à l'heure. Une campagne échue reste `Active` et **continue d'être accordée** | annotée |
| `OrderAcceptanceMode.Automatic` | Aucune route ne permet l'acceptation automatique — une exigence du cahier (§3, §14) | annotée |
| `RefundStatus.PartiallySucceeded` | Un remboursement à deux tentatives dont une a réussi se lit `Failed` | annotée |
| `StoreEnforcement.Enforced` | La moitié « appliquée » d'une affectation n'a jamais été branchée | annotée |
| `FulfillmentType.Fbp` + `FulfillmentLocationType.PlatformWarehouse` | Pas d'entrepôt plateforme. **Les deux se tiennent** : un pan non construit, pas un oubli isolé | annotée |
| `ProductFunctionalStatus.ForParts` | Aucune route n'accepte la valeur : un vendeur ne peut pas déclarer « vendu pour pièces » | annotée |
| `MemberStatus.Invited` | L'invitation est un agrégat séparé ; le membre naît `Active` | annotée — **et surtout pas retirée**, voir ci-dessous |

**Retirer effacerait le seul endroit où l'exigence est écrite.** Le lecteur suivant ne
conclurait pas « ce n'est pas fait » mais « ce n'était pas prévu ».

### Et pour trois d'entre elles, retirer casserait des données

`MemberStatus`, `StoreEnforcement` et `OrderAcceptanceMode` sont stockés en **entier**
(`HasConversion<int>`), alors que tout le reste du dépôt stocke ses statuts en TEXTE. Le
commentaire de `PromotionConfigurations` dit précisément pourquoi le texte est le bon choix :

> « réordonner l'énumération réécrirait silencieusement le sens de toutes les lignes déjà en
> base »

`MemberStatus.Invited` vaut **zéro**. La retirer ferait de `default(MemberStatus)` une valeur
qui ne nomme plus rien — un `0` en base que plus aucune ligne de code ne sait lire. Ici,
**retirer coûte, garder ne coûte rien.**

### Un doublon trouvé au passage : `ReturnResolution.Refund` et `RefundOnly`

Deux noms pour la même chose, dans une énumération dont **aucune des cinq valeurs** n'est
jamais posée. Le doublon ne se voit pas tant que personne n'assigne — le jour où quelqu'un
écrira la décision, il choisira l'un des deux, et les lectures filtreront sur l'autre. Un
écran qui compte les remboursements en oublierait la moitié. **À trancher avant le premier
usage, pas après.**

### Aucun contrôle automatique, et c'est un choix

Mes deux premières mesures ont sur-signalé, l'une d'un facteur vingt. Un contrôle bâti dessus
aurait crié au loup à chaque exécution — et la règle de ce dépôt est écrite dans
`check-implementations.py` : *« un contrôle qui crie au loup dix-neuf fois est pire que pas de
contrôle du tout : on cesse de le lire. »*

Distinguer une machine à états d'une énumération d'entrée demande de savoir si la valeur est
portée par une propriété d'agrégat, et de suivre les conversions en chaîne aux frontières —
c'est-à-dire de lire le modèle EF et les contrats. Tant que ce n'est pas fait, **l'annotation
dans le code vaut mieux qu'un contrôle approximatif** : elle est au bon endroit, elle est lue
par qui touche à la valeur, et elle ne produit aucun faux positif.

---

## D41 — L'identité d'appelant gRPC est SIGNÉE par hôte ; la clé partagée reste, mais elle n'identifie personne

**Contexte.** `Internal:ApiKey` est **une** chaîne, la même sur les vingt-quatre hôtes. Elle
était présentée comme suffisante « pour treize services maîtrisés ». Elle ne l'était pas :
elle atteste l'**appartenance au réseau**, pas l'**identité**. Un service compromis —
n'importe lequel — la lit dans son environnement et appelle dès lors n'importe quel RPC de
n'importe quel service en se présentant comme n'importe qui.

La surface concernée n'est pas théorique : `GetSellerPayout` rend le numéro Mobile Money d'un
vendeur et s'énumère par identifiant ; `RefundPayment`, `ReleaseReservation` et
`CancelDelivery` écrivent.

**Ce qui a été écarté.**

*Une clé par service.* C'était la correction évidente, et elle échoue sur un point : avec un
secret **symétrique**, celui qui vérifie doit connaître le secret de celui qui signe.
financial-service, pour vérifier order-service, détiendrait la clé d'order-service — donc
compromettre financial-service rendrait toutes les clés qu'il vérifie. Le problème serait
déplacé, pas fermé.

*mTLS.* C'est la bonne réponse, et elle reste à faire. Elle demande une autorité de
certification, une distribution et une rotation — c'est-à-dire de l'infrastructure, pas du
code. Rien de ce qui suit ne s'y oppose ; tout s'y superpose.

**Décision.** Signature **asymétrique** P-256, une paire par hôte. Chaque hôte détient sa clé
privée et seulement elle ; le registre des clés **publiques** est identique partout et n'a
aucune valeur pour un attaquant. Compromettre un service donne le pouvoir d'usurper ce
service-là, et rien d'autre.

L'attestation est liée à la **méthode appelée** et expire en **trente secondes**. La clé
partagée demeure, en première barrière : elle est la moins chère à vérifier, et elle écarte un
balayage de port avant toute cryptographie.

**Et une table d'autorisations, parce que l'identité seule ne suffit pas.** Savoir qui appelle
ne sert à rien si tout le monde a le droit de tout appeler. `AutorisationsGrpc` restreint
chaque hôte aux RPC qu'il appelle réellement. Elle est **engendrée depuis le code** et
`scripts/check-autorisations-grpc.py` échoue à la moindre divergence — dans les deux sens.

| RPC | appelants possibles avant | après |
|---|---|---|
| `FinancialApi/RefundPayment` | 24 | **1** |
| `PromotionApi/ReleaseCoupon` | 24 | **2** |
| `InventoryApi/ReleaseReservation` | 24 | **5** |
| `MerchantApi/GetSellerPayout` | 24 | **10** |

**CE QUE CETTE DÉCISION NE COUVRE PAS.**

- **Le réseau reste en clair.** Il n'y a pas de TLS entre services — c'est la raison même des
  deux ports de `GrpcHostExtensions`. Un attaquant **en coupure** lit les charges utiles et
  peut **rejouer** une attestation captée pendant sa fenêtre de validité. Le modèle de menace
  fermé ici est « un service compromis », pas « un observateur du réseau ».
- **Pas de cache anti-rejeu.** Il exigerait un état partagé entre répliques, donc Redis sur le
  chemin critique de chaque appel interne. La liaison à la méthode et les trente secondes sont
  les seules protections.
- **La granularité est celle du paquet de contrats, pas de l'appelant métier.** Une enveloppe
  `*.Contracts.Grpc` est **une** classe qui appelle tous les RPC de son service : référencer le
  paquet donne droit à tous. C'est pourquoi `GetSellerPayout` descend à dix et non à un.
  Le resserrer demande de découper les enveloppes par interface — un lot en soi.
- **En développement, les identités ne sont pas signées.** `Internal:IdentitesNonSignees`
  laisse passer un nom nu ; `AddHbaGrpc` **refuse de démarrer** si le drapeau est posé hors
  `Development`. La table d'autorisations, elle, s'applique quand même — donc une autorisation
  manquante se voit en montant la pile de développement.

**Deux pannes trouvées en câblant ceci**, toutes deux invisibles à la compilation :

- **order-service n'avait aucune `Internal__ApiKey`** dans `infra/docker/compose.services.yml`.
  Il ne pouvait ni servir ses RPC, ni en appeler un seul — c'est-à-dire que tout le parcours de
  commande était mort sur cette pile. Les douze autres services l'avaient.
- **La passerelle non plus**, dans `compose.gateway.yml` : le contrôle de révocation de jeton
  (D27) échouait à la première requête authentifiée. Et il lui manquait aussi
  l'enregistrement de `DisjoncteurClientInterceptor`, oublié au lot 8.8 parce que la passerelle
  n'appelle pas `AddHbaGrpc`.

## D42 — dispatch-service est retiré : l'affectation vit là où sont les données

**Date** : 27 août 2026 · **Statut** : tranchée · **Corrige** : `audit/2026-08-27-defauts-et-deploiement/AUDIT.md` §1.3

Le dépôt contenait **deux affectations de livreur**. La décision retire la fausse.

| | delivery-service | dispatch-service |
|---|---|---|
| base de données | `DeliveriesDbContext`, 17 migrations | aucune |
| état | table `deliveries.drivers` — position, disponibilité | `ConcurrentDictionary` en mémoire de processus |
| recherche de proximité | `IDriverLocationCache.FindNearbyAsync`, adossé à Redis | **deux GUID codés en dur** |
| ce qui l'alimente | `POST /api/deliveries/mine/online\|offline\|position` | rien |
| affectation | `DispatchDeliveryCommand`, réelle | `BuildCandidates`, fictive |
| manifeste Kubernetes | aucun (domaine delivery non déployé) | aucun |
| appelants dans le dépôt | — | **zéro** |

**LE CONSTAT D'AUDIT VISAIT D'ABORD LE MAUVAIS SERVICE.** Il disait qu'il manquait
« un appel de dispatch vers driver-service ». Vérification faite, driver-service ne
porte **délibérément** ni disponibilité ni position — son agrégat le documente, et
son RPC `SetBusyState` renvoie `Unimplemented` en désignant delivery-service. La
capacité manquante n'était pas absente : elle était à côté, et privée.

### Pourquoi retirer plutôt que brancher

Brancher aurait demandé d'exposer `FindNearbyAsync` en RPC, de donner une base à
dispatch-service, un manifeste, une identité gRPC, une clé dans le Secret — pour
qu'il refasse ce que son voisin fait déjà avec les données en main.

**CE QUE LE RETRAIT COÛTE, ET IL FAUT L'ÉCRIRE.** dispatch-service exposait trois
capacités d'exploitation sans équivalent côté delivery-service :

- `POST /{deliveryId}/manual-assign` — affectation manuelle par un exploitant
- `POST /{deliveryId}/retry` — relance explicite
- `GET /{deliveryId}/assignment` et `/jobs/{deliveryId}` — consultation

Elles reposaient sur un dictionnaire perdu à chaque redéploiement, dans un service
sans base ni manifeste : **elles ne fonctionnaient pas**. Les retirer ne retire rien
qui marche. Mais elles restent à écrire dans delivery-service, où elles auront la
base et le cache de proximité. C'est une dette, pas une suppression de périmètre.

### Ce que le retrait a touché

Neuf points, tous vérifiés après coup :

- 4 projets `HBA.Delivery.Dispatch.*` et 2 paquets `shared/contracts/HBA.Dispatch.Contracts*`
  — ces derniers n'étaient **dans aucune solution**, donc jamais compilés
- `HBA.sln` : 232 → 228 projets, aucun GUID orphelin en configuration
- `docker-compose.dev.yml` : 32 → 31 services, plus la route `SERVICES__DISPATCH`
- `AutorisationsGrpc` : 24 → 23 appelants
- `HbaTopics` et les 3 overlays Kafka : 23 → 22 topics
- `generer-identites-internes.sh` : 24 → 23 hôtes
- `dev-up.sh`, `dev-doctor.sh`

**UN COMMENTAIRE FAUX TROUVÉ AU PASSAGE.** L'en-tête d'`AutorisationsGrpc`
annonçait « six hôtes de livraison n'ont plus AUCUN droit d'appel sortant ». Ils
étaient **cinq** avant ce retrait — le compte n'avait jamais été recalculé après
que Delivery.Core et Delivery.Pricing ont reçu des droits. Ils sont quatre
aujourd'hui. Un commentaire qui annonce un chiffre de sécurité plus favorable que
la réalité est pire qu'un commentaire absent : on ne revérifie pas ce qu'on croit
déjà compté.

## D43 — tracking-service et proof-of-delivery-service sont retirés : la preuve et le suivi vivent dans delivery-service

**28 août 2026.** Deux services de plus quittent le dépôt, pour la même raison que
`dispatch-service` en D42 : ils dupliquaient en mémoire une capacité que
`delivery-service` persiste déjà, et personne ne les appelait.

### Ce qui a été vérifié avant de les retirer

Le choix n'a pas été fait sur la lecture d'un README. Cinq constats, chacun
vérifiable dans le dépôt à la date de cette décision :

1. **Aucune entrée dans la passerelle.** `ServicesOptions` déclare dix-neuf
   adresses de service ; ni `Tracking`, ni `ProofOfDelivery` n'y figurent, et
   `ServiceKeys` ne les nomme pas non plus. La passerelle étant la seule entrée
   depuis l'extérieur, **ces deux services n'étaient joignables par aucun client**.
   Les variables `SERVICES__TRACKING` et `SERVICES__PROOFOFDELIVERY` posées sur
   la passerelle dans `docker-compose.dev.yml` ne se liaient donc à rien : elles
   avaient l'air d'un câblage et n'en étaient pas un.

2. **Aucun appelant gRPC.** Aucun `AddTrackingGrpcClient` ni `AddProofGrpcClient`
   n'était enregistré nulle part, et la table d'autorisations les listait tous
   deux avec un ensemble d'appels sortants VIDE.

3. **Aucune référence de projet, aucun `using`.** En dehors de leurs propres
   arborescences, zéro `ProjectReference` vers `HBA.Tracking.Contracts*` ou
   `HBA.ProofOfDelivery.Contracts*`, et zéro fichier `.cs` important ces espaces
   de noms. Ces quatre projets de contrats n'étaient d'ailleurs même pas dans
   `HBA.sln` : ils ne se compilaient plus.

4. **Aucun consommateur de leurs événements.** `check-event-consumers.py` compte
   0 perte après retrait. Les douze ruptures signalées par
   `check-event-contracts.py` sont exactement les événements des trois services
   sortis (dispatch, tracking, proof) — aucun n'avait de gestionnaire.

5. **Et surtout : ni base, ni migration.** `TrackingStore` et `ProofStore`
   tiennent tout dans des `ConcurrentDictionary` de processus. L'état disparaît
   au redémarrage et n'est pas partagé entre deux réplicas. L'en-tête de
   `ProofStore` le disait lui-même, et posait la question qu'on tranche ici :
   « la question n'est pas comment le persister mais lequel des deux garde-t-on ».

### Ce que delivery-service fait déjà, et qui est persisté

**La preuve.** `POST /api/v1/driver/deliveries/{id}/delivered` porte un corps
`ProofRequest`. `Delivery.MarkDelivered` appelle `ProofOfDelivery.Capture`, qui
compare le PIN **à temps constant** contre `IssuedPin` — que le livreur ne voit
jamais — et refuse pour photo et signature tout ce qui n'a pas la forme d'une
référence de stockage. `FailedProofAttempts` verrouille après cinq mauvaises
réponses, sans compter les absences. `ProofPolicy` choisit le genre exigé à la
création. Le tout est dans `DeliveriesDbContext`, avec ses migrations.

**Le suivi.** `POST /api/v1/driver/position` reçoit les positions,
`GET /api/v1/deliveries/{id}/tracking` les expose, et le RPC gRPC
`DeliveryApi/GetTracking` est autorisé pour six hôtes dans la table d'autorisations.

### Ce que ce retrait NE COUVRE PAS

- **La photo n'a pas d'écran de dépôt dédié.** `proof-of-delivery-service`
  offrait un `POST /{id}/media/presign` que `delivery-service` n'a pas. Ce n'est
  pas une perte : ce presign était lui aussi en mémoire, sans stockage derrière.
  Le chemin réel d'envoi existe et vit ailleurs — `media-service` expose
  `POST /api/v1/media` puis `GET /{id}/download-url`. **Ce qui manque est le
  raccordement** : rien, aujourd'hui, ne dit à l'application livreur d'envoyer
  d'abord la photo à media-service et de passer l'identifiant obtenu comme
  `ProofValue`. C'est une dette, elle est ici, elle n'est pas résolue.

- **`ProofPolicy` ne produit jamais `Signature`.** Le genre existe, l'agrégat
  sait le vérifier, aucune règle ne le choisit — délibérément, faute d'écran de
  signature.

- **`route-service` reste**, bien qu'il ait exactement le même profil : zéro
  appelant, aucune entrée dans `ServicesOptions`. Il n'est pas retiré parce que
  le calcul d'itinéraire a un remplaçant qui tourne (`FALLBACK_HAVERSINE`) et
  une décision propre à prendre, qui n'est pas celle-ci.

### Ce qui a été touché

`HBA.sln` (228 → 220 projets, 104 lignes de configuration, aucun GUID orphelin) ·
`docker-compose.dev.yml` (31 → 29 services, deux variables mortes de la
passerelle) · `AutorisationsGrpc.cs` (23 → 21 appelants) · `HbaTopics.cs` ·
les trois `k8s/overlays/*/kafka-topics.yaml` (22 → 20 sujets chacun) ·
`generer-identites-internes.sh` · `dev-up.sh` · `dev-doctor.sh` ·
`k8s/base/services/kustomization.yaml`. Les six projets sont dans
`_to_delete/2026-08-28-tracking-et-proof/`.

## D44 — le routage reste dégradé, mais il le dit, et le levier est posé

**28 août 2026.** Le calcul d'itinéraire de la plateforme est une ligne droite.
Il le restera tant qu'aucun moteur de routage n'est branché. Ce qui change ici :
il ne le cache plus, et la correction se règle sans toucher au code.

### L'audit désignait le mauvais service

Le §1.2 de l'audit du 27 août plaçait le défaut dans `route-service`
(`RouteStore.cs`, `source = "FALLBACK_HAVERSINE"`). C'est exact, et sans
conséquence : `route-service` n'a **aucun appelant** et **aucune entrée dans
`ServicesOptions`** — exactement le profil des trois services retirés en D42 et
D43. Corriger là n'aurait rien changé pour personne.

Le Haversine qui compte est dans **`delivery-pricing-service`**, appelé en gRPC
par delivery-service :

```csharp
var distance = request.DistanceMeters ?? ServiceabilityPolicy.HaversineMeters(...);
var duration = request.DurationSeconds ?? Math.Max(60, (int)(distance / 5.8));
var breakdown = PricingPolicy.BuildBreakdown(rule, distance, duration, ...);
//   distanceFee = distance/1000 × PerKmFee
//   minuteFee   = duration/60   × PerMinuteFee
```

**Ce n'est donc pas une estimation d'affichage : c'est le prix facturé.** Et la
même ligne droite décide de la desserte, comparée au plafond de 25 km de
`ServiceabilityPolicy`. La constante `5.8` — 20,9 km/h — était le seul modèle de
circulation de toute la plateforme, dupliqué à l'identique dans `route-service`
sans que rien ne relie les deux.

### Ce qui a été fait

1. **Les deux constantes sont sorties du code** dans `EstimationItineraireOptions` :
   `VitesseMoyenneMetresParSeconde` (5,8) et `FacteurCorrectionUrbaine` (1,0),
   plus `DureeMinimaleSecondes` (60). Validées au démarrage : une vitesse nulle
   ou un facteur inférieur à 1,0 empêche le service de démarrer, au lieu de
   produire des devis faux en silence. La lecture est faite à la main en
   `InvariantCulture` plutôt qu'avec `Get<T>()` : d'une part les paquets du
   binder ne sont pas déclarés par ce projet, d'autre part « 1.3 » lu sous une
   locale française vaut **13** — un facteur multiplié par dix ne lève aucune
   exception, il multiplie par dix le prix de toutes les courses. Une valeur
   présente mais illisible est une erreur de démarrage, jamais un repli
   silencieux sur le défaut.

2. **Un seul chemin vers la distance.** `ServiceabilityPolicy.DistanceRoutiereEstimeeMetres`
   applique le facteur ; `CreateQuoteAsync` et `GetServiceabilityAsync` passent
   tous deux par elle. Auparavant chacun appelait `HaversineMeters` directement :
   deux chemins que rien n'obligeait à rester d'accord, donc une plateforme qui
   aurait pu refuser une course puis la facturer.

3. **La provenance est persistée et rendue.** `DeliveryQuote.SourceEstimation`
   (`CLIENT_PROVIDED` / `FALLBACK_HAVERSINE`) et `FacteurCorrectionApplique`,
   migration `20260828120000_SourceEstimationDevis` écrite à la main sur le
   modèle de `JournalDAuditDeliveryPricing`. Le facteur est **persisté et non
   relu de la configuration** : un devis chiffré à 1,0 doit rester explicable
   après un passage à 1,3.

4. **La durée est déclarée comme un plancher**, dans le proto et dans
   `DeliveryQuoteDetails` : « à partir de N min », jamais « N min ».

### Le facteur vaut 1,0, et c'est le cœur de la décision

Un facteur de détour urbain vaut typiquement 1,2 à 1,4 en tissu dense. Le poser à
1,3 « parce que c'est l'usage » majorerait le prix de **toutes** les courses
d'environ trente pour cent, sur la foi d'un chiffre que **personne n'a mesuré à
Cotonou**. On pose donc le levier sans le tirer : le prix produit aujourd'hui est
**exactement** celui d'avant ce commit. Le jour où l'écart réel est mesuré, il se
règle par configuration.

### CE QUE CETTE DÉCISION NE COUVRE PAS

- **La plateforme sous-facture, et ce commit ne le corrige pas.** Le trajet réel
  est toujours plus long que la ligne droite ; l'écart croît avec la distance.
  Le défaut devient réglable et visible, il ne disparaît pas.

- **Et elle accepte des courses hors zone.** Une course de 30 km par la route
  mais 24 km à vol d'oiseau passe sous le plafond de 25 km. Le même facteur
  corrige les deux, quand il sera réglé.

- **Un facteur unique pour tout le pays.** Le détour n'est pas le même dans le
  centre de Cotonou et sur la route de Porto-Novo. `DeliveryZone` sait déjà
  situer un point : c'est le prolongement naturel, il n'est pas fait.

- **Aucune reprise des devis existants.** Ils reçoivent `''` et `0`. On pourrait
  les marquer `FALLBACK_HAVERSINE` — c'était le seul chemin avant ce commit —
  mais ce serait une déduction, pas une donnée : rien en base ne dit si
  l'appelant avait fourni sa propre distance.

- **`route-service` n'est pas touché.** Il garde sa constante `5.8` en dur et son
  état en mémoire. Il n'a toujours aucun appelant. Sa disposition — le retirer
  comme les trois autres, ou en faire le futur porteur d'un vrai moteur — reste
  ouverte.

- **La migration a été écrite à la main, sans `dotnet ef`.** Le snapshot a été
  mis à jour dans le même commit et `check-migrations.py` rejoue les 24 contextes
  sans incohérence, mais **aucun `dotnet ef migrations list` n'a été exécuté**.
  À relire avant de l'appliquer en production.

## D45 — un environnement inconnu est la production, pas le développement

**28 août 2026.** Six installeurs portaient chacun une copie de la même méthode
`IsProduction`, toutes **fail-open**. Elles délèguent désormais à
`EnvironnementDeploiement.EstProduction`, en un seul exemplaire.

### Ce qui était cassé

```csharp
var env = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"] ?? "";
return string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
```

Tout ce qui n'était pas littéralement « Production » était traité comme du
développement — **y compris la chaîne vide, y compris une variable absente**. Or
ASP.NET Core, lui, considère une variable absente comme la production : le socle
et le framework se contredisaient sur le cas exact où ça compte le plus.

Ce que ces six copies gardaient : la **clé de chiffrement** des codes de
réinitialisation et de vérification (sans clé configurée et hors production,
`AesGcmSecretProtector` retombe sur une clé dérivée d'une phrase fixe et publique,
présente dans ce dépôt — les codes traversent alors l'outbox et Kafka
« chiffrés » avec une clé que quiconque lit le code peut recalculer), le refus de
démarrer avec des **adaptateurs gRPC simulés**, et le refus de démarrer avec des
**fournisseurs de paiement simulés**. Un `ASPNETCORE_ENVIRONMENT` oublié sur un
vrai serveur donnait un démarrage normal, sans erreur, avec des effets métier
fictifs et une cryptographie décorative.

### La règle retenue

1. **Variable absente ou vide → production.** Défaut d'ASP.NET Core, et seul
   défaut sûr.
2. **Nom explicitement listé hors production → pas la production.** La liste est
   courte, en dur : Development, Local, Test, Testing, CI, Staging.
3. **Tout autre nom → production**, avec un avertissement qui nomme les valeurs
   acceptées.

Le point 3 change un comportement, et c'est le cœur. L'ancien commentaire
justifiait le repli permissif par « un nom mal orthographié empêcherait de
travailler ». C'est vrai, et incomparable : une faute de frappe produit désormais
un refus de démarrer avec la liste des noms valides — une minute de correction,
immédiatement visible. La même faute sur un serveur de production produisait une
clé publique et des remboursements fictifs, invisibles jusqu'à ce que quelqu'un
compare des données.

### CE QUE CETTE DÉCISION NE COUVRE PAS

- **« Staging » reste hors production**, sciemment : une préproduction ne doit pas
  encaisser de vrai argent. Conséquence : un staging sans `Secrets:Key` utilise la
  clé de développement publique. Le remède est de poser la clé.
- **Aucune porte de sortie configurable.** On ne peut pas étendre la liste par
  variable d'environnement, et c'est délibéré : c'est exactement le mécanisme par
  lequel ce genre de garde finit désactivé « le temps d'un test ».
- **Les trois adaptateurs gRPC de return-refund restent simulés.** Le garde mord
  mieux ; il ne remplit pas ce qu'il garde. `return-refund-service` ne peut donc
  toujours pas démarrer en production, par construction.


## D46 — la durée de vie des clés d'idempotence est enfin lue

**28 août 2026.** `IdempotencyRecord.ExpiresAtUtc` existait depuis l'origine :
déclarée dans l'entité, initialisée à 24 h, `IsRequired()` dans la configuration,
et portant un **index dédié** — `ix_idempotency_keys_expires_at`, commenté
« index de purge » — dans la migration de chacun des sept services concernés.
**Aucune ligne de code ne la lisait.**

C'est ce qui rendait le défaut invisible : tout avait l'apparence d'un mécanisme
réglé. Un index dédié dit à qui relit « quelqu'un interroge cette colonne ».

### Ce que ça coûtait

Une réservation n'est complétée que si le gestionnaire rend la main, normalement
ou par une exception attrapée. Si le processus meurt entre la réservation et la
complétion — OOM, `kill`, éviction de pod, redéploiement — la ligne reste
inachevée **pour toujours**, et toute nouvelle tentative avec la même clé reçoit
409. En plein paiement, c'est une commande que le client ne peut ni finir ni
recommencer, sans aucun recours automatique.

### Ce qui a été fait, sans aucune migration

Le schéma était déjà là, dans les sept services.

1. `TryBeginAsync` reprend une réservation inachevée dont l'échéance est passée :
   la ligne est supprimée, et la contrainte d'unicité arbitre à nouveau — comme au
   premier passage. La reprise est bornée à **une seule** par un drapeau explicite,
   et non par un appel récursif : la récursion marcherait presque toujours et
   n'aurait aucune borne prouvable, sous verrou de base, dans une requête HTTP.
2. `IdempotencyPurger` efface les lignes périmées par tranches, sur un **curseur
   d'échéance**. On ne pouvait pas copier la mécanique par identifiants
   d'`OutboxPurger` : `IdempotencyRecord` n'a pas de clé simple mais le triplet
   (Key, Scope, Endpoint), et un `Contains` sur des n-uplets ne se traduit pas de
   façon fiable.
3. `AddIdempotence<TDbContext>()` pose les deux **ensemble**. Les enregistrer en
   deux lignes dans les sept installeurs aurait reconduit le mécanisme qui a
   produit le défaut : il aurait suffi qu'un huitième service ne copie que la
   première ligne pour n'avoir jamais de purge, sans rien casser ni rien signaler.

### CE QUE CETTE DÉCISION NE COUVRE PAS

- **Un effet métier déjà produit sera reproduit.** Si la première exécution avait
  payé, envoyé ou expédié avant de mourir, le rejeu après 24 h recommence.
  L'idempotence de la couche HTTP ne remplace pas celle du domaine ; les
  opérations qui déplacent de l'argent ont la leur (`Refund.IdempotencyKey`,
  l'inbox des consommateurs). Vingt-quatre heures est le compromis : assez long
  pour toute reprise réseau normale, assez court pour qu'un client bloqué ne le
  reste pas plus d'une journée.
- **Rien ne compte les réservations mortes.** Le purgeur les efface sans trace.
  Savoir combien de processus meurent en pleine réservation demanderait une
  métrique dans `TryBeginAsync`, pas une ligne conservée en base.


## D47 — la route de rejeu des lettres mortes n'existe pas, et on cesse de la promettre

**28 août 2026.** Quatre endroits du socle renvoyaient vers
`GET /admin/outbox/dead-letters`. **Aucune route de ce nom n'est montée nulle part
dans le dépôt.**

Le pire des quatre est le message `LogCritical` émis au moment exact où un
événement métier est définitivement perdu : « Corriger la cause, puis rejouer via
/admin/outbox/dead-letters ». Un exploitant réveillé par cette ligne partait
chercher une surface absente, à l'heure où il en avait le plus besoin.

Le portail d'administration, lui, avait raison depuis le début : il classe sa
section « Outbox » comme SANS AMONT, avec la bonne raison — la table est interne
au service, et l'exposer donnerait accès aux charges utiles des événements, dont
certaines portent un secret.

**On corrige donc les messages, pas le manque.** Les quatre mentions décrivent
maintenant le geste réel : remettre `DeadLetteredOnUtc` à NULL, `AttemptCount` à 0
et `NextAttemptAtUtc` à NULL sur la ligne, une fois la cause corrigée. L'index
partiel sur `DeadLetteredOnUtc IS NOT NULL` existe précisément pour retrouver ces
lignes.

**CE QUE ÇA NE COUVRE PAS.** Il n'y a toujours aucune surface de rejeu, et le
geste reste manuel, en base, sur le bon service. Construire cette surface suppose
de trancher ce qu'on accepte d'exposer d'une charge utile qui peut contenir un
secret — c'est une décision, pas un oubli. Le jour où elle est prise, c'est dans
ces quatre messages qu'il faudra la nommer.

## D48 — un contrôle d'appartenance ne compare pas une valeur fournie par celui qu'il contrôle

**28 août 2026.** Le média rattaché à une fiche produit était vérifié par
`media.OwnerType == "Product" && media.OwnerId == productId`. Ces deux valeurs sont
**déclarées par l'appelant au téléversement**, et media-service ne les vérifie pas —
il ignore ce qu'est un produit (§20), et son propre commentaire le dit.

`MediaAsset.CreatedByUserId` existait depuis la migration initiale, `IsRequired()`,
et ne sortait par aucun contrat. Il est désormais rendu par `MediaView` : il vient
du **jeton**, c'est le seul champ de cette vue que l'appelant ne choisit pas.
`AddProductMediaCommandHandler` exige que le déposant du média soit celui qui le
rattache, et le paramètre absent vaut REFUS.

**La route de téléversement n'a PAS été fermée aux administrateurs**, malgré ce que
son commentaire annonçait : l'application vendeur l'appelle directement pour cinq
types de propriétaire, et la fermer supprimerait le dépôt de photos de tout le
portail vendeur. Le commentaire promettait une garde qui aurait cassé le produit.

### CORRIGÉ DANS LA FOULÉE : « LE MÊME COMPTE » ÉTAIT TROP STRICT

La première version exigeait `CreatedByUserId == RequestedByUserId`. C'était faux
pour toute boutique à plusieurs membres : un `SellerMember` téléverse les photos,
un autre monte la fiche. La route accepte les deux — elle raisonne sur la
CAPACITÉ, pas sur la personne — et le gestionnaire les aurait départagés en
refusant le second. Un contrôle de sécurité qui casse un parcours légitime se fait
retirer, et emporte la propriété qu'il défendait.

La règle retenue : le déposant doit avoir LUI AUSSI le droit sur ce vendeur.
Catalog interroge `IMerchantAccessApi.HasCapabilityAsync` ; seller-service, qui
EST le service vendeur, résout la question en local par `MemberAccessResolver` —
aucun appel distant. Le cas courant — même personne des deux côtés — prend un
chemin rapide, sans aller-retour.

### LES DEUX POINTS DE RATTACHEMENT SONT TRAITÉS

`AddProductMediaCommandHandler` et `AddKybDocumentCommandHandler` sont les deux
seuls consommateurs d'`IMediaModuleApi` dans le dépôt — vérifié. Le second est le
plus sensible : c'est la pièce d'identité d'un commerçant.

Son encadré annonçait que ses deux contrôles « ferment les deux exploitations ».
C'est exact pour les deux décrites — aucune ne permet de RÉÉCRIRE l'appartenance
d'un média existant — mais aucun des deux ne ferme la CRÉATION d'un média neuf à
l'appartenance mensongère. L'encadré le dit désormais.

### CE QUE CETTE DÉCISION NE COUVRE PAS

- **N'importe quel compte peut toujours créer un média à l'appartenance
  mensongère.** Vérifié : aucun chemin de lecture ne l'expose aujourd'hui —
  `ListMediaByOwnerQuery` n'a aucune route, `IMediaModuleApi.ListByOwnerAsync`
  aucun appelant. Mais la méthode existe, et le premier écran « les pièces de ce
  vendeur » rendra ces fichiers visibles chez leur victime. **Les deux contrôles
  posés ici sont donc préventifs** : ils ferment un chemin LATENT, pour que le
  jour où cette route existera, elle n'ouvre pas la brèche en même temps qu'elle
  rend service.
- **Décider qui peut déclarer quel propriétaire est une décision de produit** :
  toute règle stricte casse un parcours vendeur existant.
- **Restaurant, boutique et plat ne vérifient rien** — mais ils n'appellent pas
  media-service du tout : ils stockent un identifiant sans le relire. Ce n'est
  pas le même défaut, et il n'est pas traité ici.


## D49 — renommer une catégorie déplace sa branche

**28 août 2026.** `Category.Update` recalculait le chemin de la catégorie et
d'aucun descendant. Renommer « Animaux » laissait `/animaux/chiens` sous un
`/animaux` qui n'existait plus : `ListDescendantsAsync` cherchant par PRÉFIXE, la
branche entière devenait introuvable — publication en cascade, dépublication et
filtres par catégorie la perdaient, sans erreur.

`Category.RebasePath` réécrit par **substitution de préfixe**, et le gestionnaire
l'applique à la branche. Deux choix à connaître : les descendants sont chargés
AVANT la mutation, parce que l'ancien chemin est la clé de recherche ; et la
substitution est préférée à une reconstruction par `BuildPath`, qui exigerait de
traiter la branche par profondeur en tenant une carte des chemins déjà réécrits.

Aucun contrôle d'unicité n'est refait sur les descendants : la structure relative
est conservée, donc si la nouvelle racine est libre — vérifié par l'appelant —
aucun descendant ne peut entrer en collision.


## D50 — la grille tarifaire est choisie sur ce qu'on demande, et un niveau sans grille refuse le devis

**28 août 2026.** La sélection ne filtrait que sur le statut et les dates, puis
prenait la priorité la plus haute. `ServiceLevel` et `VehicleType` étaient portés
par `PricingRule`, remplis par la console d'administration, transmis par la
demande — et n'entraient dans aucune sélection. EXPRESS en voiture et STANDARD en
moto recevaient le même prix.

`ServiceLevel` doit désormais correspondre exactement ; `VehicleType` est nullable
et le nul EST le joker — c'est déjà le sens de la colonne. La grille qui nomme le
véhicule passe devant la générique, `Priority` départage le reste.

**LE CHANGEMENT DE COMPORTEMENT EST ASSUMÉ.** Un niveau sans grille active ne rend
plus le prix d'un autre niveau : le devis ÉCHOUE, avec un message qui nomme le
niveau manquant. Un prix emprunté est facturé au client et ne se voit nulle part ;
un devis refusé se voit tout de suite.

### CE QUE CETTE DÉCISION NE COUVRE PAS

- **`Scope` n'entre pas dans la sélection**, contrairement à ce que l'audit
  affirmait : `CreateQuoteRequest` ne porte aucun champ de portée. L'y faire entrer
  supposerait d'abord de décider ce qu'une portée désigne.
- **Aucun joker sur le niveau de service.** En inventer un — « ANY », « * » —
  poserait une convention que la console d'administration ne sait pas produire.
- **Le jeu semé ne contient qu'une grille STANDARD / MOTORBIKE.** Aucun appelant
  n'envoie aujourd'hui de niveau sur une demande de devis, mais il faut le savoir
  avant d'en envoyer un.

## D51 — le nombre de pods au repos se règle sur le HPA, et une cible de patch qui ne désigne rien est une erreur

**28 août 2026.** Deux défauts de la couche de déploiement, dont le second n'avait
été vu par personne.

### Le HPA écrit `spec.replicas`, pas nous

Dès qu'un HorizontalPodAutoscaler cible un Deployment, c'est lui qui écrit
`spec.replicas` à chaque réconciliation. La valeur posée par l'overlay n'est que le
point de départ du premier lancement. L'overlay prod passait dix services à
`replicas: 2` sans toucher aux HPA, restés à `minReplicas: 1` : quelques minutes
après le déploiement, tous retombaient à un pod. La redondance était écrite, relue
en revue, et jamais obtenue.

Chaque patch de `replicas` est désormais **collé** à un patch `minReplicas` sur le
HPA du même service. Les séparer laisserait un jour ajouter un service à l'un des
deux endroits seulement.

### Cinq de ces dix patches ne désignaient rien

`commerce-service`, `financial-service` et `merchant-service` : la base produit
`cart-service`, `payment-service` et `seller-service`. `delivery-service` et
`food-service` : pas de manifeste du tout.

**Kustomize n'échoue pas sur une cible sans correspondance.** Le build réussit, le
patch n'est appliqué à rien, la sortie est identique à celle d'un patch qui a
mordu. Cinq services « critiques » n'avaient donc jamais reçu leur second replica,
pas même au premier lancement.

Les trois premiers sont le **même** défaut que documente `InternalRoutes.cs`
depuis des semaines : la plateforme porte deux vocabulaires, par domaine et par
dépôt. Cet écart a fini par produire cinq patches morts dans le fichier qui décide
combien de pods servent la production.

`scripts/check-k8s.py` résout maintenant chaque cible contre les objets que la base
produit, **sans kustomize** — c'est tout son intérêt : sur un poste sans l'outil,
ce fichier ne vérifiait rien. Il a trouvé deux cibles mortes de plus :
`Cluster/postgres` dans dev et staging, un reste du plan abandonné d'une base dans
le cluster.

### Le PDB bloquait le drain, et le commentaire disait le contraire

`pdb.yaml` reconnaissait le problème puis le désamorçait : « Kubernetes tranche en
faveur du drain après un délai. » C'est faux. L'API d'éviction rend 429 tant que le
budget n'est pas satisfait, `kubectl drain` réessaie indéfiniment, et `--timeout`
fait échouer la commande sans autoriser l'éviction.

`minAvailable: 1` → `maxUnavailable: 1`. Équivalents à deux replicas ; à un seul,
l'ancien autorisait zéro éviction et bloquait pour toujours, le nouveau en autorise
une. À trois et plus, le nouveau est même plus strict.

### CE QUE CETTE DÉCISION NE COUVRE PAS

- **À un replica, il n'y a plus aucune protection** — seulement l'absence de
  blocage. C'est honnête : il n'y a rien à protéger avec un seul pod. La vraie
  réponse est deux replicas.
- **Rien n'empêche un drain de vider plusieurs services à la fois.** Chaque budget
  raisonne sur son service ; dix services à un replica sur le même nœud partent
  ensemble, chacun dans son droit.
- **Le contrôle des cibles ne remplace pas `kustomize build`.** Il reconstitue les
  noms produits en appliquant les `namePrefix` à la main — assez pour attraper une
  cible morte, pas pour valider un rendu.
- **`delivery` et `food` devront retrouver leurs patches** quand leur lot sera
  déployé, sous le nom que produira réellement leur `namePrefix` — pas celui du
  domaine.

## D52 — un retrait de service doit passer par `tests/`, et une référence de projet morte est une erreur

**28 août 2026.** Le retrait de `dispatch`, `tracking` et `proof-of-delivery`
(D42, D43) a laissé trois `ProjectReference` mortes dans
`tests/HBA.Delivery.UnitTests`. La compilation a échoué le lendemain.

### Pourquoi l'inventaire de retrait ne l'a pas vu

Il couvrait neuf points — `HBA.sln`, le compose, `AutorisationsGrpc`,
`HbaTopics`, les trois `kafka-topics.yaml`, `generer-identites-internes.sh`,
`dev-up.sh`, `dev-doctor.sh`, les manifestes. **Tous côté production.** Aucun ne
regardait un projet de test, exactement parce qu'un test « n'est déployé nulle
part » — le raisonnement qui figure d'ailleurs, écrit noir sur blanc, dans le
`.csproj` fautif.

**Et `check-solution.py` ne pouvait pas l'attraper** : les projets de test ne
sont pas dans `HBA.sln`. Il n'y avait rien à vérifier de son côté. Le défaut est
passé dans l'espace entre deux contrôles, pas au travers de l'un d'eux.

### Le symptôme désigne la mauvaise cause

MSBuild rend un **avertissement** MSB9008 — « le projet référencé n'existe pas » —
puis compile quand même, et échoue ensuite sur les `using` en CS0234 : « le nom
d'espace de noms n'existe pas ». On lit cinq erreurs qui parlent d'espaces de
noms, et la seule ligne qui dit la vraie cause est un warning au milieu.

### `scripts/check-refs.py`

Il part des `.csproj` **du disque** — tous, y compris ceux qu'aucune solution ne
référence — et refuse toute `ProjectReference` dont la cible n'existe pas.
178 projets, 547 références.

**Il a été éprouvé en le faisant échouer**, sur un dépôt synthétique portant une
référence morte : un contrôle qui n'a jamais été rouge n'est pas un contrôle
vérifié.

### Ce qui a été fait des tests eux-mêmes

- **`SuiviDeCourseTests`** (5 tests, ISSUE-058) : retiré. La règle qu'il protégeait
  — « n'importe qui publiait la position de n'importe quel livreur » — est
  **structurellement impossible** dans le survivant : `ReportPositionAsync` tire
  l'identité du JETON via `ResolveDriverQuery`, et `PositionRequest` ne porte
  aucun `DriverId`. Vérifié avant de retirer.
- **`AcceptationUniqueTests`** : quatre tests sur l'agrégat conservés, quatre sur
  `DispatchStore` retirés.
- **`PreuveDeRemiseTests`** (12 tests) : **réécrit** contre `Delivery` /
  `ProofOfDelivery`, 11 tests. C'est la vérification de l'affirmation de D43 —
  « delivery-service porte la même capacité, persistée ». Sans ces tests, cette
  phrase n'était qu'une lecture.

### CE QUE CE RETRAIT A RÉELLEMENT COÛTÉ, ET QUI N'AVAIT PAS ÉTÉ DIT

- **La concurrence réelle n'est plus éprouvée nulle part.** `DispatchStore` et
  `ProofStore` étant des `ConcurrentDictionary`, deux acceptations — ou deux
  soumissions du même code — vraiment simultanées y étaient testables. Sur un
  agrégat persisté, ce qui arbitre est l'index unique partiel
  `ux_deliveries_engaged_driver` et le jeton `xmin` : il faudrait une base et deux
  transactions. La couverture est passée de « éprouvée sur une maquette » à
  « éprouvée nulle part ». La maquette n'était pas la production, mais elle était
  le seul banc d'essai.
- **Le code de preuve n'expire plus.** `ProofStore` posait quinze minutes sur
  l'OTP, et deux tests l'éprouvaient. `Delivery.IssuedPin` n'a **aucune
  échéance** : le code émis à la prise en charge reste valable jusqu'à la remise.
  Le retrait a rendu cette différence effective **sans que personne ne la
  décide**. Écrit dans l'en-tête du fichier de tests réécrit.

### ET UNE ERREUR DE PLUS, RATTRAPÉE PAR UN CONTRÔLE

En nettoyant `AuthorizationTestFactory`, j'ai retiré `Services__Routes` avec les
trois autres, en écrivant qu'aucune n'était jamais lue.
`check-service-addresses.py` l'a refusé : `AddRoutesGrpcClient` existe toujours et
lève si la clé est absente. Aucun hôte ne l'appelle aujourd'hui ; le jour où l'un
le fera, ses tests d'autorisation échoueraient à la construction. La ligne a été
remise.

**On ne retire pas une adresse parce qu'on croit qu'elle ne sert pas — on la
retire quand le code qui la lit a disparu.**


---

## D53 — le Secret de production se construit par un script, jamais à la main, et le dépôt vérifie qu'il reste vide

`scripts/db/creer-bases.sh` écrit quatorze mots de passe dans un fichier, et le
runbook demandait ensuite de les recopier à la main dans treize chaînes de
connexion. Trois choses ont mal tourné le 28 août 2026, toutes de la même
famille : **un secret qui transite par un endroit prévu pour être lu.**

**Le fichier de mots de passe apparaissait dans `git status`.** Le script écrit
dans le répertoire courant et le runbook demande de le lancer depuis la racine
du dépôt. Le fichier était donc « untracked », à un `git add -A` de
l'historique. Les commentaires de `.gitignore` disaient déjà de tenir les
secrets hors du dépôt ; aucun motif ne visait ce fichier. Trois motifs ajoutés :
`motsdepasse-*.txt`, `secret-hba-platform*.yaml`, `secrets-hba-*/`.

**Une clé d'API Resend en clair était posée dans `k8s/base/common/secret.yaml`.**
Vérifié : elle n'est entrée dans aucun commit — `git log --all -S` ne la trouve
nulle part. Elle n'y avait pas sa place de toute façon : `hba-platform` est monté
par `envFrom` dans les vingt-quatre pods, alors que cette clé ne concerne que
`notification-service`, qui la lit par `secretKeyRef` depuis `hba-notifications`.

**Onze des treize chaînes portaient `Username=hector`.** Le commentaire situé
trois lignes plus haut dit « UNE RÔLE PAR BASE. Chaque service se connecte avec
son propre compte, qui n'a de droits que sur sa base. » `creer-bases.sh` fait
bien le travail — `REVOKE CONNECT ... FROM PUBLIC`, un `GRANT` au seul
propriétaire, et le cloisonnement a été éprouvé en vrai : `hba_identity` est
refusé sur `hba_user`. Mais un superutilisateur passe outre. La plateforme aurait
démarré, les quatorze essais de connexion auraient réussi, et le cloisonnement
n'aurait servi à rien le jour où il aurait fallu qu'il serve. C'est le pire des
défauts : celui qui ne se voit qu'à l'instant où on comptait dessus.

**Ce qui est choisi.** `scripts/db/secret-depuis-motsdepasse.py` lit le fichier
de mots de passe et écrit le Secret directement. Il n'affiche aucune valeur — sa
sortie ne contient que des noms de clés et des longueurs. Il refuse un fichier
source qui ne serait pas en 0600, refuse un mot de passe contenant `;` ou un
guillemet — qui couperait la chaîne de connexion en deux et ferait lire à Npgsql
un paramètre tronqué — et dérive toujours l'utilisateur du nom de la base.

Deux contrôles rejoignent `scripts/check-k8s.py`, et tournent sans kustomize :

- `verifier_secrets_vides()` — aucune valeur vivante dans
  `k8s/base/common/secret*.yaml`. §12 le disait depuis le début, dans un
  commentaire ; rien ne l'imposait. Éprouvé en reposant la clé Resend.
- `verifier_chaines_de_connexion()` — le gabarit versionné et la table `CLES` du
  générateur déclarent les mêmes clés, et le générateur construit `Username`
  depuis `Database`. Éprouvé deux fois : en écrivant `hector` en dur, et en
  retirant une clé de la table.

**Ce que ces choix ne couvrent pas.** Les contrôles lisent le dépôt, pas le
cluster : ils ne disent rien du Secret réellement appliqué, ni de l'existence du
rôle côté Postgres, ni de ses droits réels. `verifier_secrets_vides()` ne
regarde que `k8s/base/common/secret*.yaml` — un secret posé dans un ConfigMap ou
dans un overlay passe au travers. Aucun des deux ne regarde l'historique : ils
disent ce qui est là, pas ce qui y a été. Et le fichier produit par le
générateur contient, lui, les mots de passe **en clair** : il est en 0600 et
hors du dépôt, mais il doit être supprimé après `kubectl apply`.

**Un secret qui a été lu une fois est un secret à changer — pas un secret à
mieux ranger.**

**Correctif du même jour, trouvé en relisant le fichier avec Hector.** Le
générateur ne produisait que les treize chaînes de connexion et
`CONNECTIONSTRINGS__DEFAULT`. Or `secret.yaml` déclare **dix-huit** clés :
s'ajoutent `REDIS__CONNECTIONSTRING`, `AUTHENTICATION__SIGNINGKEY`,
`INTERNAL__APIKEY` et `SECURITY__SECRETPROTECTION__KEY`.

`kubectl apply -f` **remplace la carte `data` en entier**. Un fichier de quatorze
clés n'en ajoute pas quatorze à un Secret existant : il en fait un Secret de
quatorze clés et efface les quatre autres. Ce qui serait arrivé : toutes les
sessions invalidées d'un coup avec la disparition de `AUTHENTICATION__SIGNINGKEY`,
tous les appels entre services refusés avec `INTERNAL__APIKEY`, et surtout
`SECURITY__SECRETPROTECTION__KEY` perdue — une donnée chiffrée avec une clé
disparue ne se rechiffre pas, elle se perd.

Le script lit désormais la liste des clés **dans `secret.yaml`** au lieu de la
recopier, et refuse d'écrire s'il en reste une qu'il ne sait pas résoudre. Pour
les clés hors Postgres, l'ordre est : valeur déjà posée dans le cluster (reprise
telle quelle, ce qui rend le script rejouable), sinon variable d'environnement,
sinon valeur engendrée — annoncée en toutes lettres, avec un avertissement à part
pour `SECURITY__SECRETPROTECTION__KEY`. Éprouvé sur cinq chemins : marche
nominale (18 clés, accord exact avec le gabarit), clé inconnue ajoutée au
gabarit, mot de passe manquant, mot de passe contenant `;`, fichier source en
0644.

**Un `apply` ne complète pas un Secret : il le remplace. Le fichier doit donc
porter tout ce que le Secret porte, pas seulement ce qu'on vient de changer.**
