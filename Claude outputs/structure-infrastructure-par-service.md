# La structure d'Infrastructure par service — l'état réel, nœud par nœud

Ta structure est une cible complète. Avant de l'appliquer, voici où en est le dépôt face à elle :
ce qu'elle trouve et que je n'avais pas vu, ce qui existe déjà sous un autre nom, et les quelques
nœuds où « appartenir au service » et « être recopié dans le service » ne veulent pas dire la même
chose.

---

## 1. Trois trous que ta structure met au jour — et qui sont réels

Ce n'est pas de la forme. Ces trois-là existent parce que personne n'avait posé la question sous cet
angle.

### 1.1 `Inbox/InboxCleanupService.cs` — la table d'inbox n'est JAMAIS purgée

`OutboxPurger<TDbContext>` existe. `IdempotencyPurger<TDbContext>` existe. **Rien ne purge
`consumer_inbox`.** Chaque événement traité y laisse une ligne, définitivement, dans les 15 services
qui consomment.

Ce n'est pas urgent — quelques centaines de milliers de lignes par an — mais c'est une table qui ne
cesse jamais de grossir, indexée sur une clé consultée à CHAQUE message reçu. Elle finira par coûter
sur le chemin chaud, et le jour où ça arrivera, personne ne cherchera là.

### 1.2 `Observability/HealthChecks/` — un seul contrôle sur quatre

`ServiceHostExtensions` pose exactement ceci, pour les 24 services :

```csharp
services.AddHealthChecks().AddDbContextCheck<TDbContext>("database", tags: ["ready"]);
```

La base est vérifiée. **Redis, Kafka et gRPC ne le sont pas.** Un service dont le consommateur Kafka
est mort répond donc « ready » — et le déploiement individuel qu'on vient d'ajouter
(`up -d --no-deps <service>`) considérera le service comme sain. C'est exactement la panne de ce mois-ci
qui redevient invisible, à l'endroit où on venait de gagner en visibilité.

### 1.3 `Time/IClock.cs` — il n'y a pas d'horloge

Aucune abstraction du temps dans le dépôt : `DateTime.UtcNow` partout. La conséquence est connue —
tout ce qui dépend d'un délai (expiration de jeton, purge, échéance, fenêtre de rejeu) ne peut être
testé qu'en attendant vraiment.

**La réponse n'est pas d'écrire `IClock` 25 fois.** .NET 9 fournit `TimeProvider`, que
`Microsoft.Extensions.*` sait déjà injecter et que les tests remplacent par `FakeTimeProvider`. Ce
nœud de ta structure est légitime, et sa bonne implémentation est de ne pas l'implémenter.

---

## 2. L'état actuel, nœud par nœud

Quatre statuts : **déjà par service** · **à déplacer** (mécanique) · **partagé, et pourquoi** ·
**n'existe pas**.

### Persistence

| Nœud de ta structure | État |
|---|---|
| `DbContext/<X>DbContext.cs` | **déjà par service** — 25/25, aujourd'hui à plat dans `Persistence/` |
| `Configurations/` | **déjà par service** — 25/25 |
| `Repositories/` | **déjà par service** — dans `Persistence/`, non regroupés |
| `Migrations/` | **à déplacer** — 20/25 l'ont, mais à la RACINE du projet, pas sous `Persistence/`. Attention : `dotnet ef` écrit dans le dossier configuré ; déplacer sans régler `--output-dir` fera revenir les suivantes à la racine |
| `Outbox/OutboxMessage.cs` + `Configuration` | **partagé** — entité EF mappée par les migrations de 18 services sur la même table |
| `Outbox/OutboxRepository.cs` | **n'existe pas, et ne doit pas** — le dépôt, c'est le `DbSet` du `ModuleDbContext` du service |
| `Outbox/OutboxCleanupService.cs` | **partagé et déjà générique** — `OutboxPurger<TDbContext>` |
| `Inbox/InboxMessage.cs` + `Configuration` | **partagé** — `ConsumerInboxEntry` |
| `Inbox/InboxRepository.cs` | **partagé** — `EfConsumerInbox` |
| `Inbox/InboxCleanupService.cs` | **N'EXISTE PAS** — §1.1 |

