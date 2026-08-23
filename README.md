# HBA — architecture cible en microservices

Ce dossier est le **squelette de la cible**, pas un projet en fonctionnement. Il ne
contient aucun code : uniquement l'arborescence et, dans chaque dossier, la note qui
dit ce qui doit y atterrir.

## Arborescence

Depuis le 17 aout 2026, le depot suit la structure du design systeme. Le detail des
deplacements, ce qui a ete verifie et ce qui reste a faire :
**[`docs/REORGANISATION.md`](docs/REORGANISATION.md)**.

```
HBA/
├── .github/               Workflows CI/CD
├── .vscode/               Configurations locales
├── docs/                  Decisions d'architecture, contrats, runbooks
├── scripts/               Outillage (controles, migration, amorcage)
├── apps/
│   └── api-gateway/       Authentification, rate limiting, routage — ET le BFF
│                          (D38 : les trois squelettes client/seller/driver-bff
│                          ont ete retires, le BFF reel vit dans la passerelle)
├── clients/               Interfaces utilisateur (Next.js, Flutter)
├── services/
│   ├── common/            identity, user, payment, wallet, billing, notification,
│   │                      promotion, media, file, review, recommendation, wishlist
│   ├── marketplace/       catalog, seller, inventory, cart, order, return-refund
│   ├── food/              restaurant, menu, food-cart, food-order, kitchen, availability
│   └── delivery/          delivery, dispatch, driver, tracking, route,
│                          proof-of-delivery, delivery-pricing
├── shared/                common/, contracts/, proto/, kafka-schemas/, types/
├── infra/                 docker/, observability/, rembg/
├── k8s/                   Manifests Kubernetes
├── tests/                 Integration, bout en bout, charge, autorisation
├── docker-compose.dev.yml
├── Makefile               `make help` pour la liste des cibles
└── HBA.sln
```

**Tous les dossiers de `services/` ne correspondent pas a un processus.** Ceux
dont le `README.md` annonce « squelette » ont l'arborescence mais pas encore le
code : ils ne sont ni dans `HBA.sln` ni dans `docker-compose.dev.yml`, et leur
fonctionnalite est rendue par le service d'origine indique dans leur README.


## Pourquoi il coexiste avec `../src/`

Le produit tourne aujourd'hui en **monolithe modulaire** : 29 modules dans
`../src/Modules/`, une seule base PostgreSQL à schémas séparés, un seul processus.
Ce n'est pas un accident de conception — c'est ce qui a permis de déplacer des
frontières sans coordonner des déploiements.

Les deux arbres vivent donc côte à côte. Un service se remplit quand SA frontière
est prête, et le monolithe continue de servir tout le reste. Basculer les 29
modules d'un coup serait remplacer un système qui marche par treize systèmes qu'on
n'a jamais fait tourner ensemble.

## Ce qui est déjà prêt pour la découpe — et ce qui ne l'est pas

Le monolithe a été construit avec cette sortie en tête, et trois choses en
témoignent :

- **Chaque module expose une `I*ModuleApi`** — un contrat in-process qui deviendra
  un appel réseau sans que l'appelant change de forme.
- **Les modules ne se référencent que par `*.Contracts`**, et des tests
  d'architecture (`../tests/Architecture.Tests/`) le vérifient à chaque build.
- **Tout ce qui connaît deux modules vit dans `../src/Bootstrap/Marketplace.Api/Integration/`** —
  le composition root. Ces fichiers sont exactement ceux qui deviendront des
  consommateurs Kafka.

Ce qui n'est PAS prêt, et qu'aucune arborescence ne résout :

- **L'outbox est in-process.** Elle promet « au moins une fois » dans un seul
  processus. Distribuée, la même promesse coûte un broker et une politique de
  lettres mortes par service.
- **Les transactions traversent les modules.** Un checkout écrit dans Cart,
  Ordering et Inventory sous une seule transaction. Découpé, c'est une saga — avec
  ses compensations, qu'il faut écrire.
- **La base est partagée.** Les schémas sont séparés, mais rien n'empêche
  physiquement une jointure. Chaque extraction devra prouver qu'il n'y en a pas.

## La carte de migration

| Service cible | Modules actuels | Remarque |
|---|---|---|
| `identity-service` | Identity | Le seul dont personne ne dépend en écriture. |
| `user-service` | User | Profils et adresses ; déjà extrait d'Identity. |
| `seller-service` | Sellers | Dossiers KYB, boutiques, comptes de reversement. |
| `catalog-service` | Catalog, Product, Search | Catalog et Product se recouvrent — voir la note du service. |
| `inventory-service` | Inventory | Stock, lieux, réservations. |
| `restaurant-service` | Food | Le plus récent, et le plus proche d'être autonome. |
| `cart-service` | Cart, Wishlist, Pricing, Loyalty, Marketing | Tout ce qui précède la commande. |
| `order-service` | Ordering, Returns, Disputes | Le cycle de vie d'une commande, du checkout au litige. |
| `delivery-service` | Delivery, Shipping | Colis et courses ; Delivery est déjà quasi autonome. |
| `payment-service` + `wallet-service` + `billing-service` | Payments, Wallet, Billing, Tax, Fraud | Le plus risqué à découper — voir la note du service. |
| `review-service` + `recommendation-service` + `wishlist-service` | Reviews, Recommendations, Analytics | Signaux et retours clients. |
| `notification-service` | Notifications, Messaging | Sortant (push/SMS/e-mail) et conversationnel. |
| `media-service` | Media | Déjà conçu comme un service autonome : le meilleur premier candidat. |

29 modules, 13 services.

## Ordre d'extraction suggéré

Il n'est pas arbitraire : on sort d'abord ce dont **personne ne dépend en
écriture**, et on garde pour la fin ce qui porte de l'argent.

1. **media-service** — aucune écriture croisée, déjà un port `IObjectStorage`.
2. **communication-service** — purement sortant, consomme des événements.
3. **identity-service** puis **user-service** — dépendances entrantes seulement.
4. **food-service** — son test de frontière lui interdit déjà de connaître les autres.
5. **delivery-service** — conçu pour être vendu à des marchands tiers.
6. Le reste, et **financial-service en dernier**.
