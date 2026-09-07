# `Persistence/`

Vide dans **HBA.Gateway.Infrastructure**.

**Ce qui va ici :** le DbContext du service, ses configurations EF et ses depots.

**Ou ca vit aujourd'hui :** ici meme, a plat — les sous-dossiers ci-dessous rangent l'existant

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
