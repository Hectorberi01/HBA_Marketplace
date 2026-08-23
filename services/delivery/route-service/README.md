# Route Service

> ## ÉTAT : SQUELETTE — CE SERVICE N'EST PAS IMPLÉMENTÉ
>
> **Maquette en mémoire : Haversine et vitesse constante.**
>
> Ce dossier existe pour que l'arborescence corresponde à l'architecture cible.
> Le code présent est une **maquette en mémoire** : le calcul d'itinéraire est une distance à vol d'oiseau divisée par 5,8 m/s.
>
> Concrètement, aujourd'hui :
> - `IRouteProvider` est déclaré, jamais implémenté, jamais enregistré ;
> - aucun `DbContext`, aucun processeur d'outbox, aucune authentification ;
> - les ETA rendus ne valent rien en zone urbaine.
>
> Ce bandeau existe parce que rien d'autre ne le disait. Les projets `Domain`
> portent bien « ce projet est volontairement vide », mais le README décrivait le
> service au présent, comme s'il fonctionnait — et un audit l'a d'abord compté
> comme fait. Voir `docs/audit/2026-08-21-complet/` (D-3 du plan de correction :
> finir, retirer, ou assumer).
