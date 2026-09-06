# `Grpc/Mappers/`

Vide dans **HBA.Food.Order.Infrastructure**.

**Ce qui va ici :** les traductions proto <-> enregistrements de contrats, POSEES
A COTE DU CLIENT OU DU SERVEUR QUI S'EN SERT.

**Ou ca vit aujourd'hui :** chez chaque appelant qui en a besoin
(`Infrastructure/Grpc/Mappers/`) et chez chaque serveur (`<Service>.Api/Grpc/Mappers/`).
Les enveloppes partagees `shared/contracts/HBA.<Domaine>.Contracts.Grpc`, qui
portaient une traduction unique par domaine, ont ete dissoutes par le lot D.

**Pourquoi ce dossier est vide ici :** ce projet n'a ni client gRPC a nourrir,
ni serveur a servir — ou son serveur vit dans son projet `.Api`, avec ses
propres mappings.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
