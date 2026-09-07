# `Messaging/Kafka/Producers/`

Vide dans **HBA.Analytics.Infrastructure**, et ce dossier restera vide.

**Ce qui va ici :** `EvenementsPublies.cs` — ce que ce service publie, DECLARE.

**Pourquoi il n'y en a pas :** ce service ne publie AUCUN evenement d'integration.
Il consomme trois evenements et rend des lectures HTTP. Le fichier engendre par
l'arborescence disait « Ou ca vit aujourd'hui : ici », ce qui est faux pour ce
service — d'ou cette version.

Un `EvenementsPublies.cs` vide se lirait comme une liste qu'on aurait oublie de
remplir, et `AjouterMessagerieAnalytics()` n'appelle donc pas
`VerifierLesDescripteurs()` : il n'y a rien a verifier.

**Ce qui reste vrai partout ailleurs :** `Outbox/`, `Processors/` et `Retry/` sont
absents pour la meme raison. Voir `Persistence/Outbox/LISEZMOI.md`, qui porte
l'argumentaire complet et ce qu'il faudra faire le jour ou ce service publiera.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
