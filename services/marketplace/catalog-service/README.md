# Catalog Service

Produits, catégories, marques, attributs et variantes, recherche.

**Modules actuels :** `../../../../src/Modules/Catalog/`, `../../../../src/Modules/Product/`, `../../../../src/Modules/Search/`

## À savoir avant d'extraire

**CATALOG ET PRODUCT SE RECOUVRENT, ET C'EST LE PLUS GROS NŒUD DE LA DÉCOUPE.**

Le monolithe porte deux modèles de produit : `Catalog` (l'historique) et `Product` (le nouveau, avec offres et boutiques). La bascule a été faite appelant par appelant — Cart lit déjà Products, les BFF vendeur aussi — mais Catalog reste vivant parce que Search, Notifications et plusieurs écrans le lisent encore.

**Extraire ce service avant d'avoir soldé cette dualité, c'est figer deux vérités du même produit dans un service qu'on ne pourra plus corriger sans coordination.** C'est le seul point de la carte où l'ordre d'extraction n'est pas négociable : le ménage d'abord.

`Search` rejoint le même service parce que l'index se reconstruit depuis le catalogue ; l'en séparer imposerait un flux d'indexation avant même d'avoir un catalogue stable.

## Squelette attendu

```
catalog-service/
├── src/
│   ├── api/             Points d'entrée HTTP/gRPC, consommateurs d'événements
│   ├── domain/          Agrégats, invariants, événements de domaine
│   ├── application/     Commandes, requêtes, ports
│   └── infrastructure/  Persistance, adaptateurs, publication d'événements
├── tests/
├── Dockerfile
└── README.md
```