### Messaging/Kafka

Fait, à quatre nœuds près.

| Nœud | État |
|---|---|
| `Configuration/KafkaTopics.cs` | **déjà par service** — `Sujets<X>.cs`, 26 modules |
| `Configuration/KafkaOptions.cs`, `KafkaConsumerOptions.cs` | **partagé** — `KafkaEventBusOptions`, liée aux variables d'environnement du déploiement |
| `Consumers/`, `Producers/` | **déjà par service** |
| `Processors/OutboxProcessor.cs`, `InboxProcessor.cs` | **partagé et déjà générique sur le DbContext du service** |
| `Serialization/`, `Headers/` | **partagé** — enveloppe `HbaEventEnvelope`, en-têtes normalisés |
| `Retry/KafkaRetryPolicy.cs`, `DeadLetterQueueHandler.cs` | **partagé** — `OutboxRetryPolicy` + la DLQ ajoutée au lot 5.4 |
| `DependencyInjection.cs` | **déjà par service** — 26/26 |

### Grpc

Lots B et C, en cours. `Interceptors/` et `Policies/` sont le point à trancher : les quatre
intercepteurs actuels (corrélation, identité interne, disjoncteur, traduction des erreurs) sont
partagés et **uniformes sur les 17 clients** — c'est le point le plus sain du dépôt. `Policies/`
correspond au disjoncteur (existe) et à l'échéance (lot A, faite hier).

### Caching/Redis

| Nœud | État |
|---|---|
| `Services/IDistributedCacheService` | **partagé** — `DistributedCacheService` + `NoOpCacheService`, utilisés par 29 fichiers |
| `Keys/CacheKeyFactory.cs` | **déjà par service** — `CartCacheKeys`, `FoodCartCacheKeys`… |
| `Configuration/RedisOptions.cs` | **partagé** — vient du déploiement |
| `Serialization/RedisSerializer.cs` | **n'existe pas** — sérialisation JSON par défaut |
| dossier `Redis/` | 8 services l'ont ; **celui de food-cart est VIDE**, reliquat |

### Le reste

| Nœud | État |
|---|---|
| `Idempotency/` (6 fichiers) | **partagé, complet** — store, entité, purger, enregistrement. Protège aussi les routes HTTP `AllowIdempotency()`, donc ne descend pas |
| `Auditing/` | **partagé pour l'entité** (`AuditEntry`, `AuditConfiguration`), **par service pour la lecture** (`AuditTrailReader` chez seller). `AuditInterceptor`, `AuditContextAccessor`, `AuditOptions` : **n'existent pas** |
| `Observability/Logging,Metrics,Tracing` | **partagé** — `HbaTelemetry`, posé sur les 24 services d'un coup, « parce qu'un branchement à faire quatorze fois est un branchement qu'on oublie une fois » |
| `Observability/HealthChecks/` | **1 sur 4** — §1.2 |
| `Security/` | **partagé** — `SecretProtector` (AES-GCM), `VerificationDesSecretsAuDemarrage`. `identity-service` a en plus son propre `Security/` (7 fichiers) : hachage, jetons — c'est SON métier |
| `Resilience/` | **partagé** — disjoncteur gRPC, `HbaResilience` côté passerelle. `Bulkhead` : n'existe pas |
| `BackgroundJobs/Workers/` | **déjà par service** — 6/25 en ont |
| `Serialization/Json/` | **partagé** — `EventTypeName`, options JSON |
| `Time/` | **N'EXISTE PAS** — §1.3 |
| `DependencyInjection/` (7 fichiers) | **déjà par service, en 1 fichier** — `<X>ModuleInstaller.cs`. Le découper en sept est une amélioration de lisibilité réelle |

