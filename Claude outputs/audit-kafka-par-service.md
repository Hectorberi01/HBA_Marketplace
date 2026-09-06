# Vérification : chaque service gère-t-il correctement ses événements ?

Neuf contrôles sur les 24 modules. Deux défauts trouvés, un corrigé (`7cfb035`), un documenté. Le reste est une liste de faits à arbitrer.

---

## Ce qui est correct

| Contrôle | Résultat |
|---|---|
| Sujet déclaré pour chaque événement consommé | ✅ après correction |
| Sujet déclaré sans aucun gestionnaire (abonnement mort) | ✅ aucun |
| Gestionnaire écrit mais non enregistré | ✅ aucun sur 111 |
| Outbox enregistrée pour chaque service qui publie | ✅ sauf `route-service` (voir plus bas) |
| Inbox enregistrée pour chaque service qui consomme | ✅ 15 sur 15 |
| Groupe de consommation unique par conteneur | ✅ 22 groupes, aucun partagé |
| `KAFKA__PRODUCER` renseigné | ✅ partout sauf `gateway` et `rembg`, qui ne publient pas |
| Résolution des gestionnaires par le répartiteur | ✅ `GetServices`, tous appelés |
| Idempotence | ✅ centrale dans `IntegrationEventDispatcher` |

**Sur l'idempotence** : j'ai d'abord compté « 98 gestionnaires sur 111 sans `HasProcessedAsync` » avant de lire le répartiteur. C'est faux — la garde est **centrale** : `IntegrationEventDispatcher` interroge chaque `IConsumerInbox` enregistrée avant d'appeler un gestionnaire, et marque après. Un gestionnaire n'a rien à faire lui-même.

---

## 1. Corrigé — `identity-service` n'écoutait qu'un producteur sur deux

**Régression introduite par la migration.**

`DriverVerifiedIntegrationEvent` est publié par **deux services** :

- `driver-service` → `service.driver.v1` (`DriverAccountDomainEventHandlers.cs:77`)
- `delivery-service` → `service.delivery.v1` (`DeliveryDomainEventHandlers.cs:135`)

Mon générateur retenait le **premier producteur trouvé**, donc un seul sujet. `GrantDriverRoleHandler` serait resté muet une fois sur deux — un livreur vérifié par `driver-service` n'aurait jamais reçu son rôle. Aucune erreur, aucun journal.

Avant la migration ce bug n'existait pas : le service s'abonnait aux vingt sujets et recevait les deux. **C'est exactement le risque que la migration crée** — remplacer « tout écouter » par une liste, c'est déplacer la panne du gaspillage vers le silence.

Corrigé, et le générateur fait maintenant l'union de tous les producteurs.

**Ce que ça ne règle pas** : un fait métier publié par deux services reste une anomalie de contrat. Si les deux émettent pour le même livreur, le rôle est accordé deux fois — les deux messages ont des identifiants différents, l'inbox ne les déduplique pas. Il faut décider qui est propriétaire de `DriverVerified`.

---

## 2. Documenté — `route-service` publie dans le vide

Il n'a pas de `ModuleDbContext` (routes en mémoire), donc pas d'outbox, donc rien ne draine `IntegrationEventQueue`. `PublishAsync` rend `Task.CompletedTask`.

`RouteCalculated`, `RouteRecalculated`, `RouteDeliveryEtaUpdated` **n'atteignent aucun consommateur**, et personne ne les consomme non plus — donc l'effet est nul aujourd'hui. Son installeur le disait déjà ; mon `EvenementsPublies.cs` les déclarait sans le dire, ce qui laissait croire que le chemin existe. L'encadré est ajouté.

---

## 3. Trois gestionnaires qui ne recevront jamais rien

| Événement | Consommé par | Publié par |
|---|---|---|
| `ShipmentDeliveredIntegrationEvent` | `notification-service`, `wallet-service` | **personne** |
| `ShipmentShippedIntegrationEvent` | `notification-service` | **personne** |

