# Migration Kafka — les 23 services restants

Commit `a903b5f`. 249 fichiers, 24 modules, +8139 / −688.

Chaque service a maintenant `Infrastructure/Messaging/Kafka/` avec `Configuration/`, `Consumers/`, `Producers/`, `Outbox/`, `Inbox/` et un `DependencyInjection.cs` unique. Le générateur qui l'a fait est dans `tools/migration-kafka/migrer.py` et reste rejouable.

---

## 1. Ce que ça change, en chiffres

| Service | Sujets écoutés | Gestionnaires | Publié avec `[HbaEvent]` | Publié sans |
|---|---:|---:|---:|---:|
| `billing-service` | 0 | 0 | 0 | 0 |
| `identity-service` | 3 | 5 | 3 | 5 |
| `media-service` | 1 | 1 | 3 | 0 |
| `notification-service` | 10 | 48 | 0 | 1 |
| `payment-service` | 2 | 3 | 5 | 0 |
| `promotion-service` | 2 | 2 | 3 | 0 |
| `recommendation-service` | 0 | 0 | 0 | 0 |
| `review-service` | 0 | 0 | 0 | 3 |
| `user-service` | 1 | 3 | 3 | 0 |
| `wallet-service` | 3 | 6 | 2 | 0 |
| `wishlist-service` | 0 | 0 | 0 | 0 |
| `delivery-pricing-service` | 0 | 0 | 4 | 0 |
| `delivery-service` | 2 | 8 | 1 | 7 |
| `driver-service` | 0 | 0 | 5 | 0 |
| `route-service` | 0 | 0 | 3 | 0 |
| `food-cart-service` | 1 | 1 | 0 | 1 |
| `food-order-service` | 3 | 6 | 0 | 6 |
| `restaurant-service` | 4 | 5 | 0 | 12 |
| `cart-service` | 1 | 1 | 0 | 1 |
| `catalog-service` | 2 | 9 | 10 | 4 |
| `inventory-service` | 0 | 0 | 0 | 3 |
| `order-service` | 5 | 10 | 0 | 7 |
| `return-refund-service` | 0 | 0 | 0 | 2 |
| `seller-service` | 3 | 3 | 0 | 24 |

**43 abonnements au lieu de 20 × 24.** Avant, chaque service s'abonnait aux vingt sujets de la plateforme faute d'une liste. `notification-service` en écoute légitimement dix ; `order-service` cinq ; `user-service` un.

---

## 2. Trois défauts trouvés en migrant

### `GetService<AbonnementsKafka>` n'aurait branché qu'un module sur trois

Le socle lisait `sp.GetService<AbonnementsKafka>()?.Sujets` — **le dernier enregistré, pas l'union**. Trois hôtes composent plusieurs modules dans un seul processus : `HBA.Financial.Api` (payments, wallet, billing), `HBA.Engagement.Api` (reviews, recommendations, wishlist), `HBA.Communication.Api`.

Avec `GetService`, deux modules sur trois n'auraient **jamais été abonnés** : gestionnaires enregistrés, corrects, jamais appelés, aucune erreur. Exactement la panne de `user_profiles`, en trois exemplaires. Corrigé en `GetServices(...).SelectMany(...).Distinct()`.

C'est un défaut que la migration a **créé** et que la migration a rattrapé — il n'existait pas avant, puisque personne ne remplissait la liste.

### `Shipment*` : deux événements consommés, publiés par personne

`ShipmentShippedIntegrationEvent` et `ShipmentDeliveredIntegrationEvent` sont consommés par `notification-service` (deux gestionnaires) et `ShipmentDelivered` aussi par `wallet-service` — **et aucun service du dépôt ne les publie**. Les gestionnaires existent, sont enregistrés, et ne seront jamais appelés. Aucun sujet n'a été ajouté pour eux : il n'y a rien à écouter. C'est écrit dans le `Sujets*.cs` des deux services.

Trois gestionnaires morts, dont un qui devait créditer un vendeur.

### `delivery-pricing-service` allait vider son outbox deux fois

Son enregistrement d'outbox vit dans `DeliveryPricingInfrastructureModule.cs`, pas dans un `*ModuleInstaller.cs` — le générateur ne l'a donc pas retiré, tout en en créant un dans le module. Deux `OutboxProcessor` sur la même table, en concurrence. Trouvé au contrôle statique, corrigé.

