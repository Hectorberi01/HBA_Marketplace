# Proof of Delivery Service

> ## ÉTAT : SQUELETTE — CE SERVICE N'EST PAS IMPLÉMENTÉ
>
> **Maquette en mémoire : OTP universel « 123456 ».**
>
> Ce dossier existe pour que l'arborescence corresponde à l'architecture cible.
> Le code présent est une **maquette en mémoire** : les preuves sont gardées dans un dictionnaire et perdues au redémarrage.
>
> Concrètement, aujourd'hui :
> - `ProofStore.cs` rend l'OTP constant `"123456"` pour toutes les courses (ISSUE-056) ;
> - `submit` n'a pas de garde d'état : une preuve déjà vérifiée peut être rejouée ;
> - `dropoff-valid` n'a aucun appelant : la preuve n'est jamais reliée à la transition LIVRÉ ;
> - aucune authentification, aucun `DbContext`, aucun processeur d'outbox.
>
> Ce bandeau existe parce que rien d'autre ne le disait. Les projets `Domain`
> portent bien « ce projet est volontairement vide », mais le README décrivait le
> service au présent, comme s'il fonctionnait — et un audit l'a d'abord compté
> comme fait. Voir `docs/audit/2026-08-21-complet/` (D-3 du plan de correction :
> finir, retirer, ou assumer).
