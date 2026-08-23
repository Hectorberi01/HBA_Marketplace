# Audit des sagas — troisième passage

> Complète `AUDIT-SAGAS.md`. Portée : les quatre parcours (client, vendeur
> marketplace, vendeur food, livreur, admin), relus **sans** consulter les deux
> audits précédents, précisément pour ne pas reconduire leurs angles morts.

---

## État des corrections

| Constat | État |
|---|---|
| **A1** — surface HTTP non protégée | **corrigé** — socle + 8 services. Reste : IDOR de catalog / engagement / inventory (pas de résolution utilisateur → vendeur), route YARP du webhook. |
| **A2** — chaîne FOOD rompue | **corrigé** — `FoodDeliveryReturnHandlers` relit `FOOD-`, `FoodOrderDelivered` remonte à order-service, l'escrow se libère. |
| **A3** — livreur jamais payé | **corrigé** — `CreditDriverOnDeliveryCompletedHandler`. Au passage : l'index d'idempotence était inversé et aurait sauté au PREMIER paiement. |
| **A4** — remboursement n'atteint pas le PSP | **corrigé** — `RefundAsync` est appelé, l'écriture ne suit qu'un succès, l'acheteur est notifié. Fenêtre résiduelle documentée. |
| **A5** — `Result` jetés | **corrigé** — `SagaOutcome` trie état incompatible / cause passagère. |
| **A6** — course annulée, commande bloquée | **corrigé** — état `UnderReview`, arbitrage humain, et la réciproque : annuler une commande annule sa course. |
| **A8** — double canal de versement | **corrigé** — un seul grand livre, le portefeuille fait foi, imputation PEPS. |
| **B10** — multi-lieux, abandon silencieux | **corrigé** — le `return` nu devient une mise en arbitrage. |
| **A7** — l'acheteur fixe ses frais de livraison | **corrigé** — `ShippingFee` retiré du corps de la requête ; `LookupQuote` ajouté au contrat gRPC (il manquait : `GetQuote` ÉCRIT, donc ne vérifie rien) ; le checkout relit le devis et emploie SON montant. Le mécanisme opposable existait depuis le début et n'avait jamais été appelé. Reste ouvert : la marchandise sans devis part à zéro franc, faute de surface de chiffrage serveur — journalisé. |
| **B6** — moteur de commission inutilisé | **corrigé** — le moteur de règles Billing fait foi, le taux plat devient son défaut. Reste : les règles par CATÉGORIE sont inertes (la ligne de commande ne porte pas de catégorie), et le vendeur voit encore le défaut. |
| **B7** — contre-passation, mauvaise formule | **corrigé** — plus aucune multiplication par un taux : les montants appliqués sont relus au prorata. `ReverseEarningsOnOrderCancelledHandler` était déjà sain. |
| B1 à B5, B8, B9, B11, C1 à C9 | **ouverts**. |

Deux défauts **découverts pendant les corrections**, non listés plus bas :

- L'index d'idempotence des gains livreur portait sur `(ReferenceType, ReferenceId)`
  seuls alors que le handler écrit deux lignes sous cette clé : la contrainte
  aurait sauté au **premier** paiement. Corrigé sans migration.
- `EarningStatus.Reversed` **n'est assigné nulle part** : les contre-passations
  débitent le portefeuille et laissent le gain intact.

**Compilé et testé** : `dotnet build` passe sans erreur ; les 59 tests
d'autorisation ajoutés passent, aux côtés des 102 tests de passerelle
préexistants.

Ces tests ont mené à une découverte qui dépassait la tâche #181. Le
contrôle systématique de chaque `AddXxxGrpcClient` contre son bloc compose
montre que **cinq services sur treize ne pouvaient pas démarrer**, faute
d'une adresse `Services__*` :

| Service | Manquait |
|---|---|
| food | `Order`, `Inventory`, `Delivery` |
| financial | `Order`, `Food`, `Merchant` — plus `Internal__ApiKey` et la clé de signature |
| communication | `Catalog`, `Merchant`, `Identity`, `Order`, `Delivery` |
| engagement | `Order` — plus les mêmes secrets |
| user | `Identity` |

