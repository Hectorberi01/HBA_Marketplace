# Plan de correction — événements Kafka

Ordonné par ce qui casse en production si on ne le fait pas, pas par difficulté. Chaque lot dit ce qu'il ne couvre pas.

---

## Lot 0 — avant de déployer quoi que ce soit

La compilation passe. Rien d'autre n'a été vérifié.

| # | Action | Pourquoi maintenant |
|---|---|---|
| 0.1 | `dotnet test` sur toute la solution | 111 enregistrements de gestionnaires ont changé d'assembly. Les tests d'intégration montent un hôte complet : ils exerceront `GardeDeCablage`, l'union des `AbonnementsKafka` et les inbox multiples. C'est le seul filet qui existe avant la prod. |
| 0.2 | Vérifier au démarrage local qu'aucun service ne lève `GardeDeCablage` | Un `AjouterMessagerie…()` oublié dans un hôte composé se voit là, pas en CI. |
| 0.3 | Lire une fois les journaux d'un service au démarrage : la ligne « abonnement à N sujets » | Confirme que la liste par service est bien prise en compte et qu'on n'est pas retombé sur les vingt sujets. |

**Ce que ça ne couvre pas** : aucun test ne publie ni ne consomme un vrai message Kafka aujourd'hui (lot 6).

---

## Lot 1 — deux arbitrages de contrat, petits diffs, effet immédiat

### 1.1 `DriverVerified` a deux propriétaires — en garder un

**Constat.** `driver-service` le publie depuis `DriverAccountVerifiedDomainEvent` (la vérification du dossier — le fait réel). `delivery-service` le republie depuis son agrégat `DeliveryDriver` (`DeliveryDriver.cs:254`), c'est-à-dire depuis sa **projection locale** de ce que driver-service lui a déjà dit.

**Recommandation** : `driver-service` est propriétaire. Retirer `DriverVerifiedDomainEventHandler` de `delivery-service` — le domain event interne peut rester, c'est la *publication* qui doit partir. Puis retirer `service.delivery.v1` des sujets d'`identity-service`, qui redevient inutile.

**Effet si on ne fait rien** : le rôle livreur peut être accordé deux fois pour un même livreur, sur deux messages d'identifiants différents que l'inbox ne déduplique pas.

**Ce que ça ne couvre pas** : je n'ai pas vérifié si un autre service dépend de la republication par delivery. À confirmer avant de supprimer.

### 1.2 `HBA.Shipping.Contracts` décrit un service qui n'existe pas

**Constat.** Aucun `shipping-service` dans le dépôt. Le projet de contrats existe, trois événements y sont déclarés, **rien ne les publie**, et trois gestionnaires les attendent.

Le plus sérieux, `ReleaseSellerEarningsOnShipmentDeliveredHandler`, devait libérer les gains d'un vendeur à la livraison de SON expédition. Il n'a jamais tourné. **Les vendeurs sont payés quand même** : `ReleaseEarningsOnOrderDeliveredHandler` écoute `OrderDelivered` et se décrit lui-même comme le « filet de sécurité complémentaire du handler par expédition » — il libère tout ce qui reste `Accrued`.

**Recommandation** : supprimer les trois gestionnaires et le projet `HBA.Shipping.Contracts`, avec un commentaire dans le handler `OrderDelivered` disant qu'il n'est plus un filet mais le chemin unique. Coût : une dizaine de fichiers.

**Ce qu'on perd** : la libération **par expédition** dans une commande multi-vendeur. Aujourd'hui un vendeur qui livre en premier attend que toute la commande soit livrée. C'est le comportement actuel, pas une régression — mais c'est une fonctionnalité à re-créer si le multi-vendeur partiel devient un besoin. **Décision à prendre avant de supprimer.**

---

## Lot 2 — six gestionnaires rejouables, dont trois qui envoient des secrets

Le répartiteur marque la trace dans chaque inbox, mais elle n'est committée que par le module qui appelle `SaveChanges`. Un gestionnaire qui ne sauvegarde rien refera son effet au rejeu.

| Priorité | Gestionnaire | Effet d'un rejeu |
|---|---|---|
| **Haute** | `OtpDeliveryHandlers` (notification) | Renvoie un code à usage unique. Un rebalancement de partition suffit. |
| **Haute** | `AccountEmailHandlers` (notification) | Renvoie le lien de vérification / de réinitialisation. |
| Moyenne | `MemberEmailHandlers` (notification) | Renvoie une invitation. |
| Moyenne | `ReleaseCouponsOnOrderCancelledHandlers` (promotion) | Libère un coupon déjà libéré — à vérifier, peut être naturellement idempotent. |
| Moyenne | `EnqueueWebhookOnDeliveryEvents` (delivery) | Ré-enfile un webhook déjà envoyé au partenaire. |
| Basse | `CancelOrderOnFoodOrderRefusedHandlers` (order) | La commande est déjà annulée ; probablement inoffensif. |

**Correction** : faire écrire quelque chose au gestionnaire dans le contexte de son module — la trace part alors avec le `SaveChanges`. Pour les trois de notification, `NotificationDispatcher` le fait déjà : il suffit de passer par lui plutôt que par l'envoi direct. À vérifier fichier par fichier avant de généraliser ; je n'ai pas lu les six corps de méthode.

---

