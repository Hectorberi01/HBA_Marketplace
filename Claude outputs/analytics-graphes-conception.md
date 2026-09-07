# Les graphes admin et vendeur — ce que les événements permettent déjà, et ce qui manque

Service analytics dédié, roll-ups journaliers, lecture en une requête. La
conception tient en une question : **les événements publiés portent-ils les
chiffres qu'il faut ?**

J'ai relu les payloads. **Deux familles sur quatre sont constructibles
aujourd'hui. Les deux autres demandent d'ajouter des champs à des contrats.**

---

## 1. Ce qui est constructible dès maintenant

### Vendeur — ventes et revenus

`OrderConfirmedIntegrationEvent` porte exactement ce qu'il faut :

```csharp
public sealed record OrderSellerShare(Guid SellerId, int ItemCount, decimal Amount);

SellerShares : IReadOnlyCollection<OrderSellerShare>
Currency     : string
Kind         : "Goods" | "Food"
```

Un vendeur, ses articles, son montant, la devise. Le roll-up journalier
`(seller_id, date, currency) → commandes, articles, chiffre d'affaires` se
construit sans rien demander à personne.

**Graphes servis** : chiffre d'affaires par jour, commandes par jour, panier
moyen (dérivé), part marchandise / repas.

### Admin — activité de la plateforme

Même événement, agrégé sans le vendeur : `(date, kind, currency) → commandes,
GMV`. Plus `SellerRegisteredIntegrationEvent` et
`UserRegisteredIntegrationEvent` pour les inscriptions.

**Graphes servis** : commandes et GMV par jour, répartition marketplace / food,
inscriptions vendeurs et acheteurs par jour.

---

## 2. Ce qui manque, et le champ exact qui manque

### `OrderCancelled` ne porte ni vendeur ni montant

```csharp
OrderCancelledIntegrationEvent : OrderId, BuyerId, Reason
```

Conséquence : **la répartition par statut d'un vendeur est impossible.** On peut
compter les annulations de la plateforme, pas celles d'un vendeur, ni le montant
perdu.

Ce qu'il faut ajouter, en **optionnel**, donc conforme à la convention additive
(D32) — aucune rupture pour les consommateurs actuels :

```csharp
public IReadOnlyCollection<OrderSellerShare>? SellerShares { get; init; }
public string? Currency { get; init; }
```

### `PaymentCaptured` et `PaymentFailed` ne portent ni fournisseur ni montant

```csharp
PaymentCapturedIntegrationEvent : PaymentId, OrderId, OrderType
PaymentFailedIntegrationEvent   : PaymentId, OrderId, OrderType, Reason
```

Conséquence : **« taux d'échec de paiement par fournisseur » ne se construit
pas.** Ni le volume encaissé par fournisseur, ni le délai d'encaissement.

Ce qu'il faut ajouter, en optionnel :

```csharp
public string? Provider { get; init; }     // fedapay, kkiapay…
public decimal? Amount { get; init; }
public string? Currency { get; init; }
```

**CETTE ABSENCE VAUT D'ÊTRE REGARDÉE POUR ELLE-MÊME.** Un événement de paiement
qui ne dit ni combien ni par qui laisse l'exploitant sans réponse à « quel
fournisseur nous coûte des ventes ce mois-ci ». Le graphe n'est que ce qui rend
le manque visible.

### Aucun événement ne porte les LIGNES d'une commande

`OrderConfirmed` agrège par vendeur. Aucun événement ne dit quel SKU, quel
produit, en quelle quantité.

Conséquence : **« top produits vendus » est hors de portée par les événements.**
Deux voies, et elles ne se valent pas :

- **enrichir l'événement** d'une collection de lignes — il grossit, et il portera
  du détail que six consommateurs sur sept n'utiliseront jamais ;
- **le service analytics appelle order-service en gRPC** au moment du roll-up,
  pour les commandes du jour. Le détail reste chez son propriétaire, et
  l'analytics ne le stocke que sous forme agrégée.

Je recommande la seconde. Elle demande `GET /orders/{id}/lines` ou un RPC
équivalent — à trancher avec toi.