Ces levées se produisent **à la construction de l'hôte**, avant que le
serveur n'existe : le conteneur sort sans qu'aucune sonde ne réponde. Et la
correspondance n'est pas devinable — `AddOrderingGrpcClient` lit
`Services:Order`, `AddMerchantsGrpcClient` lit `Services:Merchant`,
`AddProductsGrpcClient` lit `Services:Catalog`.

Corrigé dans `compose.services.yml`, avec le webhook PSP enfin routé en
anonyme par la passerelle. **Reste à valider par un démarrage à froid réel.**

---

## Pourquoi ce passage trouve autant de choses que les deux premiers ont manquées

Les deux premiers audits ont regardé **le câblage des sagas** : qui émet, qui
consomme, qui appelle qui en gRPC. C'est ce qui leur a été demandé, et c'est ce
qu'ils ont fait.

Personne n'a regardé :

1. **La surface HTTP.** Un événement peut être parfaitement câblé et la
   transition qu'il déclenche être appelable par n'importe qui via une route
   ouverte. C'est le cas ici, et c'est la découverte la plus grave du passage.
2. **Le bout de la chaîne de l'argent.** Les audits ont vérifié que les
   événements circulaient ; pas que l'argent arrivait. Deux bénéficiaires sur
   trois ne sont jamais payés.
3. **La symétrie des deux préfixes de course.** `ORDER-` a été branché au
   passage 2 ; `FOOD-`, introduit au même moment, ne l'a pas été. C'est un
   défaut **introduit** par la correction précédente.
4. **Les `Result` jetés.** Un `Task` renvoyé sans être inspecté acquitte le
   message Kafka quel que soit le résultat. Invisible dans une carte des
   événements : le câblage est correct, l'effet ne l'est pas.

---

## GRAVE — perte d'argent, ou commande définitivement bloquée

### A1. Tout le cycle de vie d'une commande est appelable par n'importe quel compte inscrit

`order-service/src/HBA.Order.Api/Endpoints/OrderEndpoints.cs:15-28` — le groupe
est `MapAuthenticatedGroup`, c'est-à-dire `RequireAuthorization()` **sans rôle**
(`shared/common/HBA.Shared.Hosting/Http/ApiAuthorization.cs:128`). Aucun handler
ne vérifie la propriété de la commande :

| Ligne | Route | Effet |
|---|---|---|
| `:19`, `:58` | `POST /{id}/payment/confirm` | `MarkPaid()` + décrément du stock + `Confirm()` → accrual des gains, création de la course, escrow libéré à la livraison. **On confirme une commande sans jamais avoir payé.** |
| `:20`, `:61` | `POST /{id}/cancel` | déclenche le remboursement du paiement d'un tiers |
| `:22`, `:67` | `POST /{id}/delivered` | libère l'escrow et crédite le vendeur |
| `:17`, `:38` | `GET /{id}` | IDOR : adresse, téléphone, montants de n'importe quelle commande |
| `:24` | `GET /api/admin/orders` | le nom dit « admin », la politique dit « authentifié » : toutes les commandes de la plateforme |

Ce n'est pas propre à order-service. Même défaut, vérifié :

- `merchant-service/.../MerchantEndpoints.cs:31,41-49` — `kyb/approve`,
  `kyb/reject`, `suspend`, `lift-suspension`, `DELETE /{sellerId}`. **Un acheteur
  valide son propre KYB, suspend un concurrent, supprime un vendeur.**
- `financial-service/.../FinancialEndpoints.cs:28,42,46,60,67,68,76,77,78` —
  remboursement forcé, règles de commission, approbation de retrait, lancement
  des versements, tous derrière un simple jeton.
- `inventory-service/.../InventoryEndpoints.cs:20,29,32` — ajustement du stock et
  libération des réservations de n'importe quel vendeur.
- `engagement-service/.../EngagementEndpoints.cs:25-27` — `reject` / `restore`
  d'un avis par quiconque.
- `catalog-service/.../CatalogEndpoints.cs:49` — groupe nommé `/admin`, politique
  « authentifié ».

