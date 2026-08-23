# Inventory Service

Stock et entrepôts, réservation, mouvements, disponibilité, ajustements.

**Modules actuels :** `../../../../src/Modules/Inventory/`

## À savoir avant d'extraire

**La réservation de stock est le premier endroit où la découpe fait mal.**

Aujourd'hui, `PlaceOrder` réserve le stock dans la même transaction que la création de la commande : si la réservation échoue, rien n'est écrit. Séparés, c'est une saga — réserver, puis compenser si le paiement échoue — et la compensation doit exister avant l'extraction, pas après.

**`TryReserveAsync` répond VRAI pour un SKU non suivi.** C'est voulu, et c'est ce qui a laissé passer les commandes de repas dans la chaîne marchandise pendant tout un développement. Un service distant amplifierait ce silence.

## Squelette attendu

```
inventory-service/
├── src/
│   ├── api/             Points d'entrée HTTP/gRPC, consommateurs d'événements
│   ├── domain/          Agrégats, invariants, événements de domaine
│   ├── application/     Commandes, requêtes, ports
│   └── infrastructure/  Persistance, adaptateurs, publication d'événements
├── tests/
├── Dockerfile
└── README.md
```
