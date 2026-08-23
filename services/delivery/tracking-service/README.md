# Tracking Service

> ## ÉTAT : SQUELETTE — CE SERVICE N'EST PAS IMPLÉMENTÉ
>
> **Maquette en mémoire : ETA codé en dur, suivi non protégé.**
>
> Ce dossier existe pour que l'arborescence corresponde à l'architecture cible.
> Le code présent est une **maquette en mémoire** : les positions sont gardées dans un dictionnaire et perdues au redémarrage.
>
> Concrètement, aujourd'hui :
> - l'ETA est la constante 540 s ;
> - le `driverId` est lu dans le CORPS de la requête, et le jeton de flux est fabriqué sans jamais être vérifié : **le suivi n'est pas réservé au livreur affecté** (ISSUE-058) ;
> - aucune authentification ;
> - `IDriverLocationCache` de `delivery-service` n'est alimenté par personne — c'est pourquoi aucune course n'est jamais proposée (ISSUE-029).
>
> Ce bandeau existe parce que rien d'autre ne le disait. Les projets `Domain`
> portent bien « ce projet est volontairement vide », mais le README décrivait le
> service au présent, comme s'il fonctionnait — et un audit l'a d'abord compté
> comme fait. Voir `docs/audit/2026-08-21-complet/` (D-3 du plan de correction :
> finir, retirer, ou assumer).
