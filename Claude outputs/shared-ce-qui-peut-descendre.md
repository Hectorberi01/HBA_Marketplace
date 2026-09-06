# `shared/` — ce qui peut encore descendre, et ce qui ne doit pas

État après les lots gRPC, cache, audit, observabilité, outbox et inbox.

```
shared/
├── proto/       22 .proto            le contrat gRPC — reste, par decision
├── contracts/   14 projets           les contrats publies des domaines
└── common/      5 projets, 87 fichiers, ~11 700 lignes
```

La réponse courte : **il reste trois choses vraiment déplaçables**, une poignée de
fichiers mal rangés, et un noyau qui ne doit pas bouger — pour des raisons que je
nomme une par une plutôt que de dire « c'est partagé ».

---

## 1. Ce qui peut descendre, par ordre d'évidence

### 1.1 Les 14 projets de `shared/contracts/` → chez leur propriétaire

C'est le plus gros morceau, et le plus simple. Ces projets portent les
enregistrements et les `I<X>ModuleApi` d'un domaine. Ils sont consommés par
plusieurs services — donc ils ne peuvent pas descendre dans un consommateur —
**mais ils peuvent rejoindre le `src/` de leur propriétaire**, exactement comme
`HBA.Merchants.Contracts` vit déjà dans `seller-service/src/`.

| Projet | Propriétaire | Consommateurs |
|---|---|---:|
| `HBA.Ordering.Contracts` | order-service | 7 |
| `HBA.Identity.Contracts` | identity-service | 4 |
| `HBA.Media.Contracts` | media-service | 4 |
| `HBA.Promotions.Contracts` | promotion-service | 4 |
| `HBA.Products.Contracts` | catalog-service | 3 |
| `HBA.Returns.Contracts` | return-refund-service | 3 |
| `HBA.DeliveryPricing.Contracts` | delivery-pricing-service | 3 |
| `HBA.Drivers.Contracts` | driver-service | 2 |
| `HBA.Payments.Contracts` | payment-service | 2 |
| `HBA.Pricing.Contracts` · `HBA.Pricing.Promotion` | à trancher (voir §4) | 3 · 2 |
| `HBA.Users.Contracts` | user-service | **0** |
| `HBA.Routes.Contracts` | route-service | **0** |
| `HBA.Shipping.Contracts` | **aucun** | 2 morts |

**Ce que ça change vraiment** : aujourd'hui, « qui possède le contrat de commande »
n'a pas de réponse dans l'arborescence — il est dans un dossier neutre que
personne ne possède. Après, il est dans `order-service/src/`, et la seule règle à
tenir devient lisible : *un service modifie SES contrats, jamais ceux des autres.*

**Ce que ça ne change pas** : les consommateurs les référencent toujours. Un
contrat publié est partagé par nature ; ce qui bouge, c'est son adresse, pas son
statut.

**Coût** : 14 déplacements de dossier, ~40 `ProjectReference` à réécrire, 14
entrées de solution. Mécanique, et le compilateur voit tout.

### 1.2 `Idempotency/` → dans chaque service

Six fichiers dans `HBA.Shared.Infrastructure` : `IdempotencyRecord` (entité EF),
sa configuration, `EfIdempotencyStore`, `IdempotencyPurger`, l'enregistrement, et
le port `IIdempotencyStore`.

**C'est exactement la forme de l'outbox et de l'inbox** — table par service, purge
générique sur le DbContext, port au socle. Ta structure a d'ailleurs un nœud
`Idempotency/` par service. Descendent : l'entité, la configuration, le store, la
purge, l'enregistrement. Reste : `IIdempotencyStore`, parce que
`IdempotencyEndpointFilter` (couche HTTP, partagée) l'appelle sur chaque route
annotée `AllowIdempotency()`.

**Une nuance qui compte** : l'idempotence ne sert pas qu'à Kafka. Elle protège
aussi les routes HTTP rejouées par un client mobile sur un réseau instable. La
descendre ne doit pas la sortir du chemin HTTP — c'est la seule chose à vérifier
en le faisant.

**Même conséquence EF que l'audit et l'outbox** : les instantanés de modèle
référencent `HBA.Shared.Infrastructure.Idempotency.IdempotencyRecord`. Ça ferait
la **troisième** entité à régénérer dans le même `dotnet ef migrations add`.
Autant les faire ensemble.

### 1.3 Trois fichiers HTTP utilisés par un ou deux services

| Fichier | Utilisé par | Verdict |
|---|---|---|
| `Hosting/Http/StepUpAuthentication.cs` | identity-service **seul** | descend chez identity |
| `Hosting/Http/UploadValidation.cs` + `Infrastructure/Files/FileSignature.cs` | media-service, catalog-service | descend chez media, devient un contrat que catalog référence |
| `Hosting/Http/IdempotencyEndpointFilter.cs` | catalog-service seul aujourd'hui | à descendre **avec** le lot 1.2, pas séparément |

`FileSignature` mérite une ligne à part : il vit dans `Infrastructure` alors que
son **seul appelant** est `UploadValidation`, dans `Hosting`. Ce n'est pas du code
mort, c'est du code rangé au mauvais étage — et personne ne s'en serait aperçu en
lisant l'un ou l'autre fichier.

---

## 2. Ce qui doit rester, et la raison exacte

Trois raisons seulement. Aucune n'est « c'est pratique ».

