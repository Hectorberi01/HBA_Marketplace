# Dispatch Service

> ## ÉTAT : SQUELETTE — CE SERVICE N'EST PAS IMPLÉMENTÉ
>
> **Maquette en mémoire : candidats codés en dur, affectation sans verrou.**
>
> Ce dossier existe pour que l'arborescence corresponde à l'architecture cible.
> Le code présent est une **maquette en mémoire** : un `ConcurrentDictionary` singleton tient lieu de persistance.
>
> Concrètement, aujourd'hui :
> - `DispatchStore.AssignAsync` écrit sans relire : **deux livreurs peuvent accepter la même course** (ISSUE-028) ;
> - la route `manual-assign` est anonyme ;
> - les candidats sont deux GUID écrits dans le code ;
> - aucun `DbContext`, aucun processeur d'outbox : les événements publiés sont perdus (ISSUE-007) ;
> - l'affectation qui fonctionne réellement est celle de `delivery-service`.
>
> Ce bandeau existe parce que rien d'autre ne le disait. Les projets `Domain`
> portent bien « ce projet est volontairement vide », mais le README décrivait le
> service au présent, comme s'il fonctionnait — et un audit l'a d'abord compté
> comme fait. Voir `docs/audit/2026-08-21-complet/` (D-3 du plan de correction :
> finir, retirer, ou assumer).
