# Tableaux de bord

Tout fichier `.json` déposé ici est chargé par Grafana au démarrage, dans le
dossier « HBA » — voir `../provisioning/dashboards/dashboards.yml`.

**Ce dossier doit exister même vide.** `docker-compose.observability.yml` le
monte en lecture seule ; s'il est absent du dépôt, Docker le crée à la volée avec
les droits de root, et Grafana — qui tourne sans privilèges — échoue à le lire
avec une erreur de permission qui ne dit rien du dossier manquant. Git ne suivant
pas les dossiers vides, ce README est ce qui le maintient en place.

**Ce dossier contenait `datasources.yml`, qui n'avait rien à y faire.** Il
déclarait le *fournisseur* de tableaux de bord, et vivait hors de
`provisioning/` — donc Grafana ne le lisait pas. Le fichier des *sources*, lui,
était dans `provisioning/dashboards/`. Les deux étaient intervertis, et la pile
aurait démarré sans aucune source ni aucun tableau de bord. Voir l'encadré en
tête de `../provisioning/datasources/datasources.yml`.

## Ce qu'il reste à construire

Aucun tableau de bord n'est versionné pour l'instant. Les trois qui manquent le
plus, dans l'ordre où ils serviront :

1. **Santé par service** — taux d'erreur, p95, débit, par `service_name`.
2. **Outbox** — messages en attente, tentatives, et surtout **lettres mortes** :
   chacune est une perte métier réelle (un gain vendeur jamais crédité, un stock
   jamais libéré) et ne se manifeste aujourd'hui que par un journal `Critical`.
3. **Consommation Kafka** — décalage par groupe et par sujet, et événements
   abandonnés après reprises.

Les trois se construisent dans l'interface puis s'exportent en JSON ici : le
provisionnement est en `allowUiUpdates: true` précisément pour permettre ce
va-et-vient.
