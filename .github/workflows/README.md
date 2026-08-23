# Workflows

**Un workflow par service.** Le monolithe se construit d'un bloc ; c'est
exactement ce qu'on quitte. Un pipeline unique annulerait le seul gain immédiat de
la découpe : corriger la restauration sans redéployer les paiements.

À prévoir :
- `ci-<service>.yml` — build, tests, image, par service
- `ci-shared.yml` — contrats et schémas ; **le seul qui doit reconstruire tous les consommateurs**
- `cd-<environnement>.yml` — promotion d'images déjà construites, jamais de rebuild
