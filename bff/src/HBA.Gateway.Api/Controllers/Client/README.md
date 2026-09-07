# Façades BFF

Une façade par audience, conformément au §13 :

| Préfixe                      | Audience                        | État                        |
|------------------------------|---------------------------------|-----------------------------|
| `/api/bff/client/express/*`  | application cliente — Express   | `home` implémenté           |
| `/api/bff/client/food/*`     | application cliente — Food      | `home` implémenté           |
| `/api/bff/merchant/*`        | portail vendeur                 | à créer avec ses écrans     |
| `/api/bff/restaurant/*`      | portail restaurateur            | à créer avec ses écrans     |
| `/api/bff/driver/*`          | application livreur             | à créer avec ses écrans     |
| `/api/bff/admin/*`           | administration                  | à créer avec ses écrans     |

Les quatre dernières façades n'ont **pas** de contrôleur vide.

Un contrôleur sans point de terminaison n'apporte rien et coûte : il apparaît
dans la surface d'API, il faut décider de son autorisation, il finit par
recevoir un premier point de terminaison ajouté « puisque le fichier existe »
plutôt que parce qu'un écran le demande. Les dossiers correspondants existent
dans `HBA.Gateway.Application/Bff/` pour marquer la frontière ; le contrôleur
viendra avec le premier écran réel.