---

## 3. La distinction qui décide de tout : posséder ≠ recopier

Tu écris : « tout cela appartient au service concerné ». Je suis d'accord, et c'est déjà vrai pour
l'essentiel — mais pas par le mécanisme qu'on croit.

**`OutboxProcessor<TDbContext>` est générique sur le DbContext du service.** À l'exécution,
user-service a SA boucle, sur SA table, dans SON conteneur, avec SA politique de rejeu. Le service
possède son outbox complètement. Ce qu'il ne possède pas, c'est le **texte de la classe** — et c'est
la seule chose que la duplication lui donnerait.

Le prix de ce texte, précisément :

- **`OutboxMessage`** est une entité EF dont les colonnes sont créées par les migrations de 18
  services. Une copie locale qui ajoute, renomme ou retire une propriété produit un `DbUpdateException`
  ou, pire, une colonne silencieusement ignorée. Le contrat n'est pas le code : c'est la table.
- **`HbaEventEnvelope` et les en-têtes Kafka** sont le format sur le fil. Deux implémentations, c'est
  deux formats — et un consommateur qui ne reconnaît plus ce qu'un producteur écrit. C'est
  littéralement la panne `livraison.*` / `service.*` de ce mois-ci.
- **Les quatre intercepteurs gRPC** sont aujourd'hui uniformes sur 17 clients, vérifié. Un cinquième,
  local, réintroduit la question « lequel des deux est le bon ».

Appliquée à la lettre, ta structure fait **~80 fichiers nommés × 25 services ≈ 2 000 fichiers**, dont
la grande majorité seraient des copies de la quarantaine de types partagés. Ce n'est pas un argument
d'effort — le générateur les écrirait en une heure, comme pour Kafka. C'est un argument de nombre de
choses à tenir d'accord.

---

## 4. Trois façons de l'appliquer

**A — La forme partout, l'implémentation partagée.** Chaque service reçoit l'arborescence complète.
Dans les dossiers où le mécanisme est partagé, un fichier unique porte **la politique de CE service**
et nomme l'implémentation qu'il câble : `Persistence/Outbox/OutboxUsers.cs` déclare la rétention, le
rejeu, et appelle `AddOutboxProcessor<UsersDbContext>()`. Le dossier n'est jamais vide, il n'est
jamais un doublon, et un développeur qui ouvre `Persistence/Outbox/` trouve la réponse à « comment
mon service fait-il sortir ses événements ». C'est ce que fait déjà le module Kafka.

**B — La duplication complète.** Chaque service porte son propre code pour tout. Autonomie maximale
d'équipe, y compris le droit de diverger. Coût : ~2 000 fichiers, et 40 contrats à maintenir en 25
exemplaires — dont trois qui sont des formats partagés (table, fil, protocole) où diverger n'est pas
une liberté mais une panne.

**C — La forme là où il y a du contenu.** Comme A, mais on ne crée pas le dossier quand il n'y a
rien à y mettre — la règle déjà appliquée au module Kafka (`Serialization/` et `Interceptors/` y sont
délibérément absents, avec la raison écrite). Un dossier vide se lit comme une promesse tenue
ailleurs.

Dans les trois cas, les §1.1, 1.2 et 1.3 sont à combler — ce sont de vrais manques, indépendants du
choix.

---

## 5. Ce que ce document ne couvre pas

- Je n'ai pas vérifié les 25 `Infrastructure` fichier par fichier : le tableau du §2 vient d'un
  inventaire des dossiers et des types, pas d'une lecture complète.
- Le déplacement de `Migrations/` sous `Persistence/` touche l'outillage EF, pas seulement des
  fichiers. À traiter séparément, avec les 5 services qui n'en ont pas.
- Rien ici ne dit ce qui est JUSTE. Une structure ne rend pas un service autonome ; elle rend visible
  ce dont il dépend. Le lot gRPC en cours, lui, retire une dépendance réelle.
