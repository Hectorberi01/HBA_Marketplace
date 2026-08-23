# Commerce Service

Panier, liste d'envies, promotions et coupons, tarification, points de fidélité, campagnes.

**Modules actuels :** `../../../../src/Modules/Cart/`, `../../../../src/Modules/Wishlist/`, `../../../../src/Modules/Pricing/`, `../../../../src/Modules/Loyalty/`, `../../../../src/Modules/Marketing/`

## À savoir avant d'extraire

Tout ce qui se passe **avant** la commande, et qui n'engage donc rien de définitif — d'où la base Redis pour le panier.

**Le panier porte deux natures de ligne** : marchandise et repas, et il refuse de les mélanger. Une commande mixte devrait être à la fois cuisinée et expédiée, avec deux délais de livraison incompatibles.

**La tarification décide de l'argent, et le panier ne fait que l'afficher.** Un prix de panier est une estimation ; le prix facturé est recalculé à la commande. Séparer Pricing du panier casserait ce lien — c'est pourquoi les deux restent dans le même service.

## Squelette attendu

```
commerce-service/
├── src/
│   ├── api/             Points d'entrée HTTP/gRPC, consommateurs d'événements
│   ├── domain/          Agrégats, invariants, événements de domaine
│   ├── application/     Commandes, requêtes, ports
│   └── infrastructure/  Persistance, adaptateurs, publication d'événements
├── tests/
├── Dockerfile
└── README.md
```
