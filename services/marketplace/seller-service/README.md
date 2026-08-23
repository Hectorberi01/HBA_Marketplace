# Merchant Service

Vendeurs et restaurateurs, dossiers KYB, boutiques, documents, paramètres.

**Modules actuels :** `../../../../src/Modules/Sellers/`

## À savoir avant d'extraire

**C'est ce service qui porte l'argent des marchands.** `SellerId` est la clé de tout le reversement : portefeuille, demande de retrait, payout Mobile Money. Un restaurant lui-même désigne un dossier vendeur (`Restaurant.PayoutSellerId`) — c'est par lui qu'il est payé.

**Les pièces KYB sont des données personnelles sensibles.** Elles ne vivent plus ici : le service média les stocke dans un bucket privé, et ce service n'en garde que l'identifiant. Une extraction ne doit pas ramener les octets.

## Squelette attendu

```
merchant-service/
├── src/
│   ├── api/             Points d'entrée HTTP/gRPC, consommateurs d'événements
│   ├── domain/          Agrégats, invariants, événements de domaine
│   ├── application/     Commandes, requêtes, ports
│   └── infrastructure/  Persistance, adaptateurs, publication d'événements
├── tests/
├── Dockerfile
└── README.md
```
