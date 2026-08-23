# Financial Service

Paiements (passerelle), portefeuilles, commissions et reversements, facturation, taxes, détection de fraude.

> ## CE PROCESSUS HÉBERGE TROIS MODULES, ET DEUX N'ONT PAS DE DOSSIER ICI
>
> Le dossier `payment-service/` porte **payments**. Son `Program.cs` installe en
> plus, dans le même processus :
>
> | Module | Dossier | Schéma |
> |---|---|---|
> | `payments` | `payment-service/src/HBA.Financial.Payments.*` | `payments` |
> | `billing` | **`services/common/billing-service/`** | `billing` |
> | `settlement` (wallet) | **`services/common/wallet-service/`** | `settlement` |
>
> Les deux derniers ont la forme d'un service — quatre projets, base, migrations —
> et **ni `Program.cs`, ni `Dockerfile`, ni entrée de compose**. Ils partent avec
> l'image de celui-ci, dans son conteneur, sur sa base `hba_financial`, chacun
> dans son propre schéma. Voir leurs README.
>
> **RIEN NE LE DISAIT NULLE PART**, et l'audit d'août les a comptés comme deux
> services de plus — c'est-à-dire deux fois : comme service à déployer, et comme
> module déjà fourni.

**LES « MODULES ACTUELS » CI-DESSOUS POINTAIENT VERS `src/Modules/`, QUI
N'EXISTE PLUS.** L'extraction a eu lieu ; ce README décrivait encore le monolithe
d'avant, au présent. Le raisonnement qui suit, lui, reste exact — et il explique
précisément pourquoi billing et wallet sont TOUJOURS dans ce processus.

## Pourquoi les trois modules sont encore ensemble


**LE PLUS RISQUÉ À DÉCOUPER, ET DONC LE DERNIER.**

Cinq modules y sont réunis non par commodité mais parce qu'ils se lisent l'un l'autre à chaque écriture : un paiement encaissé crédite un portefeuille, une commission alimente un solde plateforme, un remboursement contre-passe les trois. Séparer ces écritures, c'est accepter qu'un solde soit faux pendant quelques secondes — sur de l'argent réel, et sans qu'aucune alarme ne le dise.

**L'escrow est le point le plus délicat.** Les fonds sont retenus à la confirmation et libérés à la livraison. Le chemin de libération est différent pour un colis (toutes les expéditions livrées) et pour un repas (la course remise). Un maillon manquant ne lève aucune erreur : l'argent reste simplement bloqué.

**Le grand livre est la source de vérité, pas les soldes.** Toute écriture doit y laisser une trace référencée — c'est ce qui rend les rejeux d'outbox détectables.

## Le « squelette attendu » a été retiré : il est en place

Il décrivait `api/ domain/ application/ infrastructure/` comme une cible. Les
quatre projets existent, sous leurs noms .NET
(`HBA.Financial.Payments.Domain`, `.Application`, `.Infrastructure`, et
`HBA.Financial.Api` pour l'hôte). Une cible atteinte laissée en « attendu » finit
par être re-visée.
