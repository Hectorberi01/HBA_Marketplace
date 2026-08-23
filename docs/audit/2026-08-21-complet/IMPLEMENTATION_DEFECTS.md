# IMPLEMENTATION_DEFECTS — HBAExpress

*Consolidation des anomalies relevées par l'audit statique du 21/08/2026.*

**Lecture de ce document.** Les anomalies CRITICAL et HIGH sont détaillées une par une, au format demandé. Les MEDIUM et LOW sont regroupées en fin de document et renvoient au rapport détaillé qui les porte, pour ne pas noyer l'essentiel.

**Traçabilité.** Chaque anomalie cite un fichier et, quand il est déterminant, une ligne. Les preuves complètes — extraits, chaînes d'appel, vérifications — sont dans les rapports thématiques listés en fin de document.

**Réserve de méthode.** Aucun compilateur .NET n'était disponible : tous les constats sont issus d'une lecture de code, aucun d'une exécution. Les rares points non tranchés sont signalés « à confirmer ».

---

## Compte

| Sévérité | Nombre |
|---|---:|
| CRITICAL | 39 |
| HIGH | 36 |
| MEDIUM | 63 |
| LOW | 24 |
| **Total** | **162** |

> **Correction du 21/08 (consolidation).** Cinq anomalies CRITICAL supplémentaires ont été
> remontées en relisant les sections détaillées de `DATABASE_AUDIT.md` et `KAFKA_EVENT_MATRIX.md` :
> elles y figuraient, mais n'avaient pas été reprises dans la première rédaction de ce document.
> Elles sont ajoutées en section F, et deux d'entre elles passent en tête du plan de correction.

---

# A. Le bus d'événements

## ISSUE-001
**Statut :** ✅ **CORRIGÉE** le 21 août 2026 — lot 2.2, décision D31. `HbaTopics` est la table unique, lue par le producteur et par le consommateur ; `SubscribeTopics` est vide par défaut. Les trois collisions d'`eventType` sont tranchées. Les manifestes k8s sont régénérés depuis le code et `scripts/check-kafka-topics.py` rapproche les trois sources. Le §19.2 — un sujet par agrégat — reste la cible.

**Severity:** CRITICAL
**Domain:** Transverse
**Service:** Socle Kafka (`shared/common/HBA.Shared.Infrastructure`)
**Saga:** Toutes

**Problem:**
Les producteurs dérivent le nom du topic de leur propre identité, les consommateurs s'abonnent à une liste de topics constante. Les deux ne coïncident que par hasard. Sur 136 événements d'intégration déclarés, **20 atteignent réellement un consommateur**.

**Evidence:**
- `KafkaEventNaming.cs:38` — dérivation du topic côté producteur
- `KafkaEventBusOptions.cs:21` — `SubscribeTopics`, liste constante
- `DependencyInjection.cs:79-90` — abonnement du consommateur
- 96 handlers écrits, **96 enregistrés en DI** : aucun oubli de câblage
- 37 événements ont un producteur ET un consommateur, mais leur topic n'est écouté par personne

**Expected:** un événement publié par le service A est reçu par le handler enregistré du service B.
**Actual:** l'événement part sur un topic auquel B ne s'est pas abonné ; il est perdu sans erreur ni journal.

**Impact:** c'est la cause première de la majorité des sagas rompues de cet audit. Le paiement, la libération de stock, l'ouverture de ticket cuisine, la création de course : tout passe par là.

**Recommended fix:** unifier la dérivation du nom de topic entre producteur et consommateur — une seule fonction, appelée des deux côtés. Puis rejouer la matrice de `KAFKA_EVENT_MATRIX.md` pour vérifier les 136.

**Tests required:**
- test de contrat : pour chaque `IntegrationEvent`, le topic calculé côté producteur est égal au topic calculé côté consommateur ;
- test d'intégration bout en bout sur au moins un événement par service.

---

## ISSUE-002
**Statut :** ✅ **CORRIGÉE** — lot 2.2 (le sujet) + lot 2.3 (la preuve). Les deux bouts étaient câblés : `ConfirmOrderOnPaymentCapturedHandler` est enregistré. Le message n'arrivait pas parce que payment publiait sur `service.payment.v1` et order écoutait `service.financial.v1`. `tests/HBA.Order.IntegrationTests` le prouve désormais contre une vraie base et un vrai courtier.

**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Order Service
**Saga:** Achat marketplace

**Problem:** `PaymentCaptured` est publié par payment-service et consommé par personne. La commande n'est jamais confirmée.

**Evidence:** `PaymentDomainEventHandlers.cs:30` ; `OrderingModuleInstaller.cs:61`
**Expected:** `PaymentCaptured` → `Order.MarkPaid()` → `OrderConfirmed`.
**Actual:** l'acheteur est débité ; la commande reste `AwaitingPayment` indéfiniment.
**Impact:** **argent encaissé sans contrepartie.** Aucun parcours d'achat ne peut aboutir.
**Recommended fix:** dépend d'ISSUE-001. Une fois les topics unifiés, vérifier que le handler est bien atteint, et le rendre idempotent (ISSUE-008).
**Tests required:** E2E « paiement capturé → commande `Paid` » ; rejeu du même événement → un seul effet.

---

## ISSUE-003
**Statut :** ✅ **CORRIGÉE** — même cause, même preuve. `CancelOrderOnPaymentFailedHandler` appelle `ReleaseReservationAsync` par ligne réservant du stock ; le test le vérifie sur un double d'`IInventoryModuleApi` qui journalise ses appels.

**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Inventory Service
**Saga:** Achat marketplace

