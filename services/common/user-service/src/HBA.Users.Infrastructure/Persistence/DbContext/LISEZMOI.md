# `Persistence/DbContext/`

Vide dans **HBA.Users.Infrastructure**.

**Ce qui va ici :** `<Service>DbContext.cs`.

**Ou ca vit aujourd'hui :** a plat dans `Persistence/` dans les 25 services qui en ont un

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
