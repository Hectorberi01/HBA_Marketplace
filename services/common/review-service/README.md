# Engagement Service

Avis et notes, interactions utilisateurs, recommandations, signaux de préférence.

> ## CE PROCESSUS HÉBERGE TROIS MODULES, ET DEUX N'ONT PAS DE DOSSIER ICI
>
> Le dossier `review-service/` porte **reviews**. Son `Program.cs` installe en
> plus, dans le même processus :
>
> | Module | Dossier | Schéma |
> |---|---|---|
> | `reviews` | `review-service/src/HBA.Engagement.Reviews.*` | `reviews` |
> | `recommendations` | **`services/common/recommendation-service/`** | `recommendations` |
> | `wishlist` | **`services/common/wishlist-service/`** | `wishlist` |
>
> Les deux derniers n'ont **ni `Program.cs`, ni `Dockerfile`, ni entrée de
> compose** : ils partent avec l'image de celui-ci, sur sa base `hba_engagement`,
> chacun dans son propre schéma. Voir leurs README.

**LES « MODULES ACTUELS » CI-DESSOUS POINTAIENT VERS `src/Modules/`, QUI
N'EXISTE PLUS** — ce README décrivait encore le monolithe d'avant, au présent.

## Pourquoi les trois modules sont encore ensemble


Ce qui se nourrit du comportement plutôt que de le décider. Aucune de ces trois fonctions ne bloque une transaction : elles observent, agrègent, suggèrent.

**La liste d'envies est chez Commerce, ses SIGNAUX sont ici.** Le même geste sert deux usages : conserver un article pour plus tard (transactionnel) et alimenter la recommandation (analytique). Les deux n'ont ni la même fraîcheur ni la même base.

**Analytics écrit dans ClickHouse, pas dans PostgreSQL.** C'est le seul service à porter deux bases de natures différentes, et c'est assumé : agréger du volume et servir une fiche d'avis ne demandent pas le même moteur.

## Le « squelette attendu » a été retiré : il est en place

Les quatre projets existent sous leurs noms .NET. Une cible atteinte laissée en
« attendu » finit par être re-visée.
