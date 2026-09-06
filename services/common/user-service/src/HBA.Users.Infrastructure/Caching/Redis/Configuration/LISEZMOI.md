# `Caching/Redis/Configuration/`

Vide dans **HBA.Users.Infrastructure**.

**Ce qui va ici :** les reglages Redis de ce service.

**Ou ca vit aujourd'hui :** `RedisOptions` vient du deploiement — une seule source pour l'adresse et le mot de passe

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