**Problem:** `PaymentFailed` n'est consommé par personne : les réservations de stock ne sont jamais libérées.
**Evidence:** `OrderingModuleInstaller.cs:62`
**Expected:** `PaymentFailed` → `ReleaseReservation` → stock de nouveau vendable.
**Actual:** la commande échoue, le stock reste réservé sans limite de temps.
**Impact:** **survente par étranglement** — le stock disparaît de la vente sans qu'aucune vente n'ait eu lieu. Cumulatif : chaque paiement échoué en retire un peu plus.
**Recommended fix:** brancher le consommateur (dépend d'ISSUE-001) **et** ajouter le balayeur d'expiration d'ISSUE-016 — les deux, car ni l'un ni l'autre ne couvre le cas de l'autre.
**Tests required:** E2E « paiement échoué libère le stock » ; rejeu idempotent.

---

## ISSUE-004
**Statut :** ✅ **CORRIGÉE** — `ReceiveFoodOrderOnMealOrderConfirmedHandler` consomme enfin `MealOrderConfirmedIntegrationEvent` dans restaurant-service. L'ancien gestionnaire, qui écoutait l'événement de la MARKETPLACE, reste en place le temps de la bascule. Gain au passage : plus d'aller-retour gRPC vers order-service, et `CustomerNote` (« allergie arachide ») atteint enfin le passe.

**Severity:** CRITICAL
**Domain:** Food
**Service:** Restaurant Service
**Saga:** Commande food

**Problem:** `MealOrderConfirmedIntegrationEvent` n'a aucun consommateur. Le seul ouvreur de ticket de cuisine écoute `OrderConfirmedIntegrationEvent`, qui vient de la marketplace.
**Evidence:** `services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Program.cs:65-67` ; `MealOrderDomainEventHandlers.cs:67`
**Expected:** commande food confirmée → ticket de cuisine ouvert.
**Actual:** client débité, aucune cuisine servie.
**Impact:** la chaîne restauration neuve ne produit aucun repas.
**Recommended fix:** faire consommer `MealOrderConfirmed` par restaurant-service, en gardant l'ancien handler le temps de la bascule.
**Tests required:** E2E « commande food confirmée → ticket ouvert ».

---

## ISSUE-005
**Severity:** CRITICAL
**Domain:** Food
**Service:** Restaurant Service → Delivery
**Saga:** Commande food

**Problem:** restaurant-service publie « repas prêt » sur `service.restaurant.v1` et s'abonne à `service.food.v1`. Il n'entend pas son propre événement ; aucune course n'est créée.
**Evidence:** `FoodOrderBridgeHandlers.cs:215` ; `services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Program.cs:70`
**Expected:** repas prêt → course de livraison créée → livreur affecté.
**Actual:** le repas est prêt et le reste. Le restaurateur n'est jamais réglé.
**Impact:** la commande food n'atteint jamais le client, même quand la cuisine a travaillé.
**Recommended fix:** conséquence directe d'ISSUE-001.
**Tests required:** E2E « repas prêt → course créée ».

---

## ISSUE-006
**Statut :** ⛔ **BLOQUÉE EN AMONT, pas reportée.** Brancher la libération d'entiercement pour le food fabriquerait deux gestionnaires qui ne peuvent pas s'exécuter : `InitiatePaymentCommandHandler` écrit `PaymentOrderType.Marketplace` EN DUR — son propre encadré le dit — parce qu'il lit la commande via le module Ordering, qui ne connaît que la marketplace. `PaymentOrderType.Food` existe et rien ne le produit. **Aucun paiement de repas n'est jamais créé.** À rouvrir quand le chemin de paiement food existera.

**Severity:** CRITICAL
**Domain:** Food / Commun
**Service:** Payment Service
**Saga:** Commande food

**Problem:** `MealOrderDelivered` et `MealOrderCancelled` ne sont pas consommés par payment-service : la somme mise sous séquestre n'est jamais libérée, le remboursement jamais déclenché.
**Evidence:** `PaymentsModuleInstaller.cs:237,248`
**Expected:** livré → séquestre libéré vers le restaurateur ; annulé → remboursement client.
**Actual:** l'argent reste immobilisé indéfiniment.
**Impact:** **perte d'argent des deux côtés** : le restaurateur n'est pas payé, le client n'est pas remboursé.
**Recommended fix:** conséquence d'ISSUE-001 ; vérifier ensuite la présence des compensations.
**Tests required:** E2E livraison et E2E annulation, avec vérification du solde.

---

## ISSUE-007
**Statut :** 🟡 **PARTIELLEMENT CORRIGÉE — lot 5.2.** driver-service a désormais un vrai `DbContext`, sa table `drivers.outbox_messages` et son `AddOutboxProcessor` : ses événements sont sauvés.

**Les QUATRE autres restent des maquettes** — `dispatch`, `route`, `tracking`, `proof-of-delivery` n'ont aucun `DbContext`, leur état tient dans un `ConcurrentDictionary` et leurs événements partent dans une file jamais drainée. La fiche disait « la correction se fait à leur implémentation réelle, pas avant », et c'est toujours vrai. Le lot 5.4 a laissé dans chacun des cinq installeurs un encadré **à l'endroit exact** où la ligne manquante devra être écrite, nommant les événements perdus et leur conséquence.

**Statut :** ⏳ **VÉRIFIÉE ET NON CORRIGÉE au 3 septembre 2026 — lot 5.4.** État réel constaté service par service : **aucun des cinq n'a de `DbContext`**, ni de table `outbox_messages`, ni de migration, ni de chaîne de connexion. Les cinq `Program.cs` portent encore la mention « ce service n'a pas de base à sonder » et appellent `AddHbaSecurity()` au lieu de `AddHbaService<TDbContext>()`. Les cinq installeurs enregistrent `IntegrationEventQueue` — une `List<>` scopée — comme `IIntegrationEventPublisher` : les 16 événements (dispatch 3, driver 2, route 3, tracking 4, preuve 4) sont ajoutés à une liste que personne ne draine, et `PublishAsync` rend `Task.CompletedTask`, donc l'appelant voit un succès.

`AddOutboxProcessor<TContext>()` exige `where TContext : DbContext, IOutboxDbContext`. **Il n'y a rien à quoi le brancher.** Fabriquer une persistance ici reviendrait à décider en passant du schéma de cinq services — c'est le travail du lot 5.2 (décision D30). Ce que le lot 5.4 a fait à la place : **nommer le défaut sur place**, par un encadré dans chacun des cinq installeurs, à l'endroit exact où la ligne manquante devra être écrite.

Seul `delivery-pricing-service` — qui n'est pas dans les cinq — a déjà sa base, ses migrations et son `AddOutboxProcessor<DeliveryPricingDbContext>()`. C'est le modèle le plus proche.

**Severity:** CRITICAL
**Domain:** Delivery
**Service:** dispatch, driver, route, tracking, proof-of-delivery
**Saga:** Livraison

**Problem:** ces cinq services publient des événements **sans processeur d'outbox**. Rien ne draine leur file : la perte est totale et systématique, pas occasionnelle.
**Evidence:** aucun `AddOutboxProcessor` dans leurs installeurs ; 16 événements publiés dans une file mémoire jamais drainée
**Expected:** publication transactionnelle, drainée par un `BackgroundService`.
**Actual:** l'événement est écrit en mémoire et disparaît au redémarrage — souvent avant.
**Impact:** aucune coordination possible dans le domaine livraison.
**Recommended fix:** ces cinq services étant des squelettes (ISSUE-030 à ISSUE-034), la correction se fait à leur implémentation réelle, pas avant.
**Tests required:** test d'intégration outbox par service, une fois implémentés.

---

## ISSUE-008
**Statut :** ✅ **CORRIGÉE** le 21 août 2026 — lot 2.1. La garde vit dans `IntegrationEventDispatcher`, pas dans les 96 gestionnaires : trace posée AVANT l'appel, donc committée dans le même `SaveChangesAsync` que l'effet métier. Quinze services enregistrent `IConsumerInbox`. Le dispatcher marque dans **toutes** les inbox enregistrées, ce qui règle le cas de `HBA.Financial.Api` — trois modules, trois schémas dans un seul processus.

**Severity:** CRITICAL *(HIGH aujourd'hui, CRITICAL dès qu'ISSUE-001 est corrigée)*
**Domain:** Transverse
**Service:** Tous les consommateurs Kafka
**Saga:** Toutes

**Problem:** `EfConsumerInbox` existe et fonctionne. **6 handlers sur 96 l'utilisent.** Sept services ont une table `consumer_inbox` ; cinq l'ont créée sans jamais résoudre `IConsumerInbox`.
**Evidence:** `MealOrderingModuleInstaller.cs:71-92` ; aucun `IConsumerInbox` dans order, cart, inventory, food-cart, food-order
**Expected:** Kafka livre *au moins une fois* ; chaque handler doit être rejouable sans effet supplémentaire.
**Actual:** un rejeu de partition recrédite un vendeur, réserve à nouveau du stock, renvoie une notification.
**Impact:** ce défaut est **masqué** par ISSUE-001 — les messages n'arrivent pas. Il se révélera à l'instant précis où les topics seront corrigés.
**Recommended fix:** brancher `IConsumerInbox` sur tous les handlers à effet de bord, **avant ou en même temps** que la correction des topics. L'ordre n'est pas négociable.
**Tests required:** pour chaque handler à effet de bord, un test « double livraison du même `eventId` → un seul effet ».

---

# B. Argent

## ISSUE-009
**Statut :** ✅ **CORRIGÉE le 28 août 2026 — lot 3.2, puis fermée pour de bon par la décision D33.**

Premier temps : cesser de nuire. `IPaymentGateway.SupportsRefund` déclare la limite ; les quatre adaptateurs répondent `false` ; `GatewayRefundResult.Transient` distingue la panne passagère du refus définitif ; le gestionnaire d'`OrderCancelled` **ne lève plus** sur un refus métier et ne rejoue que sur `Transient`.

Second temps, et c'est celui qui rembourse réellement le client : **décision D33**. Implémenter le remboursement chez FedaPay est impossible — il n'expose aucune API pour cela. L'argent revient donc au client sur **son portefeuille** (`ICustomerWalletApi.CreditRefundAsync`, appelé par `RefundPaymentCommandHandler` quand `SupportsRefund` est faux) ; le virement vers son Mobile Money est une **demande distincte**, exécutée et marquée payée à la main par un administrateur. Stripe, qui sait rembourser, continue de rendre l'argent sur la carte : le routage se fait sur la capacité déclarée, pas sur une règle globale.

La règle vit dans **payment-service** et non dans return-refund, parce que trois flux remboursent un client — le retour, l'annulation de commande, le geste administratif direct — et que les trois passent par `RefundPaymentCommand`.

**Le garde-fou de démarrage a été RETIRÉ**, avec le drapeau `Payments:AllowGatewaysWithoutRefund`. Sa prémisse — « aucun client payé par eux ne sera jamais remboursé automatiquement » — a cessé d'être vraie. Ce qui reste est un avertissement au démarrage disant par quel chemin l'argent repart.

**Ce que cela ne ferme pas** : la plateforme porte désormais une dette envers ses clients (la somme des soldes est de l'argent dû), aucun rapprochement avec la trésorerie réelle n'existe, et aucune règle de péremption n'est posée — un solde de portefeuille est une créance. Les deux points sont nommés en D33.

**Severity:** CRITICAL
**Domain:** Commun
**Service:** Payment Service
**Saga:** Remboursement

**Problem:** le remboursement est codé en dur à `Success:false` sur FedaPay, MTN, Moov et PayPal. Le handler `OrderCancelled` **lève une exception** sur cet échec : le message est rejoué indéfiniment.
**Evidence:** `Real/FedaPayHttpGateway.cs:104` ; `RefundPaymentOnOrderCancelledHandler.cs:112`
**Expected:** annulation → remboursement effectif ou échec traité et tracé.
**Actual:** aucun remboursement ne part ; la file se remplit d'un message qui ne passera jamais.
**Impact:** **client jamais remboursé**, et saturation progressive du consommateur.
**Recommended fix:** implémenter le remboursement chez chaque fournisseur ; en attendant, ne pas lever — enregistrer un échec explicite et sortir la commande de la file (DLQ).
**Tests required:** contrat par fournisseur ; test « échec de remboursement ne bloque pas la file ».

---

## ISSUE-010
**Severity:** CRITICAL
**Domain:** Commun
**Service:** Payment / Wallet
**Saga:** Versement vendeur

**Problem:** `SimulatedPayoutGateway` est enregistré **en production**.
**Evidence:** `PaymentsModuleInstaller.cs:217`
**Expected:** un versement réel, ou un refus de démarrer si aucun fournisseur n'est configuré.
**Actual:** le retrait est clôturé « payé », le solde du vendeur est débité, **aucun argent ne part**.
**Impact:** le vendeur perd son solde sans rien recevoir, et le système croit l'avoir payé. Irréversible sans reprise manuelle.
**Recommended fix:** refuser le démarrage si la passerelle réelle n'est pas configurée — le dépôt applique déjà ce principe ailleurs (`AddXGrpcClient` lève à la construction de l'hôte). Appliquer la même règle ici.
**Tests required:** test de démarrage : sans configuration de versement, l'hôte ne démarre pas.

---

## ISSUE-011
**Statut :** ✅ **CORRIGÉE le 28 août 2026 — lot 3.2.** `GatewayEvent` porte désormais `RefundAmount`, `TotalRefundedAmount`, `RefundCurrency` et `RefundReference` (facultatifs, convention additive D32). Un webhook de remboursement **sans montant** est refusé explicitement (`payments.refund_amount_missing`) au lieu d'être imputé comme total. Corrigé au passage : `StripeHttpGateway.ExtractReference` préférait `ch_…` au `payment_intent`, donc rapprochait le remboursement sur la mauvaise référence.

**Severity:** CRITICAL
**Domain:** Commun
**Service:** Payment Service
**Saga:** Remboursement

**Problem:** un webhook de remboursement **partiel** est enregistré comme remboursement **total** : `GatewayEvent` ne porte aucun montant.
**Evidence:** `GatewayConfirmationCommands.cs:34`
**Expected:** le montant remboursé est lu depuis le webhook et imputé.
**Actual:** un remboursement de 5 000 F sur une commande de 50 000 F clôt la commande comme intégralement remboursée.
**Impact:** **perte d'argent directe** et comptabilité fausse.
**Recommended fix:** ajouter le montant à `GatewayEvent` et imputer partiellement.
**Tests required:** webhook partiel → solde restant correct ; deux partiels successifs → cumul correct.

---

## ISSUE-012
**Statut :** ✅ **CORRIGÉE le 28 août 2026 — lot 3.2.** `RefundRetryWorker` et `ExpireReturnsWorker` sont implémentés : la décision écrit un remboursement `Pending`, le balayage le reprend et envoie `ExecuteRefundCommand`. Cinq tentatives, puis `ManualReview` — jamais de boucle infinie. `BeginRefund` incrémente `Version` (jeton de concurrence) **avant** l'appel au prestataire : deux exécutants concurrents ne peuvent pas partir ensemble. `OutboxPublisherWorker` a été **supprimé**, pas implémenté : `AddOutboxProcessor<ReturnRefundDbContext>()` draine déjà la file, et un second drain aurait publié chaque message deux fois.

**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Return & Refund Service
**Saga:** Retour / remboursement

**Problem:** `ExecuteRefundCommand` n'a **aucun émetteur**. Aucun handler d'événement n'est enregistré. Aucun remboursement n'est jamais versé.
**Evidence:** `ReturnLifecycleCommands.cs:17` ; `ReturnRefundModuleInstaller.cs:57-87` ; `ReturnRefundWorkers.cs:12-41` (coquilles vides)
**Expected:** décision de remboursement → exécution → `ReturnStatus.Refunded`.
**Actual:** la décision est écrite en base et ne déclenche rien. `Refunded` est inatteignable.
**Impact:** **aucun remboursement de la plateforme n'aboutit**, quelle que soit la décision prise.
**Recommended fix:** implémenter les workers et raccorder la commande à la décision.
**Tests required:** E2E « décision de remboursement → argent versé → statut `Refunded` ».

---

## ISSUE-013
**Statut :** ✅ **CORRIGÉE le 28 août 2026 — lot 3.2.** `ReturnStateMachine` refuse `from == to`. `TotalRefunded()` compte `Pending` et `Processing` — les ignorer laissait deux décisions successives lire chacune « rien encore remboursé ». L'unicité en base est portée par l'index posé au lot 3.1 sur `refunds.IdempotencyKey`, la clé `return:{Id}:refund:{n}` embarquant déjà l'identifiant du dossier.

**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Return & Refund Service
**Saga:** Retour / remboursement

**Problem:** double remboursement possible : la machine à états autorise `from == to`, et `TotalRefunded()` ignore les remboursements `Pending`.
**Evidence:** `Domain/Policies/ReturnStateMachine.cs:29`
**Expected:** une transition vers l'état courant est refusée ; le cumul remboursé inclut les demandes en cours.
**Actual:** deux exécutions concurrentes voient chacune « rien encore remboursé » et remboursent chacune la totalité.
**Impact:** **double remboursement**, argent perdu.
**Recommended fix:** interdire `from == to` ; compter les `Pending` dans `TotalRefunded()` ; ajouter une contrainte d'unicité en base sur (retour, tentative).
**Tests required:** test de concurrence — deux exécutions simultanées, un seul remboursement.

---

## ISSUE-014
**Statut :** ✅ **CORRIGÉE le 28 août 2026 — lot 3.2.** Order-service ne possède pas les retours : il n'avait aucune source, d'où le zéro en dur. Il en a une désormais. `ReturnRefundedIntegrationEvent` porte, en champs **facultatifs** (D32), les lignes reprises et le total remboursé du dossier ; `RecordReturnSettlementOnRefundHandler` les inscrit dans l'agrégat commande (tables `ordering.order_return_settlements` / `…_lines`, migration `20260828000600`) ; `GetOrderReturnContextAsync` répond enfin `order.ReturnedQuantityFor(ligne)` et `order.RefundedAmount`. Les valeurs sont **cumulées par dossier et posées, non additionnées** : un rejeu Kafka n'impute rien de plus, et un message hors séquence ne fait pas reculer le compteur.

**Order-service ne voit que les remboursements ABOUTIS.** Entre l'ouverture d'un dossier et son versement, il ne sait rien : deux dossiers ouverts en parallèle sur la même ligne seraient passés tous les deux. Cette moitié est fermée côté return-refund, qui possède ses dossiers en cours et les compte (`ListOpenQuantitiesByOrderAsync`, appelée à la création **et** à la décision de remboursement).

**Le plafond ne compte plus `TotalRefunded()`.** Maintenant qu'`AlreadyRefundedAmount` déduit les versements aboutis, les repasser à `RefundCalculationPolicy.Validate` les compterait deux fois et refuserait un remboursement légitime. Seul l'engagement **non abouti** du dossier (`Pending`/`Processing`) y est ajouté ; les aboutis pèsent par `RefundBreakdown.PreviousRefunds`, donc dans le premier contrôle.

**Rien n'est rétroprojeté.** Les tables naissent vides ; les retours antérieurs à la migration n'y figurent pas. Sur une base déjà exploitée, les commandes concernées restent trop permissives jusqu'à un rattrapage — qui exige de lire la base de return-refund, ce qu'une migration d'order-service ne peut pas faire.

Tests : `tests/HBA.Returns.UnitTests` — dont « second retour sur le même article → refus », le test que cette fiche exige.

**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Order Service (API module)
**Saga:** Retour / remboursement

**Problem:** `AlreadyReturnedQuantity: 0` et `AlreadyRefundedAmount: 0m` sont codés en dur dans la réponse consommée par return-refund.
**Evidence:** `OrderingModuleApi.cs:54,71`
**Expected:** les quantités déjà retournées et les montants déjà remboursés sont lus depuis la commande.
**Actual:** chaque nouvelle demande repart de zéro.
**Impact:** un même article peut être retourné et remboursé **autant de fois que voulu**.
**Recommended fix:** calculer réellement ces deux valeurs depuis les retours passés.
**Tests required:** second retour sur le même article → refus.

---

## ISSUE-015
**Statut :** ✅ **CORRIGÉE le 30 août 2026 — lot 3.3.** `MarkPayoutFailedCommand` + route admin `POST /api/financial/settlements/{batchId}/payouts/{payoutId}/failed` : le portefeuille du vendeur est recrédité, une contre-écriture part au grand livre, et les gains du lot sont dé-soldés (`Unsettle()`) pour redevenir payables dans un lot ultérieur. Calqué pas à pas sur `CancelSettlementBatchCommandHandler`, qui faisait déjà cela pour un lot entier.

**`MarkPayoutFailed` ne gardait rien** : elle écrasait le statut quel qu'il fût. Elle refuse désormais la transition depuis `Paid` — l'argent est parti, recréditer le ferait sortir deux fois — et est idempotente depuis `Failed`.

**Le motif d'échec n'est pas persisté** : `Payout` n'a pas de colonne pour lui, il ne vit que dans le journal. L'administration ne peut pas afficher pourquoi un virement a échoué.

**Un virement `Paid` puis rejeté par l'opérateur n'a aucun chemin automatique** — refusé en 409 par construction. L'écriture est manuelle.

La route n'est pas relayée par la passerelle, comme `.../paid` : les gestes d'administration du règlement ne sont atteignables que depuis le réseau interne.

**Severity:** CRITICAL
**Domain:** Commun
**Service:** Wallet Service
**Saga:** Versement vendeur

**Problem:** `SettlementBatch.MarkPayoutFailed` n'a aucun appelant. Un virement de lot refusé n'est jamais compensé ; `PayoutStatus.Failed` est un état mort.
**Evidence:** `Domain/Batches/SettlementBatch.cs:144`
**Expected:** virement refusé → lot marqué en échec → solde vendeur restitué → nouvelle tentative possible.
**Actual:** le vendeur est débité, le virement échoue, rien ne le signale ni ne le répare.
**Impact:** **saga financière sans compensation.**
**Recommended fix:** appeler `MarkPayoutFailed` sur retour d'échec de la passerelle et restituer le solde.
**Tests required:** E2E « virement refusé → solde restitué → statut `Failed` ».

---

# C. Autorisation et fuites de données

## ISSUE-016
**Severity:** CRITICAL
**Domain:** Transverse
**Service:** driver, tracking, route, dispatch, proof, delivery-pricing, menu, availability, kitchen-prep, food-review
**Saga:** Toutes

**Problem:** **dix services** n'appellent ni `AddHbaService` ni `UseAuthentication`/`UseAuthorization`. Toute leur surface est publique, y compris `POST /api/v1/admin/delivery-pricing/rules` et les routes `/internal/v1/*`.
**Evidence:** `HBA.Food.Review.Api/Program.cs:1-13` ; `DeliveryPricingEndpoints.cs:47` ; idem pour les huit autres
**Expected:** authentification obligatoire, politique admin sur les routes d'administration.
**Actual:** aucune vérification. La tarification de livraison de toute la plateforme est modifiable sans jeton.
**Impact:** atténué aujourd'hui — ces services ne sont pas routés par YARP et ne publient pas de port. **L'atténuation est une coïncidence de déploiement, pas un contrôle.** Le jour où l'un d'eux est exposé, il l'est nu.
**Recommended fix:** appeler `AddHbaService` dans les dix `Program.cs` ; politique admin explicite sur les routes d'administration.
**Tests required:** test d'autorisation par service : route d'écriture sans jeton → 401.

---

## ISSUE-017
**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Return & Refund Service
**Saga:** Retour vendeur

**Problem:** `approve`, `reject`, `inspection`, `refund-decision`, `receive` n'exigent que le rôle `Seller`. **Aucun handler ne compare le vendeur du dossier à l'appelant.**
**Evidence:** `SellerReturnsEndpoints.cs:14-22,32-51`
**Expected:** le vendeur n'agit que sur ses propres retours.
**Actual:** tout vendeur inscrit approuve, inspecte et **chiffre le remboursement** du dossier d'un concurrent.
**Impact:** **fuite inter-vendeur et sabotage financier.** Un concurrent peut rembourser à la place d'un autre, ou refuser ses retours.
**Recommended fix:** garde d'appartenance sur les cinq routes, avec la règle 403/404 maison.
**Tests required:** vendeur A sur dossier de B → 403 ; test par route.

---

## ISSUE-018
**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Return & Refund Service
**Saga:** Retour vendeur

**Problem:** `Guid sellerId` est lié depuis la **query string** — le groupe `/api/v1/seller/returns` ne comporte pas de `{sellerId}`.
**Evidence:** `SellerReturnsEndpoints.cs:26`
**Expected:** l'identifiant de vendeur vient du jeton.
**Actual:** `?sellerId=<autre>` rend le carnet de retours complet d'un concurrent.
**Impact:** fuite de données commerciales en une requête, sans outil.
**Recommended fix:** dériver `sellerId` du jeton, jamais de l'entrée client.
**Tests required:** `?sellerId` d'un tiers → 403.

---

## ISSUE-019
**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Return & Refund Service
**Saga:** Retour client

**Problem:** `CreateAsync` ne lit aucune identité ; le `CustomerId` est déduit de la commande désignée. `GET /{id}` et `/timeline` non plus.
**Evidence:** `CustomerReturnsEndpoints.cs:15-16,25,34,44`
**Expected:** le client n'ouvre un retour que sur ses propres commandes.
**Actual:** un retour peut être ouvert, lu et annulé sur la commande de n'importe qui.
**Impact:** fuite de données personnelles et sabotage de commande.
**Recommended fix:** identité obligatoire, recoupée avec l'acheteur de la commande.
**Tests required:** retour sur commande d'autrui → 403.

---

## ISSUE-020
**Severity:** CRITICAL
**Domain:** Commun
**Service:** Media Service
**Saga:** Transverse

**Problem:** `DELETE /api/v1/media/{id}` n'a ni `ClaimsPrincipal` ni contrôle de propriétaire.

> **Rectification du 21/08, après lecture du code lors de la correction.** La route **est authentifiée** : le groupe entier passe par `MapAuthenticatedGroup`. Le défaut n'est donc pas un accès anonyme mais un **IDOR** — tout compte *inscrit* pouvait effacer n'importe quel média. La gravité reste CRITICAL (pièces KYB, preuves de livraison), la formulation initiale était trop large. Même rectification pour ISSUE-021.
**Evidence:** `MediaEndpoints.cs:172`
**Expected:** seul le propriétaire ou un administrateur supprime.
**Actual:** tout compte inscrit efface photos produit, pièces KYB et preuves de livraison.
**Impact:** **destruction de données probantes** — les pièces KYB et les preuves de livraison sont des éléments de preuve juridique.
**Recommended fix:** authentification + contrôle de propriété + suppression logique plutôt que physique sur les pièces probantes.
**Tests required:** suppression par un tiers → 403 ; suppression d'une preuve → refusée.

---

## ISSUE-021
**Severity:** CRITICAL
**Domain:** Commun
**Service:** Media Service
**Saga:** KYB / documents

**Problem:** `GET /media/{id}/download-url` délivre une URL signée sur n'importe quel fichier privé à tout compte inscrit ; `CreateSignedUrlAsync` ignore `Visibility` et ne plafonne pas `expiresIn`.
**Evidence:** `MediaEndpoints.cs:56` ; `MediaModuleApi.cs:101`
**Expected:** URL signée uniquement au propriétaire, durée plafonnée par le serveur.
**Actual:** CNI, permis, RCCM, pièces KYB accessibles, pour une durée choisie par le demandeur.
**Impact:** **fuite de pièces d'identité.** Le plus lourd des défauts en conséquences réglementaires.
**Recommended fix:** contrôle de propriété, respect de `Visibility`, plafond serveur sur la durée.
**Tests required:** pièce KYB d'autrui → 403 ; `expiresIn` excessif → plafonné.

---

## ISSUE-022
**Statut :** ✅ **CORRIGÉE** le 21 août 2026 — décision D27 appliquée. `TokenRevocationMiddleware` vérifie le jeton auprès d'identity **à la passerelle**, après l'authentification et après le limiteur de débit ; verdict mémorisé 30 s par empreinte SHA-256 du jeton ; **échec ouvert** avec journal `Critical`. Le sursis passe de 15 minutes à 30 secondes.

**Severity:** CRITICAL
**Domain:** Commun
**Service:** Identity Service
**Saga:** Authentification

**Problem:** `IdentityModuleApi.ValidateAccessTokenAsync` n'est appelée par **aucun** service : le `security_stamp` n'est jamais vérifié.
**Evidence:** `IdentityModuleApi.cs:74` + absence d'appelant
**Expected:** déconnexion, changement de mot de passe et suspension invalident immédiatement les jetons en cours.
**Actual:** le jeton reste valide jusqu'à son expiration naturelle — **15 minutes**.
**Impact:** un compte compromis, suspendu ou déconnecté conserve tous ses droits pendant un quart d'heure. Le mécanisme de révocation existe et ne sert à rien.
**Recommended fix:** appeler la validation dans le socle d'authentification, avec un cache court.
**Tests required:** suspension → appel suivant refusé ; changement de mot de passe → anciens jetons refusés.

---

## ISSUE-023
**Severity:** CRITICAL
**Domain:** Food
**Service:** food-review-service
**Saga:** Avis food

**Problem:** aucune authentification ; le `CustomerId` est lu dans le corps de la requête.
**Evidence:** `HBA.Food.Review.Api/Program.cs:1-13`
**Expected:** avis signé par l'utilisateur authentifié, après consommation vérifiée.
**Actual:** n'importe qui poste un avis au nom de n'importe qui.
**Impact:** manipulation de la réputation des restaurants.
**Recommended fix:** authentification + identité issue du jeton + condition de commande livrée.
**Tests required:** avis sans jeton → 401 ; avis au nom d'un tiers → 403.

---

## ISSUE-024
**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Seller Service (autorisation)
**Saga:** Toutes les actions vendeur

**Problem:** le **statut du vendeur n'entre jamais dans la décision d'autorisation**.
**Evidence:** `MerchantAccessApi.cs:60-121`
**Expected:** un vendeur `Suspended` perd immédiatement ses droits de vente.
**Actual:** un vendeur suspendu et toute son équipe continuent de publier des produits, d'ajuster le stock et de demander des retraits.
**Impact:** **la suspension est décorative.** C'est la principale mesure de police de la plateforme et elle n'a aucun effet.
**Recommended fix:** intégrer le statut vendeur dans `MerchantAccessApi` et l'inclure dans la clé de cache.
**Tests required:** vendeur suspendu → toutes les routes d'écriture en 403.

---

## ISSUE-025
**Statut :** ✅ **CORRIGÉE** le 21 août 2026 — `SellerSuspendedOfferWithdrawalHandler` et `SellerSuspensionLiftedOfferReinstatementHandler` consomment l'événement côté catalog. Le volet inventory est écarté **explicitement** : `InventoryItem` ne connaît pas les vendeurs. Voir `RESTE_A_FAIRE.md`.

**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Seller / Catalog
**Saga:** Suspension vendeur

**Problem:** suspendre un vendeur — y compris par rejet du KYB — ne retire rien de la vente. `SellerSuspendedIntegrationEvent` n'a qu'un seul consommateur : une notification.
**Evidence:** `Seller.cs:405-420` ; `CatalogModuleInstaller.cs:259-260`
**Expected:** suspension → produits dépubliés → offres retirées de la vente.
**Actual:** les produits restent achetables. Un client peut commander chez un vendeur rejeté au KYB.
**Impact:** vente au nom d'un vendeur non vérifié ; exposition réglementaire.
**Recommended fix:** consommer l'événement côté catalog et inventory.
**Tests required:** E2E « suspension → offres non achetables ».

---

## ISSUE-026
**Statut :** ✅ **CORRIGÉE le 2 septembre 2026 — lot 4.2.** Les cinq routes existent sous `/api/sellers/{sellerId:guid}/orders`, chacune gardée par SA permission (`ORDER_CONFIRM`, `ORDER_REJECT`, `ORDER_MARK_PREPARING`, `ORDER_MARK_READY`, `ORDER_CANCEL`) **et** par une garde d'appartenance réelle — `DenyUnlessOwnSellerAsync`, extraite de `ListBySellerAsync` qui l'utilisait déjà. Le refus exige un motif : c'est la seule trace de pourquoi une commande payée ne sera pas honorée.

**Un refus vendeur ne rembourse encore PERSONNE.** Il lève `SellerOrderRefusedIntegrationEvent` — type neuf (D32), portant les lignes et le `ShipFromLocationId` sans lequel personne ne peut rendre le stock — et **cet événement n'a aucun consommateur**. Trois gestes manquent : remettre en rayon (inventory), rembourser une FRACTION de commande (financial ne sait faire que le tout ou rien — c'est le vrai trou), et prévenir l'acheteur. La lacune est écrite sur l'agrégat, sur l'événement, sur la route et dans l'installateur.