**La convention correcte existe et est appliquée ailleurs** :
`food-service/.../FoodEndpoints.cs:88` fait `MapAdminGroup`, `identity-service`
aussi. Ce n'est pas une lacune de conception, c'est une application incomplète.
`RequireAdmin()` est déclaré dans le socle et n'a **zéro** usage.

Aggravant : `shared/common/HBA.Shared.Hosting/ServiceHostExtensions.cs:79` fait
`services.AddAuthorization()` nu. La `FallbackPolicy` décrite dans le commentaire
d'`ApiAuthorization.cs:13` appartenait au monolithe — **dans les 13 services, un
`MapGroup` sans politique est anonyme**, pas « au moins authentifié ». Aucun cas
fautif aujourd'hui, mais le prochain oubli sera public.

### A2. La chaîne FOOD est rompue : le restaurateur n'est jamais payable

Le gain restaurant est bien accru en solde « à venir »
(`AccrueEarningsOnOrderConfirmedHandler.cs:289`). Sa libération dépend de
`OrderDeliveredIntegrationEvent`, produit uniquement par
`MarkOrderDeliveredOnDeliveryCompletedHandler.cs:106` :

```csharp
if (OrderDeliveryReference.Read(integrationEvent.Reference) is not { } orderId)
{
    return;   // « Course restauration […]. Rien à faire »
}
```

qui ne lit que le préfixe `ORDER-`. Or la course d'un repas est créée sous
`FOOD-` (`FoodOrderBridgeHandlers.cs:285`, `DeliveryReference.cs:48,51`), et
**aucun service ne consomme la fin d'une course `FOOD-`** : les seuls
`IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>` du dépôt sont
`WebhookOnDeliveryCompleted` (delivery, webhook partenaire) et celui ci-dessus.
`food-service/.../Program.cs:35-41` n'enregistre que `OrderConfirmed` et
`FoodOrderReadyForPickup`.

Conséquence : repas remis au client, commande figée en `Confirmed`, escrow jamais
libéré, solde du restaurateur bloqué en « à venir » **à vie**.

Corollaire — deux transitions mortes : `MarkFoodOrderPickedUpCommand` et
`MarkFoodOrderDeliveredCommand` (`FoodOrderCommands.cs:75,77`, handlers `:375`,
`:384`) n'ont **aucun appelant** : ni route, ni gestionnaire. `FoodOrder.MarkPickedUp`
(`FoodOrder.cs:380`) et `MarkDelivered` (`:400`) sont du code mort.

**Ce défaut a été introduit par la correction du passage 2** : `ORDER-` a été
branché, `FOOD-` créé au même moment ne l'a jamais été.

### A3. Le livreur n'est jamais payé

`CreditDriverEarningCommand` et son handler existent
(`financial-service/.../Wallets/CreditDriverEarningCommand.cs:33,39`),
`DriverWallet` et sa migration aussi. La recherche du symbole dans tout le dépôt
ne renvoie que **sa propre définition et un commentaire**
(`WalletConfigurations.cs:272`). Aucun appelant.

Le montant est pourtant calculé (`Delivery.cs:701`) et publié
(`DeliveryDomainEventHandlers.cs:176`). financial-service n'enregistre aucun
handler de `DeliveryCompleted` (`SettlementModuleInstaller.cs:95-114`).

**Le gain est calculé, publié, et tombe dans le vide.** Le portefeuille livreur
reste à zéro à vie ; l'écran « Revenus » de l'app livreur lit un solde jamais
crédité.

### A4. Le remboursement n'atteint jamais le PSP

`financial-service/.../PaymentLifecycleCommands.cs:88-104` :

```csharp
var result = payment.Refund();
if (result.IsFailure) return result;
await _unitOfWork.SaveChangesAsync(cancellationToken);
```

`IPaymentGateway.RefundAsync` (`Abstractions/Gateways/IPaymentGateway.cs:67`) est
implémenté par FedaPay, MTN, Moov, Stripe, PayPal — et **appelé par personne**.
Le paiement passe « Refunded » en base ; l'argent reste chez l'opérateur.

