# Module Kafka par service — la convention, et ce qu'elle ne dit pas

Référence de portage. `user-service` est fait (commit `119dada`), il sert de modèle.

---

## 1. La forme cible, appliquée à HBAExpress

```
Infrastructure/
└── Messaging/
    └── Kafka/
        ├── Configuration/
        │   ├── Sujets<Service>.cs        les sujets écoutés
        │   └── GardeDeCablage.cs         refuse le démarrage si le module n'est pas branché
        ├── Consumers/
        │   └── <Evenement>Handler.cs     un fichier par gestionnaire
        ├── Producers/
        │   └── EvenementsPublies.cs      DÉCLARE ce qu'on publie, n'envoie rien
        ├── Outbox/
        │   └── Outbox<Service>.cs        le câblage du chemin de sortie
        ├── Inbox/
        │   └── Inbox<Service>.cs         le câblage de la garde anti-doublon
        ├── Serialization/                absent tant qu'il n'y a pas de convertisseur propre
        ├── Interceptors/                 absent — la corrélation est dans l'enveloppe partagée
        └── DependencyInjection.cs        Ajouter Messagerie<Service>()
```

---

## 2. Ce que chaque dossier contient — et surtout ce qu'il ne contient pas

La ligne de partage tient en une phrase : **le module porte la POLITIQUE du service, le socle partagé porte le TYPE et le protocole.**

| Dossier de l'exemple | Ce qu'on y met dans HBAExpress | Ce qui reste dans `HBA.Shared.Infrastructure` | Pourquoi |
|---|---|---|---|
| `Configuration/KafkaOptions.cs` | rien | `KafkaEventBusOptions` | Liée à la section « Kafka » de la config, donc aux variables d'environnement du déploiement. La redéclarer par service crée une seconde source de vérité sur les serveurs et le préfixe de sujet. |
| `Configuration/KafkaTopics.cs` | `Sujets<Service>.cs` — la liste que CE service écoute | `HbaTopics` (le catalogue complet) | La liste locale remplit `SubscribeTopics`. Vide, le consommateur se rabat sur les 20 sujets de la plateforme. |
| `Configuration/KafkaConsumerOptions.cs` | rien pour l'instant | `KafkaEventBusOptions.ConsumerGroup` | Même raison que `KafkaOptions`. À créer le jour où un service a besoin d'un réglage que la config ne couvre pas. |
| `Consumers/` | les gestionnaires, un par fichier | `KafkaIntegrationEventConsumer` | Le consommateur est un transport, identique partout. |
| `Producers/` | `EvenementsPublies.cs` — la LISTE, avec le contrôle `[HbaEvent]` au démarrage | `KafkaIntegrationEventPublisher` | Les appels à `PublishAsync` restent dans `Application` : l'événement doit être mis en file là où le fait métier se produit, pour que `ModuleDbContext.SaveChangesAsync` le draine dans la MÊME transaction. Un `Producers/` qui enverrait vraiment casserait l'outbox. |
| `Outbox/OutboxMessage.cs` | **rien** | `OutboxMessage` | Entité EF, table créée par les migrations de 18 services. Une copie locale diverge de la colonne réelle en silence. |
| `Outbox/OutboxProcessor.cs` | **rien** | `OutboxProcessor<TDbContext>` | Déjà générique sur le DbContext. Un processeur local lirait une autre table que celle où `SaveChangesAsync` écrit. |
| `Outbox/OutboxRepository.cs` | **rien** | le `DbSet` de `ModuleDbContext` | Le dépôt existe déjà, c'est le DbContext. |
| `Outbox/OutboxOptions.cs` | **rien** | `OutboxRetryPolicy` (10 tentatives, backoff, lettre morte) | Deux politiques de rejeu divergentes, c'est la panne de la semaine sous un autre nom. |
| `Inbox/*` | idem : seulement le câblage | `ConsumerInboxEntry`, `EfConsumerInbox` | Idem. |
| `Serialization/` | absent | le sérialiseur partagé | Un dossier vide se lit comme une promesse tenue ailleurs. Le premier service qui a un vrai convertisseur le crée. |
| `Interceptors/` | absent | `traceparent` + corrélation portés par l'enveloppe | Un intercepteur local serait une seconde implémentation du même contrat. |
| `DependencyInjection.cs` | `Ajouter Messagerie<Service>()` | — | Point d'entrée unique, appelé par `Program.cs`. |

