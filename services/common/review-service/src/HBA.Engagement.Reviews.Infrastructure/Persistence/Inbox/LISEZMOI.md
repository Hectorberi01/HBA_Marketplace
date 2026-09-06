# `Persistence/Inbox/`

Vide dans **HBA.Engagement.Reviews.Infrastructure**.

**Ce qui va ici :** le cablage de la garde anti-doublon.

**Ou ca vit aujourd'hui :** `HBA.Shared.Infrastructure.Inbox` — `ConsumerInboxEntry` et `EfConsumerInbox`. MANQUE ENCORE : rien ne purge `consumer_inbox`, contrairement a l'outbox et a l'idempotence qui ont leur purger.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