### Raison 1 — c'est un FORMAT. Deux implémentations = deux formats.

| Ce qui reste | Ce que ça formate |
|---|---|
| `Kafka/HbaEventEnvelope`, `KafkaEventEnvelope` | l'enveloppe sur le fil |
| `Kafka/KafkaEventNaming`, `HbaEventNaming` | le nom d'événement et le sujet |
| `Kafka/HbaTopics` | le catalogue des sujets de la plateforme |
| `Serialization/EventTypeName` | le type écrit dans l'outbox et relu par le consommateur |
| `Grpc/MontantSurLeFil` | l'argent en texte sur gRPC — **14 services** |
| `Security/SecretProtector` | le chiffrement des codes : identity chiffre, notification déchiffre, **même clé** |
| `Persistence/ConcurrencyTokenExtensions`, `HorodatageExtensions` | les colonnes `xmin` et `updated_at` de toutes les migrations |
| `IntegrationEvents/*` | la classe de base et les attributs des 128 événements |

C'est la panne `livraison.*` / `service.*` de ce mois-ci qui donne le prix de
cette ligne : deux valeurs par défaut pour une même notion, et toute la couche
événementielle est morte sans une seule erreur.

### Raison 2 — c'est un PORT dont dépend la couche Application.

`ICacheService`, `IIdempotencyStore`, `IConsumerInbox`, `IUnitOfWork`,
`ISecretProtector`, `IPlatformPricing`, `IDomainEventHandler`,
`Observability/MetricsAbstractions`, `IModuleInstaller`.

Les descendre obligerait à changer ce dont dépend le **code métier** pour un
déplacement d'infrastructure. C'est l'inverse du sens des dépendances.

### Raison 3 — c'est une RÈGLE qui doit être identique partout.

| Ce qui reste | Ce que ça décide |
|---|---|
| `Hosting/EnvironnementDeploiement` | « sommes-nous en production » — six copies existaient, toutes fail-open |
| `Persistence/ModuleDbContext` (le drain) | un événement part dans la MÊME transaction que le fait qui l'a produit |
| `Events/IntegrationEventDispatcher` | l'idempotence est vérifiée avant CHAQUE gestionnaire |
| `Events/DrainageDOutbox` | une seule lecture de `OUTBOX_ENABLED` |
| `Persistence/PortsTechniques`, `IEntreeDeJournal` | l'exclusion des tables techniques du journal — sinon boucle infinie |
| `Grpc/` intercepteurs, `AutorisationsGrpc`, `IdentiteInterne` | disjoncteur, identité interne, autorisations : **uniformes sur 17 clients, vérifié** |
| `Hosting/ServiceHostExtensions`, `Http/*`, `OpenApi/`, `Telemetry/` | le pipeline HTTP et l'instrumentation, posés d'un coup — « un branchement à faire quatorze fois est un branchement qu'on oublie une fois » |
| `Domain/Primitives`, `Domain/Results` | la langue commune de tous les domaines |

---

## 3. Ce qui est mort, et qu'il faut retirer plutôt que déplacer

| Constat | Détail |
|---|---|
| `HBA.Shipping.Contracts` | décrit un service qui n'existe pas. Deux consommateurs, trois gestionnaires qui n'ont jamais tourné — dont un qui devait libérer les gains d'un vendeur. Le chemin `OrderDelivered` couvre le besoin. |
| `HBA.Users.Contracts` | **aucun consommateur** : seul user-service le référence. Son serveur gRPC tourne, personne ne l'appelle. |
| `HBA.Routes.Contracts` | idem : seul route-service. Et route-service n'a ni base ni outbox. |

Ce n'est pas la même décision que « déplacer ». Un contrat sans consommateur qu'on
déplace reste un contrat sans consommateur, rangé plus proprement.

---

## 4. Les deux cas que je ne peux pas trancher

- **`HBA.Pricing.Contracts` et `HBA.Pricing.Promotion`** — référencés par
  cart-service, food-cart-service, order-service, et par un autre projet de
  contrats. Aucun service ne s'en détache comme propriétaire évident. Soit
  promotion-service les prend, soit ils restent un contrat de plateforme comme
  `HbaTopics`.
- **`Configuration/PlatformPricing` et `Domain/Geography/BeninGeography`** — de la
  **donnée de référence**, pas du code : les taux de commission (billing, wallet,
  catalog, seller) et le découpage administratif du Bénin (9 services). Elles ne
  sont ni un format, ni un port, ni une règle — mais les descendre en 4 et 9
  exemplaires ferait diverger des données qui doivent être les mêmes. Elles
  appartiennent probablement à un service qui les EXPOSE, pas à une bibliothèque
  qui les recopie.

---

## 5. Ordre que je propose

1. **Compiler ce qui est déjà là.** Sept lots s'empilent sans être passés par un
   compilateur. Ajouter un huitième avant d'en avoir un vert rendrait les erreurs
   impossibles à attribuer.
2. **`Idempotency/`** — la même forme que l'outbox, faite deux fois cette semaine,
   et elle complète le passage `dotnet ef migrations add` au lieu d'en demander un
   troisième.
3. **Les 14 contrats chez leur propriétaire** — mécanique, gros diff, zéro risque
   d'exécution.
4. **Les trois fichiers HTTP mal rangés** — dix minutes.
5. **Le tri du mort** (§3) — demande ton arbitrage métier, pas du code.
