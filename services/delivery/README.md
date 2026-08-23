# `delivery/` — l'acheminement

| Service | Rôle |
|---|---|
| `delivery-service` | Demandes de livraison, affectation, livreurs, suivi, itinéraires, preuves, tarification |

**UN SEUL SERVICE LÀ OÙ LE DIAGRAMME EN MONTRE SEPT** (*Delivery, Dispatch,
Driver, Tracking, Route, Proof of Delivery, Delivery Pricing*).

Même raison que pour `food/` : l'affectation d'un livreur lit sa position, sa
disponibilité, sa charge et le tarif dans la même décision. Sept services
signifieraient six appels réseau pour affecter une course — sur un réseau béninois,
et pendant qu'un plat refroidit.