**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Seller Service / Order Service
**Saga:** Commandes vendeur

**Problem:** les cinq permissions `ORDER_CONFIRM`, `ORDER_REJECT`, `ORDER_MARK_PREPARING`, `ORDER_MARK_READY`, `ORDER_CANCEL` ne gardent **aucune route**. Le rôle `ORDER_MANAGER` ne peut que lire.
**Evidence:** `OrderEndpoints.cs:119-120` ; `MerchantPermission.cs:40-142`
**Expected:** le vendeur confirme, prépare et déclare prêtes ses commandes.
**Actual:** aucune de ces actions n'existe côté API. Le rôle promet une autorité qu'il n'exerce pas.
**Impact:** **le parcours vendeur s'arrête à la réception de la commande** ; rien ne peut avancer vers la livraison.
**Recommended fix:** dépend d'ISSUE-027 — sans agrégat `SellerOrder`, ces routes ne peuvent pas être écrites honnêtement.
**Tests required:** une fois `SellerOrder` construit, test par permission et par transition.

---

## ISSUE-027
**Statut :** ✅ **CORRIGÉE le 2 septembre 2026 — lot 4.2, décision D29.** L'agrégat `SellerOrder` existe : identité forte, lignes figées, verrou optimiste, et les états `AwaitingConfirmation → Confirmed → Preparing → ReadyForPickup → HandedOver`, plus `Rejected` et `Cancelled`.

