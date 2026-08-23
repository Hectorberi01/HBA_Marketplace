# return-refund-service

**Return & Refund Service (Retours, Remboursements)**

## Etat

Service Marketplace Return & Refund extrait en couches .NET 9 selon le cahier de
charge : Domain, Application, Infrastructure, API, contrats gRPC et schemas Kafka.

Le service possede le dossier de retour et la decision de remboursement. Les
commandes, paiements, stocks, medias et livraisons restent appeles via ports gRPC
sortants ; aucun acces direct aux bases des autres domaines n'est introduit.

## Structure

```
return-refund-service/
├── src/
│   ├── HBA.Marketplace.ReturnRefund.Domain/           entites metier
│   ├── HBA.Marketplace.ReturnRefund.Application/      cas d'usage, DTO, ports
│   ├── HBA.Marketplace.ReturnRefund.Infrastructure/   persistance, Kafka, gRPC
│   └── HBA.Marketplace.ReturnRefund.Api/              endpoints HTTP et gRPC
├── contracts/
│   ├── grpc/return_refund.proto
│   └── kafka/
│       ├── return-events-v1.json
│       └── refund-events-v1.json
├── tests/
├── Dockerfile
├── Directory.Build.props
└── README.md
```
