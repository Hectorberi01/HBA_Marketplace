# `Persistence/Migrations/`

Vide dans **HBA.Order.Infrastructure**.

**Ce qui va ici :** les migrations EF de ce service.

**Ou ca vit aujourd'hui :** A LA RACINE du projet dans 20 services. Les deplacer touche l'outillage `dotnet ef`, pas seulement des fichiers : sans `--output-dir`, la migration suivante revient a la racine. C'est un lot a part.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