Et `PaymentRefundedIntegrationEvent` (`PaymentDomainEventHandlers.cs:83`) n'a
aucun consommateur : le client n'est même pas prévenu du remboursement qui n'a
pas eu lieu.

### A5. Les deux gestionnaires du dénouement de paiement jettent leur résultat

`order-service/.../PaymentOutcomeHandlers.cs:19-20` et `:30-31` :

```csharp
public Task HandleAsync(PaymentCapturedIntegrationEvent e, CancellationToken ct = default)
    => _sender.Send(new ConfirmOrderPaymentCommand(e.OrderId), ct);
```

Le `Result` n'est ni inspecté, ni journalisé, ni levé. Si `MarkPaid()` refuse —
commande déjà annulée, introuvable, stock insuffisant — le message Kafka est
**acquitté** : paiement encaissé, commande jamais confirmée, silence total. Idem
sur l'échec de paiement : le stock réservé n'est jamais libéré.

Les autres handlers du même circuit journalisent ou lèvent
(`MarkOrderDeliveredOnDeliveryCompletedHandler.cs:116-128`,
`RefundPaymentOnOrderCancelledHandler.cs:99-115`). Ces deux-là, non — et ce sont
les deux qui gardent l'argent.

### A6. Course annulée : les deux systèmes divergent en silence

`DeliveryCancelledIntegrationEvent` n'a qu'un consommateur, interne à delivery
(`EnqueueWebhookOnDeliveryEvents.cs:166` / `DeliveriesModuleInstaller.cs:148`).
Rien ne remonte à order-service ni à food-service. Et `Order.Cancel`
(`Order.cs:373`) refuse l'état `Confirmed`.

Une course annulée après confirmation laisse la commande **bloquée en Confirmed**
pour toujours : ni livraison, ni annulation, ni remboursement, escrow gelé, stock
déjà décrémenté.

Réciproquement : annuler une commande n'annule pas la course.
`IDeliveryDispatchApi` n'apparaît dans order-service que dans
`CreateDeliveryOnOrderConfirmedHandler.cs:53,59`. **Un livreur part chercher un
colis pour une commande annulée.**

### A7. Le client fixe lui-même ses frais de livraison

`OrderEndpoints.cs:54,76-79` : `ShippingFee` et `DeliveryQuoteId` viennent du
corps de la requête. `IDeliveryDispatchApi.RequestQuoteAsync`
(`DeliveryDispatchContracts.cs:111`) **n'a aucun appelant dans le dépôt**. Aucun
devis serveur n'existe, et `CreateDeliveryOnOrderConfirmedHandler.cs:189` achète
la course sur le devis dicté par l'acheteur.

### A8. Deux canaux de versement concurrents sur le même gain

- **Canal A** — `RequestWithdrawalCommandHandler` débite le portefeuille
  (`WalletCommands.cs:66`) puis `ApproveWithdrawalCommandHandler` appelle FedaPay
  (`:187`). Ne touche **jamais** au statut des `SellerEarning`.
- **Canal B** — route vivante `POST /settlements` (`FinancialEndpoints.cs:76`) :
  `RunSettlementCommandHandler` prend tous les gains `Released`
  (`SettlementCommands.cs:51`), crée un payout (`:83`), les marque `Settled`
  (`:92`) — **sans aucun débit de portefeuille**.

Un même gain peut être encaissé par retrait (argent réellement parti) **puis**
re-versé dans un lot de reversement. Aucun des deux ne voit l'autre.

---

## MOYEN

### B1. Le vendeur n'est prévenu ni de la validation de son KYB, ni de l'issue de son versement

`Seller.ApproveKyb()` lève `SellerKybVerifiedDomainEvent` (`Seller.cs:231`) —
aucun handler ne l'écoute, et il n'existe aucun `SellerKybVerifiedIntegrationEvent`.
Le **refus** est notifié, pas l'acceptation.

`Withdrawal` (`Withdrawal.cs:12-146`) n'appelle jamais `Raise(...)` : ni
`Complete` (`:125`), ni `Fail` (`:133`), ni `Reject` (`:141`). Le vendeur qui
demande un retrait n'apprend rien de son sort.

