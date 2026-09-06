# `Grpc/Services/`

Vide dans **HBA.Merchants.Infrastructure**.

**Ce qui va ici :** rien — le serveur gRPC est la SURFACE du service.

**Ou ca vit aujourd'hui :** `<Service>.Api/Grpc/Services/` depuis le lot B. Ce dossier existe pour que l'arborescence soit la meme partout ; le serveur, lui, n'est pas un detail d'infrastructure.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
