# `Idempotency/`

Vide dans **HBA.Users.Infrastructure**.

**Ce qui va ici :** le cablage de l'idempotence de ce service.

**Ou ca vit aujourd'hui :** `HBA.Shared.Infrastructure.Idempotency` — store, entite, purger. Elle protege AUSSI les routes HTTP annotees `AllowIdempotency()`, qui n'ont rien a voir avec Kafka : elle reste enregistree dans `<Service>ModuleInstaller`.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