### B2. L'acheteur n'est jamais prévenu que sa commande est livrée

`NotificationsModuleInstaller.cs:278` affirme le contraire en commentaire. Il
n'existe **aucun** `IIntegrationEventHandler<OrderDeliveredIntegrationEvent>`
dans communication-service. Le seul message « livré » existant est
`ShipmentDeliveredNotificationHandler`, branché sur un événement que plus personne
n'émet. Silence à l'étape finale — et l'invitation à laisser un avis disparaît
avec.

### B3. Le restaurateur n'est pas notifié d'une commande entrante

`FoodOrderReceivedIntegrationEvent` est publié
(`FoodOrderDomainEventHandlers.cs:33`) et n'a aucun consommateur. Le seul handler
vendeur, `SellerOrderConfirmedNotificationHandler`, sort explicitement sur
`Kind == "Food"` (`SellerOrderNotificationHandler.cs:61-64`). En acceptation
manuelle, le ticket attend qu'un humain regarde l'écran cuisine.

### B4. Suspendre un vendeur n'a aucun effet, sauf un e-mail

`SellerSuspendedIntegrationEvent` a un seul consommateur : la notification
(`NotificationsModuleInstaller.cs:232`). financial-service ne l'écoute pas
(`SettlementModuleInstaller.cs:95-114`) → **les versements ne sont pas gelés**.
Les commandes en cours ne sont pas annulées. (Le retrait des offres relève de
Products/Offers, non extrait ; le gel des versements, non — financial est extrait.)

### B5. Suspendre ou bloquer un livreur ne libère ni ses courses ni son rôle

`Driver.Suspend` / `Block` (`Driver.cs:212-232`) posent un statut et ne lèvent
**aucun événement** — à comparer avec `Verify()` qui fait `Raise(new
DriverVerifiedDomainEvent(...))` (`:202`). Donc : le rôle `Driver` n'est jamais
retiré côté identity (qui ne consomme que `DriverVerified`,
`IdentityModuleInstaller.cs:84`), et les courses `PickedUp` / `InTransit` du
livreur bloqué restent affectées à lui, sans réattribution. **Le colis est
immobilisé.**

### B6. Le moteur de commission par vendeur ne sert à rien

L'argent utilise un taux plat `PricingOptions.PlatformCommissionRate`
(`AccrueEarnings…:86`). En parallèle, Billing porte un moteur de règles par
vendeur/catégorie (`CommissionModuleApi.cs:27`) avec une API publique
(`/commissions/compute`, `FinancialEndpoints.cs:41`). `ICommissionModuleApi`
**n'a aucun appelant hors du module Billing**. Un admin crée une règle
« vendeur X à 5 % », la voit dans l'UI, et l'argent prélève 10 %.

### B7. La contre-passation de retour applique la mauvaise formule aux repas

Accrual marchandise : `Math.Round(gross * commRate / (1 + total))`
(`AccrueEarnings…:105`). Accrual restauration : `Math.Round(brut * commRate)`
sans division (`:243`, argumenté). Mais
`ReverseEarningsOnReturnRefundedHandler.cs:83-95` applique la formule
**marchandise avec le taux marchandise** à tout remboursement, sans regarder la
nature. Les deux ne se compensent pas.

### B8. La candidature livreur n'a pas de pièces

Aucun fichier contenant `Document` dans tout delivery-service. `Driver.Create`
(`Driver.cs:150-171`) n'exige que nom, téléphone, véhicule ; `Verify()` (`:176`)
ne vérifie que « pas bloqué ». L'étape « pièces → validation admin » du parcours
livreur n'a **aucun support serveur** — les écrans Flutter (DRV2/DRV3) n'ont rien
derrière.

### B9. « Aucun livreur disponible » n'escalade nulle part

La boucle de reprise, elle, est correcte : expiration puis réouverture
(`DeliveryDispatchService.cs:146-154`, `:270-281`). Mais
`DeliveryNoDriverAlertHandler` (`DeliveryTrackingNotificationHandlers.cs:145-161`)
fait un `LogError` et rien d'autre, et aucune surface admin ne liste les courses
`NoDriverAvailable`. La marchandise est prête, personne ne vient, personne ne le
sait.