`ReleaseSellerEarningsOnShipmentDeliveredHandler` devait **libérer les gains d'un vendeur**. Il est enregistré, correct, et n'a pas d'émetteur. Deux issues : publier l'événement depuis le service qui expédie, ou supprimer les trois gestionnaires. Laisser en l'état, c'est garder du code qui ressemble à une fonctionnalité.

---

## 4. Quarante-huit événements publiés que personne ne consomme

| Service | Nb | Événements |
|---|---:|---|
| `catalog-service` | 14 | Brand·Created/Requested/RequestApproved, Category·Created, Product·Approved/Archived/Deleted/MediaRemoved/Published/Rejected/Restored/Submitted/Suspended/Unpublished |
| `delivery-pricing-service` | 4 | PricingRule·Created/Updated, Quote·Created/Consumed |
| `media-service` | 3 | MediaDeleted, MediaProcessingFailed, MediaReady |
| `promotion-service` | 3 | CouponUsed, PromotionCreated, PromotionExhausted |
| `route-service` | 3 | Route·Calculated/Recalculated/EtaUpdated (voir §2) |
| `seller-service` | 3 | SellerKybApproved, SellerKybSubmitted, StoreSuspensionLifted |
| `user-service` | 3 | UserAddressCreated, UserDeviceRegistered, UserProfileChanged |
| `food-order-service` | 3 | MealOrder·Cancelled/UnderReview/ResumedAfterReview |
| `driver-service` | 2 | DriverCreated, DriverVehicleUpdated |
| `identity-service` | 2 | TokenRevoked, UserEmailConfirmed |
| `payment-service` | 2 | PaymentCreated, PaymentRefundFailed |
| autres | 8 | CartCheckedOut, FoodCartCheckedOut, FoodOrderReceived, ReviewRejected, SellerOrderRefused, StockReserved, MealOrder… |

Ce n'est pas un défaut en soi — un événement d'audit ou d'analytics n'a pas besoin de consommateur. Mais certains surprennent : **`SellerKybApproved`** n'est écouté par personne, alors que l'approbation d'un dossier KYB devrait au minimum notifier le vendeur. **`TokenRevoked`** avait été ajouté pour l'invalidation des caches de session — rien ne l'écoute.

Chacun de ces 48 coûte une écriture d'outbox, un message Kafka et sa rétention.

---

## 5. Six gestionnaires possiblement rejouables

Le répartiteur marque la trace dans **chaque** inbox enregistrée, mais elle n'est committée que par le module qui appelle `SaveChanges`. Un gestionnaire qui ne sauvegarde rien laisse sa trace en attente : au rejeu, il **refait son effet**.

Candidats — à confirmer un par un, je n'ai pas lu chaque corps de méthode :

- `notification-service` : `OtpDeliveryHandlers`, `MemberEmailHandlers`, `AccountEmailHandlers`
- `promotion-service` : `ReleaseCouponsOnOrderCancelledHandlers`
- `order-service` : `CancelOrderOnFoodOrderRefusedHandlers`
- `delivery-service` : `EnqueueWebhookOnDeliveryEvents`

Les trois premiers envoient des e-mails ou des OTP. Un rebalancement de partition renverrait le code à usage unique. Les 40+ autres gestionnaires de notification passent par `NotificationDispatcher`, qui persiste puis `SaveChangesAsync` — la trace est committée, ils sont protégés.

---

## 6. Ce que ces contrôles ne couvrent pas

Ils lisent le **code**, pas le **fil**. Ils ne disent rien de :

- ce qui est réellement sur les topics en production, ni de la rétention ;
- la compatibilité de schéma entre un producteur déployé et un consommateur plus ancien ;
- l'ordre des messages entre partitions ;
- les 76 événements sans `[HbaEvent]`, qui changeront de nom sur le fil le jour où le nommage canonique sera branché — c'est la liste `SansDescripteur` de chaque service, et elle doit être vidée **avant** ce basculement.

Le seul contrôle qui vaudrait pour le fil, c'est un test d'intégration qui publie et vérifie l'arrivée. Il n'existe pas.
