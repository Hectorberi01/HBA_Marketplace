# Ce qui traverse les services

**LE DOSSIER LE PLUS DANGEREUX DE L'ARBORESCENCE.**

Chaque ligne mise ici est une ligne que treize services doivent adopter en même
temps. Un partagé qui grossit reconstitue le monolithe — avec le déploiement
distribué en plus, et sans la transaction unique qui le rendait sûr.

La règle : **on ne partage qu'un contrat, jamais un comportement.** Un DTO, un
schéma d'événement, une définition gRPC, oui. Une règle métier, une validation, un
calcul de prix : non. Ils appartiennent au service qui en répond.

Le monolithe applique déjà cette discipline — les modules ne se voient que par leurs
`*.Contracts`, et un test d'architecture le vérifie à chaque build.
