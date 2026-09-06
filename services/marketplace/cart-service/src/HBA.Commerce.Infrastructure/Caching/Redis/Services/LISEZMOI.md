# `Caching/Redis/Services/`

Vide dans **HBA.Commerce.Infrastructure**.

**Ce qui va ici :** un cache propre a ce service.

**Ou ca vit aujourd'hui :** `HBA.Shared.Infrastructure.Caching` — `DistributedCacheService` et `NoOpCacheService`

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
