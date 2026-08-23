# Communication Service

Notifications sortantes (e-mail, SMS, push), messagerie entre acheteurs et vendeurs, gabarits, préférences.

**Modules actuels :** `../../../../src/Modules/Notifications/`, `../../../../src/Modules/Messaging/`

## À savoir avant d'extraire

**Purement consommateur d'événements** — il n'est appelé par presque personne et ne bloque aucune transaction. C'est ce qui en fait un candidat précoce à l'extraction.

**UNE NOTIFICATION N'EST PAS IDEMPOTENTE.** Un rejeu d'outbox renvoie un e-mail. Le défaut est réel et connu : quand un consommateur de « commande confirmée » échouait, le message entier était rejoué et l'acheteur recevait une dizaine de « Commande confirmée » pour un repas qu'il n'aurait jamais. Chaque envoi a besoin de sa propre garde.

**Cinq événements de restauration n'ont aucun consommateur** — reçue, acceptée, en préparation, enlevée, annulée. Sur un service où l'attente se compte en minutes, c'est le manque le plus visible pour le client.

## Squelette attendu

```
communication-service/
├── src/
│   ├── api/             Points d'entrée HTTP/gRPC, consommateurs d'événements
│   ├── domain/          Agrégats, invariants, événements de domaine
│   ├── application/     Commandes, requêtes, ports
│   └── infrastructure/  Persistance, adaptateurs, publication d'événements
├── tests/
├── Dockerfile
└── README.md
```
