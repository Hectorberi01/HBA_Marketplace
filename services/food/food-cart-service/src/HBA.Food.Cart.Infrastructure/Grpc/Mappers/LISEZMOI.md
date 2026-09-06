# `Mappers/` — vide aujourd'hui, et voici pourquoi

Ce dossier accueillera les traductions proto <-> enregistrements de contrats de ce service.

**Aujourd'hui, ils vivent dans `shared/contracts/HBA.<Domaine>.Contracts.Grpc`,
en un seul exemplaire par domaine**, partagé par tous les appelants.

## Ce qui bloque leur descente ici

Un adaptateur implémente `I<Domaine>ModuleApi` **en entier** — l'interface l'exige.
Une copie par service consommateur serait donc une copie INTÉGRALE, sans la
réduction qui la rendrait défendable : `merchant.proto` a neuf consommateurs, et
les neuf devraient porter les mêmes treize traductions.

Deux domaines sur quinze échappent à cette règle, parce qu'ils exposent DEUX
interfaces séparées :

- `HBA.Merchants` — `ISellerModuleApi` et `IMerchantAccessApi`
- `HBA.Deliveries` — `IDeliveryModuleApi` et `IDeliveryDispatchApi`

Là, un consommateur qui n'a besoin que d'une des deux peut n'en porter qu'une.
Partout ailleurs, réduire supposerait de découper `I<X>ModuleApi` par appelant —
un vrai travail de conception, pas un déplacement de fichiers.

## Ce qui est décidé, et ce qui ne l'est pas

Décidé : le CÂBLAGE descend ici — `Grpc/DependencyInjection.cs` et
`Grpc/Configuration/`. C'est ce qui rend la liste des dépendances d'un service
lisible en un fichier.

Pas décidé : la duplication des adaptateurs. Elle coûte environ 12 500 lignes
copiées contre 2 700 aujourd'hui, et elle ne peut pas être réduite comme prévu.
