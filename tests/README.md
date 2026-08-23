# Tests transverses

Ce dossier ne contient PAS les tests unitaires : ils vivent dans chaque service,
avec le code qu'ils protègent.

- `integration/` — un service contre ses vraies dépendances (base, broker, stockage).
- `e2e/` — un parcours complet à travers plusieurs services.
- `load/` — tenue en charge et comportement en saturation.

**C'est ici que se paie la découpe.** Dans le monolithe, un test unitaire suffit à
vérifier qu'une commande payée atteint la cuisine : tout est dans le même processus.
Une fois les services séparés, la même garantie demande un test d'intégration avec
un broker réel — et sans lui, personne ne s'apercevra que le message n'arrive plus.