---

## 3. Le fait qui gouverne le reste : `[HbaEvent]` couvre 42 événements sur 118

**76 événements publiés n'ont pas de descripteur.** `seller-service` en publie 24 sans, `restaurant-service` 12, `order-service` 7.

`user-service` avait une vérification qui **refuse le démarrage** quand un événement publié n'a pas `[HbaEvent]`. Générer la même chose partout aurait empêché `identity-service`, `seller-service`, `order-service` et douze autres de démarrer.

Chaque `EvenementsPublies.cs` porte donc **deux listes** :

- `Types` — les événements avec descripteur ; la vérification les contrôle et lève au démarrage si l'un le perd ;
- `SansDescripteur` — les autres, **nommés un par un**, avec la raison écrite : les faire échouer arrêterait un service qui tourne aujourd'hui, pour un défaut sans effet tant que `HbaEventNaming` n'est pas branché sur le fil.

Ce que ça coûte déjà est écrit là aussi : leur nom et leur sujet tombent sur le repli de `KafkaEventNaming`. **Le jour où le nommage canonique sera branché, ces 76 événements changeront de nom sur le fil.** C'est cette liste qu'il faut vider *avant* ce basculement, pas après.

---

## 4. Ce que la migration n'a pas fait

**Un service sans consommateur s'abonne encore à tout.** Sept services ont zéro sujet, et le consommateur partagé traite « liste vide » comme « pas de liste » : il se rabat sur les vingt sujets. Sans gestionnaire, l'effet métier est nul, mais le trafic reste. Distinguer les deux cas touche `KafkaIntegrationEventConsumer`, donc les 24 services à la fois — pas fait ici, écrit dans chaque `Sujets*.cs` concerné.

**`MessagingModuleInstaller` n'a pas été porté.** C'est le second module de l'hôte `HBA.Communication.Api` — la messagerie interne. Il ne consomme rien et publie un seul événement : un module complet lui aurait donné un `Sujets` vide, un `Consumers/` vide et un second `AjouterMessagerie…()` dans le même `Program.cs`. L'exception et son coût sont écrits dans son installeur.

**Rien n'a été compilé.** 249 fichiers, dont 111 enregistrements de gestionnaires déplacés et une soixantaine de fichiers changés d'espace de noms. Les contrôles passés sont statiques : accolades équilibrées sur chaque fichier touché, chaque classe citée dans un `DependencyInjection` retrouvée dans le `Consumers/` du même service, un seul espace de noms par dossier `Consumers/`, aucune collision de nom de classe, aucun enregistrement de gestionnaire resté hors module, `GardeDeCablage` enregistrée dans les 24 services, et chaque `Program.cs` appelant autant de `AjouterMessagerie…()` qu'il installe de modules.

C'est ce qu'on peut vérifier sans compilateur. Ce n'est pas une compilation.

---

## 5. Ce que le générateur a préservé, et pourquoi ça comptait

Ce dépôt met la raison d'un enregistrement dans le commentaire qui le précède — « SANS CES DEUX LIGNES, SUSPENDRE UN VENDEUR NE RETIRE RIEN (ISSUE-025) », quatorze lignes sur la rupture de stock dans `catalog-service`.

Déplacer la ligne seule aurait produit deux mensonges d'un coup : un commentaire qui explique du code absent, et du code sans sa raison. Le générateur remonte le bloc de commentaire contigu au-dessus de chaque enregistrement et le fait voyager avec lui. Les fichiers déplacés dont l'en-tête disait « CE FICHIER A DÉMÉNAGÉ VERS `Infrastructure/Integration` » ont vu la phrase mise à jour plutôt que laissée à indiquer une pièce vide.

---

## 6. La suite

1. **Compiler.** C'est la seule chose qui compte maintenant. `rm -rf` de `obj/` et `bin/` sur `HBA.Financial.Api` lève le `NETSDK1177`.
2. Vider `SansDescripteur`, service par service, **avant** de brancher le nommage canonique.
3. Décider du sort des trois gestionnaires `Shipment*` : soit publier l'événement, soit les supprimer.
4. Le déploiement individuel — entrée `service` dans `workflow_dispatch`, `up -d --no-deps <service>` — toujours à faire.
