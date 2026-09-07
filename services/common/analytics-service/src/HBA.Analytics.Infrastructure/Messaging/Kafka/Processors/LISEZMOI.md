# `Messaging/Kafka/Processors/`

Vide dans **HBA.Analytics.Infrastructure**.

**Ce qui va ici :** le cablage de l'outbox et de l'inbox.

**Ou ca vit aujourd'hui :** l'inbox seule, dans `Persistence/Inbox/` — entite,
configuration EF, depot et purge —, enregistree par `InboxAnalytics`.

**Il n'y a pas d'`OutboxProcessor` ici**, et ce n'est pas un oubli : ce service ne
publie aucun evenement, donc il n'a aucune file a drainer. Voir
`Persistence/Outbox/LISEZMOI.md`.

**Consequence a connaitre.** Dans les vingt-cinq autres services, la purge de
l'inbox est enregistree par `AjouterLOutboxLocale` — un raccourci qui tient tant
que tout service qui consomme publie aussi. Ici elle est enregistree par
`InboxAnalytics`, sans quoi `consumer_inbox` grossirait indefiniment.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
