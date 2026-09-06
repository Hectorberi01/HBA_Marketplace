# `Observability/HealthChecks/`

Vide dans **HBA.Media.Infrastructure**.

**Ce qui va ici :** les controles de sante des dependances de CE service.

**Ou ca vit aujourd'hui :** `AddDbContextCheck<TDbContext>("database")`, et RIEN D'AUTRE. Redis, Kafka et gRPC ne sont pas verifies : un service dont le consommateur Kafka est mort repond `ready`, et le deploiement individuel le croit sain. C'est un vrai trou.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
