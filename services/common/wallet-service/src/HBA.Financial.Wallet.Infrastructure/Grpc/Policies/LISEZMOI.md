# `Grpc/Policies/`

Vide dans **HBA.Financial.Wallet.Infrastructure**.

**Ce qui va ici :** la resilience des appels sortants de ce service.

**Ou ca vit aujourd'hui :** disjoncteur par service appele, et l'echeance par defaut de 5 s posee par `InternalCallClientInterceptor`. La surcharge se declare dans `Grpc/Configuration/DestinationsGrpc.cs`.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
