# Migration Kafka — où on en est

Tests verts : 1055 sur 1055. C'est la première fois depuis le début de la migration.
Ce document dit ce qui est fait, ce qui ne l'est pas, et ce qui casse si on s'arrête là.

---

## 1. Ce qui est fait, et vérifié par la compilation et les tests

**26 modules Kafka**, un par service, à la forme que tu as donnée :
`Infrastructure/Messaging/Kafka/{Configuration,Consumers,Producers,Outbox,Inbox,DependencyInjection.cs}`.
Chaque service déclare ses sujets, ses gestionnaires, ce qu'il publie, son outbox et son inbox — et
`GardeDeCablage` refuse le démarrage si le module n'a pas été appelé.

**43 abonnements au lieu de 20 × 24.** Avant, chaque service s'abonnait aux vingt sujets de la
plateforme faute de liste. C'est le gain direct de l'autonomie que tu vises : un service peut être
déployé seul parce qu'il déclare seul ce qu'il écoute.

**Le déploiement individuel existe.** Entrée `service` dans `workflow_dispatch`,
`up -d --no-deps <service>`. Les sujets `.dlq` sont provisionnés au passage.

**File d'attente morte.** Après N tentatives, l'événement part sur `<sujet>.dlq` avec le sujet, la
partition, l'offset, l'horodatage et la raison en en-têtes — au lieu d'être abandonné. Éprouvé sur un
vrai courtier (Testcontainers), pas seulement lu.

**Les 128 événements ont un descripteur `[HbaEvent]`.** Les listes `SansDescripteur` des 22 fichiers
`EvenementsPublies.cs` sont vides. C'était 80 événements sans descripteur au début du mois.

**101 gestionnaires portent `[NomDeConsommateur]`.** La clé d'idempotence ne dépend plus du nom de
type : la migration l'avait orphelinée sur 96 gestionnaires, ce qui aurait rejoué tous les événements
déjà traités au prochain rééquilibrage — dont six qui créditent des vendeurs.

**La passerelle consomme `TokenRevoked`.** La fenêtre de 30 secondes sur un jeton révoqué tombe à la
latence Kafka. Quatre décisions préviennent enfin leur destinataire : `SellerKybApproved`,
`ProductApproved`, `ProductRejected`, `ReviewRejected`.

---

## 2. Ce qui n'est pas fait — par ordre de ce qui coûte le plus

### 2.1 Rien n'est déployé. 23 commits ne sont pas poussés.

C'est le point qui rend tout le reste théorique. Toute cette migration n'existe que sur ton disque.

### 2.2 Les clés d'inbox doivent être vérifiées EN BASE avant le premier démarrage

`consumer_inbox.consumer_name` contient aujourd'hui les anciens `FullName`. Les
`[NomDeConsommateur]` ont été écrits pour les reproduire à l'identique — mais **ça n'a été vérifié
que par lecture de code**. Un seul écart, et le gestionnaire concerné rejoue tout son historique.

La vérification est une requête, à faire après déploiement et **avant** toute remise à zéro
d'offsets : comparer les `consumer_name` distincts de la table aux 101 valeurs déclarées.

### 2.3 Le nommage canonique n'est pas branché, et ne doit pas l'être en l'état

Le fil porte encore le nom de repli de `KafkaEventNaming` (`payment.captured`). Les 48 noms
canoniques déclarés diffèrent **tous** du repli (`payment.succeeded`). Brancher `HbaEventNaming` sur
le publieur renommerait 128 événements d'un coup : tout consommateur non redéployé cesserait de
reconnaître quoi que ce soit, en silence.

Ce qui manque est une **fenêtre de double lecture** dans `ResolveEventType` : le consommateur accepte
l'ancien nom ET le nouveau pendant le temps du déploiement. Elle n'est pas écrite. Tant qu'elle ne
l'est pas, le nommage canonique reste gelé — et c'est le bon état.

### 2.4 Les `.dlq` n'ont ni consommateur ni alerte

Un message qui échoue N fois est maintenant conservé au lieu d'être perdu. Personne ne le regarde.
Un topic de lettres mortes que personne ne lit est un progrès sur la perte, pas sur le silence.

### 2.5 Deux arbitrages de contrat en attente (lot 1)

- **`DriverVerified` a deux propriétaires** : `driver-service` (le fait réel) et `delivery-service`
  (sa projection locale). Le rôle livreur peut être accordé deux fois, sur deux identifiants que
  l'inbox ne déduplique pas. Recommandation : retirer la publication de `delivery-service`.
- **`HBA.Shipping.Contracts` décrit un service qui n'existe pas.** Trois gestionnaires morts, dont un
  qui devait libérer les gains d'un vendeur par expédition. Les vendeurs sont payés quand même, par
  le chemin `OrderDelivered`. Supprimer ou publier — mais pas laisser du code qui ressemble à une
  fonctionnalité.

### 2.6 Le rattrapage des deux profils manquants

Remise à zéro des offsets `hba-user-service` sur `service.identity.v1` pour rejouer les deux
`user.registered` perdus. **Les profils rattrapés n'auront pas de nom de famille** — l'événement
d'origine ne le portait pas. À faire après 2.2, jamais avant.

### 2.7 Un contrôle qui manquerait au prochain renommage

`tools/HBA.Controls` a 20 contrôles, dont `event-consumers`. Aucun ne refuse un
`IIntegrationEventHandler` sans `[NomDeConsommateur]`. Le prochain gestionnaire écrit sans l'attribut
retombera sur son `FullName`, et le prochain déplacement de fichier réorphelinera sa clé — exactement
la panne qu'on vient de fermer, rouverte par omission.

Même famille : rien ne vérifie qu'un hôte de test qui appelle `UseEnvironment` pose aussi
`ASPNETCORE_ENVIRONMENT`. C'est ce qui a fait échouer les 41 tests de la passerelle.

---

## 3. Ce que la migration a changé au risque, honnêtement

Elle a remplacé « tout écouter » par « une liste ». C'est ce qui permet de déployer un service seul —
et c'est aussi ce qui **déplace la panne du gaspillage vers le silence**. Un sujet oublié dans un
`Sujets<X>.cs` ne produit aucune erreur : le gestionnaire existe, il est enregistré, il n'est jamais
appelé.

Trois défauts de cette forme ont été trouvés pendant la migration — `GetService` au lieu de
`GetServices`, le producteur unique de `DriverVerified`, les 96 clés d'inbox. Aucun n'a été trouvé par
un test : tous par lecture. **Le seul dispositif qui les aurait attrapés seuls est le test qui publie
sur un vrai courtier et vérifie l'effet ailleurs.** Il en existe maintenant quatre. Il en faudrait un
par pont critique.

---

## 4. Réponse courte

**Non, elle n'est pas finie.** Le code est fait et les tests passent ; l'exploitation ne l'est pas.

Dans l'ordre :

1. Pousser les 23 commits.
2. Déployer un service seul — la passerelle ou user-service — pour éprouver `--no-deps`.
3. Vérifier `consumer_inbox.consumer_name` en base.
4. Puis seulement : remise à zéro des offsets user-service.
5. Le reste (2.3 à 2.7) est du travail de fond, sans urgence de production.

Et toujours en attente, hors Kafka : **la rotation des 24 secrets de production circulés en clair**.
