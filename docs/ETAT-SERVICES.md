# Quels services sont prêts pour la production

Relevé du 29 août 2026, établi en lisant le dépôt — pas de mémoire. « Prêt »
veut dire trois choses à la fois : le service **démarre** en production, il a un
**manifeste** Kubernetes, et ce qu'il fait une fois démarré est **utilisable**.
Un service qui démarre sans rien pouvoir faire n'est pas prêt.

---

## Déployés dans le lot de production — 9

Ils ont un manifeste, une image, et aucun garde-fou ne les empêche de démarrer.

| Service | Ce qui marche | Ce qui ne marche pas |
|---|---|---|
| **identity-service** | Comptes, jetons, rôles. Le compte administrateur est amorcé au premier démarrage. | Aucun « mot de passe oublié » : l'e-mail passe par notification-service, qui n'est pas déployé. Le compte amorcé est le seul moyen d'entrer. |
| **user-service** | Profils, adresses. | — |
| **media-service** | Stockage objet câblé sur MinIO. | Les buckets ne sont pas créés par le déploiement (étape 7 bis du runbook). MinIO est sur le même disque que les pods, sans sauvegarde, et porte les pièces KYB. |
| **payment-service** | Démarre, répond, tient sa base. | **Aucun paiement n'aboutit.** En production, une passerelle non configurée n'est pas simulée : elle n'est pas enregistrée du tout. Stripe, PayPal, Moov : aucune n'a de clés. |
| **promotion-service** | Campagnes, coupons. | — |
| **review-service** | Avis, recommandations, listes d'envies — trois modules, trois `DbContext`. | — |
| **delivery-service** | Courses, cycle de vie. | Manifeste écrit le 29 août, **jamais construit ni déployé**. |
| **driver-service** | Livreurs. | Idem : manifeste neuf, jamais éprouvé. |
| **delivery-pricing-service** | Tarification des courses. | Le facteur de correction urbaine vaut 1.0 : la plateforme sous-facture les zones denses. Décision en attente. Pas de migration propre — il lit les tables de delivery-service. |

---

## Refusent de démarrer en production — 3

Ce ne sont pas des pannes : chaque refus est un garde-fou délibéré, avec un
message qui nomme la conséquence métier. Les déployer tels quels donnerait un
`CrashLoopBackOff`.

**notification-service** — `NotificationsModuleInstaller` lève dans **les deux
branches** du canal SMS. Configuré : aucun adaptateur `ISmsSender` de production
n'existe dans le dépôt. Non configuré : le SMS est le canal OTP par défaut, et un
code qui n'atteint personne est un échec totalement silencieux. Il n'y a pas de
troisième chemin. Conséquence pour toute la plateforme : **aucun courriel ni SMS
ne part**.

**return-refund-service** — refuse parce que **trois adaptateurs gRPC sont des
bouchons** : la marchandise retournée n'est jamais remise en stock, aucune course
d'enlèvement n'est créée alors qu'un numéro est rendu au client, et aucune preuve
photo n'est vérifiée. Le service bloque le lot marketplace entier tant que ces
trois-là ne sont pas écrits.

**food-cart-service** — refuse parce qu'il n'est branché sur aucun service de
promotion : toute remise serait ignorée et tout code promo refusé, en silence.
Son propre commentaire chiffre la levée à une demi-journée — brancher
`PromotionPricingModuleApi` comme `cart-service` le fait déjà.

---

## Ont un manifeste mais ne sont pas déployés — 5

Le lot marketplace. Les dossiers existent, six lignes commentées dans
`k8s/base/services/kustomization.yaml` suffisent à les activer. Leurs bases,
leurs rôles et leurs clés d'identité existent déjà.

`catalog-service`, `cart-service`, `inventory-service`, `order-service`,
`seller-service`.

**Deux réserves sérieuses avant d'activer ce lot :**

- **`order-service` a 18 tests en échec** — 10 en autorisation, 8 en intégration,
  tous rendant 401 là où l'on attend 403, 404, 409 ou 201. La cause n'est **pas
  trouvée** : la lecture d'identité est identique au caractère près à celle de
  vingt autres services, aucun middleware ni filtre propre à cet hôte ne peut
  rendre 401, et la configuration a été éliminée. Ne pas déployer order-service
  avant d'avoir compris ce 401.
- **`return-refund-service` bloque au démarrage** (voir ci-dessus). Le lot
  marketplace est donc incomplet par construction tant que ses trois bouchons
  gRPC sont là.

---

## Sans manifeste Kubernetes — 4

Ils ont un Dockerfile et du code, mais rien dans `k8s/`.

| Service | État |
|---|---|
| **food-order-service** | Aucun garde-fou de production. Il ne manque que les manifestes. |
| **restaurant-service** | Idem. |
| **food-cart-service** | Manifestes ET garde-fou à lever. Sans lui, le lot food n'a pas de panier. |
| **route-service** | **Aucun appelant dans le dépôt** (audit du 27 août). Le déployer ferait tourner un pod que personne n'interroge. |

---

## Ne sont pas des services — 4

`billing-service`, `wallet-service`, `recommendation-service`,
`wishlist-service` vivent sous `services/common/` mais n'ont ni Dockerfile ni
manifeste : ce sont des **modules**, hébergés dans un autre processus.
`billing` et `wallet` tournent dans payment-service ; `recommendation` et
`wishlist` dans review-service. C'est ce que confirment leurs `DbContext`
respectifs, migrés par les Jobs de ces deux services.

L'arborescence est trompeuse sur ce point : un dossier sous `services/` n'est pas
forcément un service déployable.

---

## Ce que ce relevé ne dit pas

**L'état des tests est partiel.** Je connais l'issue de quatre suites : Merchants
(14 échecs, corrigés — c'était un vrai défaut de production, pas un défaut de
test), Order.AuthorizationTests (10, ouverts), Order.IntegrationTests (8,
ouverts), ReturnRefund.AuthorizationTests (2, corrigés — deux cas visaient une
route supprimée). Les autres suites, je ne les ai pas vues passer.

**Rien n'a été déployé.** Les neuf services du lot n'ont jamais tourné en
production : les huit Jobs de migration sont en `ImagePullBackOff`, les images ne
sont pas promues, et `ci.yml` échouait encore il y a peu. « Prêt » signifie ici
« rien de connu ne l'en empêche », pas « éprouvé ».

**Trois manifestes ont un jour d'âge.** `delivery-service`, `driver-service` et
`delivery-pricing-service` ont été écrits le 29 août et vérifiés statiquement —
`kustomize build` rend ce qu'on attend — mais aucun n'a jamais été appliqué à un
cluster.

**Aucune supervision, aucune sauvegarde de base.** Vrai pour les neuf.
