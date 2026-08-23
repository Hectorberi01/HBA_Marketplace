# Driver Service

Le **dossier** du livreur : inscription, pièces justificatives, vérification,
suspension. C'est le service qui répond à « cette personne a-t-elle le droit de
livrer pour HBA ? ».

## Ce qui a changé au lot 5.2 (ISSUE-029, ISSUE-030)

Ce README décrivait un squelette, et il avait raison. Le service tenait son état
dans un `ConcurrentDictionary` (`DriverStore`) exposant un **`DefaultDriverId`
codé en dur** : les six routes `/api/v1/drivers/me*` opéraient toutes dessus,
c'est-à-dire que **tous les livreurs étaient le même livreur**.

Depuis ce lot :

* un agrégat `DriverAccount` réel, avec pièces, véhicules et cycle de
  vérification ;
* un `DbContext` (`DriverDbContext`, schéma `drivers`), sa migration initiale
  `20260905000100_InitialDrivers` et son **index unique sur `UserId`** ;
* `AddOutboxProcessor<DriverDbContext>()` — ISSUE-007 : les événements du module
  étaient jusqu'ici publiés dans une file que **personne ne drainait** ;
* les routes `/me` opèrent sur **l'appelant**, dont l'identité vient du **jeton**
  (`CurrentUserId`), jamais d'un identifiant reçu ;
* le socle passe de `AddHbaSecurity` à `AddHbaService<DriverDbContext>`.

## Ce que ce service ne fait PAS, et où c'est passé

> **La disponibilité, la position et le carnet de courses ne sont pas ici.**
>
> Ils vivent dans `deliveries.drivers`, chez **delivery-service**, parce que
> c'est cette table que le dispatch lit à chaud pour affecter une course. Les
> tenir aussi ici aurait donné deux écrivains sur un même fait, dont celui qui
> décide de proposer une course aurait toujours lu l'autre avec du retard.
>
> Les routes correspondantes sont sous **`/api/deliveries/mine`** :
> `online`, `offline`, `break`, `position`, `accept`, `decline` et les cinq
> étapes d'exécution. Voir `DriverDeliveryEndpoints`.

**Le livreur parle donc à deux services**, et c'est le prix assumé de deux
propriétaires. Le lien entre les deux est l'événement
`driver.dossier-verified` : quand l'exploitation vérifie un dossier ici,
delivery-service crée sa projection dispatchable là-bas.

## Ce qui reste ouvert

* **`DriverSuspendedIntegrationEvent` n'est consommé par personne.** Un livreur
  suspendu dans son dossier continue de recevoir des propositions chez
  delivery-service. C'est le manque le plus sérieux du découpage.
* **Aucune pièce n'est réellement vérifiée.** `ObjectKey` désigne un objet chez
  media-service dont ni l'existence ni le propriétaire ne sont contrôlés.
  `Verify()` approuve toutes les pièces d'un bloc sur décision humaine.
* **Le port gRPC n'a aucun appelant** : aucun service n'enregistre
  `AddDriversGrpcClient`. `SetBusyState` y rend délibérément `Unimplemented` —
  cet état appartient à delivery-service.
* **Le nom, le téléphone et le véhicule ne sont recopiés qu'à la vérification.**
  Les modifier ensuite ici ne met pas à jour la projection.
