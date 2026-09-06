# `Time/`

Vide dans **HBA.Financial.Payments.Infrastructure**.

**Ce qui va ici :** l'horloge de ce service.

**Ou ca vit aujourd'hui :** N'EXISTE PAS : `DateTime.UtcNow` partout. La bonne reponse n'est PAS d'ecrire `IClock` 26 fois — .NET 9 fournit `TimeProvider`, que les tests remplacent par `FakeTimeProvider`. Ce dossier restera vide, et c'est la reponse.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
