# Implementation Status

Last verified date: 2026-08-20
**Relu et corrigé le 2026-08-28 — trois lignes portaient sur des composants
retirés du dépôt.**

> ## CE QUI A ÉTÉ INVALIDÉ PAR DES DÉCISIONS POSTÉRIEURES AU 20 AOÛT
>
> Ce fichier décrivait l'avancement de travaux sur des services qui n'existent
> plus. Un tableau de suivi qui pointe des composants retirés est pire qu'un
> tableau absent : il fait planifier du travail sans objet.
>
> | Ligne d'origine | Ce qui s'est passé depuis |
> |---|---|
> | **P0.5 — Dispatch persistence/scoring** | `dispatch-service` a été **retiré du dépôt** (D42, 27 août). Il dupliquait une affectation que delivery-service fait déjà, avec deux identifiants codés en dur et sans base. La ligne n'a plus de sujet — ce n'est pas un travail à faire, c'est un travail annulé. Ce qui subsiste est une DETTE nommée dans D42 : porter `manual-assign`, `retry` et la vue d'affectation dans delivery-service. |
> | **P0.4 — Driver persistence/JWT** | La moitié « persistence » est FAITE, et l'était déjà : `DriverDbContext` existe, avec deux migrations (`InitialDrivers`, `JournalDAuditDriver`). Seule la partie JWT n'a pas été revérifiée ici — la ligne reste donc ouverte, mais pour une raison plus étroite que ce qu'elle annonce. |
> | **P0.7 et « BFF Status »** | Les trois BFF — client, vendeur, livreur — ont été **supprimés** en D38 : la passerelle EST le BFF. `apps/` ne contient plus que `api-gateway`. Toute la section « BFF Status » plus bas décrit des routes qui n'existent plus dans aucun processus. |
>
> **Ce qui N'A PAS été revérifié le 28 août**, et reste donc tel que le 20 :
> P0.1, P0.2, P0.3, P0.6, et les sections « Contract Versions Changed » et
> « Broken Sagas Remaining » hors des points ci-dessus. Rien n'a été promu de
> `PARTIAL` à `DONE` — aucun test n'a tourné.

| Lot | Status | PR/Commit | Migrations | Tests | Remaining |
|-----|--------|-----------|------------|-------|-----------|
| P0.1 | PARTIAL | local changes | `20260824000000_AddOrderPaymentId` | Order builds green; ReturnRefund builds green; Order authorization tests green; Merchants integration tests blocked by existing duplicate `TraceParent` migration | Full CreateReturn E2E with live Order DB not executed; delivered timestamp and already-returned counters still approximated |
| P0.2 | PARTIAL | local changes | `20260824010000_AddPaymentRefunds` | Financial build green; ReturnRefund build green; Payments tests green 20/20 | Refund gRPC branché et idempotent côté DB; reconciliation PSP après timeout reste limitée aux statuts persistés, pas de polling provider universel |
| P0.3 | TODO | - | - | - | Real PSP adapter |
| P0.4 | PARTIAL | - | `20260905000100_InitialDrivers`, `20260828000800_JournalDAuditDriver` | non rejoués | La persistance EXISTE (`DriverDbContext` + 2 migrations) — la ligne l'ignorait. Reste la partie JWT, non revérifiée. |
| P0.5 | ~~TODO~~ **ANNULÉ** | D42 | - | - | `dispatch-service` retiré du dépôt le 27 août. Reste la dette D42 : porter `manual-assign`, `retry` et la vue d'affectation dans delivery-service. |
| P0.6 | TODO | - | - | - | Delivery security |
| P0.7 | PARTIAL | local changes | none | Client BFF build green; docker-compose config green; no Client BFF DB reference found | Client BFF lot 1 exposes home + marketplace order list/read/create facade; CreateOrder still uses delegated HTTP because Order gRPC has no CreateOrder RPC; Seller/Driver/Admin BFF lots still open |

## Current Production Blockers

- P0.1 is code-complete for build, but not DONE because contract/integration/E2E acceptance is not green.
- P0.2 Payment Refund gRPC is implemented for build and DB idempotency, but remains PARTIAL until provider-level refund reconciliation is covered end-to-end.
- P0.3 PSP production/sandbox real adapters are still missing.
- P0.4 : la persistance livreur existe ; seule la partie JWT reste ouverte.
- ~~P0.5 Dispatch~~ : **sans objet**, le service a été retiré (D42).
- P0.6 Delivery endpoint security remains open.

## Broken Sagas Remaining

- Return/refund can call Payment.RefundPayment via gRPC; real provider reconciliation remains tied to P0.3/provider capabilities.
- Driver saga : dépend encore de P0.6 et de la partie JWT de P0.4. P0.5 ne la
  bloque plus — l'affectation vit dans delivery-service depuis D42.
- Client BFF marketplace order facade is available for lot 1, but Food/tracking/return/review and Seller/Driver/Admin BFF persona flows remain unavailable until the next BFF lots.

## Contract Versions Changed

- `shared/proto/order/v1/order.proto`: additive RPC `GetOrderReturnContext`.
- `HBA.Orders.Contracts.IOrderingModuleApi`: additive `GetOrderReturnContextAsync`.
- `HBA.Ordering.Contracts.IOrderingModuleApi`: additive `GetOrderReturnContextAsync`.
- `shared/proto/financial/v1/financial.proto`: additive fields on `RefundPaymentRequest` and `FinancialOperationResponse` for idempotent refund context.

## BFF Status — SECTION PÉRIMÉE, CONSERVÉE POUR MÉMOIRE

> **AUCUNE DE CES ROUTES N'EXISTE.** Les trois BFF ont été supprimés en D38 : la
> passerelle EST le BFF, et `apps/` ne contient plus qu'`api-gateway`. Ce qui
> suit décrit l'état du 20 août et n'est plus vrai d'aucun processus. La section
> est gardée parce que le « Contract gap » ci-dessous, lui, reste vrai : le proto
> `order/v1` n'a toujours pas de RPC `CreateOrder`.


- Client BFF lot 1: `GET /api/v1/client/home`, `GET /api/v1/client/orders`, `POST /api/v1/client/orders`, `GET /api/v1/client/orders/{id}` implemented.
- Client BFF routes outside lot 1 return explicit `501 NOT_IMPLEMENTED` instead of a health-only skeleton.
- Contract gap: `shared/proto/order/v1/order.proto` has no `CreateOrder` RPC. The BFF delegates creation to the Order service owner endpoint and propagates JWT/correlation/idempotency headers until the RPC exists.
- Seller BFF, Driver BFF and Admin BFF still require their dedicated lots.
