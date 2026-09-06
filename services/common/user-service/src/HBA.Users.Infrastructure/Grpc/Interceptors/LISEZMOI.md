# `Grpc/Interceptors/`

Vide dans **HBA.Users.Infrastructure**.

**Ce qui va ici :** un intercepteur propre a ce service, s'il en a un.

**Ou ca vit aujourd'hui :** `HBA.Shared.Hosting.Grpc` — disjoncteur, identite interne, correlation, traduction des erreurs. Uniformes sur les 17 clients, verifie.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
