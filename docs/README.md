# Documentation

Décisions d'architecture, contrats publics, runbooks d'exploitation.

## Guides principaux

- [Guide de deploiement Kubernetes dev/staging/prod](/Users/hector/Documents/HBA/docs/GUIDE_DEPLOIEMENT_K8S_DEV_STAGING_PROD.md)

**Ce qui doit y être écrit AVANT la première extraction :**

- **La politique de cohérence.** Quelles opérations acceptent d'être éventuellement
  cohérentes, et lesquelles ne le peuvent pas. Aujourd'hui le checkout écrit dans
  trois modules sous une seule transaction ; découpé, il faut décider ce qui se
  compense et ce qui se refuse.
- **La convention de nommage des événements.** Le monolithe a des noms internes
  (`OrderConfirmedIntegrationEvent`) ; un bus partagé a besoin d'un nom stable,
  versionné, indépendant du langage.
- **La politique de lettres mortes.** L'outbox in-process abandonne après un
  plafond de tentatives. Distribuée, il faut dire qui regarde la file, et sous
  quel délai.

Les audits déjà écrits sur le monolithe (`../../AUDIT_*.md`) sont le meilleur point
de départ : ils recensent les couplages réels, pas ceux qu'on suppose.
