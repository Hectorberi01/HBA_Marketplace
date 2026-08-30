# Observabilite HBA

Ce dossier contient uniquement la partie propre a la plateforme : le collecteur
OTLP interne que les services .NET appellent via `OPENTELEMETRY__ENDPOINT`.

Les composants tiers restent installes hors base Kustomize :

- kube-prometheus-stack pour Prometheus, Alertmanager et Grafana ;
- Loki pour les journaux ;
- Tempo pour les traces.

Le collecteur expose les metriques recues sur `otel-collector:8889`. Prometheus
peut les scraper directement ou via un `ServiceMonitor` installe dans le chart de
supervision. Les traces et journaux sont gardes en export `debug` tant que Tempo
et Loki ne sont pas poses ; cela evite une fausse configuration qui remplirait les
logs de tentatives de connexion vers des services absents.
