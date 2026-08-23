# billing — facturation et règles de commission

> ## ÉTAT : **MODULE HÉBERGÉ** — CE N'EST PAS UN SERVICE AUTONOME
>
> Ce dossier a la forme d'un service — quatre projets, son domaine, sa base, ses
> migrations, son schéma. **Il n'en est pas un**, et rien dans l'arborescence ne
> le disait : il n'a **ni `Program.cs`, ni `Dockerfile`, ni entrée dans
> `docker-compose.dev.yml`, ni image, ni port**.
>
> Son code s'exécute dans le processus de **payment-service**, qui appelle
> `new BillingModuleInstaller().Install(...)` dans son `Program.cs`.
>
> **CE BANDEAU EXISTE PARCE QUE RIEN NE LE DISAIT.** Ce dossier a été compté
> comme un service par l'audit d'août — c'est-à-dire compté deux fois : une fois
> comme service à déployer, une fois comme module déjà fourni. C'est le même
> défaut que les bandeaux « SQUELETTE » du lot 0.5, dans l'autre sens.

## Ce que cela implique, concrètement

| | |
|---|---|
| **Processus** | celui de `services/common/payment-service` — même conteneur, même redémarrage, même panne |
| **Base** | `hba_financial`, partagée avec son hôte |
| **Schéma** | `billing` — isolé, avec ses propres migrations et son propre `DbContext` |
| **Port HTTP / gRPC** | aucun en propre : tout passe par l'hôte |
| **Déploiement** | aucun : il part avec l'image de son hôte |

**UNE BASE PARTAGÉE, DES SCHÉMAS SÉPARÉS.** La règle 1 de `services/README.md`
— « une base par service, pas de schéma partagé, pas de jointure entre deux
services » — n'est pas violée : ce n'est pas un service. Le `DbContext` est
distinct, le schéma est distinct, et `ModuleDbContext` interdit déjà toute
jointure inter-schéma. Ce qui est partagé, c'est la CHAÎNE DE CONNEXION, donc le
serveur et la base — pas les tables.

**CE QU'IL FAUDRAIT POUR EN FAIRE UN SERVICE** : un `Program.cs`, un
`Dockerfile`, une base à lui, une entrée de compose et de `k8s/`, une adresse
`Services:Financial` chez ses appelants, et le remplacement de ses appels en
processus par un contrat gRPC. Tant que ce n'est pas fait, **l'appeler « service »
dans un document ou un ticket est une erreur de comptage.**

## Ce que le module rend

* **Règles de commission** (`commission_rules`) — barème par vendeur et par
  catégorie, avec période de validité.
* **Factures** (`invoices`, `invoice_lines`) — l'état de compte d'un vendeur sur
  une période.

Le calcul de commission est appelé par le module `wallet` du même processus,
et par `FinancialApi.ComputeCommission` pour les appelants distants.