L'**idempotence** ne figure pas dans cette structure et n'y descend pas : elle protège aussi les routes HTTP annotées `AllowIdempotency()`. Elle reste dans `<Service>ModuleInstaller`.

---

## 3. Le prix du déplacement, et comment il est payé

Descendre l'outbox et l'inbox dans le module les sort de `<Service>ModuleInstaller`, que le composition root appelle TOUJOURS, pour les mettre dans un module qu'il peut oublier.

Un oubli ne casse **rien de visible** : le service compile, démarre, sert ses routes HTTP, écrit dans l'outbox — et n'émet ni ne consomme plus rien. C'est la panne exacte qui a laissé `users.user_profiles` vide pendant que `identity.users` se remplissait.

D'où `GardeDeCablage` : un `IHostedService` enregistré **par l'installeur**, qui refuse le démarrage si `AbonnementsKafka` est absent du conteneur. Elle est enregistrée là précisément parce qu'elle doit exister quand le module, lui, est absent.

`IHostedService` et non `BackgroundService` : une exception dans `StartAsync` arrête l'hôte, la même dans `ExecuteAsync` est avalée.

**Ce qu'elle ne couvre pas** : elle vérifie que le module a été appelé, pas qu'il est complet. Un `AjouterMessagerie<Service>()` qui oublierait un gestionnaire ou un sujet passe sans rien dire.

---

## 4. Checklist de portage, par service

1. Créer `Infrastructure/Messaging/Kafka/` avec les cinq dossiers utiles.
2. Déplacer les gestionnaires vers `Consumers/` — ils sont aujourd'hui dans six conventions différentes (tableau §5).
3. Écrire `Configuration/Sujets<Service>.cs` : **uniquement** les sujets dont un gestionnaire existe.
4. Écrire `Producers/EvenementsPublies.cs` : lister les types publiés, appeler `VerifierLesDescripteurs()`.
5. Déplacer `AddOutboxProcessor<T>` vers `Outbox/`, `IConsumerInbox` vers `Inbox/`.
6. Copier `GardeDeCablage`, l'enregistrer dans l'installeur avec `AddHostedService`.
7. Ajouter `Microsoft.Extensions.Hosting.Abstractions` au csproj — **sans `Version=`** (gestion centralisée).
8. Dans `Program.cs` : un seul `builder.Services.AjouterMessagerie<Service>();`.
9. Vérifier que l'installeur ne garde que l'idempotence.

**Point de vigilance §2** : ouvrir `Consumers/` dans `Infrastructure` oblige le projet à référencer les contrats du service émetteur. Pour `user-service` c'était `HBA.Identity.Contracts`, et la frontière invoquée par les commentaires (`UsersBoundaryTests`) n'existait pas dans le dépôt. Vérifier avant de conclure qu'une garde protège quoi que ce soit.

---

## 5. Les 23 services restants, et leur convention actuelle

| Convention actuelle | Services |
|---|---|
| `Application/<Agrégat>/EventHandlers` | identity, notification, media, payment, cart, delivery |
| `Application/Abstractions/EventHandlers` | food-cart, food-order |
| `Infrastructure/Integration` ou `Api/Integration` | catalog, order, seller, promotion, restaurant |
| `Application/Earnings` | wallet |
| directement dans `Program.cs` | order, promotion, restaurant |
| aucun consommateur trouvé | billing, recommendation, review, wishlist, inventory, return-refund, delivery-pricing, driver, route |

Les trois services qui enregistrent dans `Program.cs` sont les plus urgents : rien n'y relie le gestionnaire à un sujet.

---

## 6. Ce qui n'est toujours pas vérifié

**Rien de ce code n'est passé par un compilateur.** Le poste local bloque sur `NETSDK1177` (`apphost is already signed`) dans `HBA.Financial.Api` ; `rm -rf` de son `obj/` et `bin/` le lève.

Tant que la compilation n'a pas tourné, cette convention est une intention, pas un fait.
