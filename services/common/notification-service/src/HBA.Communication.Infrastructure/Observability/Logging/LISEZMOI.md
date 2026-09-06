# `Observability/Logging/`

Vide dans **HBA.Communication.Infrastructure**.

**Ce qui va ici :** un enrichisseur de journal propre a ce service.

**Ou ca vit aujourd'hui :** `HbaTelemetry`, pose sur les 24 services d'un coup par `AddHbaService`

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
