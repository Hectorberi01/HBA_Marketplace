# `Persistence/Outbox/`

Vide dans **HBA.Analytics.Infrastructure**, et ce dossier restera vide.

**Ce qui va ici :** le cablage de l'outbox d'un service — l'entite `OutboxMessage`,
sa configuration EF, le processeur qui draine la file vers Kafka.

**Pourquoi il n'y en a pas :** ce service ne publie AUCUN evenement d'integration.
Il consomme trois evenements et rend des lectures HTTP. Poser une table
`outbox_messages` qu'aucun code ne remplit ferait croire, a qui la trouve, que
quelque chose devrait en sortir — et le jour ou rien n'en sortirait, on chercherait
la panne.

C'est la seule difference structurelle entre ce service et les vingt-cinq autres,
et elle est volontaire. `AnalyticsDbContext` n'implemente donc pas
`IOutboxDbContext` et ne surcharge pas `AjouterAuOutbox`, dont le defaut vide est
exactement juste ici.

**Ce que ca deplace ailleurs :** la purge de l'inbox. Dans les autres services,
`InboxCleanupService` est enregistre par `AjouterLOutboxLocale` — un raccourci qui
tient tant que tout service qui consomme publie aussi. Ici elle est enregistree par
`Messaging/Kafka/Inbox`, c'est-a-dire `Persistence/Inbox/InboxAnalytics.cs`.

**Le jour ou ce service publiera** — une alerte de seuil, un rapport quotidien —
il faudra les TROIS gestes dans le meme commit : l'entite et sa configuration, la
migration qui cree la table, et l'enregistrement du processeur. Deux sur trois
donnent une panne silencieuse : la transaction reussit, l'appelant recoit son 200,
et le message n'arrive nulle part.
