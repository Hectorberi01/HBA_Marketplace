# Observabilité

Prometheus, Grafana, Loki, Jaeger.

**CE DOSSIER EST UNE CONDITION DE LA DÉCOUPE, PAS UN CONFORT.**

Dans un monolithe, une pile d'appel suffit à comprendre un incident. Distribué, un repas qui n'arrive pas peut venir de six services, et sans traçage distribué personne ne saura lequel.

Les cas déjà identifiés qui n'ont AUCUNE visibilité aujourd'hui : un message d'outbox en lettre morte, un repas prêt sans livreur, un escrow non libéré, un remboursement refusé. Chacun ne se manifeste que par une absence — et une absence ne déclenche pas d'alerte tant que personne ne la mesure.