Le découpage se fait **à la CONFIRMATION de la commande**, pas au passage : avant le paiement il n'y a rien qu'un vendeur puisse faire, et lui montrer une commande non payée l'inviterait à préparer un colis pour un paiement qui échouera. Il réutilise `Order.SellerLineGroups()` — extrait de `BuildSellerShares()` — pour que le découpage et la répartition annoncée aux vendeurs partagent la MÊME définition, filtre des lignes de repas compris. Un test le vérifie en comparant les deux.

**`SellerOrder` ne remplace pas le cycle de vie d'`Order`, il s'y ajoute.** La saga paiement → confirmation → livraison est intacte : la seule modification d'`Order` est une extraction de méthode, sans changement de comportement.

**`HandedOver` n'a aucune route, et c'est délibéré** : la remise au livreur est constatée par le livreur, pas déclarée par le vendeur. Une part reste donc `ReadyForPickup` jusqu'à ce que delivery-service sache la clore.

**Aucun rattrapage pour les commandes déjà confirmées.** La donnée existe, mais aucun statut ne serait vrai — `AwaitingConfirmation` poserait un bouton « refuser » sur une vente conclue, `HandedOver` affirmerait un fait non constaté. Un historique plausible et faux est indiscernable du vécu six mois plus tard.

**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Order Service
**Saga:** Achat marketplace / Commandes vendeur

**Problem:** l'agrégat `SellerOrder` **n'existe pas**. `OrderingModuleApi.cs:66` renvoie `SellerOrderId: null` en dur.
**Evidence:** aucune classe `SellerOrder` dans le dépôt ; `OrderingModuleApi.cs:66`
**Expected:** une commande multi-vendeurs se décompose en une commande par vendeur, avec son propre état.
**Actual:** il n'y a qu'un état global. « Confirmée » n'a pas de sens à l'échelle où le vendeur agit.
**Impact:** bloque ISSUE-026, la préparation, la remise au livreur et le calcul des parts. **Seul défaut de cet audit qui exige de construire un agrégat, pas de corriger du code.**
**Recommended fix:** construire `SellerOrder` (états, transitions, permissions, événements), puis raccorder les routes vendeur et la création de course.
**Tests required:** commande à deux vendeurs → deux `SellerOrder` indépendants ; l'un confirme sans affecter l'autre.

---

## ISSUE-028
**Statut :** ✅ **CORRIGÉE — lot 5.1.** `UsePostgresRowVersion()` sur `Delivery`, plus un index unique **partiel** : `ux_deliveries_engaged_driver` sur `AssignedDriverId`, restreint aux cinq états ENGAGÉS. L'index sec que la fiche demandait aurait été faux — il aurait interdit à un livreur d'avoir deux courses de toute son histoire. `DriverAssigned` reste hors filtre : le dispatch propose à plusieurs candidats, et une proposition n'est pas un engagement. Le jeton est RÉELLEMENT évalué (pas de piège `StockVersion` : `AcceptByDriver` écrit trois colonnes de la ligne parente). `manual-assign` et `AcceptOffer` ne sont plus anonymes.

**Trouvaille en chemin, et elle dépasse ce lot :** `ConcurrencyExceptionHandler` — cité par `OrderConfiguration`, `InventoryItemConfiguration`, `PaymentConfiguration`, `WalletConfigurations` et par l'encadré d'`UsePostgresRowVersion` comme traduisant le conflit optimiste en 409 — **n'a JAMAIS existé**. `DbUpdateConcurrencyException` dérive de `DbUpdateException` mais son inner n'est pas une `PostgresException` : le filtre « doublon » ne mordait pas, et **tout verrou optimiste du dépôt ressortait en 500**. Le client — souvent une application qui réessaie sur 5xx — relançait une écriture perdante, et l'exploitation comptait en panne serveur une garde qui avait fait son travail. Corrigé dans `ServiceMiddlewares`, avant le bloc doublon (l'ordre est obligatoire), et les cinq citations rectifiées.

**Severity:** CRITICAL
**Domain:** Delivery
**Service:** dispatch-service / delivery-service
**Saga:** Affectation de course

**Problem:** deux livreurs peuvent accepter la même mission. `DispatchStore.AssignAsync:99` écrase sans relire ; côté `delivery-service`, `Delivery` n'a **aucun jeton de concurrence** et aucun index unique sur `AssignedDriverId`. `manual-assign` et `AcceptOffer` sont de surcroît anonymes.
**Evidence:** `DispatchStore.cs:99` ; absence de `IsConcurrencyToken` sur `Delivery` (seul `ReturnRequest` en a un dans tout le dépôt)
**Expected:** la première acceptation gagne, la seconde est refusée.
**Actual:** deux livreurs partent chercher la même commande ; les deux la réclament.
**Impact:** double rémunération, conflit terrain, commande remise deux fois ou pas du tout.
**Recommended fix:** jeton de concurrence sur `Delivery` + contrainte d'unicité en base, comme pour `orders.CartId`.
**Tests required:** test de concurrence — deux acceptations simultanées, une seule réussit.

---

## ISSUE-029
**Statut :** ✅ **CORRIGÉE — lot 5.2.** La position du livreur alimente enfin `IDriverLocationCache` : `POST /api/deliveries/mine/position` → `ReportDriverPositionCommand` → `SetAsync`. Le choix de porter cette route par delivery-service et non par tracking-service est motivé : le cache est un port de ce module, tracking est encore une maquette en mémoire, et faire dépendre l'attribution des courses d'un service qui perd son état au redémarrage aurait été pire que le défaut. Redis reçoit chaque battement ; `LastKnownPosition` n'est recopié qu'à cinq minutes d'intervalle.

**Severity:** CRITICAL
**Domain:** Delivery
**Service:** delivery-service
**Saga:** Affectation de course

**Problem:** `IDriverLocationCache.SetAsync` n'a aucun appelant : le cache de positions n'est jamais alimenté.
**Evidence:** absence d'appelant de `SetAsync`
**Expected:** les positions des livreurs alimentent la recherche de candidats.
**Actual:** la recherche ne trouve jamais personne. **Aucune course n'est jamais proposée à aucun livreur.**
**Impact:** le domaine livraison est inerte, même là où il est correctement écrit.
**Recommended fix:** alimenter le cache depuis tracking-service, une fois celui-ci réel.
**Tests required:** E2E « position publiée → livreur candidat ».

---

## ISSUE-030
**Statut :** ✅ **CORRIGÉE — lot 5.2, décision D36.** driver-service a une identité, une base et un dossier réels : inscription, pièces, véhicule, vérification, suspension. Le `DefaultDriverId` codé en dur — sur lequel opéraient les six routes `/me`, si bien que **tous les livreurs étaient le même livreur** — a disparu : l'identité vient du jeton.

Le découpage retenu est une décision, pas un provisoire : `drivers.driver_accounts` répond « a-t-elle le DROIT de livrer ? », `deliveries.drivers` répond « à qui proposer cette course, MAINTENANT ? ». Deux tables, deux propriétaires, reliées par l'événement `driver.dossier-verified`. La disponibilité a quitté driver-service pour delivery-service — elle gouverne le dispatch, elle doit être écrite là où il la lit, sinon deux écrivains.

