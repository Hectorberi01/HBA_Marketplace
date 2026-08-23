# SERVICES_AUDIT — HBAExpress

*Audit détaillé, service par service, de l'arbre de travail au 21/08/2026. Lecture seule.*

Ce document rassemble les quatre audits de domaine. Chaque service y figure avec ses projets,
ses couches présentes et manquantes, ses agrégats, ses endpoints, ses événements, son statut réel
et ses défauts avec preuves.

**Synthèse d'entrée** : 31 services présents ; 9 réellement complets ; 11 squelettes ou maquettes ;
2 attendus et absents (File Service, Admin BFF). Le détail est dans `ARCHITECTURE_AUDIT.md` §2.

## Sommaire

- [Domaine commun](#domaine-commun) — 11 services
- [Domaine marketplace](#domaine-marketplace) — 7 services
- [Domaine food](#domaine-food) — 7 dossiers, 3 services réels
- [Domaine delivery et applications](#domaine-delivery-et-applications) — 7 services + passerelle + 3 BFF

---

# Domaine commun


Périmètre : `services/common/` (11 services, 623 fichiers `.cs`, 68 576 lignes).
Analyse statique uniquement (pas de compilateur .NET). Chemins relatifs à la racine du dépôt.

Remarques transverses de méthode :
- Les projets de test ne sont **pas** sous `services/common/*/tests/` : ils sont centralisés
  dans `tests/` à la racine. Les fiches citent les chemins réels.
- Les `.proto` sont centralisés dans `shared/proto/`, et plusieurs implémentations gRPC
  serveur vivent dans `shared/contracts/HBA.*.Contracts.Grpc/` plutôt que dans la couche
  `Api` du service. C'est une violation de la découpe annoncée, notée une fois ici plutôt
  qu'à chaque fiche.
- Les couches `Domain` des 12 projets `.Domain` du périmètre ne référencent QUE
  `shared/common/HBA.Shared.Domain` (vérifié sur les 12 `.csproj` et par recherche des
  `using Microsoft.EntityFrameworkCore|Microsoft.AspNetCore|Confluent|Grpc|MediatR` :
  aucune occurrence). **Aucune violation « Domain dépend d'EF/ASP/Kafka/gRPC ».**
- Aucun montant monétaire en `double`/`float` dans le périmètre. Les seuls `double` sont
  `Address.Latitude/Longitude` (`user-service/.../Domain/Addresses/Address.cs:122-124`) et
  `Recommendation.Score` (`recommendation-service/.../Domain/Recommendations/Recommendation.cs:40`) —
  légitimes.
- `AddOutboxProcessor<TContext>()` est présent dans **les 12 installeurs** du périmètre.
  Aucun outbox manquant. Le consumer Kafka (`KafkaIntegrationEventConsumer`) est enregistré
  centralement (`shared/common/HBA.Shared.Infrastructure/DependencyInjection.cs:127`),
  donc aucun consumer « déclaré mais non enregistré » au sens de l'hébergement.

---

## Récapitulatif

| Service | Statut | Défauts CRITICAL | HIGH |
|---|---|---|---|
| payment-service | PARTIEL | 3 | 6 |
| wallet-service | PARTIEL | 1 | 5 |
| media-service | PARTIEL | 1 | 3 |
| promotion-service | PARTIEL | 0 | 3 |
| billing-service | PARTIEL | 0 | 2 |
| notification-service | PARTIEL | 0 | 2 |
| identity-service | COMPLET | 0 | 0 |
| user-service | COMPLET | 0 | 0 |
| review-service | PARTIEL | 0 | 1 |
| recommendation-service | SQUELETTE | 0 | 1 |
| wishlist-service | SQUELETTE | 0 | 1 |

---

# 1. payment-service

### payment-service
**Path :** `services/common/payment-service/`

**Projects :**
- `src/HBA.Financial.Api/HBA.Financial.Api.csproj`
- `src/HBA.Financial.Payments.Application/HBA.Financial.Payments.Application.csproj`
- `src/HBA.Financial.Payments.Contracts/HBA.Financial.Payments.Contracts.csproj`
- `src/HBA.Financial.Payments.Domain/HBA.Financial.Payments.Domain.csproj`
- `src/HBA.Financial.Payments.Infrastructure/HBA.Financial.Payments.Infrastructure.csproj`

**Couches présentes :** Domain / Application / Infrastructure / Api / Contracts — les cinq.

**Couches manquantes :** aucune. En revanche `HBA.Financial.Api` **n'est pas l'API de
payment-service seul** : `src/HBA.Financial.Api/Program.cs:41-42` installe aussi
`BillingModuleInstaller` et `WalletModuleInstaller`, et
`Endpoints/FinancialEndpoints.cs` expose les routes des trois modules. Trois services du
découpage annoncé partagent donc un seul hôte, un seul port et une seule image.

**Tests :**
- `tests/HBA.Payments.Tests/PaymentRefundDomainTests.cs`
- `tests/HBA.Payments.Tests/PaymentEventContractTests.cs`
- `tests/HBA.Financial.AuthorizationTests/FinancialAuthorizationTests.cs` (partagé avec wallet/billing)

**Volume :** 80 fichiers `.cs` (~9 140 lignes).

**Agrégats & machines d'état :**

`Payment` (`Domain/Payments/Payment.cs`) → `PaymentStatus` (`Domain/Payments/PaymentIds.cs:61`) :
`Pending=0, Authorized=1, Captured=2, Failed=3, Refunded=4`.
- `Pending → Authorized` (`Authorize`, l.113)
- `Pending|Authorized → Captured` (`Capture`, l.126)
- `≠Captured,≠Refunded → Failed` (`Fail`, l.142)
- `Captured → Refunded` uniquement quand `RefundableAmount ≤ 0` (`MarkRefundSucceeded`, l.250)
- `AttachGatewaySession` (l.96) exige `Pending`.
- `ReleaseEscrow` (l.314) exige `Captured`, idempotent.

`PaymentRefund` (`Domain/Payments/PaymentRefund.cs`) → `PaymentRefundStatus` (`PaymentIds.cs:70`) :
`Processing=0 → Succeeded=1 | Failed=2`, `Failed → Processing` via `Retry` (l.48).

`SavedPaymentMethod` (`Domain/PaymentMethods/SavedPaymentMethod.cs`) → `PaymentMethodType`
(`MobileMoney`, `Card`) ; pas de machine d'état.

Autres énumérations : `PaymentMethod` (MobileMoney/Card/BankTransfer/CashOnDelivery),
`PaymentFlow` (HostedCheckout/PaymentIntent), `PaymentOrderType` (Marketplace/Food).

**Endpoints exposés** (tous dans `src/HBA.Financial.Api/Endpoints/FinancialEndpoints.cs` ;
seules les routes du module Payments sont listées ici, celles de Billing et Wallet sont
dans leurs fiches) :

| Route | Verbe | Policy | Ligne |
|---|---|---|---|
| `/api/financial/payments/` | GET | `RequireAdmin` | 24 |
| `/api/financial/payments/stats` | GET | `RequireAdmin` | 25 |
| `/api/financial/payments/{id}` | GET | authentifié + garde propriétaire | 26 / 225 |
| `/api/financial/payments/by-order/{orderId}` | GET | authentifié + garde propriétaire | 27 / 235 |
| `/api/financial/payments/` | POST | authentifié, **aucune garde de propriété** | 28 |
| `/api/financial/payments/{id}/capture` | POST | `RequireAdmin` | 52 |
| `/api/financial/payments/{id}/fail` | POST | `RequireAdmin` | 53 |
| `/api/financial/payments/{id}/refund` | POST | `RequireAdmin` | 54 |
| `/api/financial/payments/{id}/redirect/confirm` | POST | authentifié + garde propriétaire | 60 |
| `/api/financial/payments/webhooks/{provider}` | POST | `AllowAnonymous` (signature HMAC) | 75 |
| `/api/v1/payments/intents` | POST | authentifié + `RequireIdempotency` | 93 |
| `/api/v1/payments/intents/{id}` | GET | authentifié, **aucune garde** | 94 |
| `/api/v1/payments/{id}/refunds` | POST | `RequireAdmin` + `RequireIdempotency` | 95 |
| `/api/v1/payments/webhooks/{provider}` | POST | `AllowAnonymous` | 96 |
| `/api/financial/payment-methods/*` (5 routes) | GET/POST/PUT/DELETE | authentifié, scoping par `userId` du jeton | 99-103 |

**gRPC exposé :** `shared/proto/financial/v1/financial.proto`, service `FinancialApi`,
**9 RPC déclarés, 1 implémenté**. `src/HBA.Financial.Api/GrpcServices/FinancialGrpcService.cs`
ne surcharge que `RefundPayment` (l.15). Les 8 autres (`GetPayment`, `GetPaymentByOrder`,
`InitiatePayment`, `CapturePayment`, `FailPayment`, `ComputeCommission`, `GetSellerWallet`,
`GetDriverWallet`) héritent de `FinancialApiBase` et renvoient donc `UNIMPLEMENTED`.

**Événements publiés :** `PaymentCapturedIntegrationEvent`, `PaymentFailedIntegrationEvent`,
`PaymentRefundedIntegrationEvent`, `PaymentRefundFailedIntegrationEvent`,
`PaymentInitiatedIntegrationEvent` — via `Application/Payments/EventHandlers/PaymentDomainEventHandlers.cs`,
tous les 5 handlers enregistrés (`PaymentsModuleInstaller.cs:230-234`).

**Événements consommés :** `OrderDeliveredIntegrationEvent` → `ReleaseEscrowOnOrderDeliveredHandler`
(`PaymentsModuleInstaller.cs:237`) ; `OrderCancelledIntegrationEvent` →
`RefundPaymentOnOrderCancelledHandler` (l.248).

**Statut : PARTIEL.**

## Défauts — payment-service

**P-01 — CRITICAL — Le remboursement est impossible en production sur les quatre PSP du marché visé, et le handler d'annulation boucle dessus.**
Les adaptateurs HTTP réels renvoient `Success: false` en dur :
`Infrastructure/Gateways/Real/FedaPayHttpGateway.cs:104`,
`Real/MtnMomoHttpGateway.cs:98`, `Real/MoovHttpGateway.cs:88`, `Real/PayPalHttpGateway.cs:117`.
Seul `Real/StripeHttpGateway.cs:93` rembourse réellement — et Stripe n'est pas le PSP du
marché ciblé (`FedaPayOptions.Currency = "XOF"`, `PayoutMode = "mtn_open"`).
Conséquence en chaîne : `RefundPaymentCommandHandler` (`Application/Payments/Commands/PaymentLifecycleCommands.cs:246-258`)
renvoie `payments.refund_rejected`, et `RefundPaymentOnOrderCancelledHandler`
(`Application/Payments/EventHandlers/RefundPaymentOnOrderCancelledHandler.cs:112`) **lève**
`InvalidOperationException`. Le message `order.cancelled` est donc rejoué jusqu'à la lettre
morte, pour chaque commande annulée, sans qu'un franc ne revienne au client.
Le retour marchandise (`services/marketplace/return-refund-service/.../PaymentGrpcClient.cs:24`)
passe par le même chemin et échoue de la même façon.

**P-02 — CRITICAL — `SimulatedPayoutGateway` est enregistré EN PRODUCTION.**
`Infrastructure/PaymentsModuleInstaller.cs:217` : `services.AddSingleton<IPayoutGateway, SimulatedPayoutGateway>();`
est dans la branche `else` de `if (fedapayOptions.CanPayout)`. `CanPayout`
(`Gateways/PaymentGatewayOptions.cs:150`) exige `IsConfigured && EnablePayouts && !IsSandbox
&& KeyMatchesEnvironment` — quatre conditions. Dès qu'une seule manque, y compris en
`ASPNETCORE_ENVIRONMENT=Production`, un stub est injecté. Ce stub
(`Gateways/Simulation/SimulatedPayoutGateway.cs:17,21`) renvoie `Accepted("sim_payout_…")`
puis `PayoutStatus.Sent` — que `WithdrawalSettlement.ApplyAsync`
(`wallet-service/.../Application/Wallets/WithdrawalSettlement.cs:51-53`) traduit en
`withdrawal.Complete(...)`. Le vendeur est débité, le retrait est marqué « payé »,
et rien n'est parti. Le garde-fou construit pour les passerelles d'ENCAISSEMENT
(`PaymentsModuleInstaller.cs:437-442`, « en production, on n'enregistre RIEN ») n'a jamais
été appliqué à la passerelle de VERSEMENT, alors que c'est celle qui sort l'argent.

**P-03 — CRITICAL — Un webhook « refunded » partiel est enregistré comme un remboursement TOTAL.**
`Application/Payments/Commands/GatewayConfirmationCommands.cs:34` :
`GatewayOutcome.Refunded => … payment.Refund()`. Or `Payment.Refund()`
(`Domain/Payments/Payment.cs:157-174`) rembourse `RefundableAmount`, c'est-à-dire le solde
ENTIER, et appelle directement `MarkRefundSucceeded` — sans jamais consulter le montant du
webhook. `GatewayEvent` (`Application/Abstractions/Gateways/IPaymentGateway.cs:35`) ne porte
d'ailleurs aucun montant. Un remboursement partiel décidé chez le PSP passe donc le paiement
en `Refunded` pour la totalité, publie `PaymentRefundedIntegrationEvent` avec le montant
total, et déclenche en aval la contre-passation intégrale du gain vendeur
(`wallet-service/.../ReverseEarningsOnReturnRefundedHandler.cs`). Données financières fausses
et perte pour le vendeur.

**P-04 — HIGH — `GET /api/v1/payments/intents/{id}` rend le paiement de n'importe qui.**
`Endpoints/FinancialEndpoints.cs:94` mappe `GetPaymentIntentAsync` (l.434) qui envoie
`GetPaymentQuery` et rend le résultat sans passer par `PeutVoirLePaiement`. La route
historique équivalente (`/api/financial/payments/{id}`, l.26 → l.225) applique bien la garde.
La v1 est un doublon non gardé, et `tests/HBA.Financial.AuthorizationTests/FinancialAuthorizationTests.cs`
ne la couvre pas (l.35-89 : aucune ligne `/api/v1/payments`).

**P-05 — HIGH — `POST /api/financial/payments/` et `POST /api/v1/payments/intents` n'ont aucune garde de propriété.**
`InitiatePaymentCommand` (`Application/.../InitiatePayment/InitiatePaymentCommand.cs:10-17`)
ne porte pas d'identifiant d'appelant ; le handler lit l'acheteur DANS LA COMMANDE
(`InitiatePaymentCommandHandler.cs:122`, `order.BuyerId`). Le validateur
(`InitiatePaymentCommandValidator.cs`) ne vérifie rien de tel. Conséquence : tout compte
inscrit qui devine un `orderId` crée un paiement `Pending` sur la commande d'un tiers ; la
garde `payments.already_exists` (`InitiatePaymentCommandHandler.cs:103-106`) bloque alors le
vrai acheteur — commande bloquée jusqu'à ce que le PSP réponde. Le compte attaquant reçoit
en outre l'URL de redirection et le `ClientSecret`.

**P-06 — HIGH — Aucune déduplication d'événement sur les webhooks PSP.**
`ProcessGatewayWebhookCommandHandler` (`GatewayConfirmationCommands.cs:54-88`) ne consulte ni
`IConsumerInbox` ni `IIdempotencyStore`, alors que les deux sont enregistrés
(`PaymentsModuleInstaller.cs:52-53`) et que les tables existent
(`Migrations/20260818075555_AddInboxAndIdempotency.cs`). L'idempotence repose uniquement sur
le statut de l'agrégat. Cela tient pour un rejeu strict, mais pas pour une séquence
désordonnée : un `failed` arrivant après un `captured` fait échouer `payment.Fail()`
(`Payment.cs:144`), le handler renvoie une erreur (l.81-84), le PSP renvoie le webhook — en
boucle. Symétriquement, `IConsumerInbox` est **enregistré et jamais résolu** dans ce service
(vérifié : la seule résolution du dépôt est `user-service/.../CreateUserProfileOnUserRegisteredHandler.cs:65`).

**P-07 — HIGH — 8 des 9 RPC du contrat `FinancialApi` renvoient `UNIMPLEMENTED`.**
`shared/proto/financial/v1/financial.proto:8-16` déclare 9 RPC ;
`src/HBA.Financial.Api/GrpcServices/FinancialGrpcService.cs` n'en surcharge qu'un.
Le contrat publié promet notamment `InitiatePayment` (avec `payment_method_id`, l.38),
`ComputeCommission` et `GetSellerWallet` — trois appels qu'un service tiers écrirait
naturellement et qui échoueraient au premier appel, pas à la compilation.

**P-08 — HIGH — Le module « moyens de paiement enregistrés » ne sert à rien : il n'est jamais utilisé pour payer.**
`SavedPaymentMethod` a son agrégat, sa table, son repository, ses 5 routes HTTP
(`FinancialEndpoints.cs:99-103`) — et **aucun chemin de paiement ne le lit**.
`InitiatePaymentCommand` prend `Provider`/`Method`/`PayerPhone` en chaînes ; le
`payment_method_id` du proto (`financial.proto:38`) n'est honoré par personne. Un acheteur
enregistre une carte, la voit dans l'application, et ne peut jamais s'en servir. Aggravant :
le PAN complet transite par le corps HTTP et la commande MediatR
(`Application/PaymentMethods/AddPaymentMethodCommand.cs:19,44`) avant d'être réduit aux 4
derniers chiffres (`Domain/PaymentMethods/SavedPaymentMethod.cs:154-155`) — le numéro n'est
pas persisté, mais il traverse la plateforme et ses journaux de requête, pour un usage nul.

**P-09 — HIGH — `RefundAsync(GatewayRefundContext)` a une implémentation par défaut qui JETTE le montant et la clé d'idempotence.**
`Application/Abstractions/Gateways/IPaymentGateway.cs:78-79` :
`Task<GatewayRefundResult> RefundAsync(GatewayRefundContext context, …) => RefundAsync(context.ProviderReference, …);`
et `Infrastructure/Gateways/Real/HttpPaymentGatewayBase.cs:48-49` reproduit le même repli.
Seul `StripeHttpGateway.cs:107` surcharge la version complète. Pour tout autre PSP, un
remboursement partiel devient un remboursement total sans clé d'idempotence — c'est-à-dire
un double remboursement possible sur reprise réseau. Aujourd'hui masqué par P-01 (les autres
PSP refusent), mais le premier PSP branché correctement rouvrira la faille.

**P-10 — MEDIUM — Le snapshot EF n'inclut pas `PaymentRefund`.**
`Migrations/20260824010000_AddPaymentRefunds.cs` crée `payments.payment_refunds`, la
configuration existe (`Persistence/Configurations/PaymentConfiguration.cs:104-139`), mais
`Migrations/PaymentsDbContextModelSnapshot.cs` ne contient **aucune** occurrence de
`PaymentRefund` (0 sur 0). Le snapshot étant édité à la main dans ce dépôt (convention
maison), la prochaine migration générée tentera de recréer la table. Le même fichier de
migration mélange par ailleurs `amount`/`currency` en minuscules et le reste en PascalCase,
contre la convention §SQL.

**P-11 — MEDIUM — Un remboursement resté `Processing` n'est jamais repris.**
`PaymentLifecycleCommands.cs:200-204` : si un remboursement porte déjà la clé d'idempotence
et est `Processing`, le handler rend ce remboursement tel quel et sort. Aucun service de
réconciliation ne balaie les `PaymentRefundStatus.Processing` (aucun `BackgroundService` dans
le module Payments hormis l'outbox). Un crash entre `SaveChanges` (l.233) et l'appel PSP
(l.237) fige donc le remboursement pour toujours, dans un état que l'API rend comme « en
cours ». Le fichier le documente lui-même (l.124-132) sans le corriger.

**P-12 — MEDIUM — `ListCommissionRules` est ouverte à tout compte inscrit.**
`FinancialEndpoints.cs:106` : `commissions.MapGet("/", ListCommissionRulesAsync);` sans
`RequireAdmin`, alors que les cinq écritures voisines (l.108-112) l'ont. La réponse
(`ComputeCommissionQuery`/`CommissionRuleSummary`) porte `Scope`, `TargetId`, `Rate`,
`FixedFee` — donc le taux négocié par vendeur, exactement ce que la garde de
`ComputeCommission` (l.359-364) protège quelques lignes plus bas.

**P-13 — MEDIUM — `IX_payments_ProviderReference` n'est pas unique.**
`Persistence/Configurations/PaymentConfiguration.cs:98` : `builder.HasIndex(p => p.ProviderReference);`
sans `.IsUnique()`. Or `PaymentRepository.GetByProviderReferenceAsync` (l.27-30) corrèle les
webhooks sur cette seule colonne avec un `FirstOrDefaultAsync` non ordonné. Deux paiements
partageant une référence (retentative, PSP réutilisant un identifiant) verraient le webhook
appliqué au mauvais.

**P-14 — LOW — TODO de production restants dans les adaptateurs simulés.**
`Infrastructure/Gateways/Simulation/SimulatedPaymentGateway.cs:41,49,80,85` et
`Simulation/MobileMoneyPaymentGateway.cs:46,76`. Sans impact en production (ces classes ne
sont plus enregistrées en `Production`, `PaymentsModuleInstaller.cs:437-442`), mais
`GetStatusAsync` y renvoie inconditionnellement `Captured` (l.82) et `RefundAsync`
inconditionnellement `Success: true` (l.86) : en recette, tout « marche ».

---

# 2. wallet-service

### wallet-service
**Path :** `services/common/wallet-service/`

**Projects :**
- `src/HBA.Financial.Wallet.Application/HBA.Financial.Wallet.Application.csproj`
- `src/HBA.Financial.Wallet.Contracts/HBA.Financial.Wallet.Contracts.csproj`
- `src/HBA.Financial.Wallet.Domain/HBA.Financial.Wallet.Domain.csproj`
- `src/HBA.Financial.Wallet.Infrastructure/HBA.Financial.Wallet.Infrastructure.csproj`

**Couches présentes :** Domain / Application / Infrastructure / Contracts.

**Couches manquantes : Api.** Le service n'a pas d'hôte. Il est installé par
`payment-service/src/HBA.Financial.Api/Program.cs:42` (`new WalletModuleInstaller().Install(...)`)
et ses routes sont écrites dans `payment-service/.../FinancialEndpoints.cs:140-201`. Un
service qui manipule des soldes n'est ni déployable ni redéployable seul.

**Tests :** `tests/HBA.Wallet.Tests/WalletLedgerTests.cs` (5 cas, uniquement l'invariant
comptable) + `tests/HBA.Financial.AuthorizationTests/FinancialAuthorizationTests.cs`
(partagé). Aucun test sur le retrait, le lot de reversement, la contre-passation ou
le remboursement client.

**Volume :** 81 fichiers `.cs` (~14 120 lignes) — dont 37 de migrations.

**Agrégats & machines d'état :**

- `SellerWallet` (`Domain/Wallets/SellerWallet.cs`) : pas d'enum, deux soldes `decimal`
  (`PendingBalance`, `AvailableBalance`). Transitions : `CreditPending` (l.42),
  `ReleaseToAvailable` (l.77, borné par `Math.Min`), `Withdraw` (l.99, refuse si > solde),
  `CreditAvailable` (l.117), `DebitForRefund` (l.150, **peut rendre le solde négatif**,
  délibéré).
- `DriverWallet` / `PlatformWallet` : même forme, sans enum.
- `Withdrawal` (`Domain/Wallets/Withdrawal.cs`) → `WithdrawalStatus`
  (`Domain/Wallets/WalletPrimitives.cs:85`) : `Pending=0, Completed=1, Failed=2,
  Requested=3, Rejected=4, Processing=5`.
  Transitions réelles : `Create → Requested` (l.29) ; `Requested → Processing`
  (`MarkProcessing`, l.113) ; `Processing → Completed` (`Complete`, l.125) ;
  `→ Failed` (`Fail`, l.133) ; `Requested → Rejected` (`Reject`, l.141).
  **`WithdrawalStatus.Pending` n'est assigné nulle part** (valeur zéro, donc valeur par
  défaut en base : une ligne écrite sans passer par `Create` serait dans un état sans
  transition sortante).
- `SellerEarning` (`Domain/Earnings/SellerEarning.cs`) → `EarningStatus` (l.19) :
  `Accrued=0 → Released=1` (`Release`, l.125) `→ Settled=2` (`MarkSettled` l.149 /
  `MarkSettledByWithdrawal` l.169), `Settled → Released` (`Unsettle`, l.193).
  **`EarningStatus.Reversed=3` n'est assigné nulle part** (le code le reconnaît :
  `Domain/Wallets/SellerWallet.cs:66`).
- `SettlementBatch` + `Payout` (`Domain/Batches/SettlementBatch.cs`) → `SettlementStatus`
  (l.15) et `PayoutStatus` (l.31). `MarkPayoutPaid` (l.125), `MarkPayoutFailed` (l.144),
  `Cancel` (l.164).
- `CustomerRefund` (`Domain/Wallets/CustomerRefund.cs`), `WalletTransaction`
  (`Domain/Wallets/WalletTransaction.cs`, `WalletDirection` Credit/Debit,
  `WalletAccount` Pending/Available).

**Endpoints exposés** (écrits dans `payment-service/.../FinancialEndpoints.cs`) :

| Route | Verbe | Policy | Ligne |
|---|---|---|---|
| `/api/financial/wallets/sellers/{sellerId}` | GET | `WALLET_VIEW` + appartenance | 141 / 473 |
| `/api/financial/wallets/sellers/{sellerId}/transactions` | GET | `WALLET_VIEW` + appartenance | 142 |
| `/api/financial/wallets/sellers/{sellerId}/withdrawals` | GET | `PAYOUT_VIEW` + appartenance | 143 |
| `/api/financial/wallets/sellers/{sellerId}/withdrawals` | POST | `WITHDRAWAL_REQUEST` + appartenance + **step-up** | 144 / 519 |
| `/api/financial/wallets/drivers/{driverId}` | GET | appartenance livreur (gRPC delivery) | 145 / 530 |
| `/api/financial/wallets/drivers/{driverId}/transactions` | GET | idem | 146 |
| `/api/financial/wallets/platform` | GET | `RequireAdmin` | 147 |
| `/api/financial/wallets/platform/transactions` | GET | `RequireAdmin` | 148 |
| `/api/financial/wallets/withdrawals/pending` | GET | `RequireAdmin` | 149 |
| `/api/financial/wallets/withdrawals/processing` | GET | `RequireAdmin` | 150 |
| `/api/financial/wallets/withdrawals/{id}/approve` | POST | `RequireAdmin` | 151 |
| `/api/financial/wallets/withdrawals/{id}/reject` | POST | `RequireAdmin` | 152 |
| `/api/financial/settlements/` | GET | `RequireAdmin` | 155 |
| `/api/financial/settlements/{id}` | GET | `RequireAdmin` | 156 |
| `/api/financial/settlements/sellers/{sellerId}/statement` | GET | `FINANCE_VIEW` + appartenance | 172 |
| `/api/financial/settlements/sellers/{sellerId}/statement/lines` | GET | `FINANCE_VIEW` + appartenance | 173 |
| `/api/financial/settlements/sellers/{sellerId}/payouts` | GET | `PAYOUT_VIEW` + appartenance | 174 |
| `/api/financial/settlements/` | POST | `MapAdminGroup` | 199 |
| `/api/financial/settlements/{batchId}/payouts/{payoutId}/paid` | POST | `MapAdminGroup` | 200 |
| `/api/financial/settlements/{id}/cancel` | POST | `MapAdminGroup` | 201 |

Le step-up §37 est bien branché : `FinancialEndpoints.cs:738-741`
(`MerchantCapabilities.RequiresStepUp(capacite) && !user.HasRecentAuthentication()`), après
le contrôle de capacité, et uniquement pour `WITHDRAWAL_REQUEST`.

**gRPC exposé :** aucun. `GetSellerWallet` et `GetDriverWallet` sont déclarés dans
`shared/proto/financial/v1/financial.proto:15-16` et non implémentés (voir P-07).

**Événements publiés :** `SettlementIntegrationEvents` (`Contracts/IntegrationEvents/`),
via `Application/Batches/EventHandlers/PayoutPaidDomainEventHandler.cs` — seul handler de
domaine enregistré (`SettlementModuleInstaller.cs:95`).

**Événements consommés** (`Infrastructure/SettlementModuleInstaller.cs`) :
`OrderConfirmedIntegrationEvent` (l.114), `ReturnRefundedIntegrationEvent` (l.119),
`OrderCancelledIntegrationEvent` (l.127), `OrderDeliveredIntegrationEvent` (l.130),
`ShipmentDeliveredIntegrationEvent` (l.133), `DeliveryCompletedIntegrationEvent` (l.143).

**Statut : PARTIEL.**

## Défauts — wallet-service

**W-01 — CRITICAL — Un retrait vendeur est clôturé « payé » sans qu'aucun argent ne parte, en production.**
Chaîne complète : `ApproveWithdrawalCommandHandler`
(`Application/Wallets/WalletCommands.cs:298`) appelle `_payouts.SendMobileMoneyPayoutAsync`,
qui délègue à `IPayoutGateway` (`payment-service/.../Public/PayoutModuleApi.cs:18`), lequel
est `SimulatedPayoutGateway` dès que `FedaPayOptions.CanPayout` est faux
(`payment-service/.../PaymentsModuleInstaller.cs:200-218`), **y compris en Production**. Le
stub renvoie `Accepted` puis `Sent`, et `WithdrawalSettlement.ApplyAsync`
(`Application/Wallets/WithdrawalSettlement.cs:51-53`) appelle `withdrawal.Complete(...)`.
Le solde a été débité à la demande (`WalletCommands.cs:121`), les gains ont été imputés
`Settled` (l.157), et le vendeur n'a rien reçu. Voir P-02.

**W-02 — HIGH — `SettlementBatch.MarkPayoutFailed` n'a aucun appelant : un versement de lot refusé n'est jamais compensé.**
`Domain/Batches/SettlementBatch.cs:144` définit la transition ; recherche sur tout le dépôt :
aucun appel. Le code le documente lui-même
(`Application/Batches/SettlementCommands.cs:399-402` : « n'a AUCUN appelant »). Or
`RunSettlementCommandHandler` débite le portefeuille à la création du lot
(`SettlementCommands.cs:204`, `wallet.Withdraw(payable)`) et solde les gains (l.240). Si le
versement échoue chez l'opérateur, l'unique retour arrière est l'annulation du LOT ENTIER
(`CancelSettlementBatchCommandHandler`), que `SettlementBatch.Cancel()` refuse dès qu'un seul
payout du lot est parti. Un vendeur dont le virement échoue reste donc débité, ses gains
soldés, sans mécanisme de reprise. Saga sans compensation.

**W-03 — HIGH — Le lot de reversement ne verse pas d'argent : `MarkPayoutPaid` est purement déclaratif.**
`MarkPayoutPaidCommandHandler` (`Application/Batches/SettlementCommands.cs:373-410`) ne
touche aucun `IPayoutGateway` ; il écrit `providerRef` et bascule le statut. Le seul appelant
de `SendMobileMoneyPayoutAsync` côté lots… n'existe pas (recherche dépôt : les 2 appels sont
`WalletCommands.cs:298` — retrait — et `CustomerRefundCommands.cs:108` — remboursement
client). Le circuit « lot de règlement » repose donc entièrement sur un geste manuel
d'administration déclarant un virement fait hors plateforme, alors que le portefeuille a
déjà été débité et que `POST /api/financial/settlements/{batchId}/payouts/{payoutId}/paid`
(`FinancialEndpoints.cs:200`) est une simple route admin sans preuve.

**W-04 — HIGH — Les webhooks de dépôt (payout) ne sont traités par personne.**
`ApplyPayoutWebhookCommand` (`Application/Wallets/ApplyPayoutWebhookCommand.cs:21`) est
documenté comme la voie « temps réel » — **il n'a aucun appelant** (recherche dépôt).
`IPayoutModuleApi.ReadPayoutWebhook` (`payment-service/.../Public/PayoutModuleApi.cs:35`)
et `IPayoutGateway.ParseWebhook` (`.../Abstractions/Gateways/IPayoutGateway.cs:103`) ne sont
appelés nulle part non plus. Côté PSP, `FedaPayHttpGateway.ParseWebhookAsync`
(`payment-service/.../Real/FedaPayHttpGateway.cs:129-132`) **ignore** explicitement les
événements `payout.*` en affirmant « Ils sont traités ailleurs ». Il n'y a pas d'ailleurs.
Seule la réconciliation à 2 minutes rattrape (`Infrastructure/Reconciliation/WithdrawalReconciliationService.cs:25`).

**W-05 — HIGH — L'invariant comptable du §10.13 est écrit, testé, et jamais appliqué.**
`Domain/Wallets/WalletLedger.EnsureBalanced` (`Domain/Wallets/WalletLedger.cs:51`) n'est
appelé que par `tests/HBA.Wallet.Tests/WalletLedgerTests.cs` (5 appels). Aucun site de
production ne l'invoque — le fichier l'admet (l.27-42). Toutes les écritures du grand livre
(`WalletTransaction.ForSeller/ForDriver`) partent sans contrôle d'équilibre : un
remboursement écrit trois débits sans contrepartie au crédit. La dérive comptable est donc
possible et silencieuse, exactement ce que l'invariant existe pour attraper.

**W-06 — HIGH — `EarningStatus.Reversed` n'est jamais assigné : la somme des gains « Released » peut excéder le solde.**
`Domain/Earnings/SellerEarning.cs:24` déclare la valeur ;
`Application/Earnings/ReverseEarningsOnReturnRefundedHandler.cs` et
`ReverseEarningsOnOrderCancelledHandler.cs` débitent le portefeuille sans toucher au statut du
gain (`SellerWallet.cs:66-68` le documente). Combiné au `Math.Min` de `ReleaseToAvailable`
(`SellerWallet.cs:84`), le grand livre des gains et le portefeuille divergent
structurellement. Le plafonnement du lot au solde réel
(`SettlementCommands.cs:192`, `Math.Min(net, wallet.AvailableBalance)`) évite la perte
d'argent mais **retarde indéfiniment** le versement des gains concernés, sans que rien ne
le signale au vendeur.

**W-07 — MEDIUM — Un seul `BackgroundService` de réconciliation, sans verrou, déclaré non multi-instances.**
`Infrastructure/Reconciliation/WithdrawalReconciliationService.cs:18-21` : « Tourne dans
l'hôte API (un seul). En multi-instances, il faudra un verrou ». Aucun `SELECT … FOR UPDATE
SKIP LOCKED` n'existe (`Application/Wallets/ReconcileWithdrawalsCommand.cs`). Le service est
hébergé par `HBA.Financial.Api`, qui est aussi l'hôte HTTP : toute montée en charge
horizontale du paiement duplique la réconciliation des retraits.

**W-08 — MEDIUM — `WithdrawalStatus.Pending` est une valeur morte en position zéro.**
`Domain/Wallets/WalletPrimitives.cs:87`. Aucune assignation dans le dépôt. Comme c'est la
valeur par défaut de l'énumération, toute ligne insérée hors `Withdrawal.Create` (import,
correctif SQL) atterrit dans un état dont `IsPendingApproval` et `IsProcessing` sont tous
deux faux — donc ni approuvable, ni réconciliable, ni rejetable.

**W-09 — MEDIUM — Devise « XOF » codée en dur à trois endroits au moins.**
`Domain/Wallets/SellerWallet.cs:175` (`Normalize`), `Application/Wallets/CreditDriverEarningCommand.cs:132`
(`? "XOF" :`). Le barème, lui, vient bien de la configuration
(`Infrastructure/SettlementModuleInstaller.cs:51`, `new PlatformPricing(configuration)`).

**W-10 — LOW — Nom de fichier / nom de classe / `ModuleName` désaccordés.**
`Infrastructure/SettlementModuleInstaller.cs` contient `public sealed class WalletModuleInstaller`
(l.31) dont `ModuleName => "Settlement"` (l.33).

---

# 3. media-service

### media-service
**Path :** `services/common/media-service/`

**Projects :** `src/HBA.Media.Api`, `src/HBA.Media.Application`, `src/HBA.Media.Domain`,
`src/HBA.Media.Infrastructure` (4 `.csproj`).

**Couches présentes :** Domain / Application / Infrastructure / Api.
**Couches manquantes : Contracts.** Le contrat public (`IMediaModuleApi`) vit dans
`shared/contracts/HBA.Media.Contracts/`, hors du service.

**Tests :** `tests/HBA.Media.Tests/MediaTypePolicyTests.cs`,
`tests/HBA.Media.Tests/MediaEventContractTests.cs`, `tests/HBA.Media.Tests/MediaAssetTests.cs`.

**Volume :** 23 fichiers `.cs` (~3 830 lignes).

**Agrégats & machines d'état :**
`MediaAsset` (`Domain/Assets/MediaAsset.cs`) → `MediaStatus` (`Domain/Assets/MediaEnums.cs`) :
`Uploaded=0 → Processing=1 → Ready=2 | Failed=3`, `→ Deleted=4` (`SoftDelete`).
Deux états du cahier (`PendingUpload`, `Quarantined`) sont volontairement absents et
documentés comme tels (`MediaEnums.cs`, encadré).
Énumérations associées : `MediaOwnerType` (10 valeurs), `MediaType` (9 valeurs),
`MediaVisibility` (Public/Private/Restricted), `MediaVariantType` (5 valeurs).

**Endpoints exposés** (`src/HBA.Media.Api/Endpoints/MediaEndpoints.cs`) :

| Route | Verbe | Policy | Ligne |
|---|---|---|---|
| `/api/v1/media/` | POST | `MapAuthenticatedGroup` + `DisableAntiforgery` | 54 |
| `/api/v1/media/{id}` | GET | authentifié, **aucune garde** | 55 |
| `/api/v1/media/{id}/download-url` | GET | authentifié, **aucune garde** | 56 |
| `/api/v1/media/{id}` | DELETE | authentifié, **aucune garde** | 57 |
| `/api/v1/media/{id}/reprocess` | POST | authentifié, **aucune garde** | 58 |

**gRPC exposé :** `shared/proto/media/v1/media.proto`, service `MediaApi`, **4 RPC déclarés,
4 implémentés** (`shared/contracts/HBA.Media.Contracts.Grpc/MediaGrpcService.cs:23,42,63,77`),
mappé par `src/HBA.Media.Api/Program.cs:21`. Conforme.

**Événements publiés :** `media.ready`, `media.deleted`, `media.processing_failed` —
handlers enregistrés `Infrastructure/MediaModuleInstaller.cs:125-127`.
**Événements consommés :** `KybDocumentRemovedIntegrationEvent`
(`MediaModuleInstaller.cs:108`).

**Statut : PARTIEL.**

## Défauts — media-service

**M-01 — CRITICAL — `GET /api/v1/media/{id}/download-url` délivre une URL signée pour N'IMPORTE QUEL fichier privé, à tout compte inscrit.**
`Api/Endpoints/MediaEndpoints.cs:56` → handler l.165-171 : aucun contrôle d'appartenance,
aucun contrôle de rôle, la commande passe directement à
`IMediaModuleApi.CreateSignedUrlAsync(id, expiresIn ?? 300)`. Or `MediaType`
(`Domain/Assets/MediaEnums.cs`) inclut `SellerDocument` (« pièces légales vendeur, PRIVÉ »),
`DriverDocument` (« CNI, permis, assurance, carte grise, PRIVÉ »), `DeliveryProof` et
`Invoice`. Un compte inscrit en trente secondes qui énumère des GUID obtient des URL signées
sur les pièces d'identité de la flotte. Le fichier le reconnaît (l.154 : « CETTE ROUTE NE
VÉRIFIE PAS LE DROIT MÉTIER, et c'est sa limite connue ») ; le commentaire ne remplace pas la
garde. Aucun test d'autorisation n'existe pour ce service.

**M-02 — HIGH — Le commentaire d'`UploadAsync` affirme une garde qui n'existe pas.**
`Api/Endpoints/MediaEndpoints.cs:76` : « Tant que ce n'est pas fait, cette route reste
réservée aux administrateurs — voir l'audit ». La route est mappée l.54 sur
`MapAuthenticatedGroup`, **sans** `RequireAdmin`. Combiné à l.66 (« LE PROPRIÉTAIRE EST
DÉCLARÉ PAR L'APPELANT, ET CE N'EST PAS VÉRIFIÉ ICI »), tout compte inscrit rattache un
fichier au produit, à la boutique ou au restaurant de son choix. C'est le défaut que
`FinancialEndpoints.cs:157-167` dénonce ailleurs — un commentaire qui certifie une garde
absente.

**M-03 — HIGH — `DELETE /api/v1/media/{id}` et `POST /{id}/reprocess` n'ont aucun contrôle de propriété.**
`MediaEndpoints.cs:57-58` (handlers l.172 et l.175) → `Application/Assets/MediaCommands.cs:216`
(`DeleteMediaCommand`) et l.236 (`ReprocessMediaCommand`). Les deux handlers chargent l'agrégat par identifiant et
agissent. `DeleteMediaCommand` ne porte pas d'identifiant d'appelant
(`MediaCommands.cs:61` : `record DeleteMediaCommand(Guid MediaId)`). Tout inscrit efface
les photos produit d'un concurrent ou les pièces KYB d'un vendeur.

**M-04 — HIGH — `InMemoryObjectStorage` est enregistré EN PRODUCTION quand le stockage objet n'est pas configuré.**
`Infrastructure/MediaModuleInstaller.cs:78-85` : la branche `else` de `if (stockage.IsConfigured)`
enregistre le substitut mémoire et ajoute un `UnconfiguredStorageWarning` — un simple
avertissement, pas un refus de démarrer. La classe le dit elle-même
(`Infrastructure/ObjectStorage/InMemoryObjectStorage.cs:25-27` : « IL NE SIGNE RIEN. Les URL
qu'il rend sont locales et ne protègent rien. C'est acceptable en développement, et c'est
pourquoi il ne doit jamais être sélectionné en production »). Contrairement à
notification-service (`NotificationsModuleInstaller.cs:149-163`) et payment-service
(`PaymentsModuleInstaller.cs:156-164`), aucune garde `IsProduction` n'existe ici. Un secret
mal injecté = toutes les images produit et toutes les pièces KYB perdues au redémarrage,
sans erreur.

**M-05 — MEDIUM — `PurgeExpiredMediaCommand` n'a aucun appelant : les fichiers supprimés ne partent jamais du stockage.**
`Application/Assets/MediaCommands.cs:270` implémente la purge (octets d'abord, ligne ensuite).
Recherche dépôt : zéro appelant — ni endpoint, ni `BackgroundService`, ni scheduler. La
rétention §19 est donc écrite et morte. Conséquence RGPD directe : le
`DeleteMediaOnKybDocumentRemovedHandler` (`MediaModuleInstaller.cs:108`) fait un `SoftDelete`,
les octets d'une pièce d'identité restent dans le bucket indéfiniment — l'inverse exact du
but affiché du handler.

**M-06 — MEDIUM — `GET /api/v1/media/{id}` rend les métadonnées de n'importe quel média.**
`MediaEndpoints.cs:55` → handler l.145-149, sans garde (le handler délègue directement à
`IMediaModuleApi.GetAsync`). Fuite moindre que M-01 mais suffisante
pour énumérer `OwnerType`/`OwnerId` et cartographier les fichiers privés avant de les
demander via M-01.

---

# 4. notification-service

### notification-service
**Path :** `services/common/notification-service/`

**Projects (9) :**
`src/HBA.Communication.Api`, `src/HBA.Communication.Application`,
`src/HBA.Communication.Contracts`, `src/HBA.Communication.Domain`,
`src/HBA.Communication.Infrastructure`, `src/HBA.Communication.Notifications.Application`,
`src/HBA.Communication.Notifications.Contracts`, `src/HBA.Communication.Notifications.Domain`,
`src/HBA.Communication.Notifications.Infrastructure`.

**Couches présentes :** Domain / Application / Infrastructure / Api / Contracts — pour deux
tranches (`Messaging` et `Notifications`) partageant une seule Api.

**Couches manquantes :** aucune.

**Tests :** `tests/HBA.Notifications.Tests/NotificationTemplateTests.cs` (1 fichier, sur
l'agrégat justement inutilisé — voir N-03).

**Volume :** 115 fichiers `.cs`.

**Agrégats & machines d'état :**
- `Notification` (`Notifications.Domain/Notifications/`) → `NotificationStatus`
  (`Notifications/NotificationIds.cs:21`) : `Pending=0 → Sent=1 | Failed=2`, `→ Read=3`.
  En pratique `NotificationDispatcher.NotifyAsync`
  (`Notifications.Application/Notifications/NotificationDispatcher.cs:64`) appelle
  `MarkSent()` immédiatement : `Pending` et `Failed` ne sont jamais atteints par le canal
  in-app.
  `NotificationChannel` (`NotificationIds.cs:12`) : `InApp/Email/Sms/Push` — **`Sms` n'a
  aucun adaptateur** dans le dépôt.
- `Conversation` + `Message` + `MessageAttachment` + `MessageReaction`
  (`Communication.Domain/Conversations/`) — pas d'enum de statut ; archivage par booléen.
- `DeviceToken`, `NotificationPreference`, `NotificationTemplate`
  (`Notifications.Domain/`).

**Endpoints exposés :**
`Api/Endpoints/NotificationsEndpoints.cs` — `MapAuthenticatedGroup`, tout scopé au jeton :
`/api/notifications/` GET (l.37), `/unread-count` GET (l.38), `/{id}/read` POST (l.39),
`/read-all` POST (l.40), `/{id}` DELETE (l.41), `/preferences` GET+PUT (l.46-47),
`/devices` POST+DELETE (l.58-59).
`Api/Endpoints/CommunicationEndpoints.cs` — `MapAuthenticatedGroup`
`/api/notifications/messaging/conversations` (GET/POST l.17-18), `/{id}` GET (l.19),
`/{id}/messages` POST (l.20), `/{id}/read` PUT (l.21), `/{id}/archive` POST (l.22),
`/{id}/messages/{messageId}/reaction` PUT (l.23), `.../{messageId}` DELETE (l.24),
`.../mine` DELETE (l.25).

**gRPC exposé :** **aucun, alors que le contrat existe.**
`shared/proto/communication/v1/communication.proto:7-13` déclare `CommunicationApi` avec
6 RPC ; `shared/contracts/HBA.Communication.Contracts.Grpc/*.csproj` génère serveur ET
client (`GrpcServices="Both"`) et **le projet ne contient aucun `.cs`** — aucune classe
n'hérite de `CommunicationApiBase` dans tout le dépôt. `Api/Program.cs:27` appelle
`builder.AddHbaGrpc()` mais aucun `MapInternalGrpcService<...>` : le port gRPC est ouvert
et vide.

**Événements publiés :** `MessageSentIntegrationEvent` via
`Communication.Infrastructure/MessagingModuleInstaller.cs:42`.

**Événements consommés :** 46 enregistrements `IIntegrationEventHandler<...>` dans
`Notifications.Infrastructure/NotificationsModuleInstaller.cs` (commandes, expéditions,
livraisons, retours, vendeurs, restaurants, comptes, gains livreur, payouts…).

**Statut : PARTIEL.**

## Défauts — notification-service

**N-01 — HIGH — `NullPushSender` est enregistré EN PRODUCTION quand FCM n'est pas configuré.**
`Notifications.Infrastructure/NotificationsModuleInstaller.cs:115` :
`services.AddScoped<IPushSender, NullPushSender>();` sans aucune garde `IsProduction`, alors
que la même méthode **lève** deux blocs plus bas quand le canal e-mail manque en production
(l.149-163). `NullPushSender` (`Push/NullPushSender.cs:8-10`) renvoie un succès vide.
Asymétrie non justifiée : le push porte les notifications de commande, de livraison et de
gain livreur ; toutes disparaissent en silence, et `NotificationDispatcher` les enregistre
malgré tout comme `Sent` (`NotificationDispatcher.cs:64`).

**N-02 — HIGH — Le canal `Sms` est déclaré et n'existe pas.**
`Notifications.Domain/Notifications/NotificationIds.cs:16` déclare `Sms = 2`. Aucun
`ISmsSender` ni adaptateur dans le dépôt (`Notifications.Application/Abstractions/` ne
contient que `IEmailSender` et `IPushSender`). Une préférence utilisateur ou un gabarit
positionné sur ce canal produit une notification qui ne part jamais, sans erreur. C'est
exactement le défaut que `MediaEnums.cs` dit avoir évité en n'ajoutant pas `Quarantined`.

**N-03 — MEDIUM — `NotificationTemplate` : agrégat, table, migration, repository, DI — et zéro utilisateur.**
`INotificationTemplateRepository` est enregistré
(`NotificationsModuleInstaller.cs:74`) et **n'est résolu nulle part** (recherche dépôt : 3
occurrences, toutes des déclarations). Les 46 gestionnaires composent leurs sujets et corps
en dur en C# (ex. `Notifications/EventHandlers/OrderNotificationHandlers.cs`,
`Emails/AccountEmailTemplates.cs`). La table `notifications.notification_templates`
(`Migrations/20260818075553_AddNotificationTemplatesInboxAndIdempotency`) est un DbSet sans
lecteur. Le seul test du service porte dessus (`tests/HBA.Notifications.Tests/NotificationTemplateTests.cs`).

**N-04 — MEDIUM — `IConsumerInbox` est enregistré et jamais résolu ; les 46 consumers ne sont idempotents que par état.**
`NotificationsModuleInstaller.cs:77-78`. Aucun des 46 gestionnaires n'ouvre l'inbox. Pour un
service de notification, un rejeu Kafka produit un doublon visible par l'utilisateur (push +
e-mail réémis). Le §19.5 exige l'idempotence ; l'infrastructure est là, non branchée.

**N-05 — MEDIUM — Le contrat gRPC `CommunicationApi` est publié et entièrement non implémenté.**
Voir « gRPC exposé » ci-dessus. Six RPC (`ListConversations`, `GetConversation`,
`StartConversation`, `SendMessage`, `MarkConversationRead`, `ArchiveConversation`) qui
renverront `UNIMPLEMENTED`. Aucun client ne les appelle aujourd'hui — le contrat est
purement décoratif.

**N-06 — LOW — Deux gestionnaires de litige conservés en Markdown dans `src/`.**
`Notifications.Application/Notifications/EventHandlers/_LITIGES_A_REPRENDRE.md` — 150 lignes
de C# commenté, dans l'arbre source. La justification (module Disputes non extrait) est
correcte ; l'emplacement ne l'est pas. À noter : `AdminNotificationTarget.cs` existe bien en
`.cs` (`EventHandlers/AdminNotificationTarget.cs`) alors que le Markdown en contient une
seconde copie — deux versions du même code.

---

# 5. identity-service

### identity-service
**Path :** `services/common/identity-service/`

**Projects :** `src/HBA.Identity.Api`, `src/HBA.Identity.Application`,
`src/HBA.Identity.Domain`, `src/HBA.Identity.Infrastructure` (4).

**Couches présentes :** Domain / Application / Infrastructure / Api.
**Couches manquantes : Contracts** — `HBA.Identity.Contracts` et
`HBA.Identity.Contracts.Grpc` vivent dans `shared/contracts/`.

**Tests :** `tests/HBA.Identity.Tests/StepUpTests.cs`, `RotationDuJetonTests.cs`,
`IdentityEventContractTests.cs`, `MfaChallengeTests.cs`.

**Volume :** 158 fichiers `.cs` (~14 860 lignes) — le plus gros du périmètre.

**Agrégats & machines d'état :**
- `User` (`Domain/Users/User.cs`, ~1 000 lignes) → `UserStatus` (`Domain/Users/UserStatus.cs`).
  Transitions : inscription → `PendingApproval`/`Active` selon `IRegistrationPolicy` ;
  `Approve` (l.592, idempotent), `Suspend`, `Reactivate`, `Anonymize`.
  Sous-machines : vérification e-mail (`ConfirmEmail` l.331, idempotent), MFA
  (`BeginMfaSetup`/`ConfirmMfa`/`DisableMfa`, l.979), réinitialisation de mot de passe
  (`BeginPasswordReset`/`ResetPassword`), verrouillage après échecs
  (migration `20260811214020_AddLoginLockout`), instant de dernière authentification
  (migration `20260819140000_AjoutInstantDAuthentification`, base du step-up §37).
- `RefreshToken` (`Domain/Users/RefreshToken.cs`) → `RefreshTokenOutcome`
  (`Domain/Users/RefreshTokenOutcome.cs`) : rotation avec détection de réutilisation.
- `Role` + `Permission` (`Domain/Roles/`), `UserRoleAssignment`.
- `MfaChallenge` (`Domain/Mfa/MfaChallenge.cs`).

**Endpoints exposés** (`src/HBA.Identity.Api/Endpoints/IdentityEndpoints.cs`) :

`/api/v1/auth` (groupe + `RequireRateLimiting(AuthRateLimiter.PolicyName)`, 30 req/min) :
`register` POST anonyme (l.63), `confirm-email` POST anonyme (l.64), `login` POST anonyme
(l.65), `refresh` POST anonyme (l.66), `reauthenticate` POST **`RequireAuthorization`** (l.88),
`logout` POST anonyme + `AllowIdempotency` (l.98), `otp/request` POST anonyme (l.103),
`verify-otp` POST anonyme (l.104), `password/forgot` POST anonyme (l.126),
`password/reset` POST anonyme (l.127), `email/resend` POST anonyme (l.139),
`email/verify` POST anonyme (l.151).

`/api/identity/account` (`RequireAuthorization`, l.276) : `me` GET/PUT (l.278-279),
`me/change-password` POST (l.280), `me/logout` POST (l.281), `me/mfa/setup|confirm|disable`
POST (l.282-284), `me` DELETE (l.314), `me/accept-terms` POST (l.315).

`/api/identity/users` (`MapAdminGroup` + `RequireRole("Admin")`, l.455-457) :
`{id}` GET (l.459), `{id}/suspend` POST (l.460), `{id}/reactivate` POST (l.461),
`{id}/roles` POST (l.462), `{id}/roles/{roleId}` DELETE (l.463).

`/api/identity/roles` (`MapAdminGroup` + `RequireRole("Admin")`, l.505-507) :
5 routes CRUD (l.509-514).

**gRPC exposé :** `shared/proto/identity/v1/identity.proto`, service `IdentityApi`,
**5 RPC déclarés, 5 implémentés** (`shared/contracts/HBA.Identity.Contracts.Grpc/IdentityGrpc.cs:84,98,108,133,155`),
mappé par `src/HBA.Identity.Api/Program.cs:26`. Conforme.

**Événements publiés :** `UserRegisteredIntegrationEvent`,
`PasswordResetRequestedIntegrationEvent`, `UserEmailConfirmed…`, `UserProfileUpdated…`,
`UserAnonymized…` — 4 handlers de domaine enregistrés
(`Infrastructure/IdentityModuleInstaller.cs:77, 110-112`).

**Événements consommés** (`IdentityModuleInstaller.cs:90-92, 106-109`) :
`SellerRegisteredIntegrationEvent`, `RestaurantApprovedIntegrationEvent`,
`DriverVerifiedIntegrationEvent`, `SellerMemberJoinedIntegrationEvent`,
`SellerMemberRevokedIntegrationEvent`.

**Statut : COMPLET.** C'est le service le plus abouti du périmètre : gRPC complet, step-up
implémenté et testé, anti-énumération sur `password/forgot`, rotation de jeton avec détection
de réutilisation, garde de démarrage sur `Jwt:SigningKey` et sur le compte admin.

## Défauts — identity-service

**I-01 — MEDIUM — Le jeton de réinitialisation de mot de passe transite en clair par l'outbox et par Kafka.**
`Application/Users/Commands/PasswordReset/RequestPasswordResetCommandHandler.cs:71-79` :
`ResetToken = raw` dans `PasswordResetRequestedIntegrationEvent`. Le corps de l'événement est
persisté dans la table d'outbox du schéma `identity`, publié sur Kafka, puis relu par
notification-service. Un secret à usage unique valable une heure se retrouve donc au repos
dans deux bases et dans le journal Kafka, lisible par tout opérateur ayant accès à l'un des
trois. Le correctif précédent (suppression de la valeur de retour de la commande, documenté
l.10-32 de `RequestPasswordResetCommand.cs`) a fermé la fuite HTTP, pas celle-ci.

**I-02 — MEDIUM — `IConsumerInbox` enregistré et jamais résolu.**
`Infrastructure/IdentityModuleInstaller.cs:57`. Les 5 consumers
(`Application/Users/EventHandlers/BusinessRoleGrantHandlers.cs`) sont idempotents par état
(`User.AssignRole` ignore un rôle déjà présent, l.49 du fichier ; `RemoveRole` idem, l.144)
— l'exigence §19.5 est donc satisfaite en pratique, mais l'inbox déclarée reste morte et
laisse croire le contraire.

**I-03 — LOW — L'implémentation gRPC serveur ne vit pas dans le service.**
`IdentityGrpcService` est dans `shared/contracts/HBA.Identity.Contracts.Grpc/IdentityGrpc.cs:78`,
c'est-à-dire dans le paquet que les CLIENTS référencent. Un service tiers qui consomme le
client embarque donc aussi le code serveur d'identity. Même remarque pour media-service
(`shared/contracts/HBA.Media.Contracts.Grpc/MediaGrpcService.cs`) et promotion-service.

**I-04 — LOW — Le groupe `/api/v1/auth` est un `MapGroup` nu.**
`Api/Endpoints/IdentityEndpoints.cs:60`, contrairement à la règle posée dans
`shared/common/HBA.Shared.Hosting/Http/ApiAuthorization.cs:20-22` (« tout nouveau groupe part
de `MapAdminGroup` ou `MapAuthenticatedGroup`. Jamais de `MapGroup` nu »). Le filet est la
`FallbackPolicy` (`ServiceHostExtensions.cs:106`), qui rend une route ajoutée par distraction
au moins authentifiée. Écart assumable ici (le groupe est majoritairement anonyme par nature),
mais c'est une exception à une règle que le dépôt s'impose.

---

# 6. user-service

### user-service
**Path :** `services/common/user-service/`

**Projects :** `src/HBA.Users.Api`, `src/HBA.Users.Application`, `src/HBA.Users.Domain`,
`src/HBA.Users.Infrastructure` (4).

**Couches présentes :** Domain / Application / Infrastructure / Api.
**Couches manquantes : Contracts** (`shared/contracts/HBA.Users.Contracts`).

**Tests :** `tests/HBA.Users.Tests/UserDeviceTests.cs`, `UserEventContractTests.cs`,
`UserPreferencesTests.cs`.

**Volume :** 44 fichiers `.cs`.

**Agrégats & machines d'état :**
- `UserProfile` (`Domain/Profiles/UserProfile.cs`) — pas d'enum de statut.
- `Address` (`Domain/Addresses/Address.cs`) — modèle Bénin (département/commune/arrondissement,
  migration `20260805090000_BeninAddressModel`), coordonnées `double?` optionnelles (l.122-124),
  drapeau `IsDefault` avec unicité par utilisateur.
- `UserPreferences` (`Domain/Preferences/UserPreferences.cs`) — singleton par utilisateur.
- `UserDevice` (`Domain/Devices/UserDevice.cs`) — unicité du jeton, réattribution documentée
  (l.30).

Aucune machine d'état à proprement parler : le service est un CRUD de données de profil.

**Endpoints exposés** (`src/HBA.Users.Api/Endpoints/UserEndpoints.cs`) —
`MapAuthenticatedGroup("/api/v1/users")` (l.25), toutes scopées `/me` :
`me` GET (l.27), `me/profile` GET (l.35), `me/avatar` GET/PUT (l.38-39),
`me/preferences` GET/PUT (l.43-44, `AllowIdempotency`),
`me/devices` GET/POST (l.46-47, `RequireIdempotency`),
`me/addresses` GET/POST (l.49-52, `RequireIdempotency`),
`me/addresses/{id}` PUT/DELETE (l.53-54), `me/addresses/{id}/default` PUT (l.55).
Plus `/api/geo/benin` GET `AllowAnonymous` (l.105) — référentiel géographique public.

**gRPC exposé :** `shared/proto/user/v1/user.proto`, service `UserApi`, **2 RPC déclarés,
2 implémentés** (`shared/contracts/HBA.Users.Contracts.Grpc/…:49,64`), mappé par
`Api/Program.cs:56`. Conforme.

**Événements publiés :** aucun (le service est en aval).
**Événements consommés** (`Api/Program.cs:33-48`) : `UserRegisteredIntegrationEvent`,
`UserProfileUpdatedIntegrationEvent`, `UserAnonymizedIntegrationEvent`.

**Statut : COMPLET.**

## Défauts — user-service

**U-01 — LOW — Trois routes distinctes rendent le même handler.**
`Api/Endpoints/UserEndpoints.cs:27, 35, 38` : `me`, `me/profile` et `me/avatar` pointent
toutes sur `GetProfileAsync`. `GET /me/avatar` rend donc le profil entier là où le nom
promet une image. Contrat public trompeur.

**U-02 — INFO — Seul service du périmètre qui résout réellement `IConsumerInbox`.**
`Api/Integration/CreateUserProfileOnUserRegisteredHandler.cs:65,72`. C'est la référence dont
les quatre autres services (payment, identity, notification, promotion) s'écartent en
enregistrant l'inbox sans l'utiliser.

---

# 7. promotion-service

### promotion-service
**Path :** `services/common/promotion-service/`

**Projects :** `src/HBA.Promotions.Api`, `src/HBA.Promotions.Application`,
`src/HBA.Promotions.Domain`, `src/HBA.Promotions.Infrastructure` (4).

**Couches présentes :** Domain / Application / Infrastructure / Api.
**Couches manquantes : Contracts** (`shared/contracts/HBA.Promotions.Contracts` et
`…Contracts.Grpc`).

**Tests :** `tests/HBA.Promotions.Tests/PromotionRuleTests.cs`, `CouponTests.cs`,
`PromotionEventContractTests.cs`, `PromotionEventTests.cs`, `PromotionTests.cs` (5 fichiers —
la meilleure couverture unitaire du périmètre).

**Volume :** 25 fichiers `.cs` (~2 730 lignes hors migrations).

**Agrégats & machines d'état :**
- `Promotion` (`Domain/Promotions/Promotion.cs`) → `PromotionStatus`
  (`Domain/Promotions/PromotionPrimitives.cs:32`) : `Draft=0, Scheduled=1, Active=2,
  Exhausted=3, Expired=4, Cancelled=5`.
  Transitions réelles : `Create → Scheduled` (l.45) ; `Scheduled → Active` à la première
  consommation de budget (l.290-292) ; `→ Exhausted` (`Epuiser`, l.318) ;
  `Exhausted → Active` (`ReleaseBudget`, l.346-349) ; `→ Cancelled` (`Cancel`, l.352).
  **`Expired` n'est assigné nulle part** ; l'expiration est évaluée à la volée dans
  `EnsureApplicable` sur `StartsAtUtc`/`EndsAtUtc`. **`Draft` n'est assigné nulle part** non
  plus (`Create` part directement en `Scheduled`).
- `PromotionRule` (`Domain/Promotions/PromotionRule.cs`) — conditions d'éligibilité, typées
  par `rule_type` + JSON ; le constructeur interne est ouvert aux tests
  (`InternalsVisibleTo` dans le `.csproj`).
- `Coupon` + `CouponReservation` (`Domain/Promotions/Coupon.cs`) →
  `CouponReservationStatus` (l.8) : `Held=0 → Committed=1 | Released=2`.
  `Reserve` (l.109, idempotent par panier), `Commit` (l.157, idempotent), `Release` (l.181),
  `RevokeForCancelledOrder` (l.208). `HoldLifetime = 30 min` (l.39).
- Types de calcul : `PromotionScope` (Global/Marketplace/Food), `PromotionType`
  (Percent/Fixed/FreeDelivery), `PromotionContext`, `PromotionDiscount`
  (`AmountOffSubtotal`, `AmountOffDelivery`) — tous en `long` (unités entières, XOF).

**Endpoints exposés** (`src/HBA.Promotions.Api/Endpoints/PromotionEndpoints.cs`) :

| Route | Verbe | Policy | Ligne |
|---|---|---|---|
| `/api/v1/promotions/validate` | POST | `MapAuthenticatedGroup` | 37 |
| `/api/v1/merchant/promotions/` | POST | `RequireAdmin` + `RequireIdempotency` | 69-70 |
| `/api/v1/merchant/promotions/` | GET | `RequireAdmin` | 71 |
| `/api/v1/merchant/promotions/{id}` | DELETE | `RequireAdmin` | 72 |

**gRPC exposé :** `PromotionApi`, **4 RPC déclarés, 4 implémentés**
(`shared/contracts/HBA.Promotions.Contracts.Grpc/PromotionGrpcService.cs:37,46,82,104` :
`EvaluatePromotion`, `ReserveCoupon`, `CommitCoupon`, `ReleaseCoupon`), mappé par
`Api/Program.cs:59` avec `MapInternalGrpcService`. Conforme.

**Événements publiés :** `promotion.created`, `promotion.exhausted`, `coupon.used` — les 3
handlers enregistrés (`Infrastructure/PromotionsModuleInstaller.cs:68-70`).
**Événements consommés :** `OrderCancelledIntegrationEvent` et
`FoodOrderCancelledIntegrationEvent` (`Api/Program.cs:40-46`).

**Statut : PARTIEL.**

## Défauts — promotion-service

**PR-00 — HIGH (structurant) — IL N'EXISTE AUCUNE NOTION DE FINANCEUR D'UNE REMISE.**
Recherche exhaustive sur tout le dépôt des termes `Funder`, `financeur`, `FundedBy`,
`Sponsor`, `DiscountBearer`, `PlatformFunded`, `MerchantFunded` : **zéro occurrence**.
Concrètement :
- `Promotion` (`Domain/Promotions/Promotion.cs:55-78`) n'a ni `SellerId`, ni `OwnerId`, ni
  champ de financement. Le fichier des endpoints le constate lui-même
  (`Api/Endpoints/PromotionEndpoints.cs:55-58` : « la table `promotions` du §10.16 n'a AUCUNE
  colonne de propriétaire. Il n'existe donc rien sur quoi fonder un contrôle
  d'appartenance ») — et en tire la conclusion « on ferme au plus étroit », `RequireAdmin`
  sur les trois écritures. C'est un contournement d'autorisation, pas une réponse comptable.
- `PromotionDiscount` (`PromotionPrimitives.cs:63`) décompose la remise en
  `AmountOffSubtotal` / `AmountOffDelivery`, c'est-à-dire par POSTE, jamais par PAYEUR.
- `CouponUsedDomainEvent` (`Domain/Promotions/Events/PromotionDomainEvents.cs`) porte un
  `DiscountAmount` unique, sans imputation.

Or le reste de la plateforme suppose que la distinction existe :
`services/marketplace/cart-service/src/HBA.Commerce.Contracts/CartContracts.cs:33` porte
`SellerDiscount` **et** `PlatformDiscount` ; `order-service` les persiste
(`.../Persistence/Configurations/OrderConfiguration.cs:59,203`) ; et wallet-service calcule
le gain vendeur sur `(line.UnitBasePrice - line.SellerDiscount) * line.Quantity`
(`wallet-service/.../Earnings/AccrueEarningsOnOrderConfirmedHandler.cs:165`), en affirmant
en commentaire que « les réductions financées par la plateforme n'entament pas le revenu
vendeur » (`wallet-service/.../Domain/Earnings/SellerEarning.cs:17-19`).

Le pont entre les deux mondes n'existe pas : la seule source de `SellerDiscount` côté panier
est `NeutralPricingModuleApi` qui l'écrit **à zéro en dur**
(`services/marketplace/cart-service/src/HBA.Commerce.Infrastructure/Public/NeutralPricingModuleApi.cs:11`).
Résultat : **toute remise émise par promotion-service est implicitement supportée par la
plateforme**, sans que ce choix soit inscrit nulle part, sans qu'un vendeur puisse financer
une promotion sur sa propre marge, et sans qu'aucune écriture comptable ne le trace.
C'est structurant : y remédier exige une colonne sur `promotions`, un champ sur
`PromotionDiscount` et `CouponUsedDomainEvent`, et une reprise du calcul de `SellerEarning`.

**PR-01 — HIGH — Le budget d'une campagne n'est jamais rendu quand une réservation expire.**
`Domain/Promotions/Promotion.cs:337` — `ReleaseBudget` est documenté « Rend du budget quand
une réservation expire ou qu'une commande est annulée ». Ses deux seuls appelants
(`Application/Promotions/CouponUseCases.cs:332` et
`Application/Promotions/OrderCancellationUseCases.cs:76`) couvrent le `ReleaseCoupon` explicite
et l'annulation de commande. **Aucun balayage des réservations expirées n'existe** : aucun
`BackgroundService`, aucun `HostedService`, aucune commande de purge dans le service
(recherche `HasExpired` : 3 occurrences, toutes dans `Coupon.cs`, en lecture seule).
`CouponReservation.HasExpired` (`Coupon.cs:268`) ne sert qu'à ne pas compter la réservation
dans `CountsAt` (l.277) — pas à rendre le budget. Un panier abandonné immobilise donc son
budget **définitivement**, et la campagne bascule `Exhausted` sur des remises jamais
accordées. Le fichier de l'agrégat annonce l'inverse (`Promotion.cs:22-25` : « C'est pourquoi
une réservation EXPIRE. Sans expiration, quelques milliers d'abandons suffiraient à éteindre
une campagne qui n'a rien coûté ») — c'est précisément ce qui se produit.

**PR-02 — HIGH — Un vendeur ne peut créer aucune promotion ; la route « marchand » est réservée à l'administration.**
`Api/Endpoints/PromotionEndpoints.cs:69-72` : les trois routes de
`/api/v1/merchant/promotions` portent `RequireAdmin()`. Le chemin annonce « merchant », la
garde dit « plateforme ». C'est la conséquence directe de PR-00 (pas de propriétaire sur
`promotions`), assumé en commentaire (l.48, l.55-62). Fonctionnellement, la fonctionnalité promise
au §10.16 n'existe pas pour son destinataire.

**PR-03 — MEDIUM — `PromotionStatus.Expired` et `PromotionStatus.Draft` sont des valeurs mortes.**
`Domain/Promotions/PromotionPrimitives.cs:38-42`. Aucune assignation dans le dépôt
(recherche `Status = PromotionStatus.` : 5 sites, aucun vers `Expired` ni `Draft`). Des
écrans qui brancheraient un cas sur ces valeurs seraient du code mort — exactement le défaut
que `MediaEnums.cs` documente avoir évité.

**PR-04 — MEDIUM — `IConsumerInbox` enregistré et jamais résolu.**
`Infrastructure/PromotionsModuleInstaller.cs:52`. Les deux consumers
(`Api/Integration/ReleaseCouponsOnOrderCancelledHandlers.cs:35,95`) reposent sur
`RevokeForCancelledOrder` (`Coupon.cs:208`), idempotent par filtre de statut — acceptable,
mais l'inbox déclarée reste inutilisée.

**PR-05 — LOW — Devise « XOF » codée en dur en valeur par défaut.**
`Domain/Promotions/Promotion.cs:88` (`string currency = "XOF"`). Cohérent avec le marché,
mais c'est une valeur de configuration figée dans le domaine.

---

# 8. review-service

### review-service
**Path :** `services/common/review-service/`

**Projects :** `src/HBA.Engagement.Api`, `src/HBA.Engagement.Reviews.Application`,
`src/HBA.Engagement.Reviews.Contracts`, `src/HBA.Engagement.Reviews.Domain`,
`src/HBA.Engagement.Reviews.Infrastructure` (5).

**Couches présentes :** les cinq.
**Couches manquantes :** aucune. Mais comme payment-service, `HBA.Engagement.Api` est l'hôte
de **trois** services : `Api/Program.cs:35-36` installe `RecommendationsModuleInstaller` et
`WishlistModuleInstaller`, et `Endpoints/EngagementEndpoints.cs` expose leurs routes.

**Tests :** `tests/HBA.Engagement.UnitTests/ReponseAuxAvisTests.cs` (1 fichier, sur la garde
de réponse vendeur).

**Volume :** 34 fichiers `.cs`.

**Agrégats & machines d'état :**
`Review` (`Domain/Reviews/Review.cs`) → `ReviewStatus` (`Domain/Reviews/ReviewIds.cs:14`) :
`Published=0` (à la création, l.31), `→ Flagged=1` (`Flag`, l.79), `→ Rejected=2`
(`Reject`, l.91), `→ Published` (`Restore`, l.104). Publication immédiate, modération
a posteriori. `Rating` est un value object borné 1-5 (`ReviewIds.cs:22+`).
`isVerifiedPurchase` est toujours `true` : `SubmitReviewCommandHandler`
(`Application/.../SubmitReview/SubmitReviewCommandHandler.cs:73`) refuse tout avis dont la
commande n'appartient pas à l'acheteur (l.38), n'est pas payée (l.46-52) ou ne contient pas
le produit (l.55-58), et refuse les doublons (l.61).

**Endpoints exposés** (`src/HBA.Engagement.Api/Endpoints/EngagementEndpoints.cs`) —
uniquement le module Reviews :

| Route | Verbe | Policy | Ligne |
|---|---|---|---|
| `/api/engagement/reviews/{id}` | GET | `MapAuthenticatedGroup` | 18 |
| `/api/engagement/reviews/product/{productId}` | GET | authentifié | 19 |
| `/api/engagement/reviews/seller/{sellerId}` | GET | authentifié | 20 |
| `/api/engagement/reviews/product/{productId}/rating` | GET | authentifié | 21 |
| `/api/engagement/reviews/seller/{sellerId}/rating` | GET | authentifié | 22 |
| `/api/engagement/reviews/` | POST | authentifié, acheteur du jeton | 24 |
| `/api/engagement/reviews/{id}/reply` | POST | `MapSellerGroup` + résolution vendeur dans le handler | 42 |
| `/api/engagement/reviews/{id}/flag` | POST | `MapAdminGroup` (Admin+Moderator) | 65 |
| `/api/engagement/reviews/{id}/reject` | POST | `MapAdminGroup` | 66 |
| `/api/engagement/reviews/{id}/restore` | POST | `MapAdminGroup` | 67 |

**gRPC exposé :** **aucun, alors que le contrat existe.**
`shared/proto/engagement/v1/engagement.proto:7-14` déclare `EngagementApi` avec 7 RPC
(`GetReview`, `ListReviewsByProduct`, `GetProductRating`, `GetSellerRating`,
`GetProductRecommendations`, `GetUserRecommendations`, `GetWishlist`).
`shared/contracts/HBA.Engagement.Contracts.Grpc/` ne contient **que le `.csproj`** — aucune
classe n'hérite de `EngagementApiBase` dans le dépôt. `Api/Program.cs:31` appelle
`builder.AddHbaGrpc()` et **aucun `MapInternalGrpcService`** n'est mappé (vérifié : le fichier
s'arrête à `MapEngagementEndpoints()` l.42 puis les migrations).

**Événements publiés :** `ReviewPublished…`, `ReviewRejected…` — 2 handlers enregistrés
(`Infrastructure/ReviewsModuleInstaller.cs:42-43`).
**Événements consommés :** aucun.

**Statut : PARTIEL.**

## Défauts — review-service

**R-01 — HIGH — Le contrat gRPC `EngagementApi` (7 RPC) est publié et entièrement non implémenté ; le port gRPC est ouvert et vide.**
`Api/Program.cs:31` (`builder.AddHbaGrpc()`) sans aucun `MapInternalGrpcService<...>`.
Les sept RPC couvrent les notes produit et vendeur — c'est-à-dire ce que catalog-service et
merchant-service devraient lire pour afficher une note sans appel HTTP. Aujourd'hui ils ne
peuvent pas : ni serveur, ni client généré utilisé.

**R-02 — MEDIUM — Les avis et les notes produit ne sont pas lisibles sans compte.**
`Endpoints/EngagementEndpoints.cs:17` : `MapAuthenticatedGroup("/api/engagement/reviews")`
couvre les cinq lectures (l.18-22), y compris `/product/{productId}/rating`. Un visiteur non
connecté ne voit donc ni les avis ni la note d'un produit — sur une place de marché, c'est un
défaut fonctionnel, et cela oblige la vitrine à s'authentifier pour afficher des étoiles.

**R-03 — MEDIUM — Trois services partagent un hôte, trois `DbContext` et une seule sonde de vivacité.**
`Api/Program.cs:55-57` migre `RecommendationsDbContext`, `ReviewsDbContext` et
`WishlistDbContext`. Le fichier note lui-même (l.49-53) que « la sonde `/health/ready` ne
surveille que le premier : le service se déclarerait apte avec les deux tiers de son schéma
absents ». Le constat est écrit, le correctif ne l'est pas. Même situation dans
`payment-service/.../Program.cs:56-64`.

**R-04 — LOW — Un avis est publié avant toute modération.**
`Domain/Reviews/Review.cs:31` : `Status = ReviewStatus.Published` dès la création. La
modération est purement réactive (`Flag`/`Reject`), sans file d'attente ni détection
automatique. C'est un choix défendable, mais aucun `Pending` n'existe dans l'énumération :
il n'y a pas de chemin possible vers une modération a priori sans migration.

---

# 9. billing-service

### billing-service
**Path :** `services/common/billing-service/`

**Projects :** `src/HBA.Financial.Billing.Application`, `src/HBA.Financial.Billing.Contracts`,
`src/HBA.Financial.Billing.Domain`, `src/HBA.Financial.Billing.Infrastructure` (4).

**Couches présentes :** Domain / Application / Infrastructure / Contracts.
**Couches manquantes : Api.** Installé par `payment-service/src/HBA.Financial.Api/Program.cs:41` ;
routes écrites dans `payment-service/.../FinancialEndpoints.cs:105-138`.

**Tests : aucun.** Aucun projet de `tests/` ne référence `HBA.Financial.Billing.*` — pas un
seul test sur le calcul de commission, qui détermine pourtant le revenu de la plateforme sur
chaque vente.

**Volume :** 27 fichiers `.cs`, dont 8 de migrations. C'est le plus petit service financier.

**Agrégats & machines d'état :**
- `Invoice` (`Domain/Invoices/Invoice.cs:43`) → `InvoiceStatus` (l.14) :
  `Draft=0 → Issued=1 → Paid=2`. `AddLine` exige `Draft` (l.90), `Issue` exige `Draft`
  (l.102), `MarkPaid` exige `Issued` (l.119). **Aucun état `Cancelled`/`Void`** : une facture
  émise par erreur n'a aucune sortie.
- `InvoiceLine` (l.22) — entité enfant, `Amount` en `decimal`.
- `CommissionRule` (`Domain/Commissions/CommissionRule.cs:26`) → `CommissionScope` (l.14) :
  `Global`/`Category`/`Seller`, avec `Priority => (int)Scope` (l.59). Pas de machine d'état,
  seulement `IsActive` (`Deactivate` l.111 / `Reactivate` l.113) et `EffectiveFromUtc`.
- `CommissionResolver` (`Domain/Commissions/CommissionResolver.cs:9`) — service de domaine
  pur, sélectionne la règle applicable la plus spécifique.

**Endpoints exposés** (écrits dans `payment-service/.../FinancialEndpoints.cs`) :

| Route | Verbe | Policy | Ligne |
|---|---|---|---|
| `/api/financial/commissions/` | GET | authentifié, **aucune garde** | 106 |
| `/api/financial/commissions/compute` | GET | `FINANCE_VIEW` + appartenance vendeur | 107 / 359 |
| `/api/financial/commissions/` | POST | `RequireAdmin` | 108 |
| `/api/financial/commissions/{id}` | PUT | `RequireAdmin` | 109 |
| `/api/financial/commissions/{id}/deactivate` | POST | `RequireAdmin` | 110 |
| `/api/financial/commissions/{id}/reactivate` | POST | `RequireAdmin` | 111 |
| `/api/financial/commissions/{id}` | DELETE | `RequireAdmin` | 112 |
| `/api/financial/invoices/{id}` | GET | `FINANCE_VIEW` + appartenance (après lecture) | 133 / 396 |
| `/api/financial/invoices/seller/{sellerId}` | GET | `FINANCE_VIEW` + appartenance | 134 / 450 |
| `/api/financial/invoices/` | POST | `RequireAdmin` | 135 |
| `/api/financial/invoices/{id}/lines` | POST | `RequireAdmin` | 136 |
| `/api/financial/invoices/{id}/issue` | POST | `RequireAdmin` | 137 |
| `/api/financial/invoices/{id}/paid` | POST | `RequireAdmin` | 138 |

**gRPC exposé :** aucun. `ComputeCommission` est déclaré
(`shared/proto/financial/v1/financial.proto:14`) et non implémenté (voir P-07).

**Événements publiés : aucun.** Aucun `Raise(`, aucun `IIntegrationEventPublisher`,
aucun `IDomainEventHandler` dans tout le service (recherche : 0 occurrence).
**Événements consommés : aucun.**

**Statut : PARTIEL.**

## Défauts — billing-service

**B-01 — HIGH — La devise est acceptée en paramètre, ignorée dans le calcul, puis recopiée dans la réponse.**
`Infrastructure/Public/CommissionModuleApi.cs:23-32` : `ComputeCommissionAsync(..., string currency, ...)`
ne passe jamais `currency` ni à `CommissionResolver.Resolve` (l.27) ni à
`rule.ComputeCommission` (l.30), et le rend tel quel dans `CommissionResult` (l.31).
`CommissionResolver` (`Domain/Commissions/CommissionResolver.cs:11-22`) ne filtre pas sur
`CommissionRule.Currency` non plus. Une règle dont le `FixedFee`, le `MinFee` ou le `MaxFee`
sont exprimés dans une devise se voit donc appliquée à un montant d'une autre devise —
la commission rendue est fausse et la réponse affirme la bonne devise. Sur une plateforme
qui affiche déjà `Currency` sur `CommissionRule` (l.52), c'est un champ décoratif.

**B-02 — HIGH — `ListCommissionRules` est ouverte à tout compte inscrit.**
`payment-service/.../FinancialEndpoints.cs:106` — voir P-12. Le défaut appartient
fonctionnellement à billing : la réponse porte les taux négociés par vendeur
(`Application/Commissions/CommissionQueries.cs:18` : `r.Rate, r.FixedFee, r.MinFee, r.MaxFee`),
que la garde de `ComputeCommission` (l.359-364 du même fichier) protège pourtant.

**B-03 — MEDIUM — Une facture émise ne peut être ni annulée ni avoirée.**
`Domain/Invoices/Invoice.cs:14-19` : `InvoiceStatus` n'a que `Draft/Issued/Paid`, et aucune
méthode `Cancel`/`Void`/`CreditNote` n'existe (l.88-125). Une facture de commission émise sur
une mauvaise période ou un mauvais montant reste définitivement dans le circuit. Aucune
migration ne prévoit la colonne.

**B-04 — MEDIUM — L'outbox est enregistré alors que rien ne le remplit.**
`Infrastructure/BillingModuleInstaller.cs:71` : `services.AddOutboxProcessor<BillingDbContext>();`
Or le service ne lève aucun événement de domaine et n'en publie aucun (voir « Événements
publiés » ci-dessus). Un `BackgroundService` tourne donc en permanence sur une table
structurellement vide. Corollaire plus gênant : une facture émise à un vendeur ne produit
**aucun** signal — ni notification, ni écriture au portefeuille, ni relance.

**B-05 — MEDIUM — Aucune génération périodique de factures.**
Le seul chemin de création est `POST /api/financial/invoices/` (`FinancialEndpoints.cs:135`),
geste manuel d'administration, suivi de trois autres appels manuels
(`/lines`, `/issue`, `/paid`). Aucun `BackgroundService`, aucune commande de clôture
mensuelle. Un service de facturation de commissions sans cycle est un CRUD.

**B-06 — MEDIUM — Zéro test sur le moteur de commission.**
Aucun projet sous `tests/` ne référence `HBA.Financial.Billing.*`. `CommissionResolver`
(priorité `Seller > Category > Global`), `CommissionRule.ComputeCommission`
(taux + fixe + plancher + plafond) et `CommissionModuleApi.DefaultCommission` (arrondi à 2
décimales, plafonné au brut, l.35-44) déterminent le revenu de la plateforme sur chaque
vente, et ne sont couverts par rien.

---

# 10. recommendation-service

### recommendation-service
**Path :** `services/common/recommendation-service/`

**Projects :** `src/HBA.Engagement.Recommendations.Application`,
`src/HBA.Engagement.Recommendations.Contracts`,
`src/HBA.Engagement.Recommendations.Domain`,
`src/HBA.Engagement.Recommendations.Infrastructure` (4).

**Couches présentes :** Domain / Application / Infrastructure / Contracts.
**Couches manquantes : Api.** Installé par `review-service/src/HBA.Engagement.Api/Program.cs:35` ;
routes écrites dans `review-service/.../EngagementEndpoints.cs:69-83`.

**Tests : aucun.**

**Volume :** 17 fichiers `.cs` — le plus petit du périmètre, dont 8 de migrations. Le code
métier tient en 4 fichiers.

**Agrégats & machines d'état :**
`Recommendation` (`Domain/Recommendations/Recommendation.cs:24`) — read model.
`RecommendationType` (Similar / FrequentlyBoughtTogether / Personalized).
**Aucune machine d'état** : `Create` (l.43) et `Refresh` (l.47), rien d'autre.
`Score` est un `double` (l.40) — acceptable, ce n'est pas un montant.

**Endpoints exposés** (écrits dans `review-service/.../EngagementEndpoints.cs`) :

| Route | Verbe | Policy | Ligne |
|---|---|---|---|
| `/api/engagement/recommendations/product/{productId}` | GET | `MapAuthenticatedGroup` | 70 |
| `/api/engagement/recommendations/me` | GET | authentifié, scopé au jeton | 71 |
| `/api/engagement/recommendations/users/{userId}` | GET | authentifié + appelant==userId ou Admin | 72 / 152 |
| `/api/engagement/recommendations/` | POST | `MapAdminGroup` | 83 |

**gRPC exposé :** aucun. `GetProductRecommendations` et `GetUserRecommendations` sont
déclarés (`shared/proto/engagement/v1/engagement.proto:12-13`) et non implémentés — voir R-01.

**Événements publiés : aucun.** **Événements consommés : aucun.**
(`Infrastructure/RecommendationsModuleInstaller.cs` : 37 lignes, aucun handler.)

**Statut : SQUELETTE.**

## Défauts — recommendation-service

**RE-01 — HIGH — Aucune recommandation n'est jamais calculée.**
Le seul chemin d'écriture du service est `POST /api/engagement/recommendations/`
(`review-service/.../EngagementEndpoints.cs:83`), une route admin qui prend
`UpsertRecommendationCommand` brut dans le corps. Aucun moteur, aucun batch, aucun
`BackgroundService`, aucun consumer d'événement (`RecommendationsModuleInstaller.cs`
n'enregistre ni `IIntegrationEventHandler` ni `IDomainEventHandler` — 0 occurrence). Le
service est une table alimentée à la main. Les écrans « produits similaires » et « pour vous »
sont donc vides en production, sans erreur.

**RE-02 — MEDIUM — Les requêtes rendent des données inventées quand rien n'existe.**
`Application/Recommendations/RecommendationsCommandsAndQueries.cs:80` et l.94 : à défaut de
ligne, les handlers renvoient un `RecommendationSummary(Guid.Empty, …, Array.Empty<Guid>(),
0d, DateTime.MinValue)` — c'est-à-dire un objet de synthèse **avec un identifiant nul et une
date de génération au 1ᵉʳ janvier 0001**, présenté au client comme une recommandation
existante. Un `null` ou un 404 serait exact ; ici l'API affirme avoir généré quelque chose.
Combiné à RE-01, tous les appels rendent cette réponse.

**RE-03 — MEDIUM — L'outbox est enregistré alors que rien ne le remplit.**
`Infrastructure/RecommendationsModuleInstaller.cs:36`. Le service ne lève aucun événement de
domaine (`Recommendation` n'appelle jamais `Raise`). `BackgroundService` permanent sur une
table vide.

**RE-04 — LOW — Pas de contrat d'événement, pas de `Contracts/IntegrationEvents/`.**
`HBA.Engagement.Recommendations.Contracts` ne contient que `RecommendationsContracts.cs`
(un DTO). Le service ne participe à aucune chorégraphie.

---

# 11. wishlist-service

### wishlist-service
**Path :** `services/common/wishlist-service/`

**Projects :** `src/HBA.Engagement.Wishlist.Application`,
`src/HBA.Engagement.Wishlist.Contracts`, `src/HBA.Engagement.Wishlist.Domain`,
`src/HBA.Engagement.Wishlist.Infrastructure` (4).

**Couches présentes :** Domain / Application / Infrastructure / Contracts.
**Couches manquantes : Api.** Installé par `review-service/src/HBA.Engagement.Api/Program.cs:36` ;
routes écrites dans `review-service/.../EngagementEndpoints.cs:85-89`.

**Tests : aucun.**

**Volume :** 19 fichiers `.cs`, dont 8 de migrations.

**Agrégats & machines d'état :**
`Wishlist` (`Domain/Wishlists/WishlistAggregate.cs:50`) + `WishlistItem` (l.17).
**Aucune machine d'état.** `Create` (l.67), `AddItem` (l.72), `RemoveItem` (l.90),
`SetAlerts` (l.102). `WishlistItem` porte `PriceAlert` et `StockAlert` (l.35-36).

**Endpoints exposés** (écrits dans `review-service/.../EngagementEndpoints.cs`) :

| Route | Verbe | Policy | Ligne |
|---|---|---|---|
| `/api/engagement/wishlist/` | GET | `MapAuthenticatedGroup`, scopé au jeton | 86 |
| `/api/engagement/wishlist/items` | POST | authentifié, scopé au jeton | 87 |
| `/api/engagement/wishlist/items/{productId}/alerts` | PUT | authentifié, scopé au jeton | 88 |
| `/api/engagement/wishlist/items/{productId}` | DELETE | authentifié, scopé au jeton | 89 |

**gRPC exposé :** aucun. `GetWishlist` est déclaré
(`shared/proto/engagement/v1/engagement.proto:14`) et non implémenté — voir R-01.

**Événements publiés : aucun.** **Événements consommés : aucun.**
(`Infrastructure/WishlistModuleInstaller.cs` : 40 lignes, aucun handler.)

**Statut : SQUELETTE.**

## Défauts — wishlist-service

**WI-01 — HIGH — Les alertes prix et stock sont stockées et ne déclenchent jamais rien.**
`Domain/Wishlists/WishlistAggregate.cs:35-36` (`PriceAlert`, `StockAlert`), route dédiée
`PUT /items/{productId}/alerts` (`review-service/.../EngagementEndpoints.cs:88`), colonne en
base (`Migrations/20250619000000_InitialWishlist.cs`). Recherche exhaustive du dépôt sur
`PriceAlert|StockAlert` **hors** du service et de son endpoint : **zéro occurrence**. Aucun
consumer de baisse de prix, aucun consumer de réapprovisionnement, aucun producteur
d'événement côté wishlist. L'utilisateur active une alerte, l'interface la lui confirme,
et rien ne se produira jamais.

**WI-02 — MEDIUM — L'outbox est enregistré alors que rien ne le remplit.**
`Infrastructure/WishlistModuleInstaller.cs:38`. Aucun `Raise` dans l'agrégat, aucun
`IDomainEventHandler` enregistré, aucun contrat d'événement dans
`HBA.Engagement.Wishlist.Contracts`. Le service est muet : personne n'apprend qu'un produit
a été mis en favori, alors que c'est précisément le signal dont recommendation-service
aurait besoin (voir RE-01).

**WI-03 — LOW — Aucun plafond au nombre d'articles.**
`Domain/Wishlists/WishlistAggregate.cs:72` (`AddItem`) ne borne pas `_items`. La liste d'un
compte peut croître indéfiniment, et `GetMyWishlistQuery` la charge entière.

---

# Annexe — constats transverses

**T-01 — MEDIUM — `IConsumerInbox` est enregistré dans 5 services et résolu dans 1 seul.**
Enregistrements : `payment-service/.../PaymentsModuleInstaller.cs:52`,
`identity-service/.../IdentityModuleInstaller.cs:57`,
`notification-service/.../NotificationsModuleInstaller.cs:77`,
`promotion-service/.../PromotionsModuleInstaller.cs:52`,
`user-service/.../UsersModuleInstaller.cs:63`.
Seule résolution : `user-service/src/HBA.Users.Api/Integration/CreateUserProfileOnUserRegisteredHandler.cs:65`.
L'exigence §19.5 (« consumers idempotents, Inbox ou équivalent ») est satisfaite par des
gardes d'état dans la plupart des handlers, mais l'infrastructure déclarée laisse croire à
une garantie plus forte qu'elle ne l'est. `IIdempotencyStore`, lui, est bien résolu par le
filtre partagé (`shared/common/HBA.Shared.Hosting/Http/IdempotencyEndpointFilter.cs:65`).

**T-02 — MEDIUM — Quatre « services » du découpage n'ont pas d'hôte et sont installés dans celui d'un voisin.**
`billing-service`, `wallet-service` → `payment-service/src/HBA.Financial.Api/Program.cs:41-42`.
`recommendation-service`, `wishlist-service` → `review-service/src/HBA.Engagement.Api/Program.cs:35-36`.
Ils partagent le port, l'image, le cycle de déploiement et la sonde `/health/ready` de leur
hôte — laquelle ne surveille que le premier `DbContext` (constat écrit dans les deux
`Program.cs`). Le découpage annoncé en « microservices .NET 9, une base logique par service »
n'est pas celui du code.

**T-03 — MEDIUM — Logique d'autorisation lourde dans la couche endpoint.**
`payment-service/src/HBA.Financial.Api/Endpoints/FinancialEndpoints.cs` fait 776 lignes et
contient `DenyUnlessOwnSellerAsync` (l.675-744) et `DenyUnlessOwnDriverAsync` (l.626-647),
qui font des appels gRPC inter-services (`IMerchantAccessApi.GetAccessAsync`,
`IDeliveryModuleApi.GetDriverAccountAsync`), évaluent des capacités et appliquent le step-up.
Cette logique appartient à un filtre d'autorisation ou à la couche Application, pas au
mapping HTTP : elle est aujourd'hui invisible des handlers, non testable unitairement, et
duplicable par oubli (P-04 en est l'illustration : la route v1 a été ajoutée sans la garde).

**T-04 — INFO — Deux contrats gRPC entiers sans serveur.**
`shared/proto/communication/v1/communication.proto` (6 RPC) et
`shared/proto/engagement/v1/engagement.proto` (7 RPC). Les deux projets
`shared/contracts/HBA.Communication.Contracts.Grpc/` et
`shared/contracts/HBA.Engagement.Contracts.Grpc/` ne contiennent que leur `.csproj`
(génération `GrpcServices="Both"`), et aucun `.cs` du dépôt n'hérite de
`CommunicationApiBase` ni de `EngagementApiBase`. Avec les 8 RPC non implémentés de
`FinancialApi`, cela fait **21 RPC déclarés au contrat et absents à l'exécution** sur le seul
périmètre `common/`.

**T-05 — INFO — Couverture de tests.**
21 fichiers de test pour 623 fichiers source sur le périmètre. Trois services n'ont aucun
test (`billing-service`, `recommendation-service`, `wishlist-service`), et billing calcule
le revenu de la plateforme. Les tests d'autorisation
(`tests/HBA.Financial.AuthorizationTests/FinancialAuthorizationTests.cs`) couvrent
`/api/financial/*` mais aucune route `/api/v1/payments/*` — c'est précisément là que se
trouve la fuite P-04.

---

# Domaine marketplace


Périmètre : `services/marketplace/{cart,catalog,inventory,order,return-refund,seller}-service`.
Analyse statique seule (pas de compilateur .NET). Tous les chemins sont relatifs à la racine du dépôt (`/root/audit-src` = racine).

---

## Vue d'ensemble

| Service | Volume | Tests | Statut |
|---|---|---|---|
| catalog-service | 197 `.cs` | 16 fichiers / 130 cas | **COMPLET** |
| seller-service | 118 `.cs` | 23 fichiers / 166 cas | **COMPLET** |
| order-service | 58 `.cs` | 1 fichier / 5 cas (autorisation) | **PARTIEL** |
| inventory-service | 46 `.cs` | **0** | **PARTIEL** |
| cart-service | 41 `.cs` | **0** | **PARTIEL** |
| return-refund-service | 55 `.cs` | **0** | **SQUELETTE** |

Aucun `TODO`, `FIXME` ni `NotImplementedException` dans le domaine. Aucun montant en `double`/`float` (seule occurrence de `double` : `ToleranceDegres` pour comparer des coordonnées GPS, `order-service/src/HBA.Order.Application/Orders/Commands/PlaceOrder/PlaceOrderCommandHandler.cs:36` — usage légitime). Les six projets `Domain` ne référencent que `HBA.Shared.Domain` : **aucune dépendance Domain → EF / ASP.NET / Kafka / gRPC**, la règle d'architecture est tenue partout.

---

### cart-service

**Path** : `services/marketplace/cart-service/`
**Projects** : `HBA.Commerce.Domain`, `HBA.Commerce.Application`, `HBA.Commerce.Infrastructure`, `HBA.Commerce.Api`, `HBA.Commerce.Contracts` (5).
**Couches présentes / manquantes** : les 5 couches présentes. Manquant : aucun projet de test.
**Tests** : **aucun**. Ni `tests/HBA.Commerce.*`, ni cas de test référençant `HBA.Commerce` dans `tests/`. 0 fichier, 0 cas. Le service le plus proche de l'argent après order n'a aucune couverture.
**Volume** : 41 fichiers `.cs` (dont 15 de migrations), ~2 150 lignes hors migrations.

**Agrégats & machines d'état**
- `Cart` (`HBA.Commerce.Domain/Carts/Cart.cs`) → enum `CartStatus { Active, CheckedOut }` (implicite, pas de fichier dédié).
  - Transitions autorisées : `Active → CheckedOut` uniquement, via `MarkCheckedOut()` (`Cart.cs:363`), qui exige `Status == Active` et au moins une ligne.
  - Transition réellement appelée : **une seule**, par `CloseCartOnOrderPlacedHandler.HandleAsync` (`HBA.Commerce.Application/Carts/EventHandlers/CloseCartOnOrderPlacedHandler.cs:35`), sur `OrderPlacedIntegrationEvent`. Aucun autre appelant : `POST /checkout` a été retiré (encadré `HBA.Commerce.Api/Endpoints/CommerceEndpoints.cs:34-62`).
  - Pas de retour possible depuis `CheckedOut` : un panier clos est terminal. Il n'existe aucune compensation si la commande échoue après `OrderPlaced` — le panier reste clos et l'acheteur doit tout resaisir (voir défaut C-5).
- `CartItem` (entité enfant) porte deux natures via `CartLineKind { Goods, Food }` ; l'homogénéité est garantie par `VerifierAjout` (`Cart.cs:212`).

**Endpoints** (11, tous sous `MapAuthenticatedGroup("/api/commerce/cart")`, `CommerceEndpoints.cs:21-32`) :

| Route | Verbe | Policy |
|---|---|---|
| `/api/commerce/cart/` | GET | authentifié, acheteur = jeton |
| `/api/commerce/cart/{id}` | GET | authentifié + comparaison `cart.BuyerId` ou rôle Admin (`CommerceEndpoints.cs:103`) |
| `/items` | POST | authentifié, acheteur = jeton |
| `/food-items` | POST | authentifié |
| `/items/{offerId}` | PUT / DELETE | authentifié |
| `/lines/{lineId}` | PUT / DELETE | authentifié |
| `/` | DELETE | authentifié |
| `/coupon` | POST / DELETE | authentifié |

Aucune route ne prend d'identifiant d'acheteur dans le corps ou l'URL : l'IDOR est fermé.

**gRPC exposé** : `hba.commerce.v1.CommerceApi` (`shared/proto/commerce/v1/commerce.proto`), 2 RPC — `GetActiveCart`, `GetCart` — implémentés par `shared/contracts/HBA.Commerce.Contracts.Grpc/CommerceGrpc.cs:35`, mappé en `HBA.Commerce.Api/Program.cs:28`. **Aucun RPC déclaré non implémenté.** Montants en chaîne, pas en `double` (bon choix, documenté au proto).

**Événements**
- Publiés : `CartCheckedOutIntegrationEvent` (`HBA.Commerce.Contracts/IntegrationEvents/CartIntegrationEvents.cs:6`).
- Consommés : `OrderPlacedIntegrationEvent` → `CloseCartOnOrderPlacedHandler` (enregistré `CartModuleInstaller.cs:49`).

**Statut** : **PARTIEL** — le cycle nominal fonctionne, mais la valorisation est un bouchon et il n'y a aucun test.

#### Défauts

| # | Sév. | Défaut | Preuve |
|---|---|---|---|
| C-1 | **CRITICAL** | `IPricingModuleApi` n'a **qu'une seule implémentation dans tout le dépôt** : le bouchon `NeutralPricingModuleApi`, enregistré en DI depuis `src/`. `CalculatePriceAsync` rend `FinalAmount = BaseAmount` (remise toujours nulle) et `ValidateCouponAsync` rend **toujours** `Invalid("pricing.unavailable")`. Conséquence : `POST /api/commerce/cart/coupon` échoue systématiquement, aucune promotion ne s'applique jamais, et toute la chaîne bâtie autour (`Cart.PromotionCode`, migration `20260714143416_AddCartPromotionCode`, `Order.PromotionCode`, `OrderConfirmedIntegrationEvent.PromotionCode`) est morte. | `HBA.Commerce.Infrastructure/CartModuleInstaller.cs:44` ; `HBA.Commerce.Infrastructure/Public/NeutralPricingModuleApi.cs:5-19` ; unique impl. vérifiée par recherche `IPricingModuleApi` sur tout le dépôt |
| C-2 | **HIGH** | **Aucune revalidation du prix.** `UnitBaseAmount` est figé à l'ajout au panier (`AddItemToCartCommandHandler.cs:156`, depuis `offer.EffectivePrice`) et n'est jamais relu ensuite : `CartPricer.PriceAsync` réutilise le snapshot (`CartPricer.cs:62`), et `PlaceOrderCommandHandler` fige ce même snapshot dans la commande (`PlaceOrderCommandHandler.cs:131-138`). Un vendeur qui baisse puis remonte son prix, ou une offre passée en promotion puis sortie, se facture au prix d'il y a une semaine. Le commentaire de `CartItem` promet que « Food refait le calcul » — pour la marchandise, personne ne le refait. | `HBA.Commerce.Domain/Carts/CartItem.cs:125` ; `HBA.Commerce.Application/Carts/CartPricer.cs:62` |
| C-3 | **HIGH** | **Le contrôle « produit publié » n'est pas revérifié au checkout.** `AddItemToCartCommandHandler` vérifie `offer.IsPurchasable` et `product.IsVisible` (lignes 97 et 115) — mais uniquement à l'ajout. `PlaceOrderCommandHandler` ne relit ni l'offre ni le produit : une fiche suspendue par la plateforme, dépubliée ou archivée après l'ajout se commande et se paie normalement. Le seul filtre restant est la réservation de stock. | `AddItemToCartCommandHandler.cs:97,115` vs `PlaceOrderCommandHandler.cs:63-138` (aucun appel catalogue) |
| C-4 | **MEDIUM** | `CartCheckedOutIntegrationEvent` est publié et **n'a aucun consommateur** dans le dépôt (le seul autre résultat de recherche est le `FoodCartCheckedOutIntegrationEvent` de food-cart-service, qui est un autre type). Le commentaire annonce « consommé par Ordering / analytics ». | `HBA.Commerce.Contracts/IntegrationEvents/CartIntegrationEvents.cs:6` ; `HBA.Commerce.Application/Carts/EventHandlers/CartCheckedOutDomainEventHandler.cs:16` |
| C-5 | **MEDIUM** | `CloseCartOnOrderPlacedHandler` clôt le panier sans **inbox ni clé d'idempotence** (le service n'enregistre ni `IConsumerInbox` ni `IIdempotencyStore`, cf. `CartModuleInstaller.cs:31-54`, à comparer à `CatalogModuleInstaller.cs:90-91`). Le rejeu est ici inoffensif (garde `Status != Active`, ligne 30), mais la protection est accidentelle et le prochain consommateur ajouté ne l'aura pas. Corollaire : aucune compensation ne rouvre un panier si la commande échoue ensuite. | `HBA.Commerce.Infrastructure/CartModuleInstaller.cs:31-54` |
| C-6 | **MEDIUM** | Le panier valorisé est mis en cache 2 minutes (`CartQueries.cs:19`) et `CartModuleApi.GetActiveCartAsync` — c'est-à-dire le chemin gRPC qu'emprunte order-service au checkout — passe par la **même requête cachée** (`CartModuleApi.cs:19`). Une commande peut donc être figée sur un panier vieux de deux minutes. | `HBA.Commerce.Application/Carts/Queries/CartQueries.cs:19,39-44` ; `HBA.Commerce.Infrastructure/Public/CartModuleApi.cs:17-21` |
| C-7 | **MEDIUM** | Couverture de tests nulle sur un agrégat qui porte cinq invariants non triviaux (homogénéité des natures, restaurant unique, unicité plat+options, devise, quantité). `MatchesFood` (`CartItem.cs:149`) n'est vérifié nulle part. | absence de `tests/HBA.Commerce.*` |
| C-8 | **LOW** | `Cart.RemoveItem` (`Cart.cs:264`) et `Cart.RemoveLine` (`Cart.cs:311`) ne vérifient pas `Status == Active`, contrairement à `UpdateItemQuantity` et `UpdateLineQuantity`. Un panier `CheckedOut` peut être vidé. Sans conséquence aujourd'hui (la commande est déjà figée), mais l'asymétrie est un piège. | `Cart.cs:264,311` |

---

### catalog-service

**Path** : `services/marketplace/catalog-service/`
**Projects** : `HBA.Catalog.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts` (5).
**Couches présentes / manquantes** : les 5 couches, complètes. Inbox de consommation et store d'idempotence enregistrés (`CatalogModuleInstaller.cs:90-91`) — seul service du domaine avec catalog+seller à le faire.
**Tests** :
- `tests/HBA.Catalog.UnitTests/` — 11 fichiers, **110 cas**. Couvrent réellement : cycle de vie produit (`ProductLifecycleTests.cs`, 20 cas dont `Publier_un_brouillon_est_refuse`, `Publier_un_produit_soumis_mais_non_approuve_est_refuse`, la liste blanche complète en `[Theory]`), révisions (`ProductRevisionTests.cs`), prix et variantes, état de l'article, spécifications, attributs de catégorie, demandes de marque, avis, projection publique.
- `tests/HBA.Catalog.AuthorizationTests/` — 2 fichiers, **13 cas** : routes publiques anonymes, gardes sur offres et variantes.
- `tests/HBA.Catalog.IntegrationTests/` — 3 fichiers, **7 cas** : parcours produit bout en bout, idempotence Kafka de l'inbox.
Total : 16 fichiers, 130 cas. C'est le seul service du domaine dont le cœur métier est réellement testé.
**Volume** : 197 fichiers `.cs` (~16 000 lignes hors migrations).

**Agrégats & machines d'état**

`Product` (`HBA.Catalog.Domain/Products/Product.cs`, 1 005 lignes) → `ProductStatus` (`ProductStatus.cs:32`) : `Draft, PendingReview, Approved, Rejected, Published, Unpublished, Suspended, Archived`.

Liste blanche (`ProductStatusTransitions.IsAllowed`, `ProductStatus.cs:68-118`) :

| De → Vers | Autorisé | Méthode | Appelée par |
|---|---|---|---|
| Draft → PendingReview | oui | `SubmitForReview` (`Product.cs:384`) | `ChangeProductStatusCommandHandler.cs:101` (vendeur, capacité `PRODUCT_SUBMIT_FOR_REVIEW`) |
| Draft → Archived | oui | `Archive` (`Product.cs:668`) | idem, `:104` |
| PendingReview → Approved | oui | `Approve(reviewedBy, now)` (`Product.cs:452`) | `AdminReviewCommandHandler.Handle(ApproveProductCommand)` (`AdminReviewCommands.cs:86`) |
| PendingReview → Rejected | oui | `Reject(reviewedBy, now)` (`Product.cs:484`) | `AdminReviewCommands.cs:137` |
| Rejected → Draft / Archived | oui | — / `Archive` | retour à Draft porté par la révision |
| **Approved → Published** | oui | `Publish` (`Product.cs:534`) | `ChangeProductStatusCommandHandler.cs:102` |
| **Unpublished → Published** | oui | `Publish` | idem |
| Approved → Suspended / Archived | oui | `Suspend` / `Archive` | `AdminReviewCommands.cs:156` / `:104` |
| Published → Unpublished / Suspended | oui | `Unpublish` / `Suspend` | `ChangeProductStatusCommandHandler.cs:103` / `AdminReviewCommands.cs:156` |
| Unpublished → Archived | oui | `Archive` | |
| Suspended → Approved | oui | `Restore` (`Product.cs:651`) | `AdminReviewCommands.cs:174` |
| **Draft → Published** | **NON** | — | — |

**Réponses aux questions posées :**

1. **Peut-on passer DRAFT → PUBLISHED directement ? Non.** Trois barrières indépendantes, et c'est le point fort du service :
   - la liste blanche ne contient aucune paire `(Draft, Published)` (`ProductStatus.cs:86-93`, encadré explicite) ;
   - `Product.Publish` exige que **la révision courante** soit `Approved` ou `Published` avant toute autre chose (`Product.cs:563-568`), donc « ce qui devient visible est exactement ce qu'un administrateur a lu » ;
   - `Product.Publish` re-vérifie ensuite le statut du produit (`Product.cs:570-573`).
   Testé : `tests/HBA.Catalog.UnitTests/ProductLifecycleTests.cs:22` (`Publier_un_brouillon_est_refuse`), `:35` (`Publier_un_produit_soumis_mais_non_approuve_est_refuse`), `:327` (`Les_transitions_hors_liste_blanche_sont_refusees`, `[Theory]`).
2. **Qui peut approuver ?** Uniquement l'administration. `ChangeProductStatusCommandHandler` — le seul chemin vendeur — refuse explicitement `Approved`, `Rejected` et `Suspended` avec `Error.Forbidden("catalog.product.admin_transition")` (`ChangeProductStatusCommandHandler.cs:106-109`), *avant même* que l'agrégat ne se prononce. Les routes d'approbation sont dans `MapAdminGroup` (`CatalogEndpoints.cs:124,161-164`).
3. **Y a-t-il un audit de l'acteur ?** **Oui pour approbation et rejet, non pour suspension et restauration.** `Approve`/`Reject` prennent `reviewedBy` et une ligne `ProductReview` est écrite dans la même transaction (`AdminReviewCommands.cs:92-102` et `:125-143`) avec l'ordre inversé entre les deux cas, argumenté. En revanche `SuspendProductCommand` et `RestoreProductCommand` ne portent **aucun identifiant d'acteur** et n'écrivent aucune `ProductReview` (`AdminReviewCommands.cs:45,48,148-182`) — voir défaut K-2.

`ProductOffer` → `OfferStatus` (`OfferStatus.cs:25`) : `Draft, Active, Paused, OutOfStock, Suspended, Archived`, liste blanche en `OfferStatusTransitions.IsAllowed` (`OfferStatus.cs:51-95`). Transition `Active → OutOfStock` **déclarée et jamais déclenchée** (défaut K-1).

**Endpoints** : 60 routes.
- 9 publiques `AllowAnonymous` sous `/api/v1/catalog` (`CatalogEndpoints.cs:72-107`) : marques, catégories, schéma d'attributs, produits (recherche, par id, par slug), produits d'un vendeur. Servies par `Queries/PublicCatalog/`, qui n'ont aucun paramètre capable d'élargir le périmètre.
- 25 sous `MapAdminGroup("/api/v1/catalog/admin")` (`:124-184`) : référentiel marques/catégories/attributs, vue de gouvernance, les 6 routes de validation (`/products/reviews`, `/products/{id}/review`, `approve`, `reject`, `suspend`, `restore`), demandes de marque.
- 26 sous `MapSellerGroup("/api/v1/catalog/seller")` (`:216-325`), chacune gardée par `DenyUnlessProductOwnerAsync` ou `DenyUnlessOwnerAsync` avec une capacité `MerchantCapabilities.*`. La capacité exigée par `POST /products/{id}/status` **dépend du statut visé** (`CapaciteDuStatut`, `CatalogEndpoints.cs:404-411`) : `PRODUCT_SUBMIT_FOR_REVIEW`, `PRODUCT_PUBLISH`, `PRODUCT_UNPUBLISH`. C'est le seul endroit du dépôt où une route porte trois permissions selon son corps.
- Idempotence : `AllowIdempotency()` (et non `Require`) sur les 6 créations, choix documenté (`CatalogEndpoints.cs:232-254`).

**gRPC exposé** : `hba.catalog.v1` (`shared/proto/catalog/v1/catalog.proto`), servi par `CatalogGrpcService` mappé en `Program.cs:47`. Clients consommés : `AddMerchantsGrpcClient` (garde de propriété) et `AddMediaGrpcClient` (`Program.cs:25,39`), tous deux réellement appelés.

**Événements**
- Publiés (14 handlers de domaine enregistrés, `CatalogModuleInstaller.cs:226-254`) : `ProductCreated`, `ProductSubmittedForReview`, `ProductApproved`, `ProductRejected`, `ProductPublished`, `ProductUnpublished`, `ProductSuspended`, `ProductRestored`, `ProductArchived`, `ProductMediaRemoved`, `BrandCreated`, `BrandRequested`, `BrandRequestApproved`, `CategoryCreated`.
- Consommés : `SellerClosedIntegrationEvent` → `SellerClosedProductInvalidationHandler`, `SellerDeletedIntegrationEvent` → `SellerDeletedProductPurgeHandler` (`CatalogModuleInstaller.cs:259-260`), tous deux idempotents via `IConsumerInbox` (`SellerLifecycleCatalogHandlers.cs:62,159`).

**Statut** : **COMPLET**.

#### Défauts

| # | Sév. | Défaut | Preuve |
|---|---|---|---|
| K-1 | **HIGH** | **Aucune offre ne passera jamais `OutOfStock`, et aucune ne reviendra jamais en vente après réassort.** `MarkOfferOutOfStockCommand` existe et son handler est écrit — il n'a **aucun émetteur** dans tout le dépôt. Le commentaire de `CatalogEndpoints.cs:284` dit qu'elle « appartient à Inventory, par événement » : or catalog-service **n'enregistre aucun handler** pour `StockDepletedIntegrationEvent` ni `StockReplenishedIntegrationEvent`. Le `ReactivateOffersOnStockReplenishedHandler` cité dans `OfferStatus.cs:80` **n'existe pas**. Résultat : une offre en rupture reste `Active` et achetable ; l'échec n'apparaît qu'à la réservation de stock, après le panier. | `HBA.Catalog.Application/Offers/OfferCommands.cs:48,221` (aucun `Send`) ; `HBA.Catalog.Infrastructure/CatalogModuleInstaller.cs:259-260` (2 consumers seulement) ; `HBA.Catalog.Domain/Offers/OfferStatus.cs:80` (handler inexistant) |
| K-2 | **MEDIUM** | `SuspendProductCommand` / `RestoreProductCommand` **ne portent aucun acteur** et n'écrivent aucune trace `ProductReview`, contrairement à `Approve`/`Reject`. Une suspension — la sanction la plus lourde du catalogue — laisse `SuspensionReason` mais on ne sait jamais **qui** l'a prononcée ni qui l'a levée. Asymétrie d'autant plus visible que le fichier explique longuement pourquoi la trace compte pour les deux autres. | `HBA.Catalog.Application/Reviews/AdminReviewCommands.cs:45,48` (signatures sans `ReviewedBy`), `:148-182` (aucun `_reviews.AddAsync`) |
| K-3 | **MEDIUM** | `CatalogEndpoints.cs` fait **1 465 lignes** et mélange déclaration de routes, gardes de propriété (`DenyUnlessProductOwnerAsync`, `DenyUnlessOwnerAsync`), résolution de capacités (`CapaciteDuStatut`) et 60 handlers. Les gardes sont de la logique d'autorisation métier, pas du transport. | `HBA.Catalog.Api/Endpoints/CatalogEndpoints.cs` (1 465 l.) |
| K-4 | **LOW** | `NullImageProcessor` est un no-op enregistré en DI depuis `src/` (`CatalogModuleInstaller.cs:198`). Contrairement aux bouchons de cart et return-refund, celui-ci est **acceptable** : il est conditionné à l'absence de configuration, il ne se déclare pas disponible via `IImageProcessingAvailability` (`:222-223`), et l'interface peut donc ne pas promettre le détourage. Signalé pour mémoire, pas comme défaut à corriger. | `HBA.Catalog.Infrastructure/CatalogModuleInstaller.cs:196-223` |

---

### inventory-service

**Path** : `services/marketplace/inventory-service/`
**Projects** : `HBA.Inventory.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts` (5).
**Couches présentes / manquantes** : les 5 couches. Manquant : projet de test, inbox de consommation, tout ouvrier de fond.
**Tests** : **aucun**. 0 fichier, 0 cas. Le service qui décide de la survente n'a aucun test — y compris pour `StockVersion`/`Touch`, dont le fichier de configuration explique sur 40 lignes que tout le verrou en dépend.
**Volume** : 46 fichiers `.cs` (dont 20 de migrations), ~1 700 lignes hors migrations.

**Agrégats & machines d'état**

Pas d'enum de statut : `InventoryItem` (`HBA.Inventory.Domain/Stock/InventoryItem.cs`) est un agrégat à quantités, avec `StockReservation` en entité enfant. Les « transitions » sont les mutations :

| Méthode | Garde | Événement levé | Appelée par |
|---|---|---|---|
| `Create` (`:85`) | `locationId` non vide, `onHand ≥ 0`, seuil ≥ 0 | `InventoryItemCreated` | `CreateInventoryItemCommand` ← `POST /items` (vendeur, `INVENTORY_ADJUST`) |
| `Receive(qty)` (`:106`) | `qty > 0` | `StockReplenished` si transition épuisé→disponible | `ReceiveStockCommand` ← `POST /items/{id}/receive` |
| `Reserve(orderId, qty, expiresAt)` (`:132`) | `qty > 0`, `Available ≥ qty` | `StockReserved` (+ `StockDepleted` si `Available == 0`) | `ReserveStockCommand` ← `InventoryModuleApi.TryReserveAsync` ← `PlaceOrderCommandHandler.cs:279` ; **et** `POST /reservations` (admin) |
| `ReleaseReservation(orderId)` (`:163`) | **aucune** | aucun | `ReleaseReservationCommand` ← `CancelOrderCommandHandler.cs:320` et compensation `PlaceOrderCommandHandler.cs:291` ; **et** `POST /reservations/release` (admin) |
| `ConfirmReservation(orderId)` (`:171`) | une réservation existe | aucun | `ConfirmOrderPaymentCommandHandler.cs:225` ; **et** `POST /reservations/confirm` (admin) |
| `AdjustOnHand(delta)` (`:186`) | `OnHand + delta ≥ Reserved` | `StockReplenished` si transition | `AdjustStockCommand` ← `POST /items/{id}/adjust` |
| `SetReorderThreshold` (`:208`) | seuil ≥ 0 | aucun | `PUT /items/{id}/reorder-threshold` |

`FulfillmentLocation` : type + `OwnerId` **nullable** (`null` = entrepôt plateforme), adresse béninoise. C'est cette colonne qui porte toute la chaîne d'autorisation du service.

**Réponses aux questions posées :**

- **Réservation de stock** : `Reserve` vérifie `Available = OnHand − Σ réservations` puis insère une ligne enfant. Chemin nominal = gRPC `InventoryApi.ReserveStock`, appelé par order-service.
- **Libération après échec de paiement** : oui, câblée. `PaymentFailedIntegrationEvent` → `CancelOrderOnPaymentFailedHandler` (`order-service/.../PaymentOutcomeHandlers.cs:71`) → `CancelOrderCommand` → boucle `ReleaseReservationAsync` (`OrderLifecycleCommands.cs:318-321`). Le résultat est inspecté via `SagaOutcome.Exiger` (`PaymentOutcomeHandlers.cs:92`) : un échec n'est pas avalé.
- **Survente possible ?** Sur le chemin gardé, non — le mécanisme est correct et bien raisonné (voir ci-dessous). Mais **oui par deux portes** : le SKU non suivi (I-2) et l'agrégation multi-lieux (I-3).
- **Concurrence** : le dispositif est **juste**, et c'est le point le plus soigné du service. `builder.UsePostgresRowVersion()` pose `xmin` en jeton (`InventoryItemConfiguration.cs:56`) ; comme `Reserve`/`ReleaseReservation` ne touchent que des lignes **enfants**, EF n'émettrait aucun `UPDATE inventory_items` et le jeton serait inerte — d'où `StockVersion` incrémenté par `Touch()` à **chaque** mutation (`InventoryItem.cs:83,121,150,166,181,198,216`). Deuxième écrivain → 0 ligne touchée → `DbUpdateConcurrencyException` → 409. Pas de retry automatique, délibéré (double dispatch d'événements). **Aucun test ne le vérifie.**
- **Mouvements de stock** : il n'y a **pas de journal de mouvements**. `StockMovementView` est une permission déclarée (`MerchantPermission.cs:13`, `MerchantCapabilities.cs:55`) qui ne garde aucune route et ne projette aucune table. La seule trace d'un ajustement est l'audit générique de `ModuleDbContext`.

**Endpoints** : 17 routes sur `/api/inventory` (`HBA.Inventory.Api/Endpoints/InventoryEndpoints.cs`).

| Route | Verbe | Policy |
|---|---|---|
| `/owners/{ownerId}/locations` | GET | authentifié + `acces.SellerId == ownerId` (**403**, id de vendeur) + `STOCK_LOCATION_VIEW` (`:342-369`) |
| `/items/{id}` | GET | authentifié + `DenyUnlessOwnerOfItemAsync` + `INVENTORY_VIEW` (**404**) |
| `/items/sku/{sku}` | GET | authentifié, **filtré** sur les lieux du vendeur via `MesLieuxAsync` qui exige `INVENTORY_VIEW` (`:520-550`) |
| `/items/by-locations` | POST | authentifié, lieux réduits **avant** la requête (`:490-512`) |
| `/availability/{sku}` | GET | authentifié seul — assumé, ne rend qu'un total sans lieu (`:552-567`) |
| `/locations`, `/low-stock` | GET | `MapAdminGroup` |
| `/reservations`, `/reservations/release`, `/reservations/confirm` | POST | `MapAdminGroup` — trappe d'exploitation, chemin nominal = gRPC |
| `/locations` (POST), `/locations/{id}/address` (PUT), `/locations/{id}` (DELETE) | | `MapSellerGroup` + `DenyUnlessOwnerAsync` + `STOCK_LOCATION_MANAGE` |
| `/items` (POST), `/items/{id}/receive`, `/items/{id}/adjust`, `/items/{id}/reorder-threshold` | | `MapSellerGroup` + garde propriété + `INVENTORY_ADJUST` |

La garde `DenyUnlessOwnerAsync` (`:242-295`) est correcte : propriété **d'abord** (404), capacité ensuite (403 enveloppé), `OwnerId is null` traité comme un refus explicite, step-up évalué.

**gRPC exposé** : `hba.inventory.v1.InventoryApi`, **7 RPC** déclarés (`shared/proto/inventory/v1/inventory.proto:7-25`) : `GetInventoryItem`, `ListInventoryBySku`, `GetAvailability`, `ReserveStock`, `ReleaseReservation`, `ConfirmReservation`, `GetLocation`. `InventoryGrpcService` mappé (`Program.cs:31`). Client consommé : `AddMerchantsGrpcClient` (`Program.cs:25`), réellement appelé par les 8 gardes.

**Événements**
- Publiés : `StockReservedIntegrationEvent`, `StockDepletedIntegrationEvent`, `StockReplenishedIntegrationEvent` (`InventoryDomainEventHandlers.cs`, enregistrés `InventoryModuleInstaller.cs:44-46`).
- Consommés : **aucun**. Aucun `IIntegrationEventHandler` enregistré.

**Statut** : **PARTIEL** — le noyau de concurrence est bon, mais trois trous ouvrent la survente et rien n'est testé.

#### Défauts

| # | Sév. | Défaut | Preuve |
|---|---|---|---|
| I-1 | **CRITICAL** | **Une réservation expirée n'est jamais libérée.** `StockReservation.ExpiresAtUtc` est écrite (`ReservationCommands.cs:46`, 15 min par défaut) puis **plus jamais lue** : aucun balayeur, aucun `BackgroundService` dans le service (le seul `BackgroundService` du domaine marketplace est dans return-refund, et c'est un bouchon). `Reserved` somme **toutes** les réservations sans filtrer l'expiration (`InventoryItem.cs:71`). Conséquence : tout panier abandonné entre `MarkAwaitingPayment` et un paiement qui n'arrive jamais immobilise du stock **définitivement**. Le champ donne l'illusion d'un mécanisme qui n'existe pas. | `HBA.Inventory.Domain/Stock/StockReservation.cs:25` (écrit, jamais lu) ; `HBA.Inventory.Domain/Stock/InventoryItem.cs:71` ; aucun `BackgroundService`/`IHostedService` dans `services/marketplace/inventory-service/` |
| I-2 | **HIGH** | **Un SKU sans ligne de stock est réputé disponible en quantité infinie.** `IsInStockAsync` rend `true` si `items.Count == 0` (`InventoryModuleApi.cs:90-93`) et `TryReserveAsync` rend `true` sans réserver si aucun enregistrement n'existe pour (SKU, lieu) (`:103-106`). Le comportement est documenté comme voulu pour les « articles non suivis », mais il n'existe **aucun drapeau** distinguant « non suivi » de « pas encore créé » : un vendeur qui publie une offre sans créer sa ligne de stock vend sans limite, et `ConfirmReservationAsync` rend `true` sans décrémenter (`:126`). | `HBA.Inventory.Infrastructure/Public/InventoryModuleApi.cs:90-93,103-106,124-127` |
| I-3 | **HIGH** | **`Reserve` n'est pas idempotent sur `OrderId`.** `_reservations.Add(...)` sans dédoublonnage (`InventoryItem.cs:144`) : deux `ReserveStock` pour la même commande posent **deux** réservations, et `ReleaseReservation` en libère bien les deux mais `ConfirmReservation` décrémente **la somme** (`:173,179`). La route admin `POST /reservations` et un rejeu gRPC ouvrent tous deux ce chemin. | `HBA.Inventory.Domain/Stock/InventoryItem.cs:144,173,179` |
| I-4 | **HIGH** | **La disponibilité est agrégée sur tous les lieux, donc sur tous les vendeurs.** `IsInStockAsync` et `GetAvailabilityAsync` somment `items.Sum(i => i.Available)` sur **tout** `Sku == value`, sans filtrer le lieu ni le propriétaire (`InventoryModuleApi.cs:69,95`). Deux vendeurs qui emploient le même SKU (un SKU n'est unique que par `(Sku, LocationId)`, `InventoryItemConfiguration.cs:114`) se prêtent leur stock : le panier valide l'ajout, la réservation au lieu réel échoue plus tard. Fuite d'information en prime — `GET /availability/{sku}` est ouverte à tout authentifié. | `HBA.Inventory.Infrastructure/Public/InventoryModuleApi.cs:63-69,81-95` ; `HBA.Inventory.Infrastructure/Persistence/Configurations/InventoryItemConfiguration.cs:114` |
| I-5 | **MEDIUM** | **Aucun journal de mouvements de stock.** Aucune table ni entité ne trace « qui a bougé quoi, quand, pourquoi ». `AdjustOnHand(delta)` (`InventoryItem.cs:186`) écrase la quantité sans motif ni acteur, et ne lève aucun événement en dehors de la transition épuisé→disponible. La permission `STOCK_MOVEMENT_VIEW` est déclarée et ne garde rien. | `HBA.Inventory.Domain/Stock/InventoryItem.cs:186-206` ; `HBA.Merchants.Domain/Members/MerchantPermission.cs:13` |
| I-6 | **MEDIUM** | `StockReservedIntegrationEvent` et `StockReplenishedIntegrationEvent` sont publiés et **n'ont aucun consommateur** dans tout le dépôt. `StockDepletedIntegrationEvent` n'en a qu'un, `StockDepletedNotificationHandler` (`services/common/notification-service/.../SellerLifecycleNotificationHandlers.cs:245`) : personne ne retire l'offre de la vente. Les commentaires annoncent « consommé par Ordering » et « consommé par le composition root pour relancer les offres ». | `HBA.Inventory.Contracts/IntegrationEvents/InventoryIntegrationEvents.cs:6,29` ; recherche exhaustive `IIntegrationEventHandler<Stock*>` |
| I-7 | **MEDIUM** | `ListLowStockAsync` charge **toute** la table `inventory_items` avec ses réservations puis filtre en mémoire (`InventoryItemRepository.cs:74-75`), parce que `IsLowStock` est calculé. Même schéma dans `GetAvailabilityAsync`/`IsInStockAsync` (`ToListAsync` puis `Sum`). Sur la route admin `/low-stock`, c'est un `SELECT *` du stock de la plateforme à chaque appel. | `HBA.Inventory.Infrastructure/Persistence/InventoryItemRepository.cs:71-76` |
| I-8 | **MEDIUM** | Couverture nulle sur le seul mécanisme dont tout le fichier de configuration dit qu'il empêche la survente (`StockVersion`/`Touch`/`xmin`). Un `Touch()` oublié dans une future mutation ne serait signalé par rien. | absence de `tests/HBA.Inventory.*` |

---

### order-service

**Path** : `services/marketplace/order-service/`
**Projects** : `HBA.Order.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts` (5).
**Couches présentes / manquantes** : les 5 couches. Manquant : tests unitaires du domaine, inbox de consommation.
**Tests** :
- `tests/HBA.Order.AuthorizationTests/OrderAuthorizationTests.cs` — 1 fichier, **5 cas** : un compte sans rôle ne lit pas la liste admin (`:34`), un compte qui n'est pas ce vendeur ne lit pas son carnet (`:52`), les trois transitions de saga retirées ne sont plus routées (`[Theory]`, `:72`), la sonde de vie est anonyme (`:90`), les routes protégées rendent 401 sans jeton (`[Theory]`, `:101`).
Couverture réelle : **uniquement l'autorisation HTTP**. Aucun test de la machine d'état `Order` (11 transitions), aucun test d'idempotence de `POST /api/orders`, aucun test de `BuildSellerShares`, aucun test des compensations de saga.
**Volume** : 58 fichiers `.cs` (dont 34 de migrations), ~2 400 lignes hors migrations.

**Agrégats & machines d'état**

`Order` (`HBA.Order.Domain/Orders/Order.cs`, 702 lignes) → `OrderStatus { Pending, AwaitingPayment, Paid, Confirmed, Delivered, Cancelled, Failed, UnderReview }`.

Pas de liste blanche centralisée : les gardes vivent dans chaque méthode.

| Transition | Méthode | Garde | Événement | Réellement appelée par |
|---|---|---|---|---|
| (création) → Pending | `Create` (`:181`) | ≥1 ligne, quantités > 0, natures homogènes, un seul restaurant | — | `PlaceOrderCommandHandler.cs:145` |
| Pending → AwaitingPayment | `MarkAwaitingPayment` (`:308`) | `Status == Pending` | `OrderPlaced` | `PlaceOrderCommandHandler.cs:299` |
| AwaitingPayment → Paid | `MarkPaid(paymentId)` (`:321`) | `Status == AwaitingPayment`, `paymentId != Empty` | **aucun** | `ConfirmOrderPaymentCommandHandler.cs:208` ← `PaymentCapturedIntegrationEvent` |
| Paid → Confirmed | `Confirm()` (`:339`) | `Status == Paid` | `OrderConfirmed` (avec `BuildSellerShares()`) | `ConfirmOrderPaymentCommandHandler.cs:228` |
| Confirmed/UnderReview → Delivered | `MarkDelivered` (`:392`) | `Status ∈ {Confirmed, UnderReview}` | `OrderDelivered` | `MarkOrderDeliveredCommandHandler.cs:105` ← `DeliveryCompleted` / `FoodOrderDelivered` |
| {Pending, AwaitingPayment, Paid} → Cancelled | `Cancel(reason)` (`:418`) | refuse `Confirmed`, `UnderReview`, `Cancelled`, `Failed` | `OrderCancelled` | `CancelOrderCommandHandler.cs:303` ← `POST /{id}/cancel` **et** `PaymentFailed` |
| Confirmed → Cancelled (repas) | `RejectByProvider` (`:477`) | `Kind == Food`, refuse Delivered / terminal / non payé | `OrderCancelled` | `RejectOrderByProviderCommandHandler.cs:265` ← `FoodOrderRejected` / `FoodOrderCancelled` |
| Confirmed → UnderReview | `MarkUnderReview` (`:569`) | refuse Delivered, déjà UnderReview, terminal, non confirmé | `OrderUnderReview` | `OrderReviewCommandHandler.cs:148` ← `HoldOrderOnDeliveryCancelledHandler` et `POST /admin/.../review/*` |
| UnderReview → Confirmed | `ResumeAfterReview` (`:639`) | `Status == UnderReview` | `OrderResumedAfterReview` (**pas** `OrderConfirmed`, argumenté `:627-638`) | `OrderReviewCommandHandler.cs:155` ← `POST /admin/orders/{id}/review/resume` |
| UnderReview → Cancelled | `CancelAfterReview` (`:681`) | `Status == UnderReview` | `OrderCancelled` | `OrderReviewCommandHandler.cs:160` ← `POST /admin/orders/{id}/review/refund` |
| * → Failed | `Fail(reason)` (`:697`) | **aucune garde** | **aucun** | `PlaceOrderCommandHandler.cs:294` (stock indisponible) |

Toutes les transitions déclarées ont un appelant réel. Aucune n'est morte.

**Réponses aux questions posées :**

1. **Idempotence de `POST /api/orders`** : traitée à deux niveaux, correctement. Applicatif : `GetByCartAsync(cart.CartId)` **en tout premier**, avant la relecture du devis et avant la boucle de réservation, et la commande existante est **rendue** plutôt qu'une erreur (`PlaceOrderCommandHandler.cs:93-102`). Base : index **unique** sur `ordering.orders."CartId"` (`Migrations/20260823000100_UnicitePanierParCommande.cs:81-86`), avec un contrôle préalable qui refuse la migration si des doublons existent, plutôt que de les effacer. **Manque** : la route ne porte pas `RequireIdempotency()`/`AllowIdempotency()` — l'unicité repose entièrement sur `CartId`. Voir O-1.
2. **Unicité `CartId`** : oui, index unique (ci-dessus). C'est la seule barrière contre deux requêtes **simultanées** — la lecture applicative ne les voit pas, ce que la migration documente elle-même.
3. **Lien vers le paiement** : `Order.PaymentId` (nullable, `Order.cs:85`), posé par `MarkPaid` (`:333`), colonne + index ajoutés par `Migrations/20260824000000_AddOrderPaymentId.cs`. Alimenté depuis `PaymentCapturedIntegrationEvent.PaymentId` (`PaymentOutcomeHandlers.cs:51`). Relu par return-refund via `OrderReturnContext.PaymentId` (`return-refund-service/.../OrderGrpcClient.cs:46`).
4. **`BuildSellerShares`** (`Order.cs:368-386`) : regroupe les lignes **`Goods` uniquement** par `SellerId`, somme quantités et `LineTotal` (prix final, remises comprises, **avant** commission). Le filtre `Kind == Goods` est indispensable et documenté : sans lui une commande de repas produirait une part attribuée au vendeur `00000000-…`, créditée par Wallet et comptabilisée par Sellers. Le montant est `decimal` de bout en bout. Traverse la frontière via `OrderConfirmedIntegrationEvent.SellerShares` (`OrderingIntegrationEvents.cs:65`), avec un record de contrat distinct du record de domaine.
5. **Statut piloté par quels événements** : `PaymentCapturedIntegrationEvent` → Paid+Confirmed ; `PaymentFailedIntegrationEvent` → Cancelled + libération ; `DeliveryCompletedIntegrationEvent` → Delivered ; `FoodOrderDeliveredIntegrationEvent` → Delivered ; `FoodOrderRejected`/`FoodOrderCancelled` → Cancelled ; `DeliveryCancelledIntegrationEvent` → UnderReview. Les six sont enregistrés (`OrderingModuleInstaller.cs:61-99`, `Program.cs:95-101`). Les deux qui touchent à l'argent inspectent le résultat via `SagaOutcome.Exiger` au lieu d'acquitter en aveugle (`PaymentOutcomeHandlers.cs:53,92`).

**Endpoints** : 8 routes.

| Route | Verbe | Policy |
|---|---|---|
| `/api/orders/` | GET | authentifié, acheteur = jeton |
| `/api/orders/{id}` | GET | authentifié + `GetOrderQuery(id, buyerId)` |
| `/api/orders/` | POST | authentifié ; `ShippingFee` **retiré du corps** |
| `/api/orders/{id}/cancel` | POST | authentifié + `RequesterId` comparé au `BuyerId` (`OrderLifecycleCommands.cs:298`) |
| `/api/admin/orders/` | GET | `MapAdminGroup` |
| `/api/admin/orders/{id}/review/resume` | POST | `MapAdminGroup` |
| `/api/admin/orders/{id}/review/refund` | POST | `MapAdminGroup`, motif obligatoire |
| `/api/sellers/{sellerId}/orders/` | GET | `MapSellerGroup` + `acces.SellerId == sellerId` (**403**) + `ORDER_VIEW` (`OrderEndpoints.cs:287-300`) |

Les trois routes qui exposaient des transitions de saga (`payment/confirm`, `delivered`, `provider/reject`) ont été retirées et un test le vérifie (`OrderAuthorizationTests.cs:72`).

**gRPC exposé** : `hba.order.v1` (`shared/proto/order/v1/order.proto`), `OrderingGrpcService` mappé (`Program.cs:109`). Clients consommés, **tous réellement appelés** : Inventory (réservations), Commerce (lecture du panier), Delivery (`LookupQuoteAsync` + création de course), Merchants (autorisation vendeur), Food (traduction `FOOD-…`).

**Événements**
- Publiés : `OrderPlaced`, `OrderConfirmed`, `OrderCancelled`, `OrderDelivered`, `OrderUnderReview`, `OrderResumedAfterReview` (6 handlers, `OrderingModuleInstaller.cs:46-58`).
- Consommés : `PaymentCaptured`, `PaymentFailed`, `DeliveryCompleted`, `DeliveryCancelled`, `OrderCancelled` (boucle interne course), `OrderConfirmed` (création de course), `FoodOrderRejected`, `FoodOrderCancelled`, `FoodOrderDelivered` — 9 au total.

**Statut** : **PARTIEL** — le service est fonctionnellement le plus abouti du domaine avec catalog, mais son cœur (saga, idempotence, parts vendeur) n'a aucun test unitaire.

#### Défauts

| # | Sév. | Défaut | Preuve |
|---|---|---|---|
| O-1 | **CRITICAL** | **Le stock réservé fuit si l'enregistrement de la commande échoue.** La boucle de réservation appelle Inventory (service distinct, transaction distincte) **avant** `SaveChangesAsync` (`PlaceOrderCommandHandler.cs:277-300`). Aucun `try/catch` n'entoure la ligne 300. Si l'index unique `IX_orders_CartId` refuse l'insertion — exactement le cas de deux requêtes simultanées que cet index existe pour fermer — l'exception remonte en 500 et les réservations posées ligne 279 **restent en place**, sur une commande qui n'existe pas. Combiné à I-1 (aucun balayage d'expiration), ce stock est perdu définitivement. La compensation n'existe que pour l'échec *métier* de réservation (`:288-292`), pas pour l'échec de persistance. | `order-service/src/HBA.Order.Application/Orders/Commands/PlaceOrder/PlaceOrderCommandHandler.cs:277-300` |
| O-2 | **HIGH** | **Aucune inbox : les consumers Kafka ne sont idempotents que par accident.** `OrderingModuleInstaller` n'enregistre ni `IConsumerInbox` ni `IIdempotencyStore` (à comparer à `CatalogModuleInstaller.cs:90-91` et `SellersModuleInstaller.cs:203-204`). Les 9 consumers ne sont protégés que par les gardes d'état de l'agrégat. Cela tient aujourd'hui — `MarkPaid` refuse si `Status != AwaitingPayment` — mais `ConfirmOrderPaymentCommandHandler` appelle `ConfirmReservationAsync` (décrément physique du stock, service distinct) **avant** `order.Confirm()` et avant `SaveChangesAsync` (`OrderLifecycleCommands.cs:223-234`) : si le `SaveChanges` échoue, le stock est décrémenté et la commande reste `AwaitingPayment` ; au rejeu, `MarkPaid` réussit et **décrémente une seconde fois**. | `HBA.Order.Infrastructure/OrderingModuleInstaller.cs:32-104` ; `HBA.Order.Application/Orders/Commands/OrderLifecycleCommands.cs:223-234` |
| O-3 | **HIGH** | **`POST /api/orders` n'exige aucune clé d'idempotence.** La route ne porte ni `RequireIdempotency()` ni `AllowIdempotency()` (`OrderEndpoints.cs:65`), là où user-service, promotion-service et payment-service le font (`services/common/payment-service/.../FinancialEndpoints.cs:93`). La seule protection est `CartId`. Si un jour une commande peut naître sans panier — ou si `GetActiveCartAsync` rend un panier différent, ce que le cache de 2 min rend possible (C-6) — il n'y a plus rien. | `HBA.Order.Api/Endpoints/OrderEndpoints.cs:65` |
| O-4 | **HIGH** | **`Order.Fail(reason)` n'a aucune garde d'état et ne lève aucun événement** (`Order.cs:697-701`). C'est la seule transition du fichier sans contrôle : elle écrase `Confirmed`, `Delivered` ou `Cancelled` sans protester, écrase `CancellationReason`, et n'informe personne — ni l'acheteur, ni financial-service. Elle n'a aujourd'hui qu'un appelant sûr (`PlaceOrderCommandHandler.cs:294`, sur une commande `Pending`), mais c'est un `public void` sur un agrégat public. | `HBA.Order.Domain/Orders/Order.cs:697-701` |
| O-5 | **MEDIUM** | **`MarkPaid` ne lève aucun événement de domaine** (`Order.cs:321-336`), contrairement aux dix autres transitions. L'état `Paid` — entre l'encaissement et la confirmation — est invisible de l'extérieur. Si `ConfirmReservationAsync` échoue pour une ligne, la commande reste `Paid` sans qu'aucun message ne sorte : l'acheteur est débité et rien ne le signale. C'est le seul état non observable de la machine. | `HBA.Order.Domain/Orders/Order.cs:321-336` |
| O-6 | **MEDIUM** | **Trou de recette assumé mais non chiffré** : une commande de marchandise sans `DeliveryQuoteId` est enregistrée avec `ShippingFee = 0`, la course étant achetée au prix réel par la plateforme (`PlaceOrderCommandHandler.cs:353-368`). Le refus n'existe que pour les repas. Le `LogWarning` est la seule mesure du manque — aucun compteur, aucune métrique. | `PlaceOrderCommandHandler.cs:362-368` |
| O-7 | **MEDIUM** | `PlaceOrderCommandHandler` fait **497 lignes** et cumule : lecture gRPC du panier, contrôle d'idempotence, validation des natures, construction de l'agrégat, validation d'adresse (deux règles différentes selon la nature), relecture et validation du devis de course (5 refus), boucle de réservation inter-services et compensation. Six responsabilités, trois services externes, une transaction. | `PlaceOrderCommandHandler.cs` (497 l.) |
| O-8 | **MEDIUM** | Aucun test unitaire sur les 11 transitions de `Order`, sur `BuildSellerShares` (répartition financière multi-vendeur) ni sur l'idempotence de `POST /api/orders`. Les 5 cas existants ne testent que l'autorisation HTTP. À comparer aux 110 cas de catalog sur un cycle de vie de complexité comparable. | `tests/HBA.Order.AuthorizationTests/OrderAuthorizationTests.cs` (5 cas, 1 fichier) |
| O-9 | **LOW** | `ListAllAsync` prend `int page, int pageSize` non nullables sans valeur par défaut (`OrderEndpoints.cs:144-146`) : un appel sans paramètres donne `page = 0, pageSize = 0`. À confirmer selon la normalisation faite dans `ListAllOrdersQuery`, non lue. | `OrderEndpoints.cs:144-146` |

---

### return-refund-service

**Path** : `services/marketplace/return-refund-service/`
**Projects** : `HBA.Marketplace.ReturnRefund.Domain`, `.Application`, `.Infrastructure`, `.Api` (**4** — pas de projet `Contracts`, contrairement aux cinq autres services).
**Couches présentes / manquantes** : les 4 couches existent. **Manquant** : projet `Contracts`, **toutes les migrations**, toute implémentation Kafka, toute implémentation gRPC serveur, trois clients gRPC sur cinq, le référentiel de politiques, et tout test.
**Tests** : **aucun**. 0 fichier, 0 cas. C'est le service qui rend de l'argent.
**Volume** : 55 fichiers `.cs`, ~2 470 lignes. Le service est **enregistré dans `HBA.sln:470-480` et déployé dans `docker-compose.dev.yml:584`** : ce n'est pas une maquette laissée de côté.

**Agrégats & machines d'état**

`ReturnRequest` (`Domain/Aggregates/ReturnRequest/ReturnRequest.cs`) → `ReturnStatus` (15 valeurs), table de transitions dans `Domain/Policies/ReturnStateMachine.cs:77-96`.

| Transition | Méthode | Appelée par |
|---|---|---|
| (création) → Requested → AwaitingApproval **ou** Approved | `Create` (`:89`), auto-approbation si `reasonCode ∈ AutoApproveReasons` (`:153`) | `CreateReturnCommandHandler.cs:180` |
| → Approved | `Approve` (`:182`) | `ApproveReturnCommandHandler` ← `POST /api/v1/seller/returns/{id}/approve` |
| → Rejected | `Reject` (`:194`) | `RejectReturnCommandHandler` ← `POST .../reject` **et** `POST /api/v1/admin/returns/{id}/override` |
| → Cancelled | `Cancel` (`:207`) | `CancelReturnCommandHandler` ← `POST /api/v1/marketplace/returns/{id}/cancel` |
| → AwaitingReturn | `RegisterShipment` (`:219`) | `RegisterReturnShipmentCommandHandler` ← `POST .../shipment` |
| → InReturnTransit | `MarkInTransit` (`:237`) | **PERSONNE** — aucun appelant |
| → Received | `Receive` (`:240`) | `ReceiveReturnCommandHandler` ← `POST .../receive` |
| → InspectionPending | `Inspect` (`:257`) | `InspectReturnCommandHandler` ← `POST .../inspection` |
| → RefundPending | `DecideRefund` (`:275`) | `DecideRefundCommandHandler` ← `POST .../refund-decision` |
| → Refunded | `MarkRefundSucceeded` (`:289`) | `ExecuteRefundCommandHandler.cs:79` — lui-même **sans aucun appelant** |
| → Closed | `Close` (`:309`) | `CloseReturnCommandHandler` ← `POST /api/v1/admin/returns/{id}/close` |
| → Expired, ManualReview, RejectedAfterInspection | déclarés dans la table | **aucune méthode ne les pose** |

**Réponses aux questions posées :**

- **Éligibilité** : `ReturnEligibilityPolicy.Evaluate` (`Domain/Policies/ReturnEligibilityPolicy.cs:37`) — trois règles : retour physique autorisé, remboursement-seul autorisé, fenêtre `deliveredAt + ReturnWindowDays`. Appelée depuis `ReturnRequest.Create:115`. La quantité est bornée par `ReturnItem.Create` (`ReturnItem.cs:56-60`, `RequestedQuantity ≤ DeliveredQuantity − AlreadyReturnedQuantity`). Les règles sont justes ; **le problème est la politique qu'elles appliquent** : elle est codée en dur (R-4).
- **Double remboursement possible ? OUI** — voir R-3. C'est le défaut le plus grave du domaine.
- **Qui décide ?** Formellement le vendeur (`POST /api/v1/seller/returns/{id}/refund-decision`) et l'administration. En pratique : **n'importe quel porteur du rôle `Seller`**, sur le dossier de n'importe quel autre vendeur (R-2).
- **Compensation** : aucune. Aucun chemin ne remet le stock en rayon (le client Inventory est un bouchon, R-5), aucun chemin ne rejoue un remboursement échoué (`RefundRetryWorker` est un bouchon), aucun chemin n'expire un dossier (`ExpireReturnsWorker` est un bouchon).

**Endpoints** : 19 routes.

| Groupe | Route | Verbe | Policy |
|---|---|---|---|
| Client (`CustomerReturnsEndpoints.cs:15`) | `/api/v1/marketplace/returns/` | POST | `MapAuthenticatedGroup` — **`OrderId` pris dans le corps, jamais comparé au jeton** |
| | `/` | GET | authentifié + `customerId` du jeton (seule route correcte du groupe) |
| | `/{id}` | GET | authentifié — **aucun contrôle de propriété** |
| | `/{id}/cancel` | POST | authentifié — **aucun contrôle de propriété** |
| | `/{id}/evidence` | POST | authentifié — **aucun contrôle de propriété** |
| | `/{id}/timeline` | GET | authentifié — **aucun contrôle de propriété** |
| Vendeur (`SellerReturnsEndpoints.cs:14`) | `/api/v1/seller/returns/` | GET | `MapSellerGroup` — **`sellerId` lié depuis la query string** |
| | `/{id}` GET, `/{id}/approve`, `/reject`, `/inspection`, `/refund-decision`, `/shipment`, `/receive` | | `MapSellerGroup` seul — **aucun `DenyUnlessOwner`, aucune capacité** |
| Admin (`AdminReturnsEndpoints.cs:124`) | `/api/v1/admin/returns/{id}`, `/override`, `/close` | | `MapAdminGroup` |
| Politiques (`ReturnPolicyEndpoints.cs:157`) | `/api/v1/admin/return-policies/` | GET / POST | `MapAdminGroup` — **corps codé en dur, rien n'est persisté** |

**gRPC exposé** : **aucun**. Le proto `contracts/grpc/return_refund.proto` déclare 3 RPC (`GetReturn`, `GetOrderReturnSummary`, `ValidateRefundStatus`) ; `ReturnRefundGrpcService` est une classe vide portant une constante (`Api/GrpcServices/ReturnRefundGrpcService.cs:3-6`) ; `Program.cs` appelle `builder.AddHbaGrpc()` mais **ne mappe aucun service** ; et aucun `.csproj` ne référence le `.proto`, qui n'est donc jamais compilé. **3 RPC déclarés, 0 implémenté.**

**Événements**
- Publiés : 8 événements de domaine levés (`ReturnRequested`, `ReturnApproved`, `ReturnRejected`, `ReturnShipmentRegistered`, `ReturnReceived`, `ReturnInspected`, `RefundRequested`, `RefundSucceeded`, `ReturnClosed`). **Aucun `IDomainEventHandler` n'est enregistré** (`ReturnRefundModuleInstaller.cs:57-87`) : les neuf disparaissent à la fin de l'unité de travail.
- Consommés : **aucun**. `ReturnRefundKafkaConsumers` est une classe portant un `string[]` de 6 noms de topics et rien d'autre.

**Statut** : **SQUELETTE**.

#### Défauts

| # | Sév. | Défaut | Preuve |
|---|---|---|---|
| R-1 | **CRITICAL** | **Le service ne peut pas démarrer utilement : il n'a aucune migration.** Aucun dossier `Migrations`, aucun `ModelSnapshot`, aucun fichier `[Migration(...)]` dans tout le service ; aucun script SQL ne crée le schéma `return_refund` dans `infra/`, `scripts/` ou `k8s/`. `Program.cs:20` appelle `MigrateHbaDatabaseAsync<ReturnRefundDbContext>()` qui n'appliquera rien. Toutes les routes échoueront en 500 au premier accès à la base. Les cinq autres services du domaine ont 15 à 34 fichiers de migration chacun. | `services/marketplace/return-refund-service/` (aucun fichier `*Migration*`) ; `Api/Program.cs:20` ; `Infrastructure/Persistence/ReturnRefundDbContext.cs:13` |
| R-2 | **CRITICAL** | **Fuite inter-vendeur et inter-client totale : aucune route ne vérifie à qui appartient le dossier.** (a) `ListAsync(Guid sellerId, …)` lie `sellerId` depuis la **query string** — tout compte `Seller` liste les retours de n'importe quel vendeur (`SellerReturnsEndpoints.cs:26-27`). (b) Les sept autres routes vendeur passent le `ReturnId` directement au handler ; aucun handler ne compare `request.SellerId` à l'appelant (`ReturnLifecycleCommands.cs:75-100, 109-198`) : un vendeur approuve, rejette, inspecte, réceptionne et **décide le remboursement** sur le dossier d'un concurrent. (c) Côté client, `GET /{id}`, `/{id}/timeline`, `POST /{id}/cancel` et `/{id}/evidence` ne comparent rien à `CustomerId` (`CustomerReturnsEndpoints.cs:34-45`). (d) `POST /returns` prend l'`OrderId` dans le corps et lit le `CustomerId` **depuis la commande**, jamais depuis le jeton (`CreateReturnCommand.cs:136,184`) : n'importe quel inscrit ouvre un retour sur la commande d'autrui. | `Api/Endpoints/SellerReturnsEndpoints.cs:26` ; `Api/Endpoints/CustomerReturnsEndpoints.cs:34-45` ; `Application/Commands/ReturnLifecycleCommands.cs:39-47,75-83` ; `Application/Commands/CreateReturn/CreateReturnCommand.cs:136,184` |
| R-3 | **CRITICAL** | **Double remboursement possible sur un même dossier.** Trois faits se combinent : (1) `ReturnStateMachine.CanTransition` rend `true` dès que `from == to` (`Domain/Policies/ReturnStateMachine.cs:29`), donc `RefundPending → RefundPending` est autorisé ; (2) `TotalRefunded()` ne compte que les remboursements `Succeeded`/`PartiallySucceeded` et **ignore les `Pending`** (`ReturnRequest.cs:335-336`) ; (3) `DecideRefund` ajoute une nouvelle entité `Refund` et lève `RefundRequestedDomainEvent` à chaque appel (`ReturnRequest.cs:283-286`). Deux `POST /{id}/refund-decision` successifs créent donc **deux remboursements du montant plein**, chacun avec sa propre clé d'idempotence (`$"return:{Id}:refund:{count+1}"`, `:284`) — le PSP ne peut pas les dédoublonner. Le plafond `RefundCalculationPolicy` ne mord pas, puisque `alreadyRefunded` vaut 0 tant que rien n'a abouti. | `Domain/Policies/ReturnStateMachine.cs:29` ; `Domain/Aggregates/ReturnRequest/ReturnRequest.cs:275-287,335-336` ; `Domain/Policies/RefundCalculationPolicy.cs:21-24` |
| R-4 | **CRITICAL** | **Aucun remboursement n'est jamais exécuté.** `ExecuteRefundCommandHandler` — le seul code qui appelle réellement le PSP — n'a **aucun émetteur** : `ExecuteRefundCommand` n'apparaît que dans sa propre déclaration et son handler, nulle part ailleurs dans le dépôt. `DecideRefund` lève `RefundRequestedDomainEvent`, mais **aucun `IDomainEventHandler` n'est enregistré** dans `ReturnRefundModuleInstaller`, et le `RefundRetryWorker` censé rattraper est un bouchon. Un dossier approuvé reste `RefundPending` indéfiniment : le client ne sera jamais remboursé, et rien ne le signale. | recherche `ExecuteRefundCommand` sur tout le dépôt → 2 occurrences, même fichier ; `Infrastructure/ReturnRefundModuleInstaller.cs:57-87` (aucun `IDomainEventHandler`) ; `Infrastructure/BackgroundJobs/ReturnRefundWorkers.cs:19-30` |
| R-5 | **HIGH** | **Trois clients gRPC sont des bouchons enregistrés en DI depuis `src/`.** `InventoryGrpcClient.ProcessReturnedStockAsync` rend `Result.Success()` sans rien faire (`Grpc/InventoryClient/InventoryGrpcClient.cs:9-10`) : la marchandise retournée n'est **jamais** remise en stock. `DeliveryGrpcClient.CreateReturnDeliveryAsync` fabrique une fausse référence `$"RET-DELIVERY-{returnId:N}"` (`Grpc/DeliveryClient/DeliveryGrpcClient.cs:8-9`) : aucune course de retour n'est créée, mais `RegisterShipment` la considère valide et fait avancer le dossier. `MediaGrpcClient.ValidateMediaAsync` rend `Success` pour tout `mediaId` non vide (`Grpc/MediaClient/MediaGrpcClient.cs:8-11`) : la preuve photo n'est jamais vérifiée. Les trois sont enregistrés en `AddScoped` (`ReturnRefundModuleInstaller.cs:77-79`). Seuls `OrderGrpcClient` et `PaymentGrpcClient` sont réels. | `Infrastructure/Grpc/{InventoryClient,DeliveryClient,MediaClient}/*.cs` ; `Infrastructure/ReturnRefundModuleInstaller.cs:77-79` |
| R-6 | **HIGH** | **Les trois `BackgroundService` sont des bouchons enregistrés en DI.** `ExpireReturnsWorker`, `RefundRetryWorker` et `OutboxPublisherWorker` écrivent une ligne de journal « active » puis rendent `Task.CompletedTask` (`Infrastructure/BackgroundJobs/ReturnRefundWorkers.cs:12-16, 25-29, 38-42`), tous trois enregistrés (`ReturnRefundModuleInstaller.cs:81-83`). Conséquences : aucun dossier ne passe jamais `Expired` (l'état est déclaré dans la machine et inatteignable), aucun remboursement échoué n'est rejoué, et le journal de démarrage affirme le contraire. | `Infrastructure/BackgroundJobs/ReturnRefundWorkers.cs:6-43` ; `Infrastructure/ReturnRefundModuleInstaller.cs:81-83` |
| R-7 | **HIGH** | **Aucun consumer Kafka n'existe, alors que six topics sont déclarés.** `ReturnRefundKafkaConsumers` n'est qu'un `string[]` de noms (`Infrastructure/Kafka/Consumers/ReturnRefundKafkaConsumers.cs:46-56`) ; `ReturnRefundInboxConsumer` et `ReturnRefundOutboxTopic` sont des `record` à un champ `Name` ; `ReturnRefundKafkaProducer` ne porte qu'une constante. Le compose fixe pourtant `KAFKA__CONSUMERGROUP: hba-return-refund-service` (`docker-compose.dev.yml:597`). Donc : `payment.refund.succeeded` n'est jamais consommé (le dossier ne passe jamais `Refunded` même si le PSP rembourse), `marketplace.order.delivered` n'est jamais consommé, `delivery.return-*` non plus — `MarkInTransit` (`ReturnRequest.cs:237`) n'a d'ailleurs aucun appelant. | `Infrastructure/Kafka/Consumers/ReturnRefundKafkaConsumers.cs` ; `Infrastructure/Kafka/{Inbox,Outbox,Producers}/*.cs` ; `docker-compose.dev.yml:597` |
| R-8 | **HIGH** | **Le contrôle « le montant décidé ne dépasse pas le calcul serveur » est vide.** `DecideRefundCommandHandler` construit le `RefundBreakdown` **à partir du montant envoyé par le client** : `new RefundBreakdown(amount.Value, zero, zero, zero, zero, zero, zero)` (`Application/Commands/ReturnLifecycleCommands.cs:176`). `breakdown.Total()` rend donc exactement `amount`, et le test `requested.Amount > calculated.Amount` de `RefundCalculationPolicy.cs:16` ne peut jamais échouer. En prime, `RestockingFeePercent`, `ReturnShippingCharge` et `PreviousRefunds` sont toujours zéro : les frais de remise en stock déclarés dans la politique ne sont jamais appliqués. Le seul plafond restant est `EstimatedRefundAmount`. | `Application/Commands/ReturnLifecycleCommands.cs:173-177` ; `Domain/Policies/RefundCalculationPolicy.cs:15-19` ; `Domain/ValueObjects/RefundBreakdown.cs:12-23` |
| R-9 | **HIGH** | **Aucune politique de retour n'est persistée.** `ReturnPolicyRepository.GetApplicableSnapshotAsync` ignore ses deux arguments (`productId`, `categoryId`) et rend un `PolicySnapshot` **codé en dur** : 14 jours, frais de remise en stock 0 %, auto-approbation de `WrongItem` et `DamagedOnArrival` (`Infrastructure/Persistence/Repositories/ReturnPolicyRepository.cs:62-73`). Les deux routes `/api/v1/admin/return-policies` sont du théâtre : le GET rend une liste littérale et le POST **renvoie l'écho de la requête sans rien écrire** (`Api/Endpoints/ReturnPolicyEndpoints.cs:158-170`). Un administrateur croit configurer la politique de retour de la place de marché ; rien n'est enregistré. | `Infrastructure/Persistence/Repositories/ReturnPolicyRepository.cs:62-73` ; `Api/Endpoints/ReturnPolicyEndpoints.cs:158-170` |
| R-10 | **HIGH** | **Les 3 RPC du proto sont déclarés et aucun n'est implémenté.** `ReturnRefundGrpcService` est une classe vide (`Api/GrpcServices/ReturnRefundGrpcService.cs:3-6`), aucun `MapInternalGrpcService` dans `Program.cs`, et le `.proto` n'est référencé par aucun `.csproj` — il n'est donc même pas compilé. Le compose publie pourtant `SERVICES__RETURNREFUND` (`docker-compose.dev.yml:1313`). | `contracts/grpc/return_refund.proto:7-11` ; `Api/GrpcServices/ReturnRefundGrpcService.cs` ; `Api/Program.cs:1-22` ; `Api/HBA.Marketplace.ReturnRefund.Api.csproj` |
| R-11 | **MEDIUM** | **`GetOrderReturnSummaryQueryHandler` est une implémentation factice** : il rend `new OrderReturnSummaryDto(query.OrderId, 0m, "XOF", 0)` sans lire la base (`Application/Queries/ReturnQueries.cs:128-132`). C'est le chiffre qui devrait alimenter `AlreadyReturnedQuantity` / `AlreadyRefundedAmount` côté order-service : le plafond anti-double-retour est donc alimenté par une constante. | `Application/Queries/ReturnQueries.cs:122-133` |
| R-12 | **MEDIUM** | **Les 10 constantes de `ReturnAuthorizationPolicies` ne gardent aucune route.** `return:create`, `return:approve`, `refund:decide`, `return:override`, `return-policy:manage`… sont déclarées (`Api/Authorization/ReturnAuthorizationPolicies.cs:4-13`) et n'apparaissent dans aucun `RequireAuthorization`. Symétriquement, les 6 permissions `RETURN_*` de `MerchantPermission` / `MerchantCapabilities` ne gardent aucune route non plus (voir S-1). Deux catalogues de permissions pour un service qui n'en applique aucune. | `Api/Authorization/ReturnAuthorizationPolicies.cs` ; recherche `ReturnAuthorizationPolicies` → 1 seule occurrence |
| R-13 | **MEDIUM** | `POST /api/v1/admin/returns/{id}/override` envoie un `RejectReturnCommand` (`Api/Endpoints/AdminReturnsEndpoints.cs:137`). La route s'appelle « override » et ne fait que rejeter : elle ne peut ni forcer une approbation, ni corriger un montant, ni débloquer un dossier coincé en `RefundPending` — les trois cas pour lesquels une trappe d'exploitation existe. | `Api/Endpoints/AdminReturnsEndpoints.cs:126,134-137` |
| R-14 | **MEDIUM** | `ReturnPolicyCache` est enregistré en **`AddSingleton`** (`ReturnRefundModuleInstaller.cs:71`) et encapsule un `Dictionary<string, object>` **non thread-safe** (`Infrastructure/Redis/ReturnPolicyCache.cs:79`). Il vit dans un dossier nommé `Redis/` alors qu'il est en mémoire du processus, et **il n'est injecté nulle part**. Corruption possible le jour où il servira, cache incohérent entre instances, et nom trompeur. | `Infrastructure/Redis/ReturnPolicyCache.cs:77-86` ; `Infrastructure/ReturnRefundModuleInstaller.cs:71` |
| R-15 | **MEDIUM** | Pagination fausse : `PagedResult(..., items.Count, page, pageSize)` passe la taille de la **page** comme total (`Application/Queries/ReturnQueries.cs:89,103`). Le client ne peut pas savoir combien de pages existent. Le dépôt ne fait d'ailleurs aucun `CountAsync` (`Infrastructure/Persistence/Repositories/ReturnRequestRepository.cs:163-175`). | `Application/Queries/ReturnQueries.cs:89,103` |
| R-16 | **MEDIUM** | `ReturnStatus.Expired`, `ManualReview` et `RejectedAfterInspection` figurent dans la table de transitions (`Domain/Policies/ReturnStateMachine.cs:82,85,86`) mais **aucune méthode de l'agrégat ne les pose**. Trois états morts sur quinze. `ReturnRequest.MarkInTransit` (`ReturnRequest.cs:237`) est publique et n'a aucun appelant. | `Domain/Policies/ReturnStateMachine.cs:77-96` vs `Domain/Aggregates/ReturnRequest/ReturnRequest.cs` |
| R-17 | **MEDIUM** | `Inspect` avale l'échec de transition : `if (transition.IsFailure && Status != ReturnStatus.InspectionPending)` (`ReturnRequest.cs:265`) — une inspection sur un dossier `Rejected` ou `Cancelled` échoue bien, mais le motif est perdu et l'inspection est ajoutée dans le cas `InspectionPending` sans que la garde ait servi. Par ailleurs `InspectReturnCommandHandler` appelle Inventory **dans une boucle avant** `SaveChangesAsync` (`ReturnLifecycleCommands.cs:154-159`) : même motif d'incohérence que O-1. | `Domain/Aggregates/ReturnRequest/ReturnRequest.cs:264-268` ; `Application/Commands/ReturnLifecycleCommands.cs:148-161` |
| R-18 | **LOW** | Le proto porte `int64 estimated_refund_amount` et `int64 returned_amount` (`contracts/grpc/return_refund.proto:21,31`) alors que tous les autres contrats de la plateforme font voyager les montants **en chaîne** — choix argumenté dans `shared/proto/commerce/v1/commerce.proto:19-24`. Le domaine, lui, est en `decimal`. Incohérence de contrat, sans effet tant que rien n'est compilé. | `contracts/grpc/return_refund.proto:21,31` |
| R-19 | **LOW** | Pas de projet `Contracts` : les événements d'intégration et DTO publics du service n'existent pas sous une forme consommable par un autre service, contrairement aux cinq autres. | arborescence `src/` (4 projets) |

---

### seller-service

**Path** : `services/marketplace/seller-service/`
**Projects** : `HBA.Merchants.Domain`, `.Application`, `.Infrastructure`, `.Api`, `.Contracts` (5).
**Couches présentes / manquantes** : les 5 couches, complètes. Inbox + idempotency store enregistrés (`SellersModuleInstaller.cs:203-204`). Verrou consultatif PostgreSQL par vendeur (`ISellerUnitOfWork.LockSellerAsync`).
**Tests** :
- `tests/HBA.Merchants.UnitTests/` — 8 fichiers, **125 cas** : cycle de vie vendeur (`SellerLifecycleTests.cs`), KYB (`SellerKybTests.cs`), boutiques (`StoreLifecycleTests.cs`), membres et rôles (`MembresEtRolesTests.cs`), invitations (`InvitationsTests.cs`), capacités (`CapacitesTests.cs`), + un builder `UnVendeur.cs`.
- `tests/HBA.Merchants.IntegrationTests/` — 14 fichiers, **34 cas** : parcours KYB (`ParcoursKybTests.cs`), pièces KYB (`PieceKybTests.cs`), fiche et file de validation, compte de reversement, compteurs vitrine, lieu d'expédition, purge RGPD (`PurgeRgpdTests.cs`), avec doubles d'identité, média, inventaire et bus.
- `tests/HBA.Merchants.AuthorizationTests/` — 1 fichier, **7 cas**.
Total : 23 fichiers, 166 cas. Le service le mieux testé du domaine.
**Volume** : 118 fichiers `.cs` (~13 300 lignes hors migrations).

**Agrégats & machines d'état**

**1. `Seller`** (`Domain/Sellers/Seller.cs`, 717 l.) → `SellerStatus { Pending, Active, Suspended, Closed, PendingReactivation }` (`Domain/Sellers/Enums.cs:4`) et `KybStatus { NotStarted, InReview, Verified, Rejected }` (`:26`).

| Transition | Méthode | Garde | Appelée par |
|---|---|---|---|
| → Pending (création) | `Register` (`:128`) | | `RegisterSellerCommandHandler` ← `POST /api/v1/merchants` |
| KYB NotStarted → InReview | `AddKybDocument` (`:181`) / `SubmitKyb` (`:255`) | | `POST /{sellerId}/kyb/documents`, `/kyb/submit` (capacité `KYB_MANAGE`) |
| KYB InReview → Verified | `ApproveKyb` (`:344`) | ≥1 pièce | `POST /{sellerId}/kyb/approve` (**admin**) |
| KYB InReview → Rejected | `RejectKyb(reason)` (`:382`) | refuse `NotStarted`, idempotent si déjà `Rejected` | `POST /{sellerId}/kyb/reject` (**admin**) |
| Pending → Active | `Activate` (`:432`) | `KybStatus == Verified` **et** `PayoutAccount != null` | `POST /{sellerId}/activate` (**admin**) |
| * → Suspended | `Suspend` (`:473`) | garde d'état, mémorise `SuspendedFromStatus` | `POST /{sellerId}/suspend` (**admin**) **et** `RejectKyb` en cascade (`:415`) |
| Suspended → Active/Pending | `LiftSuspension` (`:515`) | restaure `SuspendedFromStatus` | `POST /{sellerId}/lift-suspension` (**admin**) |
| Active → Closed | `RequestClosure` (`:570`) | | `POST /{sellerId}/close` (capacité `SELLER_CLOSE`) |
| Closed → PendingReactivation | `RequestReactivation` (`:583`) | | `POST /{sellerId}/reactivation` (`SELLER_REACTIVATE`) |
| PendingReactivation → Active | `ApproveReactivation` (`:624`) | | `POST /{sellerId}/reactivation/approve` (**admin**) |
| → supprimé | `MarkForDeletion` (`:670`) | | `DELETE /{sellerId}` (**admin**) |

Point notable : `RejectKyb` **suspend** un vendeur déjà `Active` (`Seller.cs:413-420`), en empruntant le chemin normal de suspension. Sans cela, un dossier refusé n'aurait aucun effet sur l'activité. Testé (`tests/HBA.Merchants.UnitTests/SellerKybTests.cs`).

**2. `Store`** (`Domain/Stores/Store.cs`) → ouverture/fermeture/suspension, avec `StoreSuspendedDomainEvent` distinct de `StoreClosedDomainEvent` (`SellersModuleInstaller.cs:186-189`).

**3. `SellerMember`** (`Domain/Members/SellerMember.cs`, 1 159 l.) → `MemberStatus { Active, Suspended, Revoked }`, rôles au niveau vendeur **et** au niveau boutique (`SellerMemberRole` + `StoreMembership`).

**4. `SellerInvitation`** (`Domain/Members/SellerInvitation.cs`, 504 l.) : jeton haché en base, rendu une seule fois à la création.

**5. `SellerRole`** (529 l.) + `MerchantPermission` (enum de 57 valeurs, `Domain/Members/MerchantPermission.cs`).

**Réponses aux questions posées :**

- **Onboarding** : `POST /api/v1/merchants` (authentifié simple, pas de rôle — c'est l'entrée), puis profil, compte de reversement, pièces KYB, soumission, approbation admin, activation admin. L'activation exige **deux** conditions (`Seller.cs:434-442`).
- **KYB** : 4 états, 4 types de pièce (`KybDocumentType`), pièces vérifiées en bloc à l'approbation (`:351-354`), motif de refus conservé (`KybRejectionReason`), suppression d'une pièce ramenant à `NotStarted` si c'était la dernière (`:323`), et purge du fichier média via `KybDocumentRemovedDomainEvent` (`:337`, handler enregistré `SellersModuleInstaller.cs:162`). Le rattachement d'une pièce est vérifié auprès de media-service (`Program.cs:29`, motivation détaillée : sans ce client, « un vendeur rattachait à son dossier la pièce d'identité d'un concurrent »).
- **Membres et rôles** : `MemberAccessResolver.ResolveAsync` (`Application/Members/MemberAccessResolver.cs:48`) est **la** garde : appartenance au vendeur, membre actif (`CanAct`), rôles chargés, `MemberActor` construit. Refus explicite pour un membre suspendu, avec un motif distinct (`:65-69`). Conséquence assumée et documentée : un administrateur plateforme **ne gère pas** l'équipe d'un vendeur.
- **Permissions par boutique** : oui. `SellerMember` distingue les rôles au niveau vendeur des `StoreMembership` par boutique ; `MerchantAccess.CanInStore` porte le contrôle, et `DenyUnlessOwnSellerAsync` accepte un `storeId` optionnel (`MerchantEndpoints.cs:689,707-742`). Le propriétaire n'a aucune affectation : son socle est son union (`SellerMember.cs:1027`).
- **Dernier propriétaire non supprimable** : **oui, et c'est correct**, y compris sous concurrence. `EnsureNotLastOwner` (`SellerMember.cs:840-845`) garde `Suspend`, `Revoke` et `Leave`. Le décompte vient de l'appelant (`CountActiveOwnersAsync`), pris **après** un `LockSellerAsync` — verrou consultatif PostgreSQL sur le vendeur, pris **avant** la lecture (`Application/Members/MemberCommands.cs:664,687` et `:567-570` pour `Leave`). L'encadré `MemberCommands.cs:653-680` corrige explicitement une affirmation antérieure fausse : `xmin` est un jeton **par ligne**, révoquer O1 et O2 écrit deux lignes différentes, aucun conflit — seul le verrou par vendeur ferme la course. En complément, `EnsureCanAdminister` refuse qu'un non-propriétaire touche un propriétaire (`SellerMember.cs:823-828`), ce qui ferme l'escalade `SELLER_ADMIN → révoque le propriétaire`.
- **Fuite inter-vendeur** : le cloisonnement est vérifié **deux fois** — dans le handler (`MemberCommands.cs:646-649`, « évite de charger des rôles pour rien ») et dans l'agrégat (`EnsureCanAdminister`, `SellerMember.cs:800-803`), avec un 404 uniforme pour ne pas révéler l'existence. Règle 403/404 respectée : identifiant de **vendeur** venant de l'URL → 403 explicite (`MerchantEndpoints.cs`, `ListOwnerLocationsAsync` d'inventory `:348-359`, `ListBySellerAsync` d'order `:280-295`) ; identifiant de **ressource** → 404.

**Endpoints** : 49 routes.

| Groupe | Préfixe | Policy |
|---|---|---|
| Inscription | `/api/v1/merchants` (`GET /me`, `POST /`) | `MapAuthenticatedGroup` (`:92-94`) |
| Vendeur | `/api/v1/merchants/{sellerId}` (7 routes) | `MapSellerGroup` + `DenyUnlessOwnSellerAsync(permission)` (`:110-145`) |
| Gouvernance | `/api/v1/merchants` (8 routes) | `MapAdminGroup` (`:164-172`) |
| Boutiques | `/{sellerId}/stores` (9 routes) | `MapSellerGroup` + permission + `storeId` (`:174-183`) |
| Gouvernance boutique | `/{sellerId}/stores/{storeId}/(suspend\|lift-suspension)` | `MapAdminGroup` (`:194-197`) |
| Équipe | `/{sellerId}/members` (12 routes + `/audit`) | `MapSellerGroup` **puis** `MemberAccessResolver` + `MerchantPermission` dans la commande (`:219-296`) |
| Rôles | `/{sellerId}/roles` (3 routes) | idem (`:243-278`) |
| Permissions | `/api/v1/merchants/permissions` | `MapSellerGroup` (catalogue en lecture) |
| Acceptation | `/api/v1/merchants/invitations/accept` | `MapAuthenticatedGroup` — **aucun `sellerId`**, le jeton d'invitation désigne tout (`:320-323`) |

**gRPC exposé** : `hba.merchant.v1` (`shared/proto/merchant/v1/merchant.proto`), `MerchantsGrpcService` mappé (`Program.cs:54`). C'est le fournisseur de `IMerchantAccessApi.GetAccessAsync`, consommé par catalog, inventory et order pour toutes leurs gardes. Clients consommés : Identity, Media, Inventory, Ordering (`Program.cs:15,29,39,48`), **tous réellement appelés**.

**Événements**
- Publiés : 15 handlers de domaine enregistrés (`SellersModuleInstaller.cs:153-190`) — cycle vendeur (5), KYB (3), boutique (4), suspension (2), pièce KYB retirée (1).
- Consommés : `UserAnonymizedIntegrationEvent` → `UserAnonymizedSellerPurgeHandler` (RGPD), `SellerRatingRecomputedIntegrationEvent` → `SellerRatingHandler`, `OrderConfirmedIntegrationEvent` → `SellerSalesCountHandler` (`:216-236`). Les trois utilisent `IConsumerInbox` (`SellerSalesCountHandler.cs:56`, `SellerRatingHandler.cs:50`, `UserAnonymizedSellerPurgeHandler.cs:66`) **et** posent une valeur recalculée plutôt que d'incrémenter — deux protections indépendantes contre le rejeu.

**Statut** : **COMPLET**.

#### Défauts

| # | Sév. | Défaut | Preuve |
|---|---|---|---|
| S-1 | **HIGH** | **23 permissions sur 57 sont déclarées et ne gardent aucune route dans tout le dépôt.** En croisant les deux catalogues (`MerchantPermission` utilisé dans seller-service, `MerchantCapabilities` utilisé par catalog/inventory/order/financial), n'apparaissent dans aucune garde : `RETURN_VIEW`, `RETURN_APPROVE`, `RETURN_REJECT`, `RETURN_CONFIRM_RECEIVED`, `RETURN_INSPECT`, `RETURN_DISPUTE_VIEW` (les 6 retours — cohérent avec R-2, aucune route de return-refund n'est gardée), `ORDER_CONFIRM`, `ORDER_REJECT`, `ORDER_MARK_PREPARING`, `ORDER_MARK_READY`, `ORDER_CANCEL` (5 gestes de commande vendeur), `INVENTORY_TRANSFER`, `STOCK_MOVEMENT_VIEW`, `REVIEW_VIEW`, `ROLE_ASSIGN`, `OWNERSHIP_TRANSFER`, `SECURITY_POLICY_UPDATE`, `BANK_ACCOUNT_UPDATE`. Elles apparaissent uniquement dans le semis des rôles système (`Domain/Members/SellerRole.cs`) et sont donc **attribuées** à des rôles, affichées dans l'écran de droits, et sans effet. Le vendeur croit déléguer ; rien ne change. | `Domain/Members/MerchantPermission.cs:40-142` ; `Contracts/MerchantCapabilities.cs:29-142` ; croisement avec toutes les occurrences hors `SellerRole.cs` |
| S-2 | **HIGH** | **`OWNERSHIP_TRANSFER` est déclarée mais le transfert de propriété n'existe pas.** `SellerMember.SetRoles` refuse de retirer le rôle propriétaire avec le message « Le rôle de propriétaire se transfère, il ne se retire pas » (`SellerMember.cs:394-398`), et `EnsureNotLastOwner` renvoie « transférez la propriété d'abord » (`:840-845`) — **or aucune commande, aucun handler et aucune route ne réalisent ce transfert** (`SystemSellerRoles.OwnerId` n'est jamais réattribué ; `SellerMember.cs:1057-1061` refuse même son attribution directe). Le dernier propriétaire d'un vendeur est donc **définitivement irrévocable** : s'il perd son accès, le dossier devient inadministrable, et le message d'erreur renvoie vers une opération qui n'existe pas. | `Domain/Members/SellerMember.cs:394-398,840-845,1057-1061` ; `Contracts/MerchantCapabilities.cs:137` ; aucune occurrence de transfert dans `Application/Members/` |
| S-3 | **MEDIUM** | Aucune trace d'acteur sur les décisions de gouvernance vendeur : `SuspendSellerCommand`, `LiftSuspensionCommand`, `ApproveKybCommand`, `RejectKybCommand`, `ApproveReactivationCommand` et `DeleteSellerCommand` ne portent pas d'identifiant d'administrateur (routes `MerchantEndpoints.cs:166-172`, handlers `Application/Sellers/Commands/`). Le motif est conservé (`KybRejectionReason`, `SuspensionReason`) mais pas l'auteur. Seul l'audit générique de `ModuleDbContext` en garde la trace, sans lien métier — à comparer à catalog, qui écrit une `ProductReview` nominative pour approbation et rejet. | `Api/Endpoints/MerchantEndpoints.cs:166-172` ; `Application/Sellers/Commands/SuspendSeller/SuspendSellerCommandHandler.cs` |
| S-4 | **MEDIUM** | `MerchantEndpoints.cs` fait **1 010 lignes** et `MemberCommands.cs` **845 lignes** ; `SellerMember.cs` fait **1 159 lignes** pour un seul agrégat. `MemberCommands.MuterAsync` (`:625-703`) cumule résolution d'acteur, chargement de cible, verrou consultatif, deux décomptes et mutation. Lisible et argumenté, mais au-delà du seuil où une relecture reste fiable. | `Api/Endpoints/MerchantEndpoints.cs` (1 010 l.) ; `Application/Members/MemberCommands.cs` (845 l.) ; `Domain/Members/SellerMember.cs` (1 159 l.) |
| S-5 | **MEDIUM** | Le semis des rôles système est conditionné à `Database:MigrateOnStartup` (`Api/Program.cs:85-108`). En production, où ce réglage est faux, **aucun rôle n'est attribuable** tant qu'un opérateur ne lance pas le semis hors ligne — et rien n'échoue : le service démarre, journalise une ligne d'information, et l'écran d'équipe est vide. Le couplage est documenté (il évite de casser les tests d'autorisation) mais reporte le risque sur l'exploitation. | `Api/Program.cs:85-108` ; `Infrastructure/Persistence/MerchantsDataSeeder.cs` |
| S-6 | **LOW** | Deux catalogues de permissions parallèles à maintenir à la main : l'enum `MerchantPermission` (domaine, seller-service) et les constantes `MerchantCapabilities` (contrats, autres services). Rien ne garantit qu'ils restent alignés — c'est d'ailleurs ce qui rend S-1 difficile à voir. | `Domain/Members/MerchantPermission.cs` vs `Contracts/MerchantCapabilities.cs` |

---

## Constats transverses

1. **Le bouchon `NeutralPricingModuleApi` est la seule implémentation de `IPricingModuleApi` du dépôt** (cart-service **et** food-cart-service l'enregistrent). Toute la mécanique promotionnelle — champ panier, migration dédiée, report dans la commande, événement de confirmation, décompte à la confirmation — est construite sur une interface qui n'a jamais de fournisseur réel.
2. **L'inbox de consommation n'existe que dans catalog et seller.** cart, inventory et order n'enregistrent ni `IConsumerInbox` ni `IIdempotencyStore` ; return-refund n'a pas de consumer du tout. Sur 15 consumers Kafka du domaine, 3 seulement sont idempotents par inbox.
3. **Cinq événements d'intégration publiés n'ont aucun consommateur** : `CartCheckedOut`, `StockReserved`, `StockReplenished` (aucun), `StockDepleted` (un, mais côté notification seulement). Chacun porte un commentaire nommant un consommateur qui n'existe pas.
4. **Le motif « appel externe avant `SaveChangesAsync` »** se répète dans les trois services qui touchent au stock ou à l'argent : `PlaceOrderCommandHandler.cs:277-300`, `OrderLifecycleCommands.cs:223-234`, `ReturnLifecycleCommands.cs:154-159`. À chaque fois, un échec de persistance laisse un effet distant sans compensation.
5. **La règle 403/404 du §29 est appliquée avec rigueur** partout sauf dans return-refund, qui n'applique aucune règle du tout.
6. **La qualité est très inégale** : catalog et seller sont documentés, testés et raisonnés jusqu'au détail des ordres d'opération ; return-refund est une maquette déployée en production.

---

## Les 12 défauts les plus graves

| Rang | Sév. | Service | Défaut | Fichier |
|---|---|---|---|---|
| 1 | CRITICAL | return-refund | Double remboursement : `from == to` autorisé + `TotalRefunded()` ignore les `Pending` | `Domain/Policies/ReturnStateMachine.cs:29` |
| 2 | CRITICAL | return-refund | Aucune route ne vérifie la propriété : tout vendeur agit sur le dossier d'un autre, tout client lit et annule celui d'un autre | `Api/Endpoints/SellerReturnsEndpoints.cs:26` |
| 3 | CRITICAL | return-refund | Aucun remboursement n'est exécuté : `ExecuteRefundCommand` n'a aucun émetteur, aucun handler d'événement enregistré | `Infrastructure/ReturnRefundModuleInstaller.cs:57-87` |
| 4 | CRITICAL | return-refund | Zéro migration : le schéma `return_refund` n'existe nulle part, le service est inopérant au démarrage | `services/marketplace/return-refund-service/` |
| 5 | CRITICAL | inventory | Réservations expirées jamais libérées : `ExpiresAtUtc` écrite et jamais lue, aucun balayeur | `Domain/Stock/StockReservation.cs:25` |
| 6 | CRITICAL | order | Stock réservé perdu si `SaveChangesAsync` échoue après la boucle de réservation (pas de `try/catch`) | `PlaceOrder/PlaceOrderCommandHandler.cs:277-300` |
| 7 | CRITICAL | cart | `NeutralPricingModuleApi` est l'unique implémentation de `IPricingModuleApi` : aucun coupon, aucune promotion ne fonctionne | `Infrastructure/CartModuleInstaller.cs:44` |
| 8 | HIGH | return-refund | Le plafond « montant ≤ calcul serveur » est vide : le breakdown est construit depuis le montant du client | `Application/Commands/ReturnLifecycleCommands.cs:176` |
| 9 | HIGH | catalog | Aucune offre ne passe `OutOfStock` ni ne revient en vente : aucun consumer stock, handler cité inexistant | `Application/Offers/OfferCommands.cs:221` |
| 10 | HIGH | inventory | SKU sans ligne de stock réputé disponible sans limite, et confirmation sans décrément | `Infrastructure/Public/InventoryModuleApi.cs:90-93,124-127` |
| 11 | HIGH | seller | 23 permissions sur 57 sont attribuées aux rôles et ne gardent aucune route | `Domain/Members/MerchantPermission.cs:40-142` |
| 12 | HIGH | cart | Aucune revalidation du prix ni de la publication entre l'ajout au panier et le paiement | `Domain/Carts/CartItem.cs:125` |

---

# Domaine food


Périmètre : `services/food/{availability,food-cart,food-order,kitchen-prep,menu,restaurant,review}-service`.
Analyse statique (pas de compilateur). Chemins relatifs à la racine du dépôt.

---

## 0. Constat d'ensemble

Le dossier `services/food/` contient **sept dossiers, trois services**.

| Service | .cs | Lignes C# | Persistance | Kafka | gRPC servi | Tests | Statut |
|---|---:|---:|---|---|---|---|---|
| restaurant-service | 66 | 17 278 (dont 5 053 de migrations) | PostgreSQL + EF + outbox | oui (7 pub / 3 cons) | `FoodGrpcService` | 206 lignes (autorisation) | **COMPLET** |
| food-order-service | 25 | 3 541 (dont 597 migrations) | PostgreSQL + EF + outbox | oui (6 pub / 5 cons) | `FoodOrderGrpcService` | aucun | **PARTIEL** (orphelin en aval) |
| food-cart-service | 26 | 2 233 (dont 366 migrations) | PostgreSQL + EF + outbox | oui (1 pub / 1 cons) | `FoodCartGrpcService` | aucun | **PARTIEL** |
| review-service | 11 | 98 | `ConcurrentDictionary` | non | non | aucun | **SQUELETTE** |
| kitchen-prep-service | 7 | 82 | `ConcurrentDictionary` | non | non | aucun | **SQUELETTE** |
| availability-service | 7 | 71 | `ConcurrentDictionary` | non | non | aucun | **SQUELETTE** |
| menu-service | 5 | 61 | `ConcurrentDictionary` | non | non | aucun | **SQUELETTE** |

Quatre services sur sept sont des **maquettes en mémoire** : aucun `DbContext`, aucun `Result<T>`,
aucun `ICommand`/`IQuery`, aucune authentification, aucun producteur ni consommateur Kafka.
Leurs projets `Domain` portent d'ailleurs le commentaire
« *Ce projet est volontairement vide : voir le README du service* »
(`services/food/menu-service/src/HBA.Food.Menu.Domain/HBA.Food.Menu.Domain.csproj:3-4`, idem
Kitchen et Availability) — et les README correspondants ne contiennent que trois lignes de
description, sans mention d'un état provisoire.

Aucun de ces quatre n'est déployé : `infra/docker/compose.services.yml` n'expose qu'un seul
service food (`food-service`, ligne 439, qui construit
`services/food/restaurant-service/Dockerfile`), et `k8s/base/services/` ne contient qu'un
`food-service/`. **food-cart-service et food-order-service ne sont pas déployés non plus**
(cf. défaut FOOD-01).

---

## 1. Fiches par service

### restaurant-service
**Path** : `services/food/restaurant-service/`
**Projects** : `HBA.Food.Restaurant.{Domain,Application,Infrastructure,Contracts,Api}` — 5 projets.
Les **assemblies** s'appellent `HBA.Food.Restaurant.*` mais **tous les namespaces** sont
`HBA.Food.*` (`HBA.Food.Domain.Orders`, `HBA.Food.Api.Endpoints`…). Écart dossier/namespace.
**Couches présentes** : Domain, Application, Infrastructure, Contracts, Api — complètes.
**Couches manquantes** : aucune.
**Tests** : `tests/HBA.Food.AuthorizationTests/` — 2 fichiers, 206 lignes, **autorisation HTTP
uniquement**. Aucun test de domaine, aucun test d'intégration, aucun test de saga.
**Volume** : 66 fichiers `.cs` (dont ~55 non triviaux ; 13 fichiers = 5 053 lignes de migrations).
**Agrégats & machines d'état** :
- `Restaurant` (`.../Domain/Aggregates/Restaurants/Restaurant.cs`, 1 081 lignes) —
  `RestaurantStatus` : Draft → PendingApproval → Approved/Rejected → Suspended ;
  + `ServiceHours`, `SpecialOpeningHour`, `KitchenLoad`, `OrderAcceptanceMode`.
- `Menu` / `MenuCategory` / `MenuItem` (653 lignes) / `OptionGroup` / `ItemAvailability` /
  `MenuServingWindow` — carte à deux niveaux, tarification par options.
- **`FoodOrder`** (`.../Domain/Entities/Orders/FoodOrder.cs`, 614 lignes) — **le ticket de cuisine**.
  `FoodOrderStatus` : PendingRestaurantAcceptance → Accepted → Preparing → ReadyForPickup →
  PickedUp → Delivered ; + Rejected, Cancelled. `KitchenTicketStatus` **dérivé** des
  `KitchenItemStatus` par article (`FoodOrderStatus.cs:107-128`).
- `RestaurantStaff` (508 lignes) + `StaffRole`/`FoodPermission` ; `PreparationStation`.

**Endpoints** (`.../Api/Endpoints/FoodEndpoints.cs`, 1 004 lignes) :
- Public — `app.MapGroup("/api/food")` + `.AllowAnonymous()` explicite sur chaque route (l.18-31) :
  `GET /restaurants`, `GET /restaurants/{id}`, `GET /restaurants/{id}/menu`.
- Partenaire — `MapAuthenticatedGroup("/api/food/partner")` (l.33), **aucun rôle**, l'appartenance
  est vérifiée route par route par `DenyUnlessStaffAsync(user, restaurantId, FoodPermission.X, …)`
  (l.246-268) : ~36 routes (`/restaurants`, `/me`, `/restaurants/{id}/service-hours|logo|
  payout-seller|location|submit|pause|resume`, `/orders`, `/kitchen`, `/orders/{id}/
  accept|reject|preparing|ready`, CRUD menus/catégories/articles/groupes d'options).
  Permissions employées : `MenuManage`, `SettingsManage`, `OrderAccept`, `OrderReject`,
  `KitchenManage`.
- Admin — `MapAdminGroup("/api/food/admin")` (l.220) : `restaurants/pending`, `approve`,
  `reject`, `suspend`, `lift-suspension`.

**gRPC exposé** : `FoodGrpcService` (`Api/Program.cs:88`, `MapInternalGrpcService`), contrat
`shared/contracts/HBA.Food.Contracts.Grpc` — `GetRestaurant`, `GetMenu`, `GetMenuItem`,
`AcceptFoodOrder`, `RejectFoodOrder`, `MarkFoodOrderReady`, `GetRestaurantByOwner`,
`GetStaffMembership`, `GetFoodOrder`.
**Clients gRPC consommés** : Ordering, Inventory, Delivery, Merchants (`Program.cs:44-63`).
**Événements publiés** (`Application/Orders/FoodOrderDomainEventHandlers.cs`) :
`FoodOrderReceived`, `FoodOrderAccepted`, `FoodOrderRejected`, `FoodOrderPreparing`,
`FoodOrderReadyForPickup`, `FoodOrderPickedUp`, `FoodOrderDelivered`, `FoodOrderCancelled`
(+ événements `Restaurant*` et `Staff*`).
**Événements consommés** (`Program.cs:65-82`) : `OrderConfirmedIntegrationEvent`
(**namespace `HBA.Orders.Contracts` — marketplace order-service**), `FoodOrderReadyForPickup`
(le sien), `DeliveryPickedUp`, `DeliveryCompleted`.
**Statut** : **COMPLET**.

---

### food-order-service
**Path** : `services/food/food-order-service/`
**Projects** : `HBA.Food.Order.{Domain,Application,Infrastructure,Contracts,Api}`.
Namespaces `HBA.FoodOrders.*` (≠ nom des dossiers/assemblies `HBA.Food.Order.*`).
**Couches présentes** : les cinq. **Manquantes** : aucune.
**Tests** : **aucun**.
**Volume** : 25 `.cs` (dont ~22 non triviaux ; 597 lignes de migrations/snapshot).
**Agrégats & machines d'état** :
- `MealOrder` (`Domain/Aggregates/Orders/MealOrder.cs`, 595 lignes) — la **commande commerciale**.
  `MealOrderStatus` (`MealOrderIds.cs:38-58`) : Pending → AwaitingPayment → Paid → Confirmed →
  Delivered ; branches Cancelled, Failed, UnderReview.
  Transitions : `MarkAwaitingPayment`, `MarkPaid`, `Confirm`, `MarkDelivered`, `Cancel`,
  `RejectByRestaurant`, `MarkUnderReview`, `ResumeAfterReview`, `CancelAfterReview`, `Fail`.
- `MealOrderLine`, `MealOrderLineOption`.

**Endpoints** (`Api/Endpoints/MealOrderEndpoints.cs`) :
| Route | Verbe | Policy |
|---|---|---|
| `/api/food/orders` | GET | `MapAuthenticatedGroup` (l.15) |
| `/api/food/orders/{id}` | GET | Authenticated + filtre propriétaire, 404 si autre acheteur (l.75) |
| `/api/food/orders` | POST | Authenticated |
| `/api/food/orders/{id}/cancel` | POST | Authenticated + `RequesterId` |
| `/api/food/restaurant/orders` | GET | Authenticated + `GetStaffMembershipAsync` → 403 (l.120-124) |
| `/api/admin/food/orders/{id}/review/resume` | POST | `MapAdminGroup` (l.44) |
| `/api/admin/food/orders/{id}/review/refund` | POST | `MapAdminGroup` |

**gRPC exposé** : `FoodOrderGrpcService` (`Program.cs:45`) — `GetOrder`, `HasPlacedOrder`.
**Clients gRPC** : FoodCarts, Food, Delivery (`Program.cs:19-31`).
**Événements publiés** : `MealOrderPlaced`, `MealOrderConfirmed` (avec ses lignes),
`MealOrderCancelled`, `MealOrderDelivered`, `MealOrderUnderReview`, `MealOrderResumedAfterReview`.
**Événements consommés** (`MealOrderingModuleInstaller.cs:71-92`) : `PaymentCaptured`,
`PaymentFailed` (filtrés sur `OrderType == "FOOD"`), `FoodOrderRejected`, `FoodOrderCancelled`,
`FoodOrderDelivered` (contrats de restaurant-service).
**Statut** : **PARTIEL** — le service est écrit avec soin mais **aucun de ses six événements
n'est consommé hors du domaine food**, et son parcours cuisine n'est branché nulle part
(défauts FOOD-02, FOOD-03, FOOD-04).

---

### food-cart-service
**Path** : `services/food/food-cart-service/`
**Projects** : `HBA.Food.Cart.{Domain,Application,Infrastructure,Contracts,Api}`.
Namespaces `HBA.FoodCarts.*`.
**Couches** : les cinq présentes.
**Tests** : **aucun**.
**Volume** : 26 `.cs` (dont ~23 non triviaux ; 366 lignes de migrations).
**Agrégats & machines d'état** : `FoodCart` (302 lignes) — `FoodCartStatus` : Active →
CheckedOut / Abandoned. `RestaurantId` **posé à la création, sans setter**
(`FoodCart.cs:71`, garde l.143-148). `FoodCartItem`, `FoodCartItemOption`.
**Endpoints** (`Api/Endpoints/FoodCartEndpoints.cs`, groupe `MapAuthenticatedGroup("/api/food/cart")`, l.18) :
`GET /`, `GET /{id}` (contrôle propriétaire l.83, 404 sinon), `POST /items`,
`PUT /lines/{lineId}`, `DELETE /lines/{lineId}`, `DELETE /`, `POST /coupon`, `DELETE /coupon`.
Pas de `/checkout` — délibéré (l.38-47).
**gRPC exposé** : `FoodCartGrpcService` (`Program.cs:31`) — `GetActiveCart`, `GetCart`.
**Clients gRPC** : Food, FoodOrders (`Program.cs:14-18`).
**Événements publiés** : `FoodCartCheckedOutIntegrationEvent` (analytique — **aucun consommateur
dans le dépôt**).
**Événements consommés** : `MealOrderPlacedIntegrationEvent` → clôture du panier
(`FoodCartEventHandlers.cs:31`).
**Validation des options / prix** : conforme à l'attendu. `AddItemToFoodCartCommandHandler`
(`FoodCartCommands.cs:64-123`) lit l'article via `IFoodModuleApi.GetMenuItemAsync`, refuse
`!IsOrderable`, puis `Coter()` (l.151-207) contrôle doublons, appartenance de l'option au plat,
disponibilité de l'option, `MinSelections`/`MaxSelections`. **Le corps HTTP ne porte ni prix ni
devise** (`AddFoodItemRequest`, l.149-154). Point acquis.
**Statut** : **PARTIEL** — fonctionnel mais dépendant d'un bouchon de tarification (FOOD-07)
et non déployé (FOOD-01).

---

### menu-service
**Path** : `services/food/menu-service/`
**Projects** : 5 `.csproj`. `Contracts` = `<Project Sdk="…"></Project>` **vide, zéro fichier**.
`Domain` ne contient qu'un `record` de 3 lignes.
**Couches présentes** : des dossiers portant les noms des couches. **Manquantes en substance** :
Domain (1 record), Application (un `ConcurrentDictionary`), Infrastructure (une ligne de DI),
Contracts (vide).
**Tests** : aucun.
**Volume** : **5 fichiers `.cs`, 61 lignes** — dont **0 non trivial**.
**Agrégats & machines d'état** : `FoodMenu(Guid Id, Guid RestaurantId, string Name, bool
Published, DateTimeOffset UpdatedAt)` — un record, aucune méthode, aucun état.
**Endpoints** : `POST /api/v1/menus`, `GET /api/v1/menus/restaurants/{restaurantId}`
(`Api/Endpoints/MenuEndpoints.cs:9-12`) — **aucune policy, aucun `RequireAuthorization`,
aucun jeton lu**. `Program.cs` n'appelle ni `AddHbaService` ni `UseHbaService`.
**gRPC exposé** : aucun. `proto/menu.proto` existe mais **c'est une copie octet pour octet de
`restaurant-service/proto/food.proto`** (md5 `384bf33c…`) : il déclare `service FoodApi`,
`package hba.food.v1`, rien qui concerne un menu. Aucun `<Protobuf Include>` dans les `.csproj`.
**Événements** : aucun, ni publié ni consommé.
**Statut** : **SQUELETTE**.

---

### availability-service
**Path** : `services/food/availability-service/`
**Projects** : 5, dont `Contracts` vide.
**Tests** : aucun.
**Volume** : **7 fichiers `.cs`, 71 lignes** — 1 seul porte de la logique
(`AvailabilityPolicy.IsOpen`, 2 lignes).
**Agrégats** : `AvailabilitySlot` et `AvailabilityOverride`, deux records sans comportement.
**Endpoints** : `POST /api/v1/availability`, `GET /api/v1/availability/restaurants/{restaurantId}`
(`Api/Endpoints/AvailabilityEndpoints.cs:10-11`) — **aucune autorisation**. Écriture anonyme :
n'importe qui peut déclarer un restaurant fermé.
**gRPC exposé** : aucun ; `proto/availability.proto` = copie de `food.proto` (md5 `384bf33c…`).
**Événements** : aucun.
**Statut** : **SQUELETTE**.

---

### kitchen-prep-service
**Path** : `services/food/kitchen-prep-service/`
**Projects** : 5, dont `Contracts` vide.
**Tests** : aucun.
**Volume** : **7 fichiers `.cs`, 82 lignes**.
**Agrégats & machines d'état** : `KitchenTicket(… string Status …)` — record avec un **statut en
chaîne libre**. `KitchenPolicy.CanMarkReady(string status)` (2 lignes) — **écrite et jamais
appelée** : `KitchenStore.MarkReady` (`Application/Abstractions/KitchenStore.cs:17-27`) passe
n'importe quel ticket à `READY` sans consulter la politique. Les entités `PrepStation` et
`PrepTask` ne sont référencées nulle part.
**Endpoints** : `POST /api/v1/kitchen/tickets`, `POST /api/v1/kitchen/tickets/{ticketId}/ready`
(`Api/Endpoints/KitchenEndpoints.cs:10-12`) — **aucune autorisation**.
**gRPC exposé** : aucun ; `proto/kitchen.proto` = **copie octet pour octet de
`food-order-service/proto/foodorder.proto`** (md5 `3e7e8a19…`) : il déclare
`package hba.foodorder.v1` / `service FoodOrderApi` / `GetOrder` / `HasPlacedOrder`.
Aucun contrat de cuisine n'existe.
**Événements** : aucun.
**Branché à quoi que ce soit ?** Non. Aucun autre fichier du dépôt ne référence
`HBA.Food.Kitchen.*`. Le vrai ticket de cuisine vit dans restaurant-service.
**Statut** : **SQUELETTE**.

---

### review-service
**Path** : `services/food/review-service/`
**Projects** : 5, dont `Contracts` vide.
**Tests** : aucun.
**Volume** : **11 fichiers `.cs`, 98 lignes** — 1 non trivial (`ReviewStore`, 27 lignes).
**Agrégats** : `FoodReview` (record). **Code mort intégral** : `IReviewRepository`
(`Domain/Repositories/IReviewRepository.cs:5`) n'a **aucune implémentation** et n'est enregistré
dans aucun conteneur ; `Rating` (`ValueObjects/Rating.cs`) n'est utilisé nulle part — `ReviewStore`
prend un `int` brut ; `ReviewStatus`, `ReviewReply` et `FoodReviewCreatedDomainEvent` ne sont
référencés par aucun fichier.
**Endpoints** : `POST /api/v1/food/reviews`, `GET /api/v1/food/reviews/restaurants/{restaurantId}`
(`Api/Endpoints/ReviewEndpoints.cs:10-11`) — **aucune autorisation, aucun contrôle
« l'auteur a-t-il commandé »** alors que le README promet des « verified Food reviews ».
`ReviewStore.Create` lève une `ArgumentOutOfRangeException` non interceptée sur une note hors
bornes → 500 au lieu d'un 400 enveloppé.
**gRPC exposé** : aucun ; `proto/review.proto` = copie de `food.proto` (md5 `384bf33c…`).
**Événements** : aucun.
**Statut** : **SQUELETTE**.

---

## 2. Réponses aux points instruits

### 2.1 `FoodOrder` dans restaurant-service — encore présent, et en double emploi

**Oui, il y est encore** : `services/food/restaurant-service/src/HBA.Food.Restaurant.Domain/
Entities/Orders/FoodOrder.cs` (614 lignes) + `FoodOrderItem.cs` (208) + `FoodOrderStatus.cs` (128)
+ `Events/FoodOrderDomainEvents.cs` (108), persistés dans `food_orders` /
`food_order_items` (`Persistence/Configurations/OrderConfigurations.cs:53,119`).

Le fichier lui-même revendique la séparation (« ce n'est pas le statut commercial »,
`FoodOrderStatus.cs:7-19`), et `MealOrder.cs:13-22` la reprend. **La séparation
`MealOrder` (commercial) / `FoodOrder` (ticket) est légitime.** Le problème n'est pas là.

**Triple duplication réelle** :

| Règle | restaurant-service | food-order-service | marketplace order-service |
|---|---|---|---|
| Restaurant ouvert / accepte | `FoodOrderCommands.cs:158-165` (`CanAcceptOrders`) | `PlaceMealOrderCommand.cs:146-151` (`AcceptsOrdersNow`) | — |
| Minimum de commande | `FoodOrderCommands.cs:215-220` (`food.order.below_minimum`) | `PlaceMealOrderCommand.cs:158-163` (`food_ordering.below_minimum`) | — |
| Tarification plat + options | `FoodOrderCommands.cs:290-296` (`MenuItem.PriceSelection`) | (reprend le prix du panier) | — |
| Idempotence de création | `FoodOrderCommands.cs:134-138` (par `OrderId`) | `PlaceMealOrderCommand.cs:122-126` (par `CartId`) | — |
| Devis de course Express | `FoodOrderBridgeHandlers.cs:284-317` | `PlaceMealOrderCommand.cs:290-385` | `PlaceOrderCommandHandler.cs:471` |
| Cycle de vie commande repas | — | `MealOrder` | `Order` avec `Kind == Food` (`Order.cs:177,220,479`) |

**Le doublon coûteux n'est pas `FoodOrder` : c'est `MealOrder` vs `Order(Kind=Food)`.**
Le parcours restauration existe **deux fois en entier** — une fois dans
`services/marketplace/order-service` (`Order.cs:177` `RestaurantId` dérivé de `_lines[0]`,
`PlaceOrderCommandHandler.cs:195,353,471`, `CreateDeliveryOnOrderConfirmedHandler.cs:89`,
`OrderEndpoints.cs:193`) et une fois dans food-order-service. **restaurant-service n'est branché
qu'à la première** (voir FOOD-02).

Duplication de tarification à effet réel : le panier fige `UnitBaseAmount` à l'ajout
(`FoodCartItem`), `MealOrder.Create` recopie `FinalUnitPrice` sans relire la carte
(`PlaceMealOrderCommand.cs:165-183`), tandis que `ReceiveFoodOrderCommand` **relit et recalcule**
le prix depuis `MenuItem.PriceSelection` (`FoodOrderCommands.cs:290`). Le montant payé et le
montant du ticket peuvent donc diverger si le restaurateur change un prix entre-temps —
et le second refuse la commande sur son propre minimum (`FoodOrderCommands.cs:215`) **après**
l'encaissement.

### 2.2 food-order-service — les invariants demandés

| Attendu | Verdict | Preuve |
|---|---|---|
| Agrégat `MealOrder` | oui | `Domain/Aggregates/Orders/MealOrder.cs` |
| Idempotence par panier | **oui** | `PlaceMealOrderCommand.cs:122-126` + index unique `MealOrderConfiguration.cs:82` (`HasIndex(o => o.CartId).IsUnique()`) |
| Restaurant ouvert | **oui** | `PlaceMealOrderCommand.cs:140-151` |
| Montant minimum | **oui**, avant paiement | `PlaceMealOrderCommand.cs:158-163` |
| Devis de livraison obligatoire | **oui**, 6 refus | `PlaceMealOrderCommand.cs:293-375` : absent, introuvable, consommé, expiré, devis partenaire, adresse ≠ devis (tolérance 0,0005°), type ≠ « Express ». Montant pris **du devis** (l.384), pas du client. |
| Chaînage vers le paiement | **non branché** | `MealOrderPlacedIntegrationEvent` n'est consommé que par food-cart-service (`FoodCartModuleInstaller.cs:53-54`). Aucun service financier ne l'écoute → défaut FOOD-03 |
| Chaînage vers la cuisine | **non branché** | `MealOrderConfirmedIntegrationEvent` : **zéro consommateur** → défaut FOOD-02 |

### 2.3 food-cart-service — options et prix

Conforme. Options validées en quatre points (`FoodCartCommands.cs:151-207`), prix lu dans la
carte via gRPC (`FoodCartCommands.cs:66`), aucun montant dans le corps HTTP
(`FoodCartEndpoints.cs:149-154`), un seul restaurant par panier garanti par une colonne sans
setter (`FoodCart.cs:71`, garde l.143-148). Réserve : la valorisation passe par
`IPricingModuleApi` qui est un bouchon (FOOD-07).

### 2.4 kitchen-prep / availability / menu — branchés à quoi ?

**À rien.** Vérifié par recherche sur tout le dépôt :
- aucun fichier hors de leur propre dossier ne référence `HBA.Food.Kitchen.*`,
  `HBA.Food.Availability.*` ou `HBA.Food.Menu.*` ;
- aucun `<Protobuf Include>` dans leurs `.csproj` : leurs `.proto` ne sont pas compilés ;
- aucun `IIntegrationEventHandler`, aucun `IIntegrationEventPublisher` : ni producteur ni
  consommateur ;
- absents de `infra/docker/compose.services.yml`, de `k8s/base/services/`, de la table de routage
  du gateway (`apps/api-gateway/src/HBA.Gateway.Api/appsettings.json`) ;
- leurs routes (`/api/v1/menus`, `/api/v1/availability`, `/api/v1/kitchen`) ne correspondent à
  aucune route du gateway et ne sont donc joignables par personne.

Les responsabilités qu'ils annoncent sont **toutes tenues par restaurant-service** :
la carte (`Menu`, `MenuCategory`, `MenuItem`, `OptionGroup`), la disponibilité
(`ItemAvailability`, `MenuServingWindow`, `ServiceHours`, `SpecialOpeningHour`), la cuisine
(`FoodOrder` + `KitchenTicketStatus` + `PreparationStation`).

### 2.5 Le parcours cuisine → livraison

**Le maillon READY → livraison existe** : `CreateDeliveryOnFoodOrderReadyHandler`
(`Api/Integration/FoodOrderBridgeHandlers.cs:215-364`, enregistré `Program.cs:69-71`) consomme
`FoodOrderReadyForPickupIntegrationEvent` et appelle `IDeliveryDispatchApi.CreateAsync` avec
`Type: "Express"`, référence `FOOD-{foodOrderId}`, et le devis déjà payé (`QuoteId:
commande.DeliveryQuoteId`, l.317). Repli sans devis si refus (l.335-355). **Ce maillon est correct.**

**Le retour de course existe aussi** : `FoodDeliveryReturnHandlers.cs` — `DeliveryPickedUp` →
`MarkFoodOrderPickedUpCommand`, `DeliveryCompleted` → `MarkFoodOrderDeliveredCommand` avec
rattrapage de l'enlèvement perdu (l.149-167).

**Mais la commande commerciale ne change d'état que sur le chemin marketplace.**
`FoodOrder.MarkDelivered()` lève `FoodOrderDeliveredDomainEvent` → publie
`FoodOrderDeliveredIntegrationEvent`, que food-order-service consomme
(`MealOrderingModuleInstaller.cs:90-92`) pour appeler `MarkMealOrderDeliveredCommand`. **Ce
câblage est correct en soi** — mais il porte `integrationEvent.OrderId`, qui est le `OrderId`
inscrit sur le ticket par `ReceiveFoodOrderCommand`, c'est-à-dire **l'identifiant d'une commande
marketplace** (`FoodOrderBridgeHandlers.cs:159`, alimenté par `OrderConfirmedIntegrationEvent`).
Aucun `MealOrder` ne porte cet identifiant : la commande n'est jamais trouvée, la transition
échoue en `food_ordering.not_found`, et `SagaOutcome.Exiger` la journalise sans la corriger.

**Réponse aux deux questions posées :**
- *Quand un ticket passe READY, quelque chose crée-t-il la livraison ?* **Oui.**
- *Quand la cuisine termine, la commande food change-t-elle d'état ?* **Non pour un `MealOrder`.**
  Pour un `Order(Kind=Food)` marketplace, oui — mais celui-ci n'est plus la commande servie par
  le gateway sur `/api/food/orders`.

---

## 3. Défauts

### restaurant-service ↔ food-order-service ↔ food-cart-service (intégration)

**FOOD-01 — CRITICAL — Le gateway ne peut pas démarrer : `Services:FoodCart` et
`Services:FoodOrder` sont obligatoires et absents.**
`ServicesOptions.FoodCart` et `.FoodOrder` sont déclarés `[Required, Url]`
(`apps/api-gateway/src/HBA.Gateway.Infrastructure/Configuration/ServicesOptions.cs:66-67`), et les
options sont liées avec `.ValidateDataAnnotations().ValidateOnStart()`
(`apps/api-gateway/src/HBA.Gateway.Infrastructure/DependencyInjection.cs:21-33`). Or la section
`Services` de `apps/api-gateway/src/HBA.Gateway.Api/appsettings.json` ne contient **ni `FoodCart`
ni `FoodOrder`** (14 clés, de `Identity` à `Promotion`), et aucun `Services__FoodCart` /
`Services__FoodOrder` n'existe dans `infra/docker/compose.services.yml` ni sous `k8s/`.
`ValidateOnStart` fait donc échouer la construction de l'hôte : **le gateway sort au démarrage,
toute la plateforme est indisponible**. Le commentaire de `ServicesOptions.cs:56-65` décrit
exactement ce risque, pour ne pas l'avoir refermé.

**FOOD-02 — CRITICAL — Une commande de repas payée n'ouvre aucun ticket de cuisine.**
Le seul gestionnaire qui crée un ticket est `ReceiveFoodOrderOnOrderConfirmedHandler`, enregistré
sur `OrderConfirmedIntegrationEvent` du namespace `HBA.Orders.Contracts.IntegrationEvents`
(`services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Program.cs:12,65-67` et
`Api/Integration/FoodOrderBridgeHandlers.cs:79-80`) — l'événement de **marketplace
order-service**. Il relit ensuite la commande par `IOrderingModuleApi.GetOrderAsync`
(`FoodOrderBridgeHandlers.cs:121`), c'est-à-dire order-service.
`MealOrderConfirmedIntegrationEvent`, que food-order-service publie avec ses lignes
(`MealOrderDomainEventHandlers.cs:64-96`), **n'a aucun consommateur dans tout le dépôt** —
la chaîne `MealOrder` n'apparaît dans aucun fichier hors de `services/food/`.
Conséquence : un client qui commande via `POST /api/food/orders` (route servie par
food-order-service, `apps/.../appsettings.json`, route `food-orders` → cluster `FoodOrder`)
est débité et **aucune cuisine n'est servie**.

**FOOD-03 — CRITICAL — Ni escrow libéré, ni remboursement, pour une commande de repas.**
payment-service consomme `OrderDeliveredIntegrationEvent` et `OrderCancelledIntegrationEvent`,
namespace `HBA.Orders.Contracts.IntegrationEvents`
(`services/common/payment-service/src/HBA.Financial.Payments.Infrastructure/
PaymentsModuleInstaller.cs:237,248` ; `.../EventHandlers/ReleaseEscrowOnOrderDeliveredHandler.cs:2,13`).
`MealOrderDeliveredIntegrationEvent` et `MealOrderCancelledIntegrationEvent` ne sont consommés
nulle part. Donc : repas remis au client → **le restaurateur n'est jamais réglé** ; cuisine qui
refuse après paiement → `RejectByRestaurant` publie `MealOrderCancelled` → **aucun remboursement**.
Les commentaires de `MealOrder.cs:429-431` et `KitchenOutcomeHandlers.cs:25-28` annoncent que
« financial-service rembourse en la consommant » — il ne la consomme pas.

**FOOD-04 — HIGH — Le paiement n'est jamais déclenché : `MealOrder` reste `AwaitingPayment`.**
`MealOrderPlacedDomainEventHandler` publie `MealOrderPlacedIntegrationEvent` en annonçant
« le panier l'écoute pour se clore, **le paiement pour démarrer** »
(`MealOrderDomainEventHandlers.cs:8-11`). Le seul consommateur enregistré est
`CloseFoodCartOnMealOrderPlacedHandler` (`FoodCartModuleInstaller.cs:53-54`). Aucun service
financier n'écoute cet événement. Symétriquement, `ConfirmMealOrderOnPaymentCapturedHandler`
attend un `PaymentCapturedIntegrationEvent` avec `OrderType == "FOOD"`
(`PaymentOutcomeHandlers.cs:42,57`) qu'aucun paiement n'émettra pour un `MealOrder`.
**Commande bloquée définitivement.**

**FOOD-05 — HIGH — Aucun consommateur Kafka du domaine food n'est idempotent.**
`IConsumerInbox` / `EfConsumerInbox` existent (`shared/common/HBA.Shared.Infrastructure/Inbox/`)
et sont employés par catalog-service, seller-service et notification-service (migrations
`AddConsumerInboxIdempotencyKeys`). Recherche sur `services/food/` : **zéro occurrence
d'`Inbox`**. Les onze consommateurs du domaine (`MealOrderingModuleInstaller.cs:71-92`,
`FoodCartModuleInstaller.cs:53`, `restaurant-service/Program.cs:65-82`) reposent uniquement sur
les gardes d'état des agrégats. Cela couvre les rejeux simples, pas le rejeu concurrent : deux
livraisons du même `PaymentCaptured` peuvent exécuter `MarkPaid()`+`Confirm()` en parallèle sur
deux instances et publier **deux** `MealOrderConfirmed` (donc deux tickets de cuisine, chacun
idempotent sur un `OrderId` différent). Violation directe de la règle du cadre
(« les consumers Kafka doivent être idempotents — Inbox ou équivalent »).

**FOOD-06 — MEDIUM — Le parcours restauration existe en double dans le dépôt.**
`services/marketplace/order-service` traite toujours `OrderLineKind.Food` de bout en bout
(`Order.cs:177,220,479` ; `PlaceOrderCommandHandler.cs:195,353,471` ; `OrderEndpoints.cs:193` ;
`CreateDeliveryOnOrderConfirmedHandler.cs:89` ; `OrderDeliveryArbitrationHandlers.cs:96`
`HoldOrderOnDeliveryCancelledHandler`), en parallèle de food-order-service. Deux agrégats, deux
jeux d'événements, deux tables, un seul consommateur côté cuisine. Détail aggravant :
`FoodDeliveryReturnHandlers.cs:60-62` renvoie explicitement à
`HoldOrderOnDeliveryCancelledHandler` **d'order-service** pour l'arbitrage — donc le nouveau
service délègue son cas d'arbitrage à l'ancien.

---

### food-order-service

**FOOD-07 — HIGH — L'état `UnderReview` est inatteignable : les deux routes d'arbitrage sont mortes.**
`PutMealOrderUnderReviewCommand` est déclaré et géré
(`Application/Commands/Orders/MealOrderLifecycleCommands.cs:30,168,177`) mais **n'est envoyé par
aucun appelant** (recherche `PutMealOrderUnderReviewCommand` sur tout le dépôt : 3 occurrences,
toutes dans ce fichier). Aucun consommateur de `DeliveryCancelled` n'existe dans le service.
Conséquence : `MealOrderStatus.UnderReview` n'est jamais atteint, donc
`POST /api/admin/food/orders/{id}/review/resume` et `.../review/refund`
(`Api/Endpoints/MealOrderEndpoints.cs:45-46`) échouent systématiquement sur
`food_ordering.not_under_review` (`MealOrder.cs:533-537,565-569`). Une course de repas annulée
laisse la commande `Confirmed` indéfiniment.

**FOOD-08 — MEDIUM — `MealOrder.Fail()` et `MealOrderStatus.Failed` sont du code mort non gardé.**
`MealOrder.cs:579-583` : `Fail` est `public void`, **sans aucune garde d'état** — elle écraserait
un `Delivered` par un `Failed`. Recherche `.Fail(` sur `services/food/` : **aucun appelant**.
`MealOrderStatus.Failed` (`MealOrderIds.cs:45`) est donc inatteignable, mais reste testé dans
`Cancel` (l.403) et `RejectByRestaurant` (l.449).

**FOOD-09 — MEDIUM — Résultat de transition ignoré au passage en commande.**
`PlaceMealOrderCommand.cs:252` : `commande.MarkAwaitingPayment();` — le `Result` renvoyé
(`MealOrder.cs:297-309`) est jeté. Aujourd'hui inoffensif (l'agrégat vient d'être créé en
`Pending`), mais c'est exactement le motif que `PaymentOutcomeHandlers.cs:14-23` dénonce chez ses
prédécesseurs. Un échec silencieux ici produirait une commande enregistrée sans
`MealOrderPlacedIntegrationEvent`, donc un panier jamais clos.

**FOOD-10 — MEDIUM — `SetShippingFee` ramène silencieusement un montant négatif à zéro.**
`MealOrder.cs:283` : `ShippingFee = fee < 0m ? 0m : fee;`. Un devis à montant négatif — donnée
corrompue, erreur de grille — serait absorbé sans trace au lieu d'échouer avant l'encaissement,
alors que tout le reste de `ResoudreFraisAsync` refuse plutôt que de replier sur zéro
(`PlaceMealOrderCommand.cs:265-272`).

**FOOD-11 — LOW — Écart dossier / assembly / namespace.**
Dossiers et `.csproj` `HBA.Food.Order.*`, namespaces `HBA.FoodOrders.*` (tous les fichiers).
Même écart dans food-cart-service (`HBA.Food.Cart.*` / `HBA.FoodCarts.*`) et restaurant-service
(`HBA.Food.Restaurant.*` / `HBA.Food.*`). Le cadre demande que le vocabulaire du code fasse foi ;
ici trois vocabulaires coexistent pour le même service.

**FOOD-12 — LOW — Aucun test.** 3 541 lignes, 8 transitions d'état, une saga à trois branches,
un contrôle de devis à six refus — zéro test. `tests/` ne contient aucun projet référençant
`HBA.Food.Order.Api`.

---

### food-cart-service

**FOOD-13 — HIGH — `IPricingModuleApi` est un bouchon enregistré en production : tout code promo
est refusé et toute remise est nulle.**
`Infrastructure/Public/NeutralPricingModuleApi.cs:15-29` : `CalculatePriceAsync` renvoie
`SellerDiscount: 0m, PlatformDiscount: 0m, FinalAmount: request.BaseAmount` ;
`ValidateCouponAsync` renvoie toujours `CouponValidation.Invalid("pricing.unavailable", …)`.
Il est enregistré **sans condition d'environnement** :
`FoodCartModuleInstaller.cs:47` — `services.AddScoped<IPricingModuleApi, NeutralPricingModuleApi>();`.
Conséquences : `POST /api/food/cart/coupon` échoue toujours (`FoodCartCommands.cs:332-338`) ;
`isFirstOrder` calculé par un appel gRPC à food-order-service (`FoodCartQueries.cs:63`) est
transmis à un calculateur qui l'ignore (`FoodCartPricer.cs:51`) ; le `PromotionCode` figé sur la
commande (`MealOrder.cs:90`) ne correspond à aucune remise appliquée. C'est un stub dans `src/`,
pas dans un projet de test.

**FOOD-14 — MEDIUM — Deux paniers actifs simultanés sont possibles.**
`FoodCartConfiguration.cs:45` : `builder.HasIndex(c => new { c.BuyerId, c.Status });` — **non
unique**, et pas de filtre partiel sur `Status = Active`. `GetActiveByBuyerAsync` fait un
`FirstOrDefault` ; deux `POST /items` concurrents sur un acheteur sans panier créent deux
agrégats `Active`. Le second devient invisible et ne sera jamais commandé ni purgé.

**FOOD-15 — MEDIUM — `FoodCartCheckedOutIntegrationEvent` est publié sans consommateur.**
`FoodCartEventHandlers.cs:62-79` publie l'événement (« analytique »). Recherche sur le dépôt :
aucun `IIntegrationEventHandler<FoodCartCheckedOutIntegrationEvent>`. Un sujet Kafka alimenté
et jamais lu.

**FOOD-16 — LOW — Le panier n'est jamais rouvert si la commande échoue après coup.**
`CloseFoodCartOnMealOrderPlacedHandler` clôt le panier sur `MealOrderPlaced`
(`FoodCartEventHandlers.cs:46-58`), c'est-à-dire **avant** le paiement. Aucun gestionnaire de
`MealOrderCancelled` ne le rouvre. Le client dont le paiement échoue doit tout ressaisir — ce que
le commentaire l.14-21 dit précisément vouloir éviter.

**FOOD-17 — LOW — Aucun test** (2 233 lignes).

---

### restaurant-service

**FOOD-18 — MEDIUM — La règle 404/403 du cadre est inversée, et les refus ne sont pas enveloppés.**
`Api/Endpoints/FoodEndpoints.cs:246-268` (`DenyUnlessStaffAsync`) : quand le `restaurantId` **de
l'URL** ne correspond pas à l'établissement du porteur du jeton, la réponse est
`Results.NotFound()` (l.262) ; quand la permission manque, `Results.Forbid()` (l.267). Le cadre
impose « identifiant de vendeur venant de l'URL → **403 enveloppé** ». Ni `NotFound()` ni
`Forbid()` ne produisent l'enveloppe §25 à cinq `error.code`. Même problème à
`FoodEndpoints.cs:437` : `Results.BadRequest(new { error = "food.order.invalid_rejection_reason" })`
— forme ad hoc, hors enveloppe.

**FOOD-19 — MEDIUM — Migrations avec `.Designer.cs`, contre la convention maison.**
Le cadre indique « migrations écrites à la main : `[DbContext]` + `[Migration]`, **pas de
`.Designer.cs`**, snapshot édité manuellement ». restaurant-service en contient cinq :
`Persistence/Migrations/20260812081143_InitialFood.Designer.cs` (391 l.),
`20260812110630_….Designer.cs` (822), `20260812121955_ImagesVersMedia.Designer.cs` (831),
`20260818183403_SyncModel….Designer.cs` (846). food-cart et food-order respectent la convention.
`20260823000000_AjoutTraceParentOutbox.cs` porte par ailleurs une date postérieure à la date
d'audit du dépôt.

**FOOD-20 — MEDIUM — Le minimum de commande est encore contrôlé après encaissement.**
`Application/Orders/FoodOrderCommands.cs:215-220` refuse le ticket sous le minimum, alors que la
commande est déjà payée à ce stade. Le commentaire l.200-206 l'assume au nom du recalcul du prix
— mais l'effet reste : client débité, `ReceiveFoodOrderCommand` échoue,
`ReceiveFoodOrderOnOrderConfirmedHandler` lève (`FoodOrderBridgeHandlers.cs:172-175`), le message
est rejoué trois fois puis abandonné. **Aucune compensation n'est déclenchée** : ni annulation,
ni remboursement, ni mise en arbitrage. Même chemin pour `food.restaurant.not_accepting` (l.161)
et `food.restaurant.saturated` (l.181).

**FOOD-21 — MEDIUM — `GetOwnerMenuAsync` exige `MenuManage` pour une lecture.**
`FoodEndpoints.cs:74` + `:336` : `GET /api/food/partner/restaurants/{id}/menu` demande
`FoodPermission.MenuManage`. Un cuisinier ou un caissier sans droit d'écriture sur la carte ne
peut pas la consulter, alors que toutes les autres lectures partenaire passent par une permission
de lecture ou par la seule appartenance.

**FOOD-22 — LOW — Couverture de test très partielle.**
`tests/HBA.Food.AuthorizationTests/` : 206 lignes, 6 cas, uniquement 401/403 sur des routes
publiques et partenaires. Aucun test des 8 transitions de `FoodOrder`, de la dérivation
`KitchenTicketStatus`, de `MenuItem.PriceSelection`, du calcul de charge `AssessLoad`, ni des
quatre ponts d'intégration.

---

### menu / availability / kitchen-prep / review (les quatre squelettes)

**FOOD-23 — HIGH — Quatre `.proto` sur sept sont des copies d'un autre contrat.**
`md5` identiques :
- `menu-service/proto/menu.proto`, `availability-service/proto/availability.proto`,
  `review-service/proto/review.proto` = `restaurant-service/proto/food.proto`
  (`384bf33c66cd47ae91fab61abf2a7d7c`, 166 lignes, `package hba.food.v1`, `service FoodApi`).
- `kitchen-prep-service/proto/kitchen.proto` = `food-order-service/proto/foodorder.proto`
  (`3e7e8a19ce8cde9e4fab217f18dd1ee1`, 72 lignes, `package hba.foodorder.v1`,
  `service FoodOrderApi`).
Aucun de ces quatre fichiers ne décrit le service qui l'héberge. Aucun n'est référencé par un
`<Protobuf Include>` : **quatre contrats gRPC déclarés, zéro implémenté** — un client qui les
compilerait obtiendrait un stub `FoodApi` pointant sur un service qui n'expose rien
(`UNIMPLEMENTED` à l'appel).

**FOOD-24 — HIGH — Aucune autorisation sur les huit routes des squelettes.**
- `menu-service/.../MenuEndpoints.cs:10-11` : `POST /api/v1/menus`,
  `GET /api/v1/menus/restaurants/{restaurantId}`
- `availability-service/.../AvailabilityEndpoints.cs:10-11` : `POST /api/v1/availability`,
  `GET /api/v1/availability/restaurants/{restaurantId}`
- `kitchen-prep-service/.../KitchenEndpoints.cs:10-12` : `POST /api/v1/kitchen/tickets`,
  `POST /api/v1/kitchen/tickets/{ticketId}/ready`
- `review-service/.../ReviewEndpoints.cs:10-11` : `POST /api/v1/food/reviews`,
  `GET /api/v1/food/reviews/restaurants/{restaurantId}`
Aucun `MapAuthenticatedGroup`, aucun `RequireAuthorization`, aucun `ClaimsPrincipal`, aucun
`AddHbaService`/`UseHbaService` dans les quatre `Program.cs` (13 lignes chacun). Écriture
anonyme sur toutes les routes `POST`, et le `restaurantId` n'est jamais confronté à un porteur.
Non exploitable aujourd'hui (services non déployés, non routés) — **le jour du déploiement, si.**

**FOOD-25 — HIGH — Persistance en mémoire, non partagée, perdue au redémarrage.**
`MenuStore` (`menu-service/.../MenuStore.cs:8`), `AvailabilityStore` (`:8`), `KitchenStore` (`:8`),
`ReviewStore` (`:9`) sont des `ConcurrentDictionary<Guid, …>` enregistrés en `AddSingleton`
(`MenuInfrastructureModule.cs:9`, `AvailabilityInfrastructureModule.cs:9`,
`KitchenInfrastructureModule.cs:9`, `ReviewInfrastructureModule.cs:9`). Aucun `DbContext`, aucune
migration, aucun schéma PostgreSQL — le cadre exige « une base logique par service ».

**FOOD-26 — MEDIUM — `KitchenPolicy.CanMarkReady` est écrite et jamais appelée.**
`kitchen-prep-service/.../Domain/Policies/KitchenPolicy.cs:5-6` définit la seule règle métier du
service ; `KitchenStore.MarkReady` (`Application/Abstractions/KitchenStore.cs:17-27`) ne la
consulte pas et passe **n'importe quel** ticket à `READY`, y compris un ticket déjà `READY`.
Statut en `string` libre (`KitchenTicketAggregate.cs:3`), pas d'énumération.

**FOOD-27 — MEDIUM — review-service : quatre types de domaine sur six sont morts.**
`Domain/Repositories/IReviewRepository.cs:5` — interface **sans implémentation** et enregistrée
dans aucun conteneur ; `Domain/ValueObjects/Rating.cs:3` — value object jamais construit
(`ReviewStore.Create` prend un `int` et appelle `ReviewPolicy.IsValidRating`, dupliquant la même
règle deux fois) ; `Domain/Enums/ReviewStatus.cs:3` et `Domain/Entities/ReviewReply.cs:3` —
jamais référencés ; `Domain/Events/ReviewEvents.cs:3` (`FoodReviewCreatedDomainEvent`) — jamais
levé ni publié.

**FOOD-28 — MEDIUM — review-service : la note hors bornes produit un 500.**
`Application/Abstractions/ReviewStore.cs:13-16` lève une `ArgumentOutOfRangeException` non
interceptée ; aucun middleware d'erreur n'est branché (`Program.cs` n'appelle pas
`UseHbaService`). Le client reçoit une 500 nue au lieu du 400 enveloppé §25. Idem
`Rating.Create` (`ValueObjects/Rating.cs:7`).

**FOOD-29 — MEDIUM — review-service ne vérifie pas que l'auteur a commandé.**
`CreateReviewRequest` (`ReviewStore.cs:27`) porte `OrderId` et `CustomerId`, mais
`ReviewStore.Create` ne les confronte à rien : ni appel à food-order-service, ni contrôle
d'unicité par commande. Le README annonce pourtant « verified Food reviews »
(`review-service/README.md:3`).

**FOOD-30 — LOW — Les quatre squelettes n'implémentent aucune convention maison.**
Pas de `Result<T>`/`Error`, pas d'`ICommand`/`IQuery`, pas de handlers `internal sealed`, pas de
`IModuleInstaller`, pas de `ModuleDbContext`, pas d'outbox, pas d'audit. Les projets `Contracts`
sont des `.csproj` de 2 lignes sans aucun fichier. Trois projets `Domain` portent un commentaire
« Ce projet est volontairement vide » (`HBA.Food.Menu.Domain.csproj:3-4`,
`HBA.Food.Availability.Domain.csproj:3-4`, `HBA.Food.Kitchen.Domain.csproj:3-4`) — mais leurs
README les décrivent au présent comme propriétaires de leur domaine, sans mentionner qu'ils ne
sont pas implémentés.

---

## 4. Recherches ciblées demandées

| Recherche | Résultat |
|---|---|
| `TODO` / `FIXME` / `HACK` / `XXX:` | **0 occurrence** dans `services/food/**/*.cs` |
| `NotImplementedException` | **0 occurrence** |
| Stubs dans `src/` | **2** : `NeutralPricingModuleApi` (FOOD-13) et les quatre `*Store` en mémoire (FOOD-25) |
| RPC déclarés non implémentés | **4 fichiers `.proto`** non compilés et hors sujet (FOOD-23) ; côté restaurant-service, le commentaire de `food.proto:16-24` signale trois RPC ajoutés après coup (`GetRestaurantByOwner`, `GetStaffMembership`, `GetFoodOrder`) — ceux-là sont bien dans le contrat partagé |
| DI enregistrée jamais résolue | `IReviewRepository` déclaré, ni implémenté ni enregistré (FOOD-27) ; `KitchenPolicy` jamais appelée (FOOD-26) ; `PrepStation`/`PrepTask`/`AvailabilityOverride`/`ReviewStatus`/`ReviewReply` jamais référencés |
| Routes sans autorisation | **8** (FOOD-24), plus les 3 routes publiques de restaurant-service — celles-ci `AllowAnonymous()` explicitement, ce qui est correct |
| `double` pour de l'argent | **0**. Toutes les sommes sont en `decimal` + `numeric(18,2)` (`MealOrderConfiguration.cs:33-37,113-116`, `FoodCartConfiguration.cs:64`, `OrderConfigurations.cs:126,150`). Les `double` présents sont des coordonnées géographiques (`MealOrder.cs:190-192`, `ShippingAddressInput`), usage légitime ; la tolérance `0,0005°` de `PlaceMealOrderCommand.cs:60` est documentée |

---

## 5. Les dix défauts les plus graves

| # | Sévérité | Service | Défaut | Fichier |
|---|---|---|---|---|
| 1 | CRITICAL | gateway ← food-cart / food-order | `Services:FoodCart` et `Services:FoodOrder` sont `[Required]` + `ValidateOnStart` et absents de toute configuration → le gateway ne démarre pas | `apps/api-gateway/src/HBA.Gateway.Infrastructure/Configuration/ServicesOptions.cs:66-67` vs `apps/api-gateway/src/HBA.Gateway.Api/appsettings.json` (section `Services`) |
| 2 | CRITICAL | restaurant ← food-order | `MealOrderConfirmedIntegrationEvent` n'a aucun consommateur ; le seul ouvreur de ticket écoute `OrderConfirmedIntegrationEvent` de marketplace → client débité, aucune cuisine servie | `services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Program.cs:65-67` |
| 3 | CRITICAL | food-order ← payment | `MealOrderDelivered` / `MealOrderCancelled` non consommés → escrow jamais libéré, remboursement jamais déclenché | `services/common/payment-service/src/HBA.Financial.Payments.Infrastructure/PaymentsModuleInstaller.cs:237,248` |
| 4 | HIGH | food-order | Le paiement n'est jamais déclenché : `MealOrderPlaced` n'a que le panier comme consommateur, `PaymentCaptured(OrderType="FOOD")` n'arrive jamais → commande bloquée en `AwaitingPayment` | `services/food/food-order-service/src/HBA.Food.Order.Application/Abstractions/EventHandlers/MealOrderDomainEventHandlers.cs:8-11` |
| 5 | HIGH | tout le domaine | Aucun consommateur Kafka n'utilise l'Inbox : 11 handlers non idempotents alors que `EfConsumerInbox` existe et sert ailleurs | `services/food/food-order-service/src/HBA.Food.Order.Infrastructure/MealOrderingModuleInstaller.cs:71-92` |
| 6 | HIGH | food-order | `UnderReview` inatteignable : `PutMealOrderUnderReviewCommand` n'est envoyé nulle part → les 2 routes admin d'arbitrage échouent toujours | `services/food/food-order-service/src/HBA.Food.Order.Application/Commands/Orders/MealOrderLifecycleCommands.cs:30` |
| 7 | HIGH | food-cart | `NeutralPricingModuleApi` — bouchon enregistré sans garde d'environnement : toute remise = 0, tout code promo refusé | `services/food/food-cart-service/src/HBA.Food.Cart.Infrastructure/FoodCartModuleInstaller.cs:47` |
| 8 | HIGH | menu / availability / kitchen / review | 8 routes d'écriture et de lecture sans la moindre autorisation | `services/food/kitchen-prep-service/src/HBA.Food.Kitchen.Api/Endpoints/KitchenEndpoints.cs:10-12` |
| 9 | HIGH | menu / availability / review / kitchen | 4 `.proto` sur 7 sont des copies octet pour octet d'un autre contrat, aucun compilé → contrats gRPC mensongers | `services/food/kitchen-prep-service/proto/kitchen.proto` (= `food-order-service/proto/foodorder.proto`) |
| 10 | HIGH | menu / availability / kitchen / review | Persistance en `ConcurrentDictionary` singleton, aucune base, aucune migration | `services/food/menu-service/src/HBA.Food.Menu.Application/Abstractions/MenuStore.cs:8` |

*Suivants immédiats* : FOOD-20 (minimum de commande contrôlé après encaissement, sans
compensation — MEDIUM à la limite du HIGH) et FOOD-06 (parcours restauration dupliqué entre
order-service et food-order-service).

---

# Domaine delivery et applications


Périmètre : `services/delivery/*`, `apps/*`. Analyse statique (pas de compilateur .NET).
Toutes les preuves sont des chemins relatifs à la racine du dépôt.

---

## 0. Synthèse en une page

| Composant | Statut | En une phrase |
|---|---|---|
| `delivery-service` | **PARTIEL** | Domaine riche et correct, mais aucune surface livreur, aucun chemin d'acceptation, et le cache de positions n'est jamais alimenté → aucune course ne peut être attribuée. |
| `delivery-pricing-service` | **PARTIEL** | Seul satellite réellement persisté (EF + outbox), mais aucune authentification sur ses routes d'administration tarifaire, et absent de `HBA.sln`. |
| `dispatch-service` | **SQUELETTE** | `ConcurrentDictionary` en mémoire, candidats codés en dur, aucune base, aucune auth, aucun événement réellement publié. |
| `driver-service` | **SQUELETTE** | `/api/v1/drivers/me` rend un livreur codé en dur ; aucune inscription, aucun document, aucune vérification ; l'agrégat `Driver` réel n'est jamais instancié. |
| `route-service` | **SQUELETTE** | Haversine en mémoire, `IRouteProvider` déclaré et jamais implémenté ni enregistré. |
| `tracking-service` | **SQUELETTE** | Positions en mémoire, `driverId` lu dans le corps de requête, aucune auth, ETA codé en dur (540 s). |
| `proof-of-delivery-service` | **SQUELETTE** | OTP codé en dur `"123456"`, stockage en mémoire, aucun lien avec la transition `DELIVERED`. |
| `api-gateway` | **PARTIEL** | Routage YARP réel et globalement sain, mais il appelle deux routes livreur qui n'existent pas, et aucune des 6 satellites n'est routée. |
| `client-bff` | **PARTIEL** | Proxy HTTP vers `order-service` seulement ; 10 des 14 routes rendent 501. |
| `seller-bff` | **VIDE** | 1 fichier, 2 sondes de santé, en-tête « SQUELETTE » assumé. |
| `driver-bff` | **VIDE** | 1 fichier, 2 sondes de santé, en-tête « SQUELETTE » assumé. |
| **Admin BFF** | **INEXISTANT** | Aucun dossier, aucun projet, aucune route. |

**Tests : zéro.** Aucun projet de test ne couvre le domaine delivery (`tests/` ne contient rien pour delivery/driver/dispatch/tracking/proof/route). `services/delivery/delivery-service/src/HBA.Delivery.Core.Application/HBA.Delivery.Core.Application.csproj:22` déclare pourtant `InternalsVisibleTo("Delivery.UnitTests")` — projet qui n'existe pas.

---

## 1. Fiches par service

### delivery-service

```
Path:        services/delivery/delivery-service/
Projects:    HBA.Delivery.Core.Domain, HBA.Delivery.Core.Application,
             HBA.Delivery.Core.Infrastructure, HBA.Delivery.Core.Api,
             HBA.Deliveries.Contracts
Couches:     Domain ✔ / Application ✔ / Infrastructure ✔ / Api ✔ / Contracts ✔
Tests:       AUCUN
Volume:      79 fichiers .cs, dont 39 non triviaux (les 40 autres sont des
             migrations EF et leurs snapshots, ~9 000 lignes)
```

**Agrégats & machines d'état**
- `Delivery` (`HBA.Delivery.Core.Domain/Aggregates/Delivery/Delivery.cs`, 758 l.) — machine à états gardée, 11 états (`DeliveryStatus.cs`). Transitions : `Create` → `StartSearching` (l.433) → `AssignTo` (l.454) → `AcceptByDriver` (l.476) / `RejectByDriver` (l.505) → `MarkArrivedAtPickup` (l.569) → `MarkPickedUp` (l.572) → `MarkInTransit` (l.589) → `MarkArrivedAtDropoff` (l.592) → `MarkDelivered` (l.624). `Cancel` (l.716) refusé après collecte. `RevokeAssignment` (l.548).
- `Partner` + `PartnerApiKey` (`Domain/Partners/`) — API partenaire externe, quota journalier.
- `WebhookDelivery` (`Domain/Webhooks/`) — file de webhooks signés HMAC.
- Objets-valeurs : `DeliveryStop`, `DeliveryPackage`, `ProofOfDelivery`, `ContactPoint`.

**Endpoints HTTP** (`HBA.Delivery.Core.Api/Endpoints/DeliveryEndpoints.cs`)

| Route | Verbe | Policy |
|---|---|---|
| `/api/deliveries/` | POST | `MapOperationsGroup` → rôle `Admin` ou `Dispatcher` (l.44) |
| `/api/deliveries/{id}` | GET | idem (l.45) |
| `/api/deliveries/{id}/tracking` | GET | idem (l.46) |
| `/api/deliveries/{id}/cancel` | POST | idem (l.47) |

`DeliveryEndpoints.cs:14` crée `var deliveries = app.MapAuthenticatedGroup("/api/deliveries")` — **variable jamais utilisée**. Aucune route livreur n'existe.

**gRPC exposé** (`GrpcServices/DeliveryGrpcService.cs`) : `CreateDelivery` (l.70), `CancelDelivery` (l.150), `GetDelivery` (l.188), `GetDeliveryByReference` (l.199), `GetTracking` (l.204), `ResolveDriver` (l.248).
`GetQuote` et `LookupQuote`, déclarés dans `shared/proto/delivery/v1/delivery.proto:28` et `:43`, **ne sont pas surchargés** → `UNIMPLEMENTED` à l'exécution.

**Événements publiés** (via outbox, `Application/EventHandlers/DeliveryDomainEventHandlers.cs`) : `delivery.created`, `delivery.assigned`, `driver.verified`, `delivery.accepted`, `delivery.picked-up`, `delivery.completed`, `delivery.cancelled`, `delivery.no-driver-available`.
**Consommés** : ses propres événements d'intégration, pour la file de webhooks (`Application/Webhooks/EnqueueWebhookOnDeliveryEvents.cs`).

**Statut : PARTIEL** — le noyau métier est bon, la chaîne opérationnelle est coupée (§2, §3).

---

### delivery-pricing-service

```
Path:        services/delivery/delivery-pricing-service/
Projects:    HBA.Delivery.Pricing.{Domain,Application,Infrastructure,Api}
Couches:     Domain ✔ (anémique) / Application ✔ (DTO seuls) / Infrastructure ✔ / Api ✔
Tests:       AUCUN
Volume:      28 fichiers .cs, dont ~8 non triviaux
```

**Agrégats** : `DeliveryQuote` (record, `Domain/Aggregates/DeliveryQuote/DeliveryQuote.cs`), `PricingRule`, `DeliveryZone`. Statuts de devis : `ACTIVE` / `EXPIRED` / `CONSUMED` — gérés dans `Infrastructure/Persistence/EfDeliveryPricingStore.cs`, pas dans le domaine.

**Endpoints**

| Route | Verbe | Policy |
|---|---|---|
| `/api/v1/delivery-pricing/quotes` | POST | **AUCUNE** |
| `/api/v1/delivery-pricing/quotes/{id}` | GET | **AUCUNE** |
| `/api/v1/delivery-pricing/serviceability` | GET | **AUCUNE** |
| `/api/v1/delivery-pricing/zones` | GET | **AUCUNE** |
| `/api/v1/admin/delivery-pricing/rules` | GET, POST | **AUCUNE** (l.47) |
| `/api/v1/admin/delivery-pricing/rules/{id}` | PATCH | **AUCUNE** |
| `/api/v1/admin/delivery-pricing/rules/{id}/activate\|deactivate` | POST | **AUCUNE** |
| `/internal/v1/delivery-pricing/*` | POST/GET | **AUCUNE** |

**gRPC** : `QuoteDelivery`, `ConsumeQuote`, `ValidateQuote`, `GetServiceability` — les 4 RPC du proto sont implémentés.
**Événements publiés** : `DeliveryQuoteCreatedIntegrationEvent`, `DeliveryQuoteConsumedIntegrationEvent` — réellement drainés (`AddOutboxProcessor<DeliveryPricingDbContext>()`, `DeliveryPricingInfrastructureModule.cs:25`).
**Argent** : en `long` (unités entières XOF) partout — **pas de `double`**. Point correct.

**Statut : PARTIEL** — le seul satellite réel, mais non authentifié et hors solution.

---

### dispatch-service

```
Path:        services/delivery/dispatch-service/
Projects:    HBA.Delivery.Dispatch.{Domain,Application,Infrastructure,Api}
Couches:     Domain ✔ (mort) / Application ✖ (une classe en mémoire) /
             Infrastructure ✖ (3 lignes de DI) / Api ✔
Tests:       AUCUN
Volume:      19 fichiers .cs, dont 5 non triviaux
```

**Agrégats & machines d'état** : `DispatchJob` est un `record` sans invariant (`Domain/Aggregates/DispatchJob/DispatchAggregate.cs`, 45 l.). Les statuts (`OFFERING`, `ASSIGNED`, `CANCELLED`) sont des chaînes manipulées dans `Application/Abstractions/DispatchStore.cs`. **Aucune transition gardée.**
`Domain/Policies/DispatchPolicy.cs` (154 l.) est la seule pièce sérieuse — mais elle est dans le namespace `HBA.Deliveries.Domain.Dispatch` et n'est utilisée que par `delivery-service`, jamais par `dispatch-service` lui-même (`HBA.Delivery.Dispatch.Application.csproj` ne référence pas `HBA.Delivery.Dispatch.Domain`).

**Endpoints** (`Api/Endpoints/DispatchEndpoints.cs`)

| Route | Verbe | Policy |
|---|---|---|
| `/api/v1/dispatch/jobs/{deliveryId}` | GET | **AUCUNE** |
| `/api/v1/dispatch/{deliveryId}/retry` | POST | **AUCUNE** |
| `/api/v1/dispatch/{deliveryId}/manual-assign` | POST | **AUCUNE** (l.28) |
| `/internal/v1/dispatch/request` | POST | **AUCUNE** |
| `/internal/v1/dispatch/{deliveryId}/cancel` | POST | **AUCUNE** |
| `/internal/v1/dispatch/{deliveryId}/assignment` | GET | **AUCUNE** |

**gRPC** : `RequestDispatch`, `CancelDispatch`, `GetAssignment`, `AcceptOffer` — implémentés, mais tous adossés au store mémoire.
**Événements** : `DispatchStartedIntegrationEvent`, `DispatchOfferCreatedIntegrationEvent`, `DeliveryAssignedIntegrationEvent` — **mis en file et jamais drainés** (§4.3).

**Preuve du caractère factice** : `DispatchStore.BuildCandidates` (`Application/Abstractions/DispatchStore.cs:135-140`) rend deux GUID constants :
```csharp
new DriverCandidate(deliveryId, Guid.Parse("00000000-0000-7000-0000-000000000017"), 920, 240, 0.91m, 1, ...),
new DriverCandidate(deliveryId, Guid.Parse("00000000-0000-7000-0000-000000000018"), 1450, 420, 0.78m, 2, ...)
```

**Statut : SQUELETTE.**

---

### driver-service

```
Path:        services/delivery/driver-service/
Projects:    HBA.Delivery.Driver.{Domain,Application,Infrastructure,Api}
Couches:     Domain ✔ (mort, sauf CompleteMission) / Application ✖ / Infrastructure ✖ / Api ✔
Tests:       AUCUN
Volume:      18 fichiers .cs, dont 5 non triviaux
```

**Agrégats & machines d'état** : `Driver` (`Domain/Aggregates/Driver/DeliveryDriver.cs`, 328 l.) est **le seul agrégat correct du parcours livreur** : deux dimensions séparées `DriverAccountStatus` (`PendingVerification`/`Active`/`Suspended`/`Blocked`) et `DriverAvailability` (`Offline`/`Available`/`Busy`/`OnBreak`), avec `CanReceiveOffers` (l.145) et `GoOnline` (l.236) qui refuse si le compte n'est pas actif.
**Aucune de ses méthodes n'est appelée nulle part** sauf `CompleteMission()`. Recherche exhaustive sur `services/`, `apps/`, `shared/` : `Driver.Register`, `Verify()`, `Suspend(`, `Block(`, `GoOnline`, `GoOffline`, `TakeBreak`, `MarkBusy`, `RecordPosition` → **0 appelant**. Seul `DeliveryProgressCommands.cs:197` appelle `driver?.CompleteMission()`.
`Domain/Entities/DriverDocument.cs` (10 l.) existe ; rien ne le lit, rien ne l'écrit.

**Endpoints** (`Api/Endpoints/DriverEndpoints.cs`)

| Route | Verbe | Policy |
|---|---|---|
| `/api/v1/drivers/me` | GET | **AUCUNE** — rend `store.GetDefaultDriver()` (l.13) |
| `/api/v1/drivers/me` | PATCH | **AUCUNE** (l.15) |
| `/api/v1/drivers/me/vehicles` | GET, POST | **AUCUNE** |
| `/api/v1/drivers/me/availability` | POST | **AUCUNE** (l.34) |
| `/api/v1/drivers/me/deliveries` | GET | **AUCUNE** — rend un objet anonyme codé en dur (`DriverStore.cs:152-155`) |
| `/internal/v1/drivers/{id}` | GET | **AUCUNE** |
| `/internal/v1/drivers/eligibility` | POST | **AUCUNE** |
| `/internal/v1/drivers/{id}/busy-state` | POST | **AUCUNE** |

**gRPC** : `GetDriver`, `GetDriversBatch`, `CheckDriverEligibility`, `SetBusyState` — implémentés sur le store mémoire.
**Événements** : `DriverVehicleUpdatedIntegrationEvent`, `DriverAvailabilityChangedIntegrationEvent` — jamais drainés.

**Preuve du caractère factice** : `DriverStore` (`Application/Abstractions/DriverStore.cs:13-34`) construit dans son constructeur UN livreur unique `00000000-0000-7000-0000-000000000017`, `ACTIVE`/`VERIFIED`, et `DefaultDriverId` est utilisé par toutes les routes `/me` — **l'identité de l'appelant n'est jamais lue**.

**Statut : SQUELETTE.**

---

### route-service

```
Path:        services/delivery/route-service/
Projects:    HBA.Delivery.Route.{Domain,Application,Infrastructure,Api}
Couches:     Domain ✖ (records dupliqués, 2 namespaces concurrents) /
             Application ✖ / Infrastructure ✖ / Api ✔
Tests:       AUCUN
Volume:      12 fichiers .cs, dont 3 non triviaux
```

**Agrégats** : aucun. `RoutePlan` et `EtaSnapshot` sont déclarés **deux fois**, dans `Domain/Entities/RoutePlan.cs` (namespace `HBA.Routes.Domain.Routes`) et dans `Application/Abstractions/RouteStore.cs` (namespace `HBA.Routes.Application`). `HBA.Delivery.Route.Domain.csproj` n'a **aucune** `ProjectReference` et n'est référencé que par `…Route.Infrastructure`, qui ne contient que 17 lignes de DI.

**Endpoints** (`Api/Endpoints/RouteEndpoints.cs`) : `/api/v1/routes/estimate` (POST), `/optimize` (POST), `/deliveries/{id}` (GET), `/internal/v1/routes/{estimate,optimize,eta}` — **aucune policy**.
**gRPC** : `EstimateRoute`, `OptimizeRoute`, `RecalculateEta` — implémentés.
**Événements** : `RouteCalculated`, `RouteRecalculated`, `RouteDeliveryEtaUpdated` — jamais drainés.

`Application/Abstractions/IRouteProvider.cs` déclare une abstraction de fournisseur d'itinéraires (Mapbox/Google/OSRM) — **jamais implémentée, jamais enregistrée dans la DI, jamais injectée**. `RouteStore.EstimateAsync` calcule toujours un Haversine divisé par 5,8 m/s (`RouteStore.cs:16-17`) et étiquette le résultat `"FALLBACK_HAVERSINE"`.

**Statut : SQUELETTE.**

---

### tracking-service

```
Path:        services/delivery/tracking-service/
Projects:    HBA.Delivery.Tracking.{Domain,Application,Infrastructure,Api}
Couches:     Domain ✖ (doublons) / Application ✖ / Infrastructure ✖ / Api ✔
Tests:       AUCUN
Volume:      11 fichiers .cs, dont 3 non triviaux
```

**Particularité** : c'est le **seul** des 7 services sans dossier `proto/` propre ; il consomme directement `shared/proto/tracking/v1/tracking.proto`.

**Agrégats** : `TrackingSession` déclaré deux fois (`Domain/Aggregates/TrackingSession/TrackingAggregate.cs` en `HBA.Tracking.Domain.Tracking`, et `Application/Abstractions/TrackingStore.cs:126` en `HBA.Tracking.Application`). Idem `LocationPoint`. `HBA.Delivery.Tracking.Domain.csproj` est vide de références.

**Endpoints** (`Api/Endpoints/TrackingEndpoints.cs`)

| Route | Verbe | Policy |
|---|---|---|
| `/api/v1/tracking/sessions/{deliveryId}/locations` | POST | **AUCUNE** (l.13) |
| `/api/v1/tracking/deliveries/{deliveryId}/latest` | GET | **AUCUNE** (l.24) |
| `/api/v1/tracking/deliveries/{deliveryId}/stream-token` | GET | **AUCUNE** (l.29) |
| `/internal/v1/tracking/sessions/start\|stop` | POST | **AUCUNE** |
| `/internal/v1/tracking/deliveries/{id}/latest` | GET | **AUCUNE** |

**gRPC** : `GetLatestLocation`, `StartTrackingSession`, `StopTrackingSession`.
**Événements** : `TrackingSessionStarted/Ended`, `TrackingLocationSampled`, `DeliveryEtaUpdated` — jamais drainés.

ETA et progression codés en dur : `TrackingStore.cs:86-87` pose systématiquement `540` secondes et `new RouteProgress(0.35m, 5100)`.

**Statut : SQUELETTE.**

---

### proof-of-delivery-service

```
Path:        services/delivery/proof-of-delivery-service/
Projects:    HBA.Delivery.Proof.{Domain,Application,Infrastructure,Api}
Couches:     Domain ✖ (doublons, csproj totalement vide) /
             Application ✖ / Infrastructure ✖ / Api ✔
Tests:       AUCUN
Volume:      13 fichiers .cs, dont 3 non triviaux
```

`HBA.Delivery.Proof.Domain.csproj` est un fichier de 3 lignes **sans aucun `ItemGroup`**. `DeliveryProof` est déclaré dans `Domain/Aggregates/DeliveryProof/DeliveryProof.cs` (namespace `HBA.ProofOfDelivery.Domain.Proofs`) et **redéclaré** dans `Application/Abstractions/ProofStore.cs:130`.

**Endpoints** (`Api/Endpoints/ProofEndpoints.cs`) : `/api/v1/proofs` (POST), `/{id}/media/presign` (POST), `/{id}/submit` (POST), `/deliveries/{deliveryId}` (GET), `/internal/v1/proofs/deliveries/{id}/dropoff-valid` (GET), `/summary` (GET) — **aucune policy, sur aucune route**.
**gRPC** : `HasValidDropoffProof`, `GetProofSummary`.
**Événements** : `ProofSubmitted`, `ProofVerified`, `ProofRejected`, `DeliveryProofCompleted` — jamais drainés.

**Statut : SQUELETTE.**

---

### api-gateway

```
Path:        apps/api-gateway/
Projects:    HBA.Gateway.{Api,Application,Infrastructure} (+ tests/HBA.Gateway.IntegrationTests)
Couches:     Api ✔ / Application ✔ / Infrastructure ✔
Tests:       apps/api-gateway/tests/HBA.Gateway.IntegrationTests (présents)
Volume:      121 fichiers .cs
```

**Rôle double** : (a) proxy YARP configuré dans `src/HBA.Gateway.Api/appsettings.json` (44 routes, 16 clusters), (b) BFF d'agrégation en contrôleurs MVC (`Controllers/Bff/{Client,Driver,Merchant,Restaurant}Controller.cs`) qui appellent les services **en HTTP**, jamais en gRPC (`Infrastructure/HttpClients/**`).

**Vérification des clusters** — `ReverseProxy:Clusters` ne porte que des noms ; les adresses sont injectées par `Infrastructure/ReverseProxy/ServiceAddressConfigFilter.cs` à partir de la section `Services`. Les 16 clusters (`Identity, User, Merchant, Catalog, Inventory, Commerce, Order, Food, FoodCart, FoodOrder, Delivery, Financial, Engagement, Communication, Media, Promotion`) sont tous résolus par `ServicesOptions.Resolve` — **cohérent**.
Mais `appsettings.json` (section `Services`) et `appsettings.Development.json` **ne contiennent ni `FoodCart` ni `FoodOrder`**, alors que `Infrastructure/Configuration/ServicesOptions.cs:67-68` les déclare `[Required, Url]` avec `ValidateOnStart()` (`Infrastructure/DependencyInjection.cs:32`). Voir défaut #9.

**Ordre des routes** — vérifié : les recouvrements sont correctement arbitrés par `Order` croissant.
- `auth-v1-otp-request` (0) < `auth-v1` (1) ✔
- `payments-webhooks` (5, anonyme) < `payments` (10, authentifié) ✔ — sans quoi les webhooks PSP repartaient en 401.
- `food-cart` / `food-orders` / `food-restaurant-orders` (5) < `food-read` (10) / `food-write` (11) ✔
- `settlements` (9, GET seulement) < `payments`/`wallet` (10) ✔
- Partout, la variante `-read` anonyme est restreinte par `Methods: [GET, HEAD, OPTIONS]` et la variante `-write` authentifiée porte un `Order` supérieur ✔

**Routes anonymes** : `auth*`, `geo` (GET), `merchants-read-*`, `catalog-read-*`, `food-read`, `reviews-read`, `media-*-read`, `payments-webhooks`. Toutes justifiées ; `payments-webhooks` s'appuie sur la vérification HMAC en aval.

**Surface Driver BFF** (`Controllers/Bff/DriverController.cs`) : `[Authorize(Policy = DriverOnly)]` au niveau de la classe, aucun `driverId` en paramètre — **conception correcte**. `GET /api/v1/bff/driver/{dashboard,missions,missions/{id},earnings,profile}`.

**Statut : PARTIEL** — voir défauts #4 et #9.

---

### client-bff

```
Path:        apps/client-bff/
Projects:    HBA.ClientBff.{Domain,Application,Infrastructure,Api}
Couches:     Domain ✖ (csproj vide) / Application ✔ (2 fichiers) /
             Infrastructure ✔ (2 fichiers) / Api ✔
Tests:       AUCUN
Volume:      9 fichiers .cs, dont 6 non triviaux
```

**Ce qu'il fait réellement** : un proxy JSON authentifié vers `order-service` uniquement.
**Protocole : HTTP**, malgré le dossier `Infrastructure/GrpcClients/MarketplaceOrder/`. `Program.cs:29` `AddHttpClient<IClientOrderGateway, ClientOrderGateway>`, `ClientOrderGateway.cs:137-143` → `GET /api/orders`, `GET /api/orders/{id}`, `POST /api/orders`. Le jeton entrant est recopié (`ClientOrderGateway.cs:192-196`). Aucune référence à Grpc.Net dans `HBA.ClientBff.Infrastructure.csproj`.

**Endpoints** (`Api/Endpoints/ClientEndpoints.cs`, groupe `.RequireAuthorization()` l.14)

| Route | Verbe | État |
|---|---|---|
| `/api/v1/client/home` | GET | statique (l.42-50) |
| `/api/v1/client/orders` | GET, POST | proxy réel |
| `/api/v1/client/orders/{id}` | GET | proxy réel |
| `/products/{id}`, `/restaurants/{id}`, `/cart`, `/checkout/preview`, `/food/cart`, `/food/orders`, `/deliveries/{id}/tracking`, `/returns`, `/reviews` | — | **501** (l.22-30) |

**gRPC exposé** : aucun. **Événements** : aucun.
**Statut : PARTIEL.**

---

### seller-bff / driver-bff

```
Path:        apps/seller-bff/ , apps/driver-bff/
Projects:    HBA.{Seller,Driver}Bff.{Domain,Application,Infrastructure,Api}
Couches:     Domain/Application/Infrastructure : csproj VIDES, 0 fichier .cs
Tests:       AUCUN
Volume:      1 fichier .cs chacun (Program.cs, 18 lignes)
Endpoints:   /health/live, /health/ready — anonymes
gRPC:        aucun     Événements: aucun
Statut:      VIDE
```

`apps/driver-bff/src/HBA.DriverBff.Api/Program.cs:3` : *« SQUELETTE — L'HOTE DEMARRE ET REPOND, MAIS N'EXPOSE AUCUN CAS D'USAGE. »* Idem `seller-bff`.
Les deux sont pourtant déclarés dans `docker-compose.dev.yml` (l.1231, l.1242).

### Admin BFF
**Inexistant.** Aucun dossier sous `apps/`, aucun projet dans `HBA.sln`, aucun service dans `docker-compose.dev.yml`, aucune route `/api/v1/bff/admin` dans la passerelle. L'exploitation passe par les routes `MapOperationsGroup`/`MapAdminGroup` des services, relayées par YARP.

---

## 2. Les six questions instruites

### 2.1 Le parcours livreur : inscription → documents → vérification → ACTIVE → disponibilité → dispatch → enlèvement → livraison → preuve

| Étape | Où elle devrait vivre | Réalité |
|---|---|---|
| Inscription | `Driver.Register` (`DeliveryDriver.cs:148`) | **Aucun appelant.** Pas de commande, pas de route, pas de RPC. |
| Documents | `DriverDocument` (`Domain/Entities/DriverDocument.cs`) | **Type déclaré, jamais lu ni écrit.** Aucun téléversement, aucun stockage. |
| Vérification → ACTIVE | `Driver.Verify()` (`DeliveryDriver.cs:176`) | **Aucun appelant.** `DriverVerifiedDomainEventHandler` est enregistré (`DeliveriesModuleInstaller.cs:101`) mais l'événement n'est jamais levé. |
| Suspension / blocage | `Suspend` (l.212) / `Block` (l.226) | **Aucun appelant.** |
| OFFLINE/AVAILABLE/BUSY | `GoOnline` (l.236) / `GoOffline` (l.249) / `MarkBusy` (l.289) | **Aucun appelant** sur l'agrégat. `driver-service` expose bien `POST /api/v1/drivers/me/availability`, mais elle écrit dans un `ConcurrentDictionary` (`DriverStore.cs:88-105`), pas dans l'agrégat. |
| Dispatch (offre) | `Delivery.AssignTo` ← `DispatchDeliveryCommandHandler` ← `DeliveryDispatchService` | Chaîne câblée, mais **inerte** (§2.3). |
| Acceptation | `Delivery.AcceptByDriver` (`Delivery.cs:476`) | **Aucun appelant dans tout le dépôt.** Pas de commande, pas de route, pas de RPC. |
| Refus | `Delivery.RejectByDriver` | Seul `ExpireDeliveryOfferCommandHandler` l'appelle, avec `expired: true`. Un livreur ne peut pas refuser explicitement. |
| Enlèvement / transit / arrivée | `MarkArrivedAtPickup`, `MarkPickedUp`, `MarkInTransit`, `MarkArrivedAtDropoff` | Handlers écrits (`DeliveryProgressCommands.cs:107-117`), **aucune route ne les envoie**. |
| Livraison | `MarkDelivered` | Handler écrit (l.143), **aucune route ne l'envoie**. |
| Preuve | `ProofOfDelivery.Capture` | Correct dans le domaine, mais inaccessible faute de route. |

**Conclusion : le parcours livreur n'existe pas.** Le domaine décrit correctement toutes les étapes ; aucune n'est atteignable. `HBA.Delivery.Core.Application/Queries/GetTimeline/MyDeliveriesQuery.cs:54` et `Domain/Aggregates/Delivery/Delivery.cs:214` décrivent un groupe `/api/deliveries/mine` et une `ResolveDriverQuery` — **ni l'un ni l'autre n'existe** (0 occurrence hors commentaires).

### 2.2 Deux livreurs peuvent-ils accepter la même mission ?

**Dans `delivery-service` : la question ne se pose pas — personne ne peut accepter.** `AcceptByDriver` n'a aucun appelant.

**Si la route existait**, la protection serait la suivante :
- *Exclusivité logique* : `Delivery.AssignTo` (l.456) exige `Status == SearchingDriver` et bascule en `DriverAssigned` → **une seule offre vivante à la fois**. `AcceptByDriver` (l.478) exige `Status == DriverAssigned` puis passe en `DriverAccepted`. Le second acceptant recevrait `delivery.invalid_transition`.
- *Index unique* : **aucun**. `Infrastructure/Persistence/Configurations/DeliveryConfiguration.cs` déclare `ux_deliveries_reference_source` (l.148, idempotence de création) et trois index non uniques ; **rien sur `AssignedDriverId` en unicité**, et la table `delivery_assignments` (l.107-127) n'a qu'un index non unique sur `DriverId`.
- *Verrou optimiste* : **aucun**. Recherche `IsRowVersion|IsConcurrencyToken|xmin|RowVersion` sur `services/delivery/` → **0 résultat**. Le code l'admet lui-même : `Infrastructure/Dispatch/DeliveryDispatchService.cs:94-96` — *« Rien dans l'agrégat ne l'attrape — Delivery n'a pas de jeton de concurrence. »*
- Le palliatif est un verrou d'avis PostgreSQL mono-processus (`Infrastructure/Dispatch/SingleRunnerLock.cs`) qui ne protège **que la boucle de dispatch**, pas les requêtes entrantes.

⇒ **Deux requêtes d'acceptation concurrentes liraient toutes deux `DriverAssigned`, passeraient toutes deux la garde, et écriraient toutes deux.** La dernière écriture gagne silencieusement ; l'autre livreur croit avoir la course. **CRITICAL**, dès que la route d'acceptation sera écrite.

**Dans `dispatch-service` : oui, sans aucune réserve.** `DispatchStore.AssignAsync` (`Application/Abstractions/DispatchStore.cs:87-112`) écrit `_assignments[deliveryId] = assignment` **sans lire l'état antérieur**, sans vérifier qu'aucune affectation n'existe déjà, sans vérifier que le livreur avait reçu une offre. Exposé anonymement par `POST /api/v1/dispatch/{deliveryId}/manual-assign` et par le RPC `AcceptOffer`. N'importe qui peut réaffecter n'importe quelle course à n'importe quel livreur, autant de fois qu'il veut.

### 2.3 Un livreur suspendu peut-il recevoir une offre ?

**Dans `delivery-service` : non.** `DispatchPolicy.Rank` (`dispatch-service/src/HBA.Delivery.Dispatch.Domain/Policies/DispatchPolicy.cs:96-99`) écarte tout candidat dont `Driver.CanReceiveOffers` est faux, et `CanReceiveOffers` (`DeliveryDriver.cs:145`) exige `AccountStatus == Active && Availability == Available`. La règle est correcte.
Réserve : rien ne peut *rendre* un livreur suspendu, puisque `Suspend()` n'a aucun appelant.

**Dans `dispatch-service` : oui.** `BuildCandidates` rend deux GUID constants sans jamais interroger le moindre livreur ni le RPC `CheckDriverEligibility` du `driver-service`. Le statut de compte n'entre nulle part.

### 2.4 Le suivi de position est-il réservé au livreur assigné ?

**Non, à trois titres.**
1. `tracking-service` : `POST /api/v1/tracking/sessions/{deliveryId}/locations` n'a **aucune authentification** et lit le `DriverId` **dans le corps de la requête** (`Api/Endpoints/TrackingEndpoints.cs:15,20` → `LocationBatchRequest.DriverId`, `Application/Abstractions/TrackingStore.cs:128`). N'importe qui peut publier des positions arbitraires pour n'importe quelle course, au nom de n'importe quel livreur.
2. `GET /api/v1/tracking/deliveries/{deliveryId}/latest` est anonyme (`TrackingEndpoints.cs:24`) → fuite de la position en direct d'un livreur à qui connaît un identifiant de course.
3. `GET /api/v1/tracking/deliveries/{deliveryId}/stream-token` (l.29) émet un jeton de flux **sans aucun contrôle**, pour n'importe quelle course.

Côté `delivery-service`, `GET /api/deliveries/{id}/tracking` est correctement restreint à `MapOperationsGroup` (Admin/Dispatcher) — mais aucun des deux services ne vérifie que l'appelant *est* le livreur assigné.

### 2.5 La preuve de livraison est-elle obligatoire, et conditionne-t-elle le passage à DELIVERED ?

**Oui, mais uniquement dans `delivery-service`, et de façon conditionnelle et locale.**
- `Delivery.MarkDelivered` (`Delivery.cs:641`) : la preuve n'est exigée que si `RequiredProof != ProofOfDeliveryKind.None`, c'est-à-dire si elle a été demandée **à la création**. Une course créée avec `None` — le défaut du paramètre, `Delivery.cs:303` — passe `DELIVERED` sans aucune preuve.
- Quand elle est exigée, elle est réellement **vérifiée** : `ProofOfDelivery.Capture` (`Domain/Entities/ProofOfDelivery.cs:77`) compare le PIN en temps constant (`CryptographicOperations.FixedTimeEquals`, l.128) contre `IssuedPin` émis à la création avec `RandomNumberGenerator` (l.70), et refuse une photo/signature qui n'a pas la forme d'une référence de stockage (l.144-155). Le compteur `FailedProofAttempts` verrouille après 5 essais (`Delivery.cs:226,643`) et est bien persisté (`DeliveryConfiguration.cs:65`).
- **`proof-of-delivery-service` ne participe à rien.** `delivery-service` n'a aucun client vers lui : recherche `HasValidDropoffProof` dans `services/delivery/delivery-service/` → 0 résultat. Les deux implémentations de la preuve coexistent sans se connaître.
- Et l'implémentation du service dédié est une porte ouverte : `ProofStore.VerifyOtp` (`proof-of-delivery-service/src/HBA.Delivery.Proof.Application/Abstractions/ProofStore.cs:126`) :
```csharp
return hash.Length == 64 && otp is "123456";
```

### 2.6 Quand une livraison passe DELIVERED, la commande marketplace/food est-elle mise à jour ? Par quel mécanisme ?

**Oui — par événement Kafka via le Transactional Outbox.** La chaîne est complète et vérifiable :

1. `Delivery.MarkDelivered` (`Delivery.cs:704`) lève `DeliveryCompletedDomainEvent`.
2. `DeliveriesDbContext.SaveChangesAsync` (héritage de `ModuleDbContext`) dispatche l'événement de domaine.
3. `DeliveryCompletedDomainEventHandler` (`Application/EventHandlers/DeliveryDomainEventHandlers.cs:161-180`) publie `DeliveryCompletedIntegrationEvent` (porte `Reference`, `Source`, `DriverId`, `DeliveredAtUtc`, `DriverEarning`, `Currency`).
4. L'événement est drainé dans l'outbox par `AddOutboxProcessor<DeliveriesDbContext>()` (`DeliveriesModuleInstaller.cs:112`) puis relayé sur Kafka.
5. Trois consommateurs :
   - **Marketplace** : `services/marketplace/order-service/src/HBA.Order.Application/Orders/EventHandlers/MarkOrderDeliveredOnDeliveryCompletedHandler.cs:118` — décode la référence via `OrderDeliveryReference.Read`, envoie `MarkOrderDeliveredCommand`, avec `SagaOutcome.Exiger` pour rejouer en cas de panne. Enregistré : `OrderingModuleInstaller.cs:74`.
   - **Food** : `services/food/restaurant-service/src/HBA.Food.Restaurant.Api/Integration/FoodDeliveryReturnHandlers.cs:126-139`. Enregistré : `Program.cs:81`.
   - **Portefeuille livreur** : `services/common/wallet-service/src/HBA.Financial.Wallet.Application/Earnings/CreditDriverOnDeliveryCompletedHandler.cs:52`. Enregistré : `SettlementModuleInstaller.cs:143`.
6. En parallèle, `WebhookOnDeliveryCompleted` (`Application/Webhooks/EnqueueWebhookOnDeliveryEvents.cs:156`) met le webhook partenaire en file.

**Ni gRPC ni appel direct.** Le mécanisme est correct et conforme à l'architecture cible. **Mais il n'est jamais déclenché**, faute de route menant à `MarkDelivered` (§2.1).

---

## 3. Restes de l'ancien nom `HBA.Deliveries.*`

Vérification demandée. Recherche de `HBA.Deliveries.(Domain|Application|Api|Infrastructure)` dans `*.csproj`, `*.sln`, `*.yml`, `Dockerfile`, `*.json`, `k8s/` : **0 résultat**. La restructuration est propre côté build.

- `HBA.sln:158` référence `HBA.Deliveries.Contracts` au bon chemin ; `HBA.sln:154,156,160,162` référencent les quatre projets `HBA.Delivery.Core.*`. ✔
- `services/delivery/delivery-service/Dockerfile:11,12,41` pointent sur `HBA.Delivery.Core.Api`. ✔
- `docker-compose.dev.yml` et `k8s/` ne nomment que des services, pas des assemblies. ✔

**Mais les namespaces C# n'ont pas suivi les assemblies.** Tout le contenu de `HBA.Delivery.Core.{Domain,Application,Infrastructure,Api}` vit dans `HBA.Deliveries.*` (ex. `Delivery.cs:5` → `namespace HBA.Deliveries.Domain.Deliveries` ; `Program.cs:1-4` → `using HBA.Deliveries.Api.Endpoints`). Ce n'est pas une rupture de compilation, mais assembly et namespace divergent dans les 5 projets — **LOW**, à corriger avant que quiconque cherche un type par son assembly.

**Défaut réel de solution** : les 4 projets `HBA.Delivery.Pricing.*` sont **absents de `HBA.sln`** (`grep -c "HBA.Delivery.Pricing" HBA.sln` → 0). Comme rien ne référence `HBA.Delivery.Pricing.Api`, `dotnet build HBA.sln` (`.github/workflows/ci.yml:105`) et `dotnet test HBA.sln` (l.117) **ne compilent jamais delivery-pricing-service**. Idem pour les 7 projets `shared/contracts/HBA.{DeliveryPricing,Dispatch,Drivers,Routes,Tracking,ProofOfDelivery}.Contracts*` (0 dans la solution), qui ne sont compilés que par référence transitive.

---

## 4. Défauts, par ordre de gravité

### CRITICAL

**#1 — Aucune course ne peut être attribuée à un livreur : le cache de positions n'est jamais alimenté.**
`services/delivery/delivery-service/src/HBA.Delivery.Core.Application/Abstractions/DeliveryAbstractions.cs:73` déclare `IDriverLocationCache.SetAsync`. Recherche exhaustive sur `services/` et `apps/` : **aucun appelant** de `SetAsync` ni de `RemoveAsync` sur cette interface. Seul `FindNearbyAsync` est consommé, par `DispatchDeliveryCommandHandler` (`Commands/AssignDriver/DispatchDeliveryCommand.cs:94`). Redis (ou le repli mémoire) reste donc toujours vide, `nearby.Count == 0` est toujours vrai (l.97), et le handler rend `new DispatchOutcome(null, 0, radiusKm)` à chaque tour. Après 5 tours la course bascule `NoDriverAvailable`. **Le moteur logistique ne peut affecter personne.** `tracking-service`, qui reçoit les positions, écrit dans son propre dictionnaire mémoire et n'a aucun lien avec ce cache.

**#2 — Aucune surface livreur n'existe dans `delivery-service`.**
`HBA.Delivery.Core.Api/Endpoints/DeliveryEndpoints.cs:14` crée le groupe `MapAuthenticatedGroup("/api/deliveries")` et **ne lui rattache aucune route** (variable `deliveries` inutilisée). Conséquences en cascade : `Delivery.AcceptByDriver` (`Delivery.cs:476`), `RevokeAssignment` (l.548), `MarkArrivedAtPickup`, `MarkPickedUp`, `MarkInTransit`, `MarkArrivedAtDropoff`, `MarkDelivered` n'ont **aucun appelant** ; `MyDeliveriesQueryHandler` (`Queries/GetTimeline/MyDeliveriesQuery.cs:69`) n'est envoyé par personne ; `ResolveDriverQuery`, cité dans six commentaires, **n'existe pas**. Une course créée ne peut jamais dépasser `DriverAssigned`, et donc jamais atteindre `DELIVERED` — ni l'escrow vendeur, ni le gain du livreur, ni la clôture de commande.

**#3 — `LookupQuote` et `GetQuote` sont déclarés au contrat gRPC et non implémentés : toute commande avec devis de livraison lève une exception.**
`shared/proto/delivery/v1/delivery.proto:28` (`GetQuote`) et `:43` (`LookupQuote`) sont générés dans `HBA.Deliveries.Contracts.Grpc` (`GrpcServices="Both"`). `services/delivery/delivery-service/src/HBA.Delivery.Core.Api/GrpcServices/DeliveryGrpcService.cs` ne surcharge que 6 des 8 RPC (l.70, 150, 188, 199, 204, 248) → la classe de base rend `RpcException(StatusCode.Unimplemented)`. Or `shared/contracts/HBA.Deliveries.Contracts.Grpc/DeliveryGrpc.cs:167` appelle `LookupQuoteAsync` **sans capturer `RpcException`**, et deux services l'appellent sur le chemin de paiement : `services/marketplace/order-service/.../PlaceOrderCommandHandler.cs:371` et `services/food/food-order-service/.../PlaceMealOrderCommand.cs:302`. Toute commande présentant un `DeliveryQuoteId` échoue en 500.

**#4 — La passerelle appelle deux routes de `delivery-service` qui n'existent pas : le Driver BFF rend 404 sur tous ses écrans.**
`apps/api-gateway/src/HBA.Gateway.Infrastructure/HttpClients/Delivery/DeliveryClient.cs:22` appelle `GET /api/deliveries/drivers/me` et l.26 `GET /api/deliveries/drivers/me/missions`. Aucune de ces deux routes n'est déclarée dans `DeliveryEndpoints.cs` (qui n'expose que `/`, `/{id}`, `/{id}/tracking`, `/{id}/cancel`). Les cinq actions de `Controllers/Bff/DriverController.cs` (dashboard, missions, missions/{id}, earnings, profile) en dépendent, et `GetDriverMissionsHandler.cs:31-34` marque la dépendance `DependencyCriticality.Critical` → l'écran entier tombe. Voir aussi `GetDriverEarningsHandler.cs`, `GetDriverDashboardHandler.cs`.

**#5 — OTP de preuve de livraison codé en dur.**
`services/delivery/proof-of-delivery-service/src/HBA.Delivery.Proof.Application/Abstractions/ProofStore.cs:126` : `return hash.Length == 64 && otp is "123456";`. Combiné à `POST /api/v1/proofs/{id}/submit` sans aucune authentification (`Api/Endpoints/ProofEndpoints.cs:27`), n'importe qui peut faire passer une preuve en `VERIFIED` et déclencher `DeliveryProofCompletedIntegrationEvent`.

**#6 — Les routes d'administration tarifaire de la livraison sont anonymes.**
`services/delivery/delivery-pricing-service/src/HBA.Delivery.Pricing.Api/Endpoints/DeliveryPricingEndpoints.cs:47` : `app.MapGroup("/api/v1/admin/delivery-pricing")` — `MapGroup` nu, sans `RequireAuthorization`. `Api/Program.cs` n'appelle ni `AddAuthentication`, ni `AddAuthorization`, ni `UseAuthentication/UseAuthorization` (seulement `AddDeliveryPricingInfrastructure` + `AddHbaGrpc`). Un appelant anonyme peut créer, modifier, activer et désactiver les règles tarifaires : `BaseFee`, `PerKmFee`, `SurgeMultiplier`, `MinFee`, `MaxFee`. **Contrôle direct du prix de toutes les livraisons de la plateforme.**

**#7 — Réaffectation anonyme de n'importe quelle course à n'importe quel livreur.**
`services/delivery/dispatch-service/src/HBA.Delivery.Dispatch.Api/Endpoints/DispatchEndpoints.cs:28` : `POST /api/v1/dispatch/{deliveryId}/manual-assign` sans policy. `DispatchStore.AssignAsync` (`Application/Abstractions/DispatchStore.cs:87`) écrase l'affectation existante sans lire l'état, sans vérifier qu'une offre a été faite, sans vérifier l'éligibilité du livreur. Même chose pour `POST /{deliveryId}/retry` et `POST /internal/v1/dispatch/request`.

### HIGH

**#8 — Aucun jeton de concurrence sur l'agrégat `Delivery`.**
`services/delivery/delivery-service/src/HBA.Delivery.Core.Infrastructure/Persistence/Configurations/DeliveryConfiguration.cs` ne déclare ni `IsRowVersion()`, ni `IsConcurrencyToken()`, ni `xmin` (0 résultat sur tout `services/delivery/`). Aucun index unique n'empêche deux affectations simultanées. Le code le documente lui-même : `Infrastructure/Dispatch/DeliveryDispatchService.cs:94-96`. Dès qu'une route d'acceptation existera (§#2), deux livreurs pourront accepter la même course. Le verrou d'avis `SingleRunnerLock.cs` ne couvre que la boucle de fond.

**#9 — Aucune donnée de `dispatch`, `driver`, `route`, `tracking`, `proof` n'est persistée, et aucun de leurs événements n'atteint Kafka.**
Les cinq services enregistrent `IIntegrationEventPublisher` → `IntegrationEventQueue` (ex. `dispatch-service/src/HBA.Delivery.Dispatch.Infrastructure/Persistence/DispatchInfrastructureModule.cs:12-14`, idem `DriversInfrastructureModule.cs`, `RoutesInfrastructureModule.cs`, `TrackingInfrastructureModule.cs`, `ProofOfDeliveryInfrastructureModule.cs`), **sans jamais appeler `AddOutboxProcessor<T>()` ni déclarer un `DbContext`**. Or `shared/common/HBA.Shared.Infrastructure/Outbox/IntegrationEventQueue.cs:16-23` est une simple `List<IntegrationEvent>` scopée que seul un `ModuleDbContext` draine. **Tous les `PublishAsync` de ces cinq services sont jetés à la fin de la requête.** Aucun état non plus : `ConcurrentDictionary` partout, dans des singletons — tout disparaît au redéploiement.

**#10 — Le devis est consommé chez le service de tarification avant que la course ne soit enregistrée : saga sans compensation.**
`services/delivery/delivery-service/src/HBA.Delivery.Core.Application/Commands/CreateDelivery/CreateDeliveryCommand.cs` — `ConsumeQuoteAsync` (appel gRPC sortant) puis `AttachQuote`, puis `_repository.AddAsync` et `_unitOfWork.SaveChangesAsync`. Si l'enregistrement échoue (contrainte, panne base), le devis reste `CONSUMED` chez `delivery-pricing-service` et **aucun geste ne le libère** : le client doit repayer un devis pour une course qui n'a jamais existé.

**#11 — `ConsumeQuoteAsync` n'est pas atomique : un devis à usage unique peut être consommé deux fois.**
`services/delivery/delivery-pricing-service/src/HBA.Delivery.Pricing.Infrastructure/Persistence/EfDeliveryPricingStore.cs:96-119` : `ValidateQuoteAsync` lit, puis `FirstAsync` relit, puis `Update(consumed)` écrit — sans `UPDATE ... WHERE Status = 'ACTIVE'`, sans jeton de concurrence sur `DeliveryQuote` (`DeliveryPricingDbContext.cs:30-41` n'en déclare aucun). Deux `CreateDelivery` concurrents avec le même `quote_id` obtiennent tous deux `Valid = true` → deux courses facturées sur un devis unique, `ConsumedByDeliveryId` écrasé.

**#12 — Le suivi de position n'a aucune authentification et fait confiance au corps de requête.**
`services/delivery/tracking-service/src/HBA.Delivery.Tracking.Api/Endpoints/TrackingEndpoints.cs:13-22` : `POST /api/v1/tracking/sessions/{deliveryId}/locations` prend `LocationBatchRequest.DriverId` du corps. `Program.cs` n'installe aucune authentification. Falsification de trajet et usurpation de livreur triviales. `GET .../latest` (l.24) et `GET .../stream-token` (l.29) sont anonymes → fuite de position en direct.

**#13 — `/api/v1/drivers/me` rend un livreur codé en dur, sans lire l'appelant.**
`services/delivery/driver-service/src/HBA.Delivery.Driver.Api/Endpoints/DriverEndpoints.cs:13,17,22,30,40,45` utilisent tous `store.DefaultDriverId`. `DriverStore` (`Application/Abstractions/DriverStore.cs:13-34`) fabrique un unique livreur `…017`, `ACTIVE`/`VERIFIED`, dans son constructeur. Aucune route n'est authentifiée. Un appelant anonyme lit et modifie le profil, ajoute des véhicules et change la disponibilité de ce livreur ; `PATCH /api/v1/drivers/me` (l.15) est écrit sans contrôle.

**#14 — La passerelle ne peut pas démarrer avec sa configuration versionnée.**
`apps/api-gateway/src/HBA.Gateway.Infrastructure/Configuration/ServicesOptions.cs:67-68` déclare `FoodCart` et `FoodOrder` en `[Required, Url]`, et `Infrastructure/DependencyInjection.cs:21-32` applique `.ValidateDataAnnotations().ValidateOnStart()`. Ces deux clés sont **absentes** de `apps/api-gateway/src/HBA.Gateway.Api/appsettings.json` (section `Services`, l.48-63) et de `appsettings.Development.json`. Seul `docker-compose.dev.yml:1332-1333` les fournit. Tout lancement hors Docker (développement, test d'intégration, exécution locale) échoue au démarrage.

### MEDIUM

**#15 — Aucun test ne couvre les 7 services du domaine delivery.**
`tests/` ne contient aucun projet delivery/driver/dispatch/tracking/proof/route. Le domaine le plus riche du dépôt (`Delivery.cs`, 758 l., 11 états) n'a pas un seul test. `HBA.Delivery.Core.Application.csproj:22` déclare `InternalsVisibleTo("Delivery.UnitTests")` — projet inexistant, vestige.

**#16 — `delivery-pricing-service` n'est jamais compilé par la CI.**
`grep -c "HBA.Delivery.Pricing" HBA.sln` → 0. Aucun projet de la solution ne référence `HBA.Delivery.Pricing.Api`. `.github/workflows/ci.yml:105` (`dotnet build HBA.sln`) et l.117 (`dotnet test HBA.sln`) l'ignorent donc entièrement. Idem pour les 7 projets `shared/contracts/HBA.{DeliveryPricing,Dispatch,Drivers,Routes,Tracking,ProofOfDelivery}.Contracts*`, absents de la solution.

**#17 — Aucune des six satellites n'est joignable depuis l'extérieur.**
`apps/api-gateway/src/HBA.Gateway.Api/appsettings.json` ne déclare **aucune route ni cluster** vers `dispatch-service`, `driver-service`, `route-service`, `tracking-service`, `proof-of-delivery-service`, `delivery-pricing-service`. `docker-compose.dev.yml:1315-1322` définit pourtant `SERVICES__DISPATCH`, `SERVICES__DRIVERS`, `SERVICES__TRACKING`, `SERVICES__ROUTES`, `SERVICES__PROOFOFDELIVERY`, `SERVICES__DELIVERYPRICING`, `SERVICES__RETURNREFUND`, `SERVICES__MENU`, `SERVICES__AVAILABILITY`, `SERVICES__KITCHENPREP`, `SERVICES__FOODREVIEW` — **onze variables que `ServicesOptions.Resolve` (l.73-92) ne connaît pas** et qui sont donc inertes.

**#18 — Les couches `Domain` des cinq satellites sont du code mort, avec des types dupliqués sous deux namespaces.**
`HBA.Delivery.{Route,Tracking,Proof}.Domain.csproj` n'ont **aucune** `ProjectReference` (celui de Proof est un fichier de 3 lignes). Aucun `*.Application.csproj` de satellite ne référence son `*.Domain.csproj`. Résultat : `RoutePlan`/`EtaSnapshot` (`route-service/.../Domain/Entities/RoutePlan.cs` vs `Application/Abstractions/RouteStore.cs:104-105`), `TrackingSession`/`LocationPoint` (`tracking-service/.../TrackingAggregate.cs` vs `TrackingStore.cs:126-127`), `DeliveryProof`/`ProofMedia` (`proof-of-delivery-service/.../DeliveryProof.cs` vs `ProofStore.cs:130-131`), `DispatchJob`/`Assignment`/`DriverCandidate` (`dispatch-service` : trois déclarations concurrentes) existent chacun en deux exemplaires divergents. Les politiques du domaine (`LocationValidationPolicy`, `ProofVerificationPolicy`, `CandidateScoringPolicy`, `RetryPolicy`, `OfferStrategyPolicy`, `DriverEligibilityPolicy`, `AvailabilityPolicy`) sont toutes **injoignables** ; leur logique est recopiée à la main dans les stores (ex. `TrackingStore.cs:120-123` recopie `LocationValidationPolicy.IsPlausible`).

**#19 — `IRouteProvider` est une abstraction sans implémentation ni enregistrement.**
`services/delivery/route-service/src/HBA.Delivery.Route.Application/Abstractions/IRouteProvider.cs` — aucune classe ne l'implémente, `RoutesInfrastructureModule.cs` ne l'enregistre pas, `RouteStore` ne l'injecte pas. Tout itinéraire est un Haversine à vitesse constante 5,8 m/s (`RouteStore.cs:16-17`), et le champ `Provider` vaut toujours `"FALLBACK_HAVERSINE"`. `RouteProvider.Mapbox/GoogleMaps/Osrm` (`Domain/Enums/RouteEnums.cs`) sont décoratifs.

**#20 — Deux types .NET distincts partagent le nom d'événement `delivery.assigned`.**
`shared/contracts/HBA.Dispatch.Contracts/IntegrationEvents/DispatchIntegrationEvents.cs:33` déclare `[HbaEvent("delivery.assigned")] DeliveryAssignedIntegrationEvent`. `services/delivery/delivery-service/src/HBA.Deliveries.Contracts/IntegrationEvents/DeliveryIntegrationEvents.cs:48` déclare un homonyme **sans attribut**, dont `KafkaEventNaming.EventType` (`shared/common/HBA.Shared.Infrastructure/Kafka/KafkaEventNaming.cs:41-50`) dérive également `delivery.assigned`. Deux producteurs différents pour un même nom d'événement métier — le consommateur `NotifyDriverOnDeliveryAssignedHandler` (`NotificationsModuleInstaller.cs:342`) est lié au type de `delivery-service`. Ambiguïté à lever avant que `dispatch-service` ne publie réellement.

**#21 — `CreateQuoteAsync` lève une exception non métier quand aucune règle tarifaire n'est active.**
`services/delivery/delivery-pricing-service/src/HBA.Delivery.Pricing.Infrastructure/Persistence/EfDeliveryPricingStore.cs:26-30` utilise `.FirstAsync(...)` : si toutes les règles sont désactivées (ce que `POST /rules/{id}/deactivate`, anonyme, permet — voir #6), l'appel lève `InvalidOperationException` → 500 sur tout devis, donc sur tout le checkout.

**#22 — Le déploiement Kubernetes ignore 12 services sur 19.**
`k8s/base/services/` ne contient que 13 dossiers (`catalog, commerce, communication, delivery, engagement, financial, food, identity, inventory, media, merchant, order, promotion, user`). Aucun des 6 satellites delivery, aucun BFF, ni `delivery-pricing-service`, ni `food-cart-service`, ni `food-order-service`. Et `k8s/base/common/configmap.yaml:59-60` pointe `SERVICES__FOODCART`/`FOODORDER` vers des services qui n'ont pas de `Deployment` → 503 garanti sur tout le parcours restauration en cluster.

**#23 — Le Driver BFF de la passerelle rapatrie tout l'historique du livreur à chaque appel.**
`apps/api-gateway/src/HBA.Gateway.Application/Bff/Driver/GetDriverMissionsHandler.cs:31-42` et `:65-70` appellent `ListMyMissionsAsync()` sans pagination ni filtre, puis tronquent dans la passerelle. Le détail d'une seule mission (`GetAsync`) charge la liste complète. Le défaut est documenté (`IDeliveryClient.cs:23-29`) mais non corrigé — et le coût croît avec l'ancienneté du livreur.

### LOW

**#24 — Assemblies `HBA.Delivery.Core.*`, namespaces `HBA.Deliveries.*`.** Les 5 projets de `delivery-service` portent le nouveau nom d'assembly et l'ancien namespace (`Delivery.cs:5`, `Program.cs:1-4`, `DeliveriesModuleInstaller.cs:32`…). Pas de rupture, mais divergence permanente entre nom d'assembly et nom de type.

**#25 — Protos dupliqués.** Chaque service porte un `proto/*.proto` local identique à celui de `shared/proto/<domaine>/v1/`, qui est le seul utilisé pour la génération (cf. `shared/contracts/HBA.Deliveries.Contracts.Grpc/*.csproj`). `diff` sur les blocs `rpc` de `services/delivery/delivery-service/proto/delivery.proto` et `shared/proto/delivery/v1/delivery.proto` : identiques aujourd'hui. Ils divergeront. `tracking-service` est le seul à ne pas avoir de copie locale — incohérence de traitement.

**#26 — `client-bff` : dossier `GrpcClients/` pour un client HTTP.** `apps/client-bff/src/HBA.ClientBff.Infrastructure/GrpcClients/MarketplaceOrder/ClientOrderGateway.cs` est un client `HttpClient` pur ; `HBA.ClientBff.Infrastructure.csproj` ne référence aucun paquet gRPC.

**#27 — `HBA.ClientBff.Domain`, `HBA.{Seller,Driver}Bff.{Domain,Application,Infrastructure}` : 7 projets vides** compilés à chaque build pour produire des assemblies sans type.

### INFO

- **Points corrects à préserver** : la machine à états de `Delivery` (transitions gardées, pas de repli `_ =>`), la séparation `DriverAccountStatus` / `DriverAvailability`, le figement du couple `DriverEarning` + `DriverShareRate` à la remise, la comparaison de PIN en temps constant, l'argent en `decimal` (delivery) et `long` (pricing) — **aucun `double` pour un montant dans tout `services/delivery/` ni `apps/`**, l'index partiel `ix_deliveries_awaiting_driver`, l'unicité `ux_deliveries_reference_source`, l'ordre des routes YARP (notamment `payments-webhooks` en `Order: 5`), le `[Authorize(DriverOnly)]` posé au niveau de la classe `DriverController`.
- **Aucun `TODO`, `FIXME`, `HACK` ni `NotImplementedException`** dans `services/delivery/` et `apps/`. Les manques ne sont pas signalés dans le code : ils sont invisibles à la recherche. Les seuls aveux sont des `501` explicites dans `apps/client-bff/src/HBA.ClientBff.Api/Endpoints/ClientEndpoints.cs:22-30` et les en-têtes « SQUELETTE » des deux BFF vides.
- **gRPC interne** : protégé par `InternalCallServerInterceptor` à clé partagée (`shared/common/HBA.Shared.Hosting/Grpc/GrpcHostExtensions.cs:60-63`), pas par le pipeline d'autorisation. Le `AllowAnonymous()` de `MapInternalGrpcService` (l.111) est donc correct — mais c'est la **seule** serrure des satellites, dont la surface HTTP reste, elle, entièrement ouverte.
