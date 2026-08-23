# Contrats gRPC internes

Communication **synchrone entre services**, sur le réseau `hba-backend`.

## Ce qui passe ici, et ce qui n'y passe pas

| Besoin | Transport |
|---|---|
| « j'ai besoin de la réponse maintenant », service → service | **gRPC**, ce dossier |
| Client → plateforme (BFF, applications) | **REST/JSON** via la passerelle |
| « quelque chose s'est produit » | **Kafka**, `shared/kafka-schemas/` |

La troisième ligne est la plus importante et la plus facile à enfreindre : un
service qui appelle un autre pour *lui annoncer* un fait crée un couplage
temporel — si l'appelé est à terre, l'appelant échoue alors qu'il n'avait rien à
attendre. Les 32 consommateurs d'événements du monolithe relèvent tous de cette
ligne-là.

## Versionnement

Le chemin et le `package` portent `v1`. Ce n'est pas décoratif : un contrat
consommé par douze services ne peut pas changer de forme en un seul
déploiement. Une rupture crée `v2` et les deux cohabitent le temps que les
consommateurs migrent.

**Règles Protobuf à ne pas enfreindre :**

- ne jamais réutiliser ni renuméroter un tag de champ — le numéro est la seule
  chose qui circule sur le fil, le nom n'est là que pour les humains ;
- ne jamais supprimer un champ sans le marquer `reserved` : sans cela, quelqu'un
  réattribuera son numéro à un champ d'un autre type, et un ancien client lira
  des octets qui ne veulent plus rien dire — sans aucune erreur ;
- ajouter un champ optionnel reste compatible dans les deux sens.

## Génération

Aucun code généré n'est versionné. Chaque projet `HBA.*.Contracts.Grpc`
référence son `.proto` via `Grpc.Tools`, qui régénère à la compilation.