✅ **`DriverSuspendedIntegrationEvent` a désormais un consommateur.** `WithdrawDriverOnDossierSuspended` (delivery-service, `DriverProjectionEventHandlers.cs`) passe la projection dispatchable en `Offline` dès que le dossier est suspendu : le livreur ne reçoit plus de proposition. Le gestionnaire lit la disponibilité AVANT de suspendre et journalise en `Critical` quand le livreur était `Busy` — `Driver.Suspend` ne refuse PAS un livreur en course, il le met hors ligne, et la course déjà acceptée continue.

**Ce que cela ne couvre pas :** la course en cours n'est ni réassignée ni annulée automatiquement — il faut une décision humaine. Et la suspension reste ASYNCHRONE : entre la décision côté dossier et l'arrivée de l'événement, une proposition peut encore partir. Pour une faute grave, un appel synchrone serait plus sûr, mais inverserait la dépendance.

**Severity:** CRITICAL
**Domain:** Delivery
**Service:** delivery-service / driver-service
**Saga:** Parcours livreur

**Problem:** la chaîne livreur est rompue en trois points : `AcceptByDriver`, les cinq commandes de progression et `RevokeAssignment` n'ont **aucun appelant**. Aucune inscription ni validation de livreur n'existe ; `DriverStore.cs:13` expose un `DefaultDriverId` codé en dur sur lequel opèrent les six routes `/api/v1/drivers/me*`.
**Evidence:** `DriverStore.cs:13` ; `DeliveryProgressCommands.cs:197` (échec ignoré silencieusement)
**Expected:** inscription → documents → vérification → ACTIF → offre → acceptation → progression → livré.
**Actual:** aucune étape n'est atteignable. `MarkDelivered` n'est jamais atteint.
**Impact:** **le livreur n'est jamais payé et la commande n'est jamais « livrée »** — donc l'avis, la clôture et le règlement du vendeur ne se déclenchent pas non plus.
**Recommended fix:** implémenter driver-service (inscription, documents, vérification) puis raccorder les commandes existantes de `delivery-service`, qui sont correctes.
**Tests required:** E2E complet du parcours livreur ; test de transition par état.

---

## ISSUE-031
**Statut :** ✅ **CORRIGÉE le 31 août 2026 — lot 3.5.** `ExpireStockReservationsWorker` balaie les réservations `Active` dépassées et les passe en `Expired`, par lots, en journalisant le **volume libéré** — sans ce chiffre, on ne saurait pas que le balayeur travaille ni combien de stock dormait. Une réservation `Confirmed` n'est jamais touchée, même expirée : elle est vendue.

**Au PREMIER démarrage après déploiement, le balayeur va libérer beaucoup.** `ExpiresAtUtc` n'ayant jamais été relue, toutes les réservations existantes sont dépassées. Le disponible de nombreux articles remontera d'un coup et des `StockReplenished` partiront en masse. À anticiper côté exploitation.

**Deux répliques d'inventory-service liraient le même lot** : le balayage ne pose pas de `SELECT … FOR UPDATE SKIP LOCKED`. `xmin` empêche la double écriture, pas le travail en double. Même contrainte que l'outbox, à traiter avant de passer à l'échelle.

**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Inventory Service
**Saga:** Achat marketplace

**Problem:** les réservations de stock expirées ne sont jamais libérées : `ExpiresAtUtc` est écrite et jamais lue ; aucun `BackgroundService` dans inventory.
**Evidence:** `Domain/Stock/StockReservation.cs:25` ; `InventoryItem.cs:71`
**Expected:** un balayeur libère les réservations dépassées.
**Actual:** toute réservation non confirmée immobilise le stock définitivement.
**Impact:** **le stock vendable s'érode à chaque panier abandonné.** Cumulatif et silencieux.
**Recommended fix:** `BackgroundService` de balayage, idempotent, avec journalisation du volume libéré.
**Tests required:** réservation expirée → libérée ; réservation confirmée → intouchée.

---

## ISSUE-032
**Statut :** ✅ **CORRIGÉE le 30 août 2026 — lot 3.4**, sur les quatre sites du motif « appel externe avant `SaveChangesAsync` ».

`PlaceOrderCommandHandler` : la persistance est encadrée, et un échec libère les réservations déjà obtenues avant de relayer l'exception. Chaque libération est isolée — Inventory peut être injoignable au moment même où la base l'est — et son échec est journalisé avec le SKU, seule prise pour rattraper à la main.

`CancelOrderCommandHandler` et `InspectReturnCommandHandler` : l'ordre est **inversé**, on persiste puis on appelle. L'arbitrage est explicite — du stock bloqué qu'on peut rendre coûte moins cher que du stock vendu deux fois qu'on ne peut pas fabriquer.

`RegisterReturnShipmentCommandHandler` : **l'inversion est impossible**, c'est la course qui produit l'identifiant qu'on veut écrire. L'échec est rendu visible à la place : l'identifiant de la course orpheline part en `Critical`. Corrigé au passage, un filtrage par motif qui fonctionnait par coïncidence — le type unifié du ternaire était toujours `Result<string>`, si bien que la branche « identifiant fourni » repassait par `ok.Value`.

**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Order Service
**Saga:** Achat marketplace

**Problem:** si `SaveChangesAsync` échoue après la boucle de réservation de stock, les réservations distantes restent, sans compensation et sans commande.
**Evidence:** `PlaceOrder/PlaceOrderCommandHandler.cs:276-300` — aucun `try/catch`
**Expected:** échec de persistance → libération des réservations déjà prises.
**Actual:** réservations orphelines, invisibles, jamais libérées (aggravé par ISSUE-031).
**Impact:** stock perdu sans trace ni vente.
**Recommended fix:** encadrer la boucle et compenser à l'échec ; le même motif « appel externe avant `SaveChangesAsync` » existe aussi dans `OrderLifecycleCommands.cs:223-234` et `ReturnLifecycleCommands.cs:154-159`.
**Tests required:** injection d'échec de persistance → réservations libérées.

---

## ISSUE-033
**Statut :** ✅ **CORRIGÉE le 1er septembre 2026 — lot 4.1, décision D28.** `PromotionPricingModuleApi` remplace `NeutralPricingModuleApi` côté marketplace et appelle RÉELLEMENT promotion-service (`IPromotionModuleApi` via le client gRPC). Les remises sont imputées **selon le financeur** — c'est tout l'objet de D28, et sans cela on rebranchait le défaut d'ISSUE-052 en croyant le corriger.

**Panne et coupon retenu sont traités différemment, et il le faut.** `CalculatePriceAsync` avale une panne de promotion-service, valorise sans remise et journalise : une panne de promotion ne doit pas devenir une panne de vente. `ValidateCouponAsync` **refuse** au contraire d'attacher un code qu'il n'a pas pu valider — l'accepter le ferait découvrir vide au checkout. Et `ReserveAsync`, qui débite le budget, n'avale rien.

**Le `NeutralPricingModuleApi` de food-cart est CONSERVÉ mais refuse de démarrer en production**, sans drapeau d'échappement — règle du dépôt appliquée aux vagues 0.3, 3.2 et 3.4. Brancher food sur promotion-service reste à faire.

**La remise est désormais CALCULÉE, elle n'est pas encore CONSOMMÉE.** Personne n'appelle `ReserveAsync` / `CommitAsync` / `ReleaseAsync` : le budget n'est jamais débité, aucun usage n'est jamais engagé. Le maillon manquant est au checkout, dans order-service.

**Severity:** CRITICAL
**Domain:** Marketplace / Food
**Service:** Cart Service / Food Cart Service
**Saga:** Achat

**Problem:** `NeutralPricingModuleApi` est la **seule** implémentation d'`IPricingModuleApi` du dépôt, enregistrée sans garde d'environnement dans les deux services de panier.
**Evidence:** `CartModuleInstaller.cs:44` ; `FoodCartModuleInstaller.cs:47`
**Expected:** une tarification réelle, adossée à promotion-service.
**Actual:** **tout code promo est refusé, toute remise vaut 0.** Toute la mécanique promotionnelle — champ panier, migration dédiée, report en commande, décompte à la confirmation — repose sur une interface sans fournisseur.
**Impact:** aucune campagne commerciale n'est possible ; promotion-service, complet, n'est appelé par personne.
**Recommended fix:** implémenter le fournisseur réel — **mais voir ISSUE-052 d'abord** : brancher promotion-service tel quel ferait supporter aux vendeurs les remises de la plateforme.
**Tests required:** coupon valide → remise appliquée ; coupon expiré → refusé ; imputation vérifiée.

---

## ISSUE-034
**Severity:** CRITICAL
**Domain:** Food
**Service:** API Gateway
**Saga:** Démarrage

**Problem:** `Services:FoodCart` et `Services:FoodOrder` sont `[Required]` avec `ValidateOnStart`, et absents de toute configuration.
**Evidence:** `ServicesOptions.cs:66-67` vs `apps/api-gateway/src/HBA.Gateway.Api/appsettings.json`
**Expected:** la passerelle démarre.
**Actual:** **la passerelle ne démarre pas.** Aucun service n'est joignable.
**Impact:** panne totale de la plateforme au démarrage. Le plus rapide à corriger de tout l'audit.
**Recommended fix:** ajouter les deux adresses dans `appsettings.json`, `docker-compose.dev.yml` et le configmap k8s ; `scripts/check-service-addresses.py` couvre déjà ce contrôle.
**Tests required:** test de démarrage de la passerelle en intégration.

---

# D. Anomalies HIGH

