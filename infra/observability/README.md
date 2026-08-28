# Observabilité

> ## AUCUN DE CES FICHIERS N'EST LU PAR QUOI QUE CE SOIT.
>
> Leur unique consommateur était `infra/docker/compose.monitoring.yml`, retiré du
> dépôt le 27 août avec tout `infra/docker/`. Personne ne monte plus ces
> configurations, aucune pile ne démarre à partir d'elles, et rien dans le dépôt
> ne les référence. Ce README est en tête de dossier pour que la lecture ne
> commence pas par la section suivante, qui dit pourquoi l'observabilité compte —
> et qui reste vraie, mais ne décrit pas ce que ce dossier FAIT aujourd'hui.

## Ce que contient réellement ce dossier, fichier par fichier

| Fichier | Lecteur avant le 27 août | Transférable vers k8s ? |
|---|---|---|
| `prometheus/prometheus.yml` | `compose.monitoring.yml` | **Non tel quel.** Sous kube-prometheus-stack, la découverte des cibles se fait par `ServiceMonitor`, pas par `scrape_configs`. Le contenu transférable est la LISTE de ce qu'on veut mesurer, pas le fichier. |
| `grafana/provisioning/datasources/datasources.yml` | `compose.monitoring.yml` | **Non.** Le chart provisionne ses propres sources de données. |
| `grafana/provisioning/dashboards/dashboards.yml` | `compose.monitoring.yml` | **Non** — il pointe le dossier ci-dessous, qui est vide. |
| `loki/loki.yml` | `compose.monitoring.yml` | **Non.** Le chart Loki a ses propres valeurs. |
| `otel/otel-collector.yml` | `compose.monitoring.yml` | **En partie.** C'est le seul dont le contenu se transpose : les pipelines et exportateurs restent les mêmes une fois posés dans un ConfigMap. Voir `OPENTELEMETRY__ENDPOINT`, aujourd'hui vide dans le ConfigMap de la plateforme. |
| `tempo/tempo.yml` | **aucun, jamais** — le compose ne l'a jamais monté | Non. |
| `grafana/dashboards/` | **aucun, jamais** — ne contient qu'un README | Sans objet : il n'y a aucun tableau de bord dedans. |

Deux des sept n'ont donc jamais eu de lecteur, avant même le retrait.

## Pourquoi le dossier n'est pas supprimé

Parce que la liste de ce qu'il faut mesurer est le seul endroit du dépôt où elle
est écrite, et qu'elle ne dépend pas de l'outil qui la lit. Supprimer ces
fichiers ferait perdre cette liste ; la garder en prétendant qu'elle tourne
serait pire. On la garde en disant qu'elle ne tourne pas.

La suite est dans `k8s/base/observability/`, qui ne contient qu'un README : la
supervision viendra par Helm (kube-prometheus-stack, Loki, Tempo), avec des
`ServiceMonitor` et les règles d'alerte du §17. Le jour où ce lot sera fait, ce
dossier-ci devra être **retiré**, pas migré — sauf `otel/otel-collector.yml`,
dont le contenu a une destination.

## Ce que l'absence d'observabilité coûte, et qui n'a pas changé

Dans un monolithe, une pile d'appel suffit à comprendre un incident. Distribué,
un repas qui n'arrive pas peut venir de six services, et sans traçage distribué
personne ne saura lequel.

Les cas déjà identifiés qui n'ont AUCUNE visibilité aujourd'hui : un message
d'outbox en lettre morte, un repas prêt sans livreur, un escrow non libéré, un
remboursement refusé. Chacun ne se manifeste que par une absence — et une absence
ne déclenche pas d'alerte tant que personne ne la mesure.

**Et il faut le lire en sachant que la pile qui devait les mesurer n'existe plus
nulle part.** Ce n'était déjà qu'une pile de développement, montée par compose
sur un poste ; rien n'a jamais supervisé un déploiement.
