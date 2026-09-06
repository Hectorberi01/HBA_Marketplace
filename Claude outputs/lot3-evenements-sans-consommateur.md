# Lot 3 — les 48 événements publiés que personne ne consomme

Liste annotée pour arbitrage. Pour chaque événement : le service qui le publie, le déclencheur exact, et s'il porte `[HbaEvent]`.

Trois piles. **Rien n'a été supprimé ni branché** — le tri est métier.

---

## Pile A — à brancher : deux trous avec un effet réel

### `TokenRevoked` — une fenêtre de 30 secondes sur les jetons révoqués

**Publié par** `identity-service`, depuis `LogoutByRefreshTokenCommandHandler`. Porte `UserId`, `Reason` (`LOGOUT`, `ADMIN_REVOKE`, `PASSWORD_CHANGED`, `SUSPENDED`) et le nombre de jetons révoqués. `[HbaEvent]` ✅

**Ce qui se passe aujourd'hui.** `TokenRevocationMiddleware` du gateway interroge identity par gRPC, puis **met le verdict en cache 30 secondes** (`TokenRevocationOptions.CacheSeconds = 30`). Un jeton révoqué reste donc accepté jusqu'à 30 secondes après la révocation — y compris sur `ADMIN_REVOKE` et `SUSPENDED`, c'est-à-dire exactement les cas où l'on veut couper l'accès *tout de suite*.

L'événement existe, il porte l'information, et personne ne l'écoute.

**Ce que ça coûterait** : un consommateur dans le gateway qui invalide l'entrée de cache du `UserId` concerné. La fenêtre tombe à la latence Kafka. Bonus : moins d'appels gRPC vers identity, donc moins de couplage — dans le sens du découpage que tu vises.

**Ce que ça ne couvre pas** : le gateway devrait consommer du Kafka, ce qu'il ne fait pas aujourd'hui (`KAFKA__CONSUMERGROUP` absent de son bloc compose). C'est le vrai coût de ce branchement, et il mérite d'être pesé : un premier consommateur dans le gateway, c'est une dépendance nouvelle sur le bus pour le composant en frontal.

### `SellerKybApproved` — le refus prévient, l'approbation non

**Publié par** `seller-service`, depuis `SellerKybVerifiedDomainEventHandler`. `[HbaEvent]` ❌

**L'asymétrie, vérifiée.** `SellerKybRejectedIntegrationEvent` **est** consommé — `SellerKybRejectedNotificationHandler` dans notification-service. `SellerKybApprovedIntegrationEvent` ne l'est par personne.

Et l'approbation n'est pas suivie automatiquement d'une activation : dans l'agrégat `Seller`, `SellerKybVerifiedDomainEvent` (ligne 417) et `SellerActivatedDomainEvent` (ligne 504) sont levés par deux méthodes différentes. Un vendeur dont le dossier vient d'être validé, mais pas encore activé, **n'apprend rien** — alors que celui dont le dossier est refusé reçoit un message.

**Ce que ça coûterait** : un gestionnaire de notification, sur le modèle exact de celui du refus.

---

## Pile B — à supprimer : publiés, jamais utiles, et facturés à chaque fois

Chacun coûte une ligne d'outbox, un message Kafka et sa rétention. Aucun n'a de consommateur, aucun n'est réclamé par un besoin connu.

### `catalog-service` — 14 événements, le cycle de vie produit et marque

| Événement | `[HbaEvent]` | Déclencheur |
|---|:--:|---|
| `BrandCreated` | ❌ | `BrandCreatedDomainEvent` |
| `BrandRequested` | ✅ | `BrandRequestedDomainEvent` |
| `BrandRequestApproved` | ✅ | `BrandRequestApprovedDomainEvent` |
| `CategoryCreated` | ❌ | `CategoryCreatedDomainEvent` |
| `ProductSubmitted` | ✅ | `ProductSubmittedForReviewDomainEvent` |
| `ProductApproved` | ✅ | `ProductApprovedDomainEvent` |
| `ProductRejected` | ✅ | `ProductRejectedDomainEvent` |
| `ProductPublished` | ✅ | `ProductPublishedDomainEvent` |
| `ProductUnpublished` | ✅ | `ProductUnpublishedDomainEvent` |
| `ProductSuspended` | ✅ | `ProductSuspendedDomainEvent` |
| `ProductArchived` | ✅ | `ProductArchivedDomainEvent` |
| `ProductRestored` | ✅ | `ProductRestoredDomainEvent` |
| `ProductDeleted` | ❌ | `DeleteProductCommand` |
| `ProductMediaRemoved` | ❌ | `ProductMediaRemovedDomainEvent` |

Tous dans `HBA.Catalog.Application/{Brands,Categories,Products}/EventHandlers/`.

**Réserve honnête** : `ProductRejected` et `ProductApproved` ressemblent à `SellerKybRejected/Approved` — un vendeur voudrait sans doute savoir que son produit a été refusé. Si c'est le cas, ces deux-là passent en pile A et non B. **C'est la question que je te pose sur ce bloc.**

### `delivery-pricing-service` — 4 événements