| # | Domaine | Service | Problème | Preuve | Correctif |
|---|---|---|---|---|---|
| ISSUE-035 | Marketplace | Order | Fuite inter-vendeur sur le carnet de commandes : le vendeur voit les lignes des autres vendeurs, plus l'adresse GPS et le téléphone de l'acheteur | `OrderQueries.cs:66-78` ; `OrderMapper.cs:9-43` | projeter uniquement les lignes du vendeur ; masquer les coordonnées jusqu'à la remise |
| ISSUE-036 | Marketplace | Seller | Retirer une permission d'un **rôle** met jusqu'à 2 min à mordre : `SellerRole` absent de l'éviction de cache | `SellersDbContext.cs:199-280` ; `SellersCacheKeys.cs:70-85` | ajouter `SellerRole` à `CollectCacheKeysToEvict` |
| ISSUE-037 | Commun | Identity | Le limiteur de débit partitionne sur l'IP de la passerelle (aucun `UseForwardedHeaders`) : 30 tentatives/min pour la plateforme entière ; `ProxyTrust:TrustAnyProxy = true` versionné | `AuthRateLimiter.cs:122` | `UseForwardedHeaders` + liste de proxys de confiance |
| ISSUE-038 | Commun | Payment | `GET /api/v1/payments/intents/{id}` sans garde de propriété, alors que sa jumelle historique l'a | `FinancialEndpoints.cs:433` vs `:225` | appliquer `PeutVoirLePaiement` |
| ISSUE-039 | Commun | Payment | `POST /payments` sans garde de propriété : un tiers crée un `Pending` sur la commande d'autrui et la bloque via `payments.already_exists` | `FinancialEndpoints.cs:258` ; `InitiatePaymentCommand.cs:10-16` | recouper l'acheteur de la commande avec l'appelant |
| ISSUE-040 ✅ | **Corrigee — lot 7.2.** `POST /api/v1/merchants/{sellerId}/members/{memberId}/ownership` : le transfert deplace le role systeme OWNER ET `Seller.UserId` dans une seule transaction, sous verrou consultatif — **et ce verrou ne tenait rien jusqu'au 22/08** : `LockSellerAsync` prenait `pg_advisory_xact_lock` hors de toute transaction, donc PostgreSQL le relâchait aussitôt, avant la première lecture du handler. Le commentaire invoquait « l'intercepteur de transaction du module », qui n'existe pas. Remplacé par `ExecuteUnderSellerLockAsync`, qui ouvre la transaction, tient le verrou autour de l'opération ENTIÈRE — résolution de l'acteur comprise, elle était faite hors verrou — et rend impossible de le prendre séparément. On ne transfere que SA PROPRE propriete — sans cette garde, un proprietaire pouvait en depouiller un autre sans retour possible. Le cedant garde `SELLER_ADMIN`. Step-up porte par la route, le groupe `members` ne l heritant pas. Ne couvre PAS le proprietaire reellement disparu : ce cas demande une decision produit. Constat d origine : Marketplace | Seller | `OWNERSHIP_TRANSFER` n'a aucune route : le rôle OWNER ne peut jamais changer de porteur, et `SELLER_CLOSE` étant `OwnerOnly`, le dossier devient inadministrable si le propriétaire disparaît | `SellerMember.cs:396-399` | route de transfert de propriété, avec step-up |
| ISSUE-041 ✅ | Marketplace | Seller | **Corrigée le 21/08/2026** — `StoreLifecycleCatalogHandlers` consomme fermeture, suspension et réouverture. Constat d'origine : fermer ou suspendre une **boutique** n'arrêtait aucune vente : les cinq événements `Store*` n'ont aucun consommateur, `IsSelling` n'est qu'affiché | `Store.cs:83,240-273` | consommer les événements côté catalog/inventory |
| ISSUE-042 ✅ | **Corrigee — lot 7.1.** 10 contextes allumes, 13 sur 24 tiennent desormais un journal : identity, payments, settlement, catalog, reviews, food, deliveries, delivery_pricing, drivers, ordering. Chacun avec sa migration ecrite a la main et son bloc de snapshot ; `check-migrations` rejoue les 24 a froid. Constat d origine : Transverse | 20 contextes sur 23 | `KeepsAuditTrail` faux : rôles, suspensions, captures, remboursements, retraits, modération, tarification ne laissent aucune trace — et `AuditQueries.cs:29-33` affirme le contraire | `ModuleDbContext.cs:61` ; `AuditQueries.cs:29-33` | activer l'audit sur les contextes sensibles ; corriger le texte mensonger |
| ISSUE-043 ✅ | **Corrigee — lot 7.1.** `AuditQueries.cs` et `SellersDbContext.cs` affirmaient tous deux que catalog, inventory et order tenaient un journal : aucun des trois n avait de surcharge ni de table. Les deux encadres disent maintenant ce qui est vrai. Et le journal ne se journalise plus lui-meme : `ConsumerInboxEntry` et `IdempotencyRecord` etaient audites a chaque message Kafka consomme. Nouveau controle `check-audit-trail.py`. Constat d origine : Marketplace | Catalog | Aucun acteur enregistré pour les gestes vendeur du catalogue | `CatalogEndpoints.cs:1215-1217` | enregistrer l'acteur sur chaque transition |
| ISSUE-044 ✅ | **Corrigée — lot 7.3.** `StockMovement` : cinq natures (`Received`, `Adjusted`, `Sold`, `TransferOut`, `TransferIn`), table `stock_movements`, deux routes vendeur gardées par `STOCK_MOVEMENT_VIEW` et `INVENTORY_TRANSFER` — le transfert est gardé **deux fois**, à la source et à la destination. Quatre signatures du domaine ont changé : `Receive`, `AdjustOnHand` et `ConfirmReservation` rendent le mouvement au lieu d'un `Result` nu, pour que l'appelant ne puisse plus omettre de l'écrire. Le transfert lit `Available`, pas `OnHand` : déplacer du stock réservé ferait échouer une commande en cours, dans un autre service, sans lien visible. Ne couvre PAS deux transferts concurrents depuis le même emplacement — c'est le verrou optimiste de l'article qui tranche, en conflit d'écriture, pas en refus métier. Constat d'origine : Marketplace | Inventory | aucun journal, aucun transfert, alors que deux permissions les promettent | `SellerRole.cs:449-457` | implémenter mouvements et transferts, ou retirer les permissions |
| ISSUE-045 ✅ | Marketplace | Inventory | **Corrigée le 31/08/2026** — `ReservationStatus { Active, Confirmed, Released, Expired }` avec ses horodatages ; **on ne supprime plus les lignes, on les marque**, donc l'historique du stock existe enfin. `Reserved` ne compte que les `Active`. Une réservation `Confirmed` n'est plus jamais relâchable : c'est du stock déjà vendu et décrémenté, le rendre à la vente serait le vendre deux fois — c'est exactement le danger que l'audit décrivait sur la route de libération. La table ne décroît plus : une purge datée reste à écrire. Constat d'origine : aucun statut | `StockReservation.cs` | ajouter un statut ; garder la route par l'état de la commande |
| ISSUE-046 ✅ | Marketplace | Inventory | **Corrigée le 31/08/2026, décision de HECTOR : refuser par défaut.** Les trois `return true;` sont inversés — pas de ligne de stock, pas de vente. L'offre ne portant aucune quantité à elle, une absence de ligne ne voulait pas dire « article non géré » mais « personne ne sait combien il y en a ». **Coût assumé et immédiat** : une offre dont le vendeur n'a pas saisi de stock cesse de se vendre. **La confirmation, elle, laisse passer** avec un journal `Critical` : refuser bloquerait une commande DÉJÀ PAYÉE, réservée sous l'ancienne règle. Branche de transition, à retirer quand la file est vidée. **Il reste à rendre la saisie de stock obligatoire à la publication d'une offre** — sinon un vendeur publie une offre invendable sans qu'on le lui dise. Constat d'origine : SKU non suivi réputé disponible sans limite | `InventoryModuleApi.cs:90-93,124-127` | refuser par défaut ; décrémenter à la confirmation |
| ISSUE-047 ✅ | **Corrigée — lot 7.4.** `StockCatalogHandlers.cs` : `WithdrawOffersOnStockDepletedHandler` et `ReactivateOffersOnStockReplenishedHandler`. Le nom du premier était déjà cité par `OfferStatus.cs:80` comme la réponse à « comment une offre repasse `OutOfStock` » — **la classe n'avait jamais existé**. Le commentaire renvoyait à du vide, et aucune offre n'est jamais sortie de la vente pour rupture. Les deux handlers filtrent sur `ShipFromLocationId` et sur le statut : seuls `Active` et `OutOfStock` sont concernés, une offre suspendue ou dépubliée ne se réactive pas parce qu'un carton est arrivé. Constat d'origine : Marketplace | Catalog | aucun consommateur de stock, le handler cité n'existe pas | `OfferCommands.cs:221` | brancher le consommateur |
| ISSUE-048 ✅ | **Corrigée — lot 7.4.** `PlaceOrderCommandHandler` interroge le catalogue pour chaque ligne Goods avant de construire les brouillons : offre disparue → `ordering.offer_unavailable`, offre non achetable → `ordering.offer_not_purchasable`, prix changé → `ordering.price_changed`. **Il REFUSE, il ne retarifie pas** : recalculer silencieusement débiterait le client d'un montant qu'il n'a jamais vu. Ne ferme PAS la fenêtre entre cette validation et la capture — elle est réduite, pas supprimée. La fermer demande un gel de prix côté catalogue, avec sa durée de vie. **Et le lot a cassé trois tests d'intégration**, pour la cinquième occurrence du même motif : `OrderIntegrationFixture` tient sa liste d'adresses à la main et n'avait pas `Services__Catalog` — l'hôte lève À LA CONSTRUCTION. La correction précédente n'avait couvert qu'une des **cinq** fabriques du dépôt ; `check-service-addresses.py` les compare désormais toutes au `Program.cs` qu'elles démarrent, déduit de leur `ProjectReference`. Et l'adresse seule n'aurait pas suffi : le catalogue n'est plus seulement exigé au démarrage, il est APPELÉ à chaque commande — d'où `CatalogueDeTest`, dont le prix par défaut est la MÊME constante que celle du panier (deux valeurs séparées feraient refuser chaque commande en `price_changed`). Le lot porte enfin ses tests : `RevalidationDuPrixTests`, cinq cas, lisant `error.details[].reason` et non `error.code` — normalisé, il rendrait les trois refus indistinguables. Constat d'origine : Marketplace | Cart | aucune revalidation du prix ni du statut « publié » entre l'ajout au panier et le paiement | `CartItem.cs:125` | revalider au passage de commande |
| ISSUE-049 ✅ | Marketplace | Return & Refund | **Corrigée le 28/08/2026** — le plafond est construit depuis la COMMANDE (`CapturedAmount`, `UnitPaidAmount`, `DeliveredQuantity`), plus depuis le montant saisi. Constat d'origine : plafond de remboursement autoréférentiel : le `RefundBreakdown` est construit **depuis** le montant demandé, donc `requested > calculated` ne peut jamais échouer | `ReturnLifecycleCommands.cs:169-181` ; `RefundCalculationPolicy.cs:15-19` | calculer le plafond depuis la commande, puis comparer |
| ISSUE-050 ✅ | Commun | Wallet | **Corrigée le 30/08/2026** — `SellerEarning.Reverse(...)` existe et est appelée par `ReverseEarningsOnReturnRefundedHandler` : quatre cumuls (`Reversed*Amount`) plutôt qu'un drapeau, car un retour est souvent PARTIEL ; le statut ne passe `Reversed` qu'à la reprise totale ; la reprise est bornée au reliquat de chaque montant ; et **tout ce qui somme un gain somme désormais le RESTANT** (lot de reversement, relevé vendeur, imputation d'un retrait). `ReverseEarningsOnOrderCancelledHandler` a le MÊME défaut sur un autre chemin — une commande annulée laisse ses gains payables — et reste ouvert. Constat d'origine : `EarningStatus.Reversed` sans appelant | `Domain/Wallets/WalletLedger.cs:51` | appeler la reprise à l'annulation/au remboursement |
| ISSUE-051 ✅ | Commun | Wallet | **Corrigée le 30/08/2026** — aucun site ne POUVAIT l'appeler : la contrepartie n'était modélisée nulle part (une confirmation de commande n'écrivait que des crédits, un remboursement que des débits). `WalletOwnerType.External` est ce compte de contrepartie — journal pur, sans solde stocké. La **confirmation de commande** (marchandise et restauration) est désormais UNE opération, contrepartie comprise, et `EnsureBalanced` la refuse si la répartition n'épuise pas le brut encaissé. Corrigé au passage : `ReleaseSellerAsync` n'écrivait QUE l'arrivée au disponible, jamais la sortie de l'en-cours. **La contre-passation d'un retour reste hors invariant, délibérément** — la borne par montant de `Reverse` et la part « port » d'un remboursement le feraient échouer sur des cas légitimes ; raisonnement complet dans l'encadré de `WalletLedger`. Constat d'origine : invariant écrit et testé, jamais appelé | `Domain/Wallets/WalletLedger.cs:51` | l'appeler dans le chemin d'écriture |
| ISSUE-052 ✅ | Commun | Promotion | **Corrigée le 01/09/2026 (D28)** — le financeur est une PART en points de base (`SellerFundedShareBps`), pas un enum à deux valeurs : le cofinancement s'exprime dès aujourd'hui, sans migration future, ce que D28 exigeait. `OwnerSellerId` s'y ajoute — une part dit « un vendeur paie », jamais lequel. Les campagnes existantes sont posées à « plateforme », et c'est la valeur VRAIE : les routes marchand étant fermées à `RequireAdmin`, aucun vendeur n'a jamais pu en créer. Constat d'origine :  **Aucune notion de financeur d'une remise.** Zéro occurrence de `Funder`/`PlatformFunded`/`MerchantFunded` dans le dépôt. `Promotion` n'a ni `SellerId` ni champ de financement, alors que le reste de la plateforme suppose la distinction (`CartContracts.cs:33` a `SellerDiscount` **et** `PlatformDiscount`, et wallet calcule le gain sur `UnitBasePrice - SellerDiscount`) — mais le seul producteur écrit `SellerDiscount: 0m` en dur | `Domain/Promotions/Promotion.cs` ; `CartContracts.cs:33` ; `OrderingModuleApi.cs` | **décision métier requise avant tout code** : qui supporte une remise de plateforme ? C'est aussi la cause du contournement `RequireAdmin` sur `/api/v1/merchant/promotions` — sans propriétaire, aucun contrôle d'appartenance n'est fondable |
| ISSUE-053 ✅ | Commun | Promotion | **Corrigée le 01/09/2026** — balayeur d'expiration des retenues, idempotent, journalisant le volume rendu. C'est ce qui rend vraie la promesse de l'encadré d'`IPromotionModuleApi` : « la compensation ne dépend pas de la bonne volonté ni de la survie de celui qui a demandé la retenue ». Constat d'origine :  Budget jamais rendu à l'expiration d'une réservation : aucun balayeur → campagne `Exhausted` sur des paniers abandonnés | `Domain/Promotions/Promotion.cs:337` | balayeur d'expiration |
| ISSUE-054 | Commun | Media | `InMemoryObjectStorage` enregistré en production (simple avertissement, pas de refus de démarrer) | `MediaModuleInstaller.cs:83` | refuser le démarrage sans stockage réel |
| ISSUE-055 | Commun | Notification | `NullPushSender` enregistré en production | `NotificationsModuleInstaller.cs:115` | idem |
| ISSUE-056 ✅ | Delivery | proof-of-delivery | **Corrigée — lot 5.3.** OTP aléatoire par course, **haché SHA-256** (ce service n'a besoin que de comparer, jamais de relire : le hachage est strictement plus sûr à fonctionnalité égale), expirant à 15 min, usage unique, 5 tentatives, comparaison à temps constant, et garde d'état — une preuve hors `DRAFT` n'est plus rejouable. Un `OtpChallenge` orphelin existait déjà avec les bons champs pendant que l'Application comparait à `"123456"` : il a été branché plutôt que réécrit. **Correction posée sur une maquette en mémoire** : réelle dans un processus, nulle entre deux. Constat d'origine : OTP universel `"123456"` ; `submit` sans garde d'état, rejouable sur une preuve déjà vérifiée | `ProofStore.cs:126` | OTP aléatoire par course, à usage unique, expirant |
| ISSUE-057 ✅ | Delivery | delivery-service | **Corrigée — lot 5.3.** `ProofPolicy.RequiredFor` décide à la création : paiement à la livraison → PIN, valeur ≥ 50 000 FCFA → PIN, sinon photo. **`None` n'est plus jamais produit.** Le paramètre `RequiredProof` a été **retiré du contrat** : le défaut n'était pas une négligence, c'était la valeur par défaut du contrat — donc reproductible par tout nouvel appelant. Les producteurs décrivent, le domaine conclut. Le seuil en FCFA deviendra faux sans rien dire en multi-devise. Constat d'origine : `RequiredProof` n'est renseigné par aucun producteur : toutes les courses naissent en `None`, donc livrables sans preuve | code de création de `Delivery` | fixer la politique de preuve à la création |
| ISSUE-058 ✅ | Delivery | tracking-service | **Corrigée — lot 5.3.** Authentification, identité prise dans le jeton et jamais dans le corps, contrôle d'appartenance. Le jeton de flux fabriqué et jamais vérifié a été **retiré, pas réparé** : un jeton qu'on ne vérifie pas est pire qu'aucun jeton, il fait passer la relecture. Conséquence assumée : l'acheteur ne peut plus suivre sa course, tracking ne sachant pas qui a commandé — préféré à une position GPS ouverte à tout inscrit. Constat d'origine : Suivi non réservé au livreur affecté : route anonyme, `driverId` lu dans le corps, jeton de flux fabriqué et jamais vérifié | `tracking-service` | authentification + contrôle d'affectation |
| ISSUE-059 ✅ | Food | food-order | **Corrigée — lot 6.1.** `InitiatePaymentCommand` porte l univers (ajout additif, defaut Marketplace) et `IPayableOrderReader` lit la commande chez Ordering OU chez FoodOrders. `PaymentOrderType.Food` est enfin PRODUIT : `ConfirmMealOrderOnPaymentCapturedHandler` existait, etait enregistre, filtrait sur `"FOOD"` — et aucun paiement ne portait jamais cette valeur. Ajout de `ReleaseEscrowOnMealOrderDeliveredHandler` : `MealOrderDelivered` etait publie sans aucun consommateur, l escrow d un repas n etait jamais leve. Constat d origine : Le paiement n'est jamais déclenché : `MealOrderPlaced` n'a que le panier comme consommateur ; `InitiatePayment` lit la commande marketplace et fige `PaymentOrderType.Marketplace` | `MealOrderDomainEventHandlers.cs:8-11` ; `InitiatePaymentCommandHandler.cs:64,122` | chemin de paiement dédié à `MealOrder` |
| ISSUE-060 ✅ | Food | food-cart | **Deja fermee** par un lot anterieur, verifiee pas a pas au lot 6.2 : `CloseFoodCartOnMealOrderPlacedHandler` clot le panier (avec garde de rejeu), `GetActiveByBuyerAsync` filtre sur `Active` donc un panier neuf s ouvre, et `HbaTopics` abonne bien food-cart-service au sujet de food-order. Constat d origine : Panier food jamais clos + idempotence par `CartId` → **le client ne peut plus jamais commander de repas** après une première tentative | food-cart / food-order | clore le panier à la commande |
| ISSUE-061 ✅ | Food | food-order | **Corrigee — lot 6.3.** `HoldMealOrderOnDeliveryCancelledHandler` est la porte d entree qui manquait : une course annulee met la commande de repas en arbitrage. Tout le reste existait deja — commande, gestionnaire, quatre gardes, colonne `ReviewReason`, index partiel — et RIEN ne l envoyait, donc les deux routes admin de SORTIE repondaient 409 a tous les coups. Elles n etaient de toute facon pas relayees : aucune route `/api/admin/*` n existait dans la passerelle (ajoutees). Constat d origine : `UnderReview` inatteignable : `PutMealOrderUnderReviewCommand` n'est envoyé nulle part → les deux routes admin d'arbitrage échouent toujours | `MealOrderLifecycleCommands.cs:30` | raccorder la commande aux routes |
| ISSUE-062 ✅ | **Corrigée — décision de HECTOR : e-mail + fournisseur SMS.** `OtpChallengeIssuedIntegrationEvent` (code sous `ProtectedCode`), publication **avant** `SaveChangesAsync` pour que l'outbox et le défi soient dans une seule transaction, `SendOtpCodeHandler` côté notification-service, `ISmsSender` + `DevelopmentSmsSender` + deux gardes de démarrage, et `verify-otp` émet enfin les jetons. **Aucun adaptateur SMS de production n'est écrit** : le fournisseur reste à choisir (contrat commercial, compte opérateur, expéditeur à homologuer). Les deux gardes refusent de démarrer plutôt que de laisser croire que les SMS partent. **CE LOT A OUVERT — ET REFERMÉ — UNE FAILLE DE STEP-UP.** `HasRecentAuthentication` ne lisait que `auth_time`, alors que son encadré annonçait « ce compte a-t-il saisi son MOT DE PASSE ». Sans conséquence tant que tout jeton naissait d'un mot de passe ; `verify-otp` est le premier chemin qui n'en exige aucun, et qui recevait un SMS aurait franchi les six gardes sensibles du dépôt — virement, compte bancaire, transfert de propriété vendeur, mouvements de stock. Le prédicat exige désormais `pwd` dans l'`amr` et refuse un `amr` absent ; trois tests dans `StepUpTests` l'éprouvent. Aucun chemin existant ne change. Ne couvre PAS : le verrouillage de compte sur mauvais code (seul le plafond du défi joue), ni un repli de canal. Constat d'origine : Commun | Identity | le code est généré puis jeté, `verify-otp` n'émet aucun jeton | chemin OTP d'identity | implémenter ou retirer la route |
| ISSUE-063 ✅ | **Corrigée — lot 7.6.** Les transformations `auth` et `auth-otp` réécrivaient vers `/api/identity/auth/…`. identity-service sert `/api/v1/auth` et **n'a jamais servi** `/api/identity/auth` : toute l'authentification passant par la passerelle rendait 404. Le commentaire de `HBA.Identity.Api/Program.cs` qui a causé cette écriture affirmait l'inverse ; il a été remplacé par la liste des quatre groupes réellement servis — la panne venait de la documentation, pas de la configuration. Constat d'origine : Transverse | API Gateway | réécriture vers un préfixe que le service ne sert plus | configuration YARP | corriger la réécriture |
| ISSUE-064 ✅ | **Corrigée — lot 7.5.** return-refund-service (21 routes), driver-service et delivery-pricing-service n'avaient **aucun cluster** : trois services entiers injoignables depuis Internet, alors que le `docker-compose` fournissait déjà leurs adresses — liées à rien. Les cinq endroits tenus d'accord pour chacun (adresse, propriété `ServicesOptions`, branche `Resolve`, clé `ServiceKeys.All`, cluster + route), plus les `SERVICES__*` du configmap : 19 clusters, 54 routes, 19 adresses. Nouveau contrôle `check-gateway.py`, éprouvé en retirant une propriété (refus) puis en la remettant. Constat d'origine : Marketplace | Return & Refund | aucune route de la passerelle ne mène à return-refund-service | configuration YARP | ajouter les routes |
| ISSUE-065 | Base de données | Return & Refund | **Zéro migration** alors que `Program.cs:21` appelle `Migrate()` : le schéma `return_refund` n'existe nulle part, le service est inopérant au démarrage | `services/marketplace/return-refund-service/` | écrire la migration initiale |
| ISSUE-066 | Base de données | Delivery Pricing | **Zéro migration** ; service également **absent de `HBA.sln`** | `services/delivery/delivery-pricing-service/` | migration initiale + inscription dans la solution |
| ISSUE-067 | Base de données | Order / Payment | Deux migrations **inertes** : `20260824000000_AddOrderPaymentId.cs` et `20260824010000_AddPaymentRefunds.cs` n'ont pas les attributs `[Migration]`/`[DbContext]`, donc EF ne les charge pas. La colonne `ordering.orders."PaymentId"` est dans le modèle et dans aucune base | les deux fichiers | ajouter les attributs ; `check-migrations.py` ne détecte pas ce cas (limite L2) |
| ISSUE-068 | Transverse | Tests | Aucun test sur les parcours critiques : domaine delivery **zéro test** (alors que `HBA.Delivery.Core.Application.csproj:22` déclare `InternalsVisibleTo("Delivery.UnitTests")`, projet inexistant) ; cart-service zéro ; food-cart et food-order zéro ; order-service 8 tests, tous sur des codes HTTP | `tests/` | socle de tests sur paiement, stock, idempotence, transitions |
| ISSUE-069 ✅ | Delivery | 4 services | **Corrigée — lot 5.4, décision D34.** Dix références croisées visaient un DOMAINE — les seules du dépôt dans ce cas — et formaient un cycle à quatre services. Cause réelle : **cinq fichiers mal classés**, vivant dans le dossier d'un service et déclarant le namespace d'un autre. Remis à leur place, aucun `using` n'a changé. Il reste une seule référence croisée, et c'est un contrat. `check-dockerfiles.py` passe de 5 projets manquants à **0**. Constat d'origine : ✅ **CORRIGÉE le 3 septembre 2026 — lot 5.4, décision D34.** Dix références croisées de domaine, formant un cycle entre `delivery`, `driver`, `dispatch` et `delivery-pricing`. Cinq fichiers mal classés en étaient la cause ; ils sont rentrés dans `HBA.Delivery.Core.Domain`, aucun `using` n'a changé. Il reste **une** référence croisée sous `services/delivery/`, et c'est un `*.Contracts` — la convention du dépôt. `check-dockerfiles.py` : 5 projets manquants → 0. — *Constat d'origine :* références `.csproj` croisées entre delivery, driver, dispatch et route : **non déployables séparément** | `.csproj` du domaine delivery | découpler par les contrats |
| ISSUE-070 ✅ | Delivery | 3 services | **Corrigée — lot 5.4.** Trois déclarations existaient bien, toutes dans driver-service : une vivante et deux mortes sans aucun appelant. Les mortes sont retirées, la vivante rapatriée auprès de la table qui la persiste. La table n'a **pas** été déplacée, et c'est devenu une décision (D36) plutôt qu'un provisoire : dossier du livreur et projection dispatchable sont deux questions différentes, deux tables, deux propriétaires, reliés par événement. Constat d'origine : ✅ **CORRIGÉE le 3 septembre 2026 — lot 5.4, décision D34,** pour la partie « une seule déclaration » ; **le propriétaire de la table est délibérément inchangé.** Trois déclarations vérifiées, deux mortes retirées (`DriverAggregate.cs` et `Entities/*`+`Enums/*` de driver-service) — les quatorze signalements `DriverId` de `check-usings.py` sont éteints. La déclaration vivante a rejoint `HBA.Delivery.Core.Domain`, auprès de la table `deliveries.drivers` qu'elle sert déjà. **La table n'a pas été déplacée vers driver-service, et aucune migration n'accompagne ce lot** : driver-service n'a aucune persistance (`ConcurrentDictionary`), et déplacer une table de production vers un service sans base échangerait un défaut de structure contre une perte de données. Transfert prévu au lot 5.3, par contrat. — *Constat d'origine :* l'agrégat `Driver` est déclaré **trois fois** (delivery, driver, dispatch), dont deux mortes ; il vit dans `driver-service` mais est persisté par `delivery-service` (table `deliveries.drivers`), que `driver-service` ne lit jamais | `services/delivery/*` | une seule déclaration, un seul propriétaire de la table |

---

# E. MEDIUM et LOW

63 anomalies MEDIUM et 24 LOW ont été relevées. Elles sont détaillées, avec preuves, dans les rapports thématiques :

| Famille | Nombre | Rapport |
|---|---:|---|
| Index manquants, contraintes d'unicité, nullable, cascade, N+1, pagination | 38 | `DATABASE_AUDIT.md` §4 à §12 |
| gRPC : absence de disjoncteur, codes de statut métier inutilisés, 13 `.proto` non compilés, 45 RPC morts | 21 | `GRPC_MATRIX.md` §8 à §13 |
| Kafka : versionnement d'événements, données sensibles superflues, double publication, effet métier avant commit | 14 | `KAFKA_EVENT_MATRIX.md` §9 à §13 |
| 83 valeurs d'énumération de statut jamais assignées | 1 (large) | `STATE_MACHINE_AUDIT.md` |
| Observabilité incomplète, sondes, métriques métier | 9 | `SERVICES_*.md` |
| Nommage, restes de `HBA.Deliveries.*`, documentation absente ou trompeuse | 24 | tous |

---

## Rapports de preuve

`ARCHITECTURE_AUDIT.md` · `SERVICES_AUDIT.md` · `GRPC_MATRIX.md` · `KAFKA_EVENT_MATRIX.md` · `DATABASE_AUDIT.md` · `SECURITY_AUDIT.md` · `SAGA_CLIENT.md` · `SAGA_SELLER.md` · `SAGA_SELLER_MEMBER.md` · `SAGA_DRIVER.md` · `SAGA_ADMIN.md` · `STATE_MACHINE_AUDIT.md`

---

# F. Anomalies critiques ajoutées à la consolidation

## ISSUE-071
**Statut :** ✅ **CORRIGÉE** le 21 août 2026 — option (A), chiffrement du champ dans la charge (AES-GCM). Les trois champs concernés — `ProtectedVerificationToken`, `ProtectedResetToken`, `ProtectedInvitationToken` — sont chiffrés par cinq producteurs et déchiffrés par trois consommateurs. Détail en `RESTE_A_FAIRE.md`. Le constat ci-dessous est conservé tel qu'il a été écrit : il décrit l'état d'avant.

**Severity:** CRITICAL
**Domain:** Commun
**Service:** Identity Service
**Saga:** Réinitialisation de mot de passe / vérification d'e-mail

**Problem:** `PasswordResetRequestedIntegrationEvent.ResetToken` et `EmailVerificationRequestedIntegrationEvent.VerificationToken` transportent le jeton **en clair** dans la charge Kafka **et** dans `identity.outbox_messages.Content`.

**Evidence:**
- `shared/contracts/HBA.Identity.Contracts/IntegrationEvents/IdentityIntegrationEvents.cs:91` et `:23`
- le commentaire du fichier (`:16-17`) assume explicitement « le jeton EN CLAIR »
- `OutboxMessage.cs:17` — `Content` est un JSON en clair
- rétention du topic : 7 jours (`k8s/overlays/prod/kafka-topics.yaml`) ; **la ligne d'outbox n'est jamais purgée**

**Expected:** l'événement porte un identifiant opaque ; le service de notification récupère le jeton par un appel authentifié, ou le jeton est chiffré.
**Actual:** quiconque lit le topic, ou fait un `SELECT` sur `identity.outbox_messages`, prend n'importe quel compte de la plateforme.
**Impact:** **prise de contrôle de compte à l'échelle de la plateforme.** Un accès en lecture à une table d'outbox — un dump, une sauvegarde, un compte de lecture analytique — suffit. C'est le défaut de sécurité le plus large de l'audit : il ne demande aucune faille applicative, seulement un accès en lecture qui paraît anodin.
**Recommended fix:** retirer le jeton de la charge ; publier un identifiant de demande ; purger l'outbox.
**Tests required:** aucun jeton dans la charge sérialisée ; purge de l'outbox effective.

---

## ISSUE-072
**Severity:** CRITICAL
**Domain:** Commun
**Service:** Payment Service
**Saga:** Encaissement

**Problem:** `payments.ProviderReference` n'a **aucune contrainte d'unicité**, et la lecture se fait par `FirstOrDefaultAsync`.
**Evidence:** `PaymentConfiguration.cs:98` ; `PaymentRepository.cs:27-30` appelé par `GatewayConfirmationCommands.cs:73`
**Expected:** une référence PSP désigne un paiement et un seul.
**Actual:** deux paiements peuvent porter la même référence ; le webhook en encaisse **un au hasard**, l'autre reste `Pending` pour toujours.
**Impact:** commande définitivement bloquée, et rapprochement comptable impossible.
**Recommended fix:** index unique sur `ProviderReference` (avec garde-fou anti-doublon dans la migration, comme `20260823000100_UnicitePanierParCommande`).
**Tests required:** insertion d'un doublon → refus ; webhook → paiement déterministe.

---

## ISSUE-073
**Severity:** CRITICAL
**Domain:** Commun
**Service:** Wallet Service
**Saga:** Remboursement client

**Problem:** `customer_refunds` n'a **aucune clé d'idempotence** : ni clé dédiée, ni unicité sur `OrderId`, ni sur `ProviderRef`.
**Evidence:** `WalletConfigurations.cs:151-176` ; `CustomerRefundCommands.cs:63-116`
**Expected:** un remboursement client donne lieu à un versement et un seul.
**Actual:** **deux versements Mobile Money** pour le même remboursement.
**Impact:** perte d'argent directe, non détectable après coup.
**Recommended fix:** clé d'idempotence + index unique ; jeton de concurrence sur la table.
**Tests required:** double exécution du même remboursement → un seul versement.

---

## ISSUE-074
**Statut :** ✅ **CORRIGÉE le 30 août 2026 — lot 3.4.** Un `SaveChangesAsync` est posé AVANT l'appel au prestataire : la ligne `customer_refunds` existe, en `Processing`, avec son écriture comptable, au moment où le versement part. `ListProcessingAsync` — la seule entrée de la réconciliation — la voit donc, quoi qu'il arrive ensuite.

**Ce que le nouvel ordre laisse ouvert, et c'est visible.** Si l'incident survient entre ce premier enregistrement et la réponse du prestataire, la ligne reste `Processing` **sans référence PSP**. La réconciliation la rencontre, ne peut rien interroger sans référence, et la saute : elle attend un arbitrage humain. Mais elle est chiffrée, datée, rattachée à une commande et à un client — c'est toute la différence avec l'état d'avant, où elle n'existait pas du tout.

**Severity:** CRITICAL
**Domain:** Commun
**Service:** Wallet Service
**Saga:** Remboursement client

**Problem:** `InitiateCustomerRefundCommandHandler` appelle le PSP Mobile Money **avant tout `SaveChangesAsync`**.
**Evidence:** `Wallets/CustomerRefundCommands.cs:100-140`
**Expected:** persister l'intention avant d'appeler le prestataire.
**Actual:** un incident entre les deux laisse l'argent parti et **aucune ligne** `customer_refunds` — `ListProcessingAsync` ne voit rien, la réconciliation est aveugle.
**Impact:** argent versé sans trace. Irrécupérable autrement qu'en rapprochant les relevés du PSP à la main.
**Recommended fix:** écrire `Processing` avant l'appel, comme le fait déjà correctement `RefundPaymentCommandHandler` côté payments.
**Tests required:** injection d'échec après l'appel PSP → la ligne existe et est réconciliable.

---

## ISSUE-075
**Statut :** ✅ **CORRIGÉE le 31 août 2026 — lot 3.5.** `InventoryItem.Reserve` est idempotent sur `(article, commande)` : une réservation active existante est **posée** à la nouvelle quantité au lieu d'en ajouter une seconde, et le disponible est jugé en rendant d'abord ce que cette même commande immobilise déjà. Un rejeu à l'identique n'écrit rien, n'appelle pas `Touch()` et ne publie aucun événement — sinon deux rejeux inoffensifs se disputeraient `xmin` en 409 et l'outbox recevrait un `StockReserved` par tentative. Index unique **partiel** `(InventoryItemId, OrderId) WHERE Status = 'Active'` : un index sec aurait refusé la reprise de paiement la plus banale (réservée → libérée → réservée).

**L'appelant a été corrigé aussi.** `PlaceOrderCommandHandler` réservait ligne par ligne : deux lignes du même SKU au même emplacement produisaient deux réservations, que l'index unique aurait refusées. Les lignes sont désormais regroupées par `(Sku, ShipFromLocationId)` et réservées en un appel pour la quantité totale.

**Severity:** CRITICAL
**Domain:** Marketplace
**Service:** Inventory Service
**Saga:** Achat marketplace

**Problem:** `ReserveStock` **n'est pas idempotent** : aucune vérification d'une réservation existante pour le même `orderId`, alors que `ReserveStockRequest` porte bien `order_id` (`inventory.proto:88`) — la clé est disponible et inutilisée.
**Evidence:** `GRPC_MATRIX.md` §7.3 ; appelant `PlaceOrderCommandHandler.cs:279` derrière une échéance de 5 s
**Expected:** un second appel pour la même commande ne réserve rien de plus.
**Actual:** un dépassement d'échéance suivi d'un rejeu réserve **deux fois**. La compensation `ReleaseReservation` supprime **toutes** les réservations de la commande, ce qui masque le problème dans un sens et l'aggrave dans l'autre.
**Impact:** **survente par immobilisation** — le stock disparaît deux fois pour une seule vente.
**Recommended fix:** rendre `ReserveStock` idempotent sur `(InventoryItemId, OrderId)`, avec la contrainte d'unicité correspondante en base.
**Tests required:** double appel avec le même `orderId` → une seule réservation ; test de concurrence.
