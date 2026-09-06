# `Persistence/Outbox/`

Vide dans **HBA.Gateway.Infrastructure** : ce service ne publie rien par outbox.

**Ce qui va ici :** `OutboxMessage`, sa configuration EF, le depot, la purge,
l'enregistrement et le publieur d'evenements d'integration.

**Ou ca vit aujourd'hui :** dans les VINGT-QUATRE services qui publient, chacun
dans son propre `Persistence/Outbox/`. `HBA.Shared.Infrastructure.Outbox`
n'existe plus : le drain reste au socle (`ModuleDbContext`, `DrainageDOutbox`,
seule lecture de `OUTBOX_ENABLED`), parce qu'un evenement doit partir dans la
MEME transaction que le fait qui l'a produit — cette regle-la ne se duplique pas.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
