# Order Service

Commandes (marchandise et restauration), commandes par vendeur, statuts et historique, retours, litiges.

**Modules actuels :** `../../../../src/Modules/Ordering/`, `../../../../src/Modules/Returns/`, `../../../../src/Modules/Disputes/`

## À savoir avant d'extraire

Le cycle de vie complet d'une commande, du checkout au litige. Retours et litiges rejoignent la commande parce qu'ils la modifient : un retour contre-passe des gains, un litige gèle un règlement.

**UNE LIGNE DE COMMANDE PORTE SA NATURE**, et ce discriminant décide de tout ce qui suit le paiement : réserver du stock et expédier, ou envoyer en cuisine et livrer chaud. Son absence ne produisait aucune erreur — juste des commandes payées que personne ne préparait.

**La saga du checkout est ici.** C'est le service qui orchestre : réservation, paiement, confirmation, compensation. C'est aussi celui dont l'extraction demandera le plus de travail sur la cohérence.

## Squelette attendu

```
order-service/
├── src/
│   ├── api/             Points d'entrée HTTP/gRPC, consommateurs d'événements
│   ├── domain/          Agrégats, invariants, événements de domaine
│   ├── application/     Commandes, requêtes, ports
│   └── infrastructure/  Persistance, adaptateurs, publication d'événements
├── tests/
├── Dockerfile
└── README.md
```
