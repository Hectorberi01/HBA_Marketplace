# `Messaging/Kafka/Retry/`

Vide dans **HBA.Engagement.Recommendations.Infrastructure**.

**Ce qui va ici :** la politique de rejeu de ce service.

**Ou ca vit aujourd'hui :** `OutboxRetryPolicy` (10 tentatives, backoff) et la file de lettres mortes `<sujet>.dlq`

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
