# `marketplace/` — la vente d'articles

| Service | Rôle |
|---|---|
| `catalog-service` | Produits, déclinaisons, mises en vente, catégories, marques |
| `merchant-service` | Vendeurs, boutiques, dossier KYB, compte de reversement |
| `inventory-service` | Stock, réservations, lieux d'expédition |

**Le panier et la commande ne sont PAS ici** — voir `../common/`. Ils portent les
deux natures de ligne, marchandise et repas, et les ranger ici ferait croire que la
restauration ne s'en sert pas.

**`inventory-service` sert aussi la restauration** : `FulfillmentLocation` est le
lieu d'où part un colis *et* celui que rattache un restaurant. Il reste néanmoins
ici, parce que le stock — sa raison d'être — n'existe que pour la marchandise : un
plat ne se décrémente pas, il se retire pour la journée.
