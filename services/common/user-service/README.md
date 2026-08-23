# User Service

Profils, adresses, contacts, préférences, moyens de paiement.

**Modules actuels :** `../../../../src/Modules/User/`

## À savoir avant d'extraire

**Déjà extrait d'Identity une fois**, et l'opération a laissé une trace utile : `UserSummary` porte encore `FirstName`/`LastName` en doublon du profil, avec dix-sept appelants à reprendre. C'est une tâche ouverte du monolithe, et elle vaut mieux d'être soldée AVANT l'extraction — sinon le doublon devient une divergence entre deux services.

**Les adresses portent la position GPS**, et c'est ce dont dépend tout le chiffrage de livraison. Une adresse sans coordonnées rend une course de repas impossible à calculer.

## Squelette attendu

```
user-service/
├── src/
│   ├── api/             Points d'entrée HTTP/gRPC, consommateurs d'événements
│   ├── domain/          Agrégats, invariants, événements de domaine
│   ├── application/     Commandes, requêtes, ports
│   └── infrastructure/  Persistance, adaptateurs, publication d'événements
├── tests/
├── Dockerfile
└── README.md
```
