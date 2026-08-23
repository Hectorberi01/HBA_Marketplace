# Identity Service

Authentification et autorisation, comptes, rôles et permissions, JWT/OAuth/MFA, sessions.

**Modules actuels :** `../../../../src/Modules/Identity/`

## À savoir avant d'extraire

**Le meilleur candidat après Media, et pour une raison précise :** aucun autre module n'écrit dans Identity. Toutes les dépendances sont entrantes — on vérifie un jeton, on attribue un rôle — et une dépendance entrante se remplace par un appel réseau sans rien réécrire.

**Le durcissement est déjà là** : verrouillage après N échecs, limitation des essais MFA, détection de rejeu de jeton de rafraîchissement. Ces compteurs vivent en base ; distribués, ils devront tenir sous plusieurs instances — c'est Redis, pas PostgreSQL.

## Squelette attendu

```
identity-service/
├── src/
│   ├── api/             Points d'entrée HTTP/gRPC, consommateurs d'événements
│   ├── domain/          Agrégats, invariants, événements de domaine
│   ├── application/     Commandes, requêtes, ports
│   └── infrastructure/  Persistance, adaptateurs, publication d'événements
├── tests/
├── Dockerfile
└── README.md
```
