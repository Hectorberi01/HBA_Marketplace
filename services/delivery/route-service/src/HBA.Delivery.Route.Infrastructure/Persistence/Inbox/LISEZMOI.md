# `Persistence/Inbox/`

Vide dans **HBA.Delivery.Route.Infrastructure**, et ce n'est pas un oubli.

route-service **n'a pas de base de donnees** : ses itineraires vivent en memoire.
Pas de `ModuleDbContext`, donc pas de table d'outbox ni d'inbox, donc rien a
mettre ici.

Ces fichiers avaient ete generes par erreur : le script cherchait le contexte du
service dans un commentaire, et avait retenu `TContext`. Ils ne compilaient pas,
et c'est la seule raison pour laquelle on s'en est apercu.

Voir l'encadre de `RoutesInfrastructureModule` : les trois evenements que ce
service publie n'atteignent aucun consommateur, faute de chemin de sortie.