## Lot 3 — les 48 événements publiés que personne ne consomme

À trier en trois piles, pas à corriger en bloc.

**À brancher** — quelqu'un devrait les écouter, et personne ne le fait :

- `SellerKybApproved` — un dossier KYB approuvé ne notifie pas le vendeur.
- `TokenRevoked` — publié pour l'invalidation des caches de session ; le gateway ne l'écoute pas.
- `UserEmailConfirmed` — aucun effet en aval.

**À supprimer** — publiés, jamais utiles, et chacun coûte une écriture d'outbox, un message et sa rétention : les 14 de `catalog-service` (cycle de vie produit et marque), les 4 de `delivery-pricing`, les 3 de `route-service` (qui ne partent pas, lot 5.1).

**À garder** — audit et analytics futurs : `PaymentCreated`, `StockReserved`, `CartCheckedOut`, `MediaReady`…

**Ce que ce tri demande** : ton avis métier, pas le mien. Je peux produire la liste annotée avec, pour chaque événement, le fichier qui le publie et la commande qui le déclenche.

---

## Lot 4 — la dette `[HbaEvent]` : 76 événements, et un ordre à respecter

`HbaEventNaming` n'est **pas branché sur le fil** aujourd'hui : le nom d'événement et le sujet viennent du repli de `KafkaEventNaming`. C'est pour ça que les 76 événements sans descripteur ne cassent rien.

**Le jour où le nommage canonique sera branché, ces 76 changeront de nom sur le fil.** Un consommateur déployé avant le producteur ne reconnaîtra plus rien — « événement reçu et NON RECONNU », en silence.

**L'ordre est le sujet, pas le travail** :

1. Ajouter `[HbaEvent]` aux 76, service par service, en vidant la liste `SansDescripteur` de chaque `EvenementsPublies.cs`. Le nom canonique doit **reproduire** le nom du repli, sinon c'est un changement de contrat déguisé.
2. Vérifier qu'aucune liste `SansDescripteur` n'est plus peuplée — c'est mécanique, un contrôle suffit.
3. **Seulement ensuite**, brancher `HbaEventNaming` sur le publieur et le consommateur, producteurs d'abord.

Sauter l'étape 2 fait de l'étape 3 une panne silencieuse à l'échelle de la plateforme.

---

## Lot 5 — les gaps structurels

| # | Constat | Correction | Portée |
|---|---|---|---|
| 5.1 | `route-service` publie dans une file mémoire que rien ne draine | Lui donner un `ModuleDbContext` + outbox, ou retirer les trois publications | 1 service |
| 5.2 | Sept services sans consommateur retombent sur les vingt sujets (« liste vide » = « pas de liste ») | Distinguer les deux cas dans `KafkaIntegrationEventConsumer` | Touche les 24 services d'un coup — à faire en une fois, pas au fil de l'eau |
| 5.3 | `MessagingModuleInstaller` (messagerie interne) n'a pas de module Kafka | Le porter, ou assumer l'exception qui est déjà écrite dans son installeur | 1 fichier |
| 5.4 | Aucune DLQ côté consommateur : après N tentatives, l'événement est abandonné et perdu | Un topic `*.dlq` et un consommateur qui l'archive | Socle partagé |
| 5.5 | Le déploiement est tout-ou-rien (`up -d --remove-orphans`) | Entrée `service` dans `workflow_dispatch` + `up -d --no-deps <service>` | Pipeline de prod |

Le 5.4 est celui qui fait le plus mal aujourd'hui : c'est par là que le message `user.registered` a disparu, et rien n'en garde trace.

---

## Lot 6 — le seul contrôle qui vaudrait vraiment

Tout ce qui précède lit le **code**. Rien ne lit le **fil**.

Un test d'intégration qui démarre Kafka (Testcontainers), publie un événement depuis le service A et vérifie l'effet dans le service B fermerait d'un coup : les sujets, le nommage, l'enveloppe, l'idempotence et la compatibilité de schéma. C'est le seul dispositif qui aurait attrapé, sans intervention humaine, chacune des pannes de ce mois — le préfixe `livraison` vs `service`, la clé SEC1, le `GetService` au lieu de `GetServices`, et `DriverVerified` à deux producteurs.

Coût réel : une journée pour le premier, une heure pour chaque suivant.

---

## Ordre proposé

1. **Lot 0** — tests, aujourd'hui.
2. **Lot 1.1** — `DriverVerified`, dès que tu confirmes le propriétaire.
3. **Lot 2, priorité haute** — les deux gestionnaires qui renvoient des secrets.
4. **Déploiement**, puis remise à zéro des offsets `hba-user-service` pour rattraper les profils.
5. **Lot 5.4** — la DLQ, avant d'ajouter du trafic.
6. **Lot 1.2 et 3** — quand tu auras tranché le métier.
7. **Lot 4** — avant tout basculement de nommage, jamais après.
8. **Lot 6** — dès qu'il y a une journée à y mettre.

Et toujours en attente, hors Kafka : **la rotation des 24 secrets de production** circulés en clair.

---

## Ce que je peux faire sans autre décision

Les lots 0.3, 5.3 et la liste annotée du lot 3. Le reste demande un arbitrage : qui possède `DriverVerified`, faut-il garder la libération des gains par expédition, et quels événements morts on supprime.