### B10. Multi-lieux : abandon silencieux côté acheteur

`CreateDeliveryOnOrderConfirmedHandler.cs:105-120` — le refus est délibéré et
argumenté, mais il se termine par un `return` après un log : ni annulation, ni
remboursement, ni notification. Commande payée, stock décrémenté, définitivement
bloquée. **Le refus était le bon choix ; l'absence de sortie ne l'est pas.**

### B11. Abandon silencieux à l'accrual marchandise

`AccrueEarningsOnOrderConfirmedHandler.cs:80-84` : `if (order is null ||
order.Lines.Count == 0) return;` sur une commande encaissée — aucun gain, aucune
commission, aucune trace. Le chemin Food du même fichier (`:204`) lève, à juste
titre, pour le même motif.

---

## MINEUR

- **C1.** État orphelin `Authorized` : `Payment.Authorize` (`Payment.cs:97`) n'a
  aucun appelant ; `GatewayOutcomeApplier` ne produit que Captured/Failed/Refunded.
- **C2.** Fenêtre de double débit : `InitiatePaymentCommandHandler.cs:127-137`
  ouvre la session PSP **avant** de persister. Si le Save échoue, le webhook
  arrive sur une référence inconnue → `GatewayConfirmationCommands.cs:73-78`
  renvoie `Success`. Acheteur débité, aucune trace.
- **C3.** `Payment.ReleaseEscrow` (`Payment.cs:154-169`) ne lève aucun événement :
  rien ne rapproche l'escrow libéré du gain libéré ; les deux dérivent en silence
  si l'un des handlers échoue.
- **C4.** Trois méthodes vendeur sans appelant : `Seller.UpdateRating` (`:493`),
  `RecordSale` (`:505`), `SetSalesCount` (`:513`). La note et le nombre de ventes
  restent à leur valeur initiale.
- **C5.** `StoreClosed` / `StoreOpened` (`StoreDomainEventHandlers.cs:20,38`) sans
  consommateur, alors que le commentaire ligne 9 affirme que c'est ce message qui
  retire les offres de la vente.
- **C6.** `UserEmailConfirmed`, `CartCheckedOut`, `StockReserved`,
  `ProductDeleted`, `ReviewRejected`, `BrandCreated`, `CategoryCreated` : émis,
  jamais consommés. À trancher un par un — certains sont légitimement inertes.
- **C7.** `ShipmentNotificationHandlers.cs:2` importe encore
  `HBA.Ordering.Contracts` : le second espace de noms (tâche #164) est toujours
  vivant.
- **C8.** Six gestionnaires morts enregistrés (`NotificationsModuleInstaller.cs:186,187,194,195`,
  `SettlementModuleInstaller.cs:100,114`) sur Shipping/Returns non extraits.
- **C9.** `RequireAdmin()` déclaré dans le socle, zéro usage. `DriverRole` semé
  mais jamais exigé, alors que son attribution est branchée
  (`IdentityModuleInstaller.cs:84`) — le commentaire justifiant l'abstention est
  périmé.

---

## Non extrait — connu, à ne pas compter comme cassé

Products/Offers, Search, Disputes, Shipping, Returns n'ont jamais été migrés du
monolithe. Rappel du plus lourd : `AddItemToCartCommandHandler.cs:70-82` rend
« catalogue indisponible » — **l'ajout au panier marchandise ne fonctionne sur
aucune installation.**

---

## Ordre de traitement proposé

1. **A1** — la sécurité d'abord : tant que n'importe qui peut confirmer le
   paiement d'une commande, corriger la chaîne de l'argent revient à mieux servir
   la fraude. Un seul chantier, mécanique, service par service.
2. **A5, A4, A2, A3** — les quatre fuites d'argent, dans cet ordre : A5 est trois
   lignes, A4 un appel, A2 un préfixe, A3 un handler.
3. **A6, A8, B10** — les impasses : une commande bloquée doit avoir une sortie.
4. **A7, B6, B7** — la cohérence des montants.
5. Le reste.