`DeliveryPricingRuleCreated`, `DeliveryPricingRuleUpdated`, `DeliveryQuoteCreated`, `DeliveryQuoteConsumed` — tous `[HbaEvent]` ✅, tous publiés depuis `EfDeliveryPricingStore` (donc depuis l'infrastructure, pas depuis un fait métier). `DeliveryQuoteConsumed` en particulier est un détail d'implémentation du devis.

### `route-service` — 3 événements qui ne partent pas

`RouteCalculated`, `RouteRecalculated`, `RouteDeliveryEtaUpdated`. Rappel du lot 5.1 : ce service n'a pas d'outbox, donc ces trois-là n'atteignent **rien** de toute façon. Les supprimer ne change aucun comportement observable — c'est le cas le plus simple de la liste.

### Les orphelins isolés

| Service | Événement | `[HbaEvent]` | Déclencheur |
|---|---|:--:|---|
| `cart-service` | `CartCheckedOut` | ❌ | `CartCheckedOutDomainEvent` |
| `food-cart-service` | `FoodCartCheckedOut` | ❌ | `FoodCartCheckedOutDomainEvent` |
| `food-order-service` | `MealOrderCancelled` | ❌ | `MealOrderCancelledDomainEvent` |
| `food-order-service` | `MealOrderUnderReview` | ❌ | `MealOrderUnderReviewDomainEvent` |
| `food-order-service` | `MealOrderResumedAfterReview` | ❌ | idem |
| `restaurant-service` | `FoodOrderReceived` | ❌ | `FoodOrderReceivedDomainEvent` |
| `review-service` | `ReviewRejected` | ❌ | `ReviewRejectedDomainEvent` |
| `order-service` | `SellerOrderRefused` | ❌ | `SellerOrderRefusedDomainEvent` |
| `seller-service` | `SellerKybSubmitted` | ❌ | `SellerKybSubmittedDomainEvent` |
| `seller-service` | `StoreSuspensionLifted` | ❌ | `StoreSuspensionLiftedDomainEvent` |
| `inventory-service` | `StockReserved` | ❌ | `StockReservedDomainEvent` |
| `identity-service` | `UserEmailConfirmed` | ❌ | `UserEmailConfirmedDomainEvent` |

**Trois réserves dans ce bloc**, à trancher plutôt qu'à supprimer d'office :

- `MealOrderCancelled` et `SellerOrderRefused` — leurs équivalents côté marketplace (`OrderCancelled`, `SellerKybRejected`) sont consommés. L'asymétrie mérite un regard.
- `ReviewRejected` — `ReviewPublished` est consommé par notification et seller. Le refus, non. Même motif que le KYB.
- `StoreSuspensionLifted` — `StoreSuspended`, `StoreClosed` et `StoreOpened` sont tous consommés par catalog. La levée de suspension ne l'est pas, et le commentaire du code dit que c'est **volontaire** : « lever la sanction repasse la boutique en `Closed`, pas en `Open` ». Donc à garder tel quel, pas à supprimer — le publier sans consommateur est le comportement voulu. Il faudrait juste que ce soit écrit du côté du producteur aussi.

---

## Pile C — à garder : audit, analytics, ou consommateur imminent

| Service | Événements | Raison |
|---|---|---|
| `payment-service` | `PaymentCreated`, `PaymentRefundFailed` | Piste d'audit d'un flux financier. `PaymentRefundFailed` est un échec : le supprimer effacerait la seule trace hors journaux. |
| `media-service` | `MediaReady`, `MediaDeleted`, `MediaProcessingFailed` | Le pipeline d'images est asynchrone ; catalog aura besoin de `MediaReady` le jour où la publication d'un produit attendra ses visuels. |
| `driver-service` | `DriverCreated`, `DriverVehicleUpdated` | Cycle de vie du livreur, utile à une projection future côté delivery. |
| `promotion-service` | `CouponUsed`, `PromotionCreated`, `PromotionExhausted` | `CouponUsed` est la matière première d'une analyse de l'efficacité des promotions. |
| `user-service` | `UserAddressCreated`, `UserDeviceRegistered`, `UserProfileChanged` | `UserDeviceRegistered` sert au ciblage des notifications push le jour où elles existeront. |

---

## Ce que ce tri révèle, au-delà du tri

**Un motif revient six fois** : l'événement « positif » et l'événement « négatif » d'une même décision ne sont pas traités pareil.

| Consommé | Ignoré |
|---|---|
| `SellerKybRejected` | `SellerKybApproved` |
| `ReviewPublished` | `ReviewRejected` |
| `OrderCancelled` | `MealOrderCancelled` |
| `StoreSuspended`, `StoreClosed`, `StoreOpened` | `StoreSuspensionLifted` |

Ce n'est pas un hasard de conception, c'est la trace de la façon dont le code a grandi : on branche le cas qui fait du bruit — le refus, l'annulation — et on oublie son symétrique. **Un seul de ces quatre écarts est documenté comme délibéré** (`StoreSuspensionLifted`). Les trois autres ressemblent à des oublis.

---

## Ce que j'attends de toi

Trois réponses suffisent pour que je puisse agir :

1. **`TokenRevoked`** : on ferme la fenêtre de 30 s, ou on l'accepte ? (Le coût est de faire consommer du Kafka au gateway.)
2. **`SellerKybApproved`, `ProductApproved`, `ProductRejected`, `ReviewRejected`** : le vendeur doit-il être prévenu ? Si oui, ils passent en pile A.
3. **La pile B, une fois les réserves levées** : je supprime la publication et le contrat, ou on les garde pour un besoin à venir ?

Le cas le plus simple ne demande aucune réponse : les trois de `route-service` ne partent nulle part. Je peux les retirer dès maintenant si tu veux commencer par là.
