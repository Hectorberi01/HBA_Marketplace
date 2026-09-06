# `Grpc/Clients/`

Vide dans **HBA.Engagement.Wishlist.Infrastructure**, et ce n'est pas un manque : ce service n'appelle aucun
voisin en gRPC. Sa table d'autorisations le dit aussi — `FrozenSet<string>.Empty`
ou l'absence d'entree dans `AutorisationsGrpc`.

**Ce qui va ici :** les adaptateurs `I<X>ModuleApi` vers le stub gRPC, un par
voisin appele, avec le `<Protobuf GrpcServices="Client">` qui va avec dans le
csproj.

**Ou ca vit aujourd'hui :** chez CHAQUE appelant, dans son propre
`Infrastructure/Grpc/Clients/` — quinze projets en ont un. Les enveloppes
partagees `shared/contracts/HBA.<X>.Contracts.Grpc` ont ete dissoutes par le
lot D : un adaptateur appartient a celui qui appelle, pas au domaine appele.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
