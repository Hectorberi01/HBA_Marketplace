# `Messaging/Kafka/Headers/`

Vide dans **HBA.Financial.Wallet.Infrastructure**.

**Ce qui va ici :** les en-tetes propres a ce service.

**Ou ca vit aujourd'hui :** portes par l'enveloppe partagee — correlation, `traceparent`, type et version

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