### Le stock faible n'a ni propriétaire ni seuil

`StockDepletedIntegrationEvent` porte `Sku` et `LocationId`, **pas le vendeur**.
Et il n'existe aucun événement « stock faible » — seulement « épuisé ».

Rappel du code existant : `GET /api/inventory/low-stock` rend le stock faible de
TOUTE la plateforme, sans filtre de propriétaire. L'appeler depuis un BFF vendeur
montrerait à un commerçant les ruptures de ses concurrents. Le manque est déjà
écrit dans `MerchantDtos.cs` : il faut
`GET /api/inventory/owners/{ownerId}/low-stock`.

### Le délai de traitement vendeur n'est pas mesurable

`order-service` expose `POST /orders/seller/{id}/confirm|preparing|ready`, mais
**aucun événement n'est publié quand un vendeur agit**. Sans lui, « délai moyen
de traitement » n'a pas de source.

Ce qu'il faut : un `SellerOrderStatusChangedIntegrationEvent` (orderId, sellerId,
statut, horodatage). C'est le seul ajout de cette liste qui soit un NOUVEL
événement et non un champ optionnel.

---

## 3. L'ordre que je propose

| Lot | Contenu | Dépend de |
|---|---|---|
| **1** | Le service, son schéma, sa consommation, et les deux familles constructibles : ventes vendeur + activité plateforme | rien |
| **2** | Les trois ajouts de champs optionnels (`OrderCancelled`, `PaymentCaptured`, `PaymentFailed`) et les graphes qu'ils débloquent | lot 1 vert |
| **3** | Santé opérationnelle : `OrderUnderReview` et `DeliveryNoDriverAvailable` existent déjà et suffisent ; le délai vendeur attend son événement | lot 1 |
| **4** | Produits et stock : demande l'accès aux lignes de commande et l'endpoint inventaire par propriétaire | arbitrage |

**Le lot 1 remplace aussi un défaut documenté.** `MerchantTodayDto` dit
lui-même que ses chiffres du jour sont calculés dans la passerelle en tirant
TOUTES les commandes du vendeur, et que « cela devient coûteux exactement chez
les vendeurs qui réussissent ». Le roll-up rend ce calcul en une ligne de table.

---

## 4. La question que je ne peux pas trancher : l'historique

Le service démarre vide. Trois réponses possibles, et il faut en choisir une
avant d'écrire le projecteur :

1. **Les graphes commencent au jour du déploiement.** Rien à écrire, rien à
   rejouer. Un an de données existantes reste invisible.
2. **Rejeu Kafka depuis le début du sujet.** Ne coûte qu'une remise à zéro
   d'offsets — mais la rétention du courtier borne ce qu'on peut rejouer, et
   `service.order.v1` ne remonte pas à la création de la plateforme.
3. **Rattrapage par lecture directe des bases source.** Un script qui lit
   `orders` et écrit les roll-ups passés. Le plus complet, et le seul qui
   demande d'ouvrir la base d'un autre service — ce que ce dépôt s'interdit
   partout ailleurs.

Mon avis : **(1) pour livrer, (3) plus tard si le besoin se confirme**, écrit
comme un import ponctuel et non comme un chemin permanent.

---

## 5. Ce que ce document ne couvre pas

- **Le nom des routes BFF et la forme des réponses** : je les proposerai avec le
  lot 1, une fois le schéma arrêté.
- **Les permissions.** Un vendeur ne doit voir que SES séries, et le contrôle
  `permissions` refusera un droit déclaré sans garde. `SELLER_ANALYTICS_VIEW` et
  `PLATFORM_ANALYTICS_VIEW` sont à créer.
- **Le fuseau horaire des journées.** « Le chiffre d'affaires du 6 septembre »
  n'a pas le même sens à Cotonou et en UTC. Le repo travaille en UTC partout ;
  un vendeur béninois lira ses journées décalées d'une heure. À trancher.
- **La rétention des roll-ups.** Une ligne par vendeur, par jour et par devise
  reste petite ; les séries horaires, si elles arrivent, ne le seraient plus.
