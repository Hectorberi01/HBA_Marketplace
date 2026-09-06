# `Messaging/Kafka/Serialization/`

Vide dans **HBA.Delivery.Route.Infrastructure**.

**Ce qui va ici :** un convertisseur propre a ce service, s'il en a un.

**Ou ca vit aujourd'hui :** `HbaEventEnvelope` — l'enveloppe est le FORMAT SUR LE FIL. Deux implementations, c'est deux formats, et un consommateur qui ne reconnait plus ce qu'un producteur ecrit : la panne `livraison.*` / `service.*` de ce mois-ci.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
