# BFF Lot 1 - Client BFF Order

## Perimetre

Premier lot du cahier BFF : Client BFF avec contexte utilisateur, lecture de commandes Marketplace et creation de commande Marketplace.

Routes livrees :

- `GET /api/v1/client/home`
- `GET /api/v1/client/orders`
- `POST /api/v1/client/orders`
- `GET /api/v1/client/orders/{id}`

Routes Client BFF du cahier hors lot 1 exposees en `501 NOT_IMPLEMENTED` :

- produits, restaurants, paniers, checkout preview, food, tracking, returns, reviews.

## Fichiers touches

- `apps/client-bff/src/HBA.ClientBff.Api/Program.cs`
- `apps/client-bff/src/HBA.ClientBff.Api/Endpoints/ClientEndpoints.cs`
- `apps/client-bff/src/HBA.ClientBff.Api/Middleware/BffExceptionMiddleware.cs`
- `apps/client-bff/src/HBA.ClientBff.Application/Abstractions/IClientOrderGateway.cs`
- `apps/client-bff/src/HBA.ClientBff.Application/DTOs/ClientHomeResponse.cs`
- `apps/client-bff/src/HBA.ClientBff.Infrastructure/Configuration/DownstreamServicesOptions.cs`
- `apps/client-bff/src/HBA.ClientBff.Infrastructure/GrpcClients/MarketplaceOrder/ClientOrderGateway.cs`

## RPC et contrats constates

Le contrat gRPC Order existant expose `GetOrder`, `ListOrdersByBuyer`, `GetOrderReturnContext`, `GetSellerSalesCount`.
Il n'expose pas encore `CreateOrder`.

Pour ne pas dupliquer la logique metier dans le BFF, la creation est deleguee au service proprietaire Order via son endpoint interne HTTP `POST /api/orders`, avec propagation du JWT, `X-Correlation-ID`, `X-Request-ID` et `Idempotency-Key`.

Dette documentee : ajouter un RPC proprietaire `CreateOrder` dans Order service, puis remplacer l'adaptateur HTTP par le client gRPC dedie.

## Risques

- Le relais HTTP garde la bonne propriete metier, mais ne respecte pas encore le standard final "gRPC pour synchrones inter-services" sur la mutation create order.
- Les erreurs du service Order sont remappees en enveloppe BFF generique pour eviter de figer les DTO internes du service dans le contrat mobile.
- Les routes hors lot rendent explicitement `501` pour eviter des endpoints silencieux ou des squelettes health-only.

## Tests prevus

- Build `HBA.ClientBff.Api`.
- Verification statique : aucun `DbContext` ou connection string dans Client BFF.
- Tests integration a ajouter au lot suivant avec fake Order server : ownership, auth, downstream timeout, idempotency.
