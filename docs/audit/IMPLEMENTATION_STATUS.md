# Implementation Status

Last verified date: 2026-08-20

| Lot | Status | PR/Commit | Migrations | Tests | Remaining |
|-----|--------|-----------|------------|-------|-----------|
| P0.1 | PARTIAL | local changes | `20260824000000_AddOrderPaymentId` | Order builds green; ReturnRefund builds green; Order authorization tests green; Merchants integration tests blocked by existing duplicate `TraceParent` migration | Full CreateReturn E2E with live Order DB not executed; delivered timestamp and already-returned counters still approximated |
| P0.2 | PARTIAL | local changes | `20260824010000_AddPaymentRefunds` | Financial build green; ReturnRefund build green; Payments tests green 20/20 | Refund gRPC branché et idempotent côté DB; reconciliation PSP après timeout reste limitée aux statuts persistés, pas de polling provider universel |
| P0.3 | TODO | - | - | - | Real PSP adapter |
| P0.4 | TODO | - | - | - | Driver persistence/JWT |
| P0.5 | TODO | - | - | - | Dispatch persistence/scoring |
| P0.6 | TODO | - | - | - | Delivery security |
| P0.7 | PARTIAL | local changes | none | Client BFF build green; docker-compose config green; no Client BFF DB reference found | Client BFF lot 1 exposes home + marketplace order list/read/create facade; CreateOrder still uses delegated HTTP because Order gRPC has no CreateOrder RPC; Seller/Driver/Admin BFF lots still open |

## Current Production Blockers

- P0.1 is code-complete for build, but not DONE because contract/integration/E2E acceptance is not green.
- P0.2 Payment Refund gRPC is implemented for build and DB idempotency, but remains PARTIAL until provider-level refund reconciliation is covered end-to-end.
- P0.3 PSP production/sandbox real adapters are still missing.
- P0.4/P0.5 Driver and Dispatch remain non-durable.
- P0.6 Delivery endpoint security remains open.

## Broken Sagas Remaining

- Return/refund can call Payment.RefundPayment via gRPC; real provider reconciliation remains tied to P0.3/provider capabilities.
- Driver saga remains broken for production until P0.4/P0.5/P0.6.
- Client BFF marketplace order facade is available for lot 1, but Food/tracking/return/review and Seller/Driver/Admin BFF persona flows remain unavailable until the next BFF lots.

## Contract Versions Changed

- `shared/proto/order/v1/order.proto`: additive RPC `GetOrderReturnContext`.
- `HBA.Orders.Contracts.IOrderingModuleApi`: additive `GetOrderReturnContextAsync`.
- `HBA.Ordering.Contracts.IOrderingModuleApi`: additive `GetOrderReturnContextAsync`.
- `shared/proto/financial/v1/financial.proto`: additive fields on `RefundPaymentRequest` and `FinancialOperationResponse` for idempotent refund context.

## BFF Status

- Client BFF lot 1: `GET /api/v1/client/home`, `GET /api/v1/client/orders`, `POST /api/v1/client/orders`, `GET /api/v1/client/orders/{id}` implemented.
- Client BFF routes outside lot 1 return explicit `501 NOT_IMPLEMENTED` instead of a health-only skeleton.
- Contract gap: `shared/proto/order/v1/order.proto` has no `CreateOrder` RPC. The BFF delegates creation to the Order service owner endpoint and propagates JWT/correlation/idempotency headers until the RPC exists.
- Seller BFF, Driver BFF and Admin BFF still require their dedicated lots.
