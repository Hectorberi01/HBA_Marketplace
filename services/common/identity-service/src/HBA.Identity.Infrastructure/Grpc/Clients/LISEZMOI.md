# `Grpc/Clients/`

Vide dans **HBA.Identity.Infrastructure**.

**Ce qui va ici :** les adaptateurs `I<X>ModuleApi` vers le stub gRPC.

**Ou ca vit aujourd'hui :** `shared/contracts/HBA.<X>.Contracts.Grpc`, un par domaine. Un adaptateur implemente l'interface ENTIERE : une copie par consommateur serait integrale.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
