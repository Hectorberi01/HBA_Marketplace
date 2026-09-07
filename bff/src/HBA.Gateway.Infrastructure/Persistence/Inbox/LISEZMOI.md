# `Persistence/Inbox/`

Vide dans **HBA.Gateway.Infrastructure** : ce service ne consomme aucun evenement d'integration.

**Ce qui va ici :** `InboxMessage`, sa configuration EF, le depot et la purge.

**Ou ca vit aujourd'hui :** dans les services qui consomment, chacun dans son
propre `Persistence/Inbox/`. `HBA.Shared.Infrastructure.Inbox` n'existe plus ;
seul le port `IConsumerInbox` reste au socle, parce que le dispatcher partage
verifie l'idempotence avant CHAQUE gestionnaire.

La purge, elle, n'existait nulle part avant ce lot : `consumer_inbox` grossissait
indefiniment dans les quinze services qui consomment.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
