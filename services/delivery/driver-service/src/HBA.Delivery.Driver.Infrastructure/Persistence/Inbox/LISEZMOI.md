# `Persistence/Inbox/`

Vide dans **HBA.Delivery.Driver.Infrastructure** : ce service ne consomme AUCUN evenement d'integration.
Verifie — zero `IIntegrationEventHandler<>`, zero consommateur Kafka.

**Ce qui va ici :** `InboxMessage`, sa configuration EF, le depot et la purge —
le jour ou ce service consommera quelque chose. Il faudra alors AUSSI une
migration qui cree `consumer_inbox` : c'est precisement ce qui manquait ici.

**Ou ca vit aujourd'hui :** dans les services qui consomment, chacun dans son
propre `Persistence/Inbox/`. Seul le port `IConsumerInbox` reste au socle, parce
que le dispatcher partage verifie l'idempotence avant CHAQUE gestionnaire.

**POURQUOI CE DOSSIER A ETE VIDE.** Le lot inbox l'avait rempli dans les 24
services sans demander si le service consomme quelque chose. Resultat : une
table mappee que nulle migration ne cree, et un `InboxCleanupService` qui
l'interrogeait a chaque tick. Le controle `migrations` l'a vu ; ni la
compilation ni les tests ne le pouvaient — le code etait correct, c'est la table
qui manquait.

---

Ce fichier existe parce que git ne versionne pas les dossiers vides : sans lui,
ce dossier n'existerait que sur la machine ou il a ete cree. Le supprimer quand
le dossier recoit du vrai contenu.

La regle de partage du depot, la meme depuis la migration Kafka : **ce dossier
porte la politique de ce service, le socle partage porte le type et le protocole.**
