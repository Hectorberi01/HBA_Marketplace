# Audit du 27 août 2026 — défauts ouverts et couche de déploiement

*Lecture de code, sans exécution : aucun compilateur .NET ni cluster n'a servi à
produire les constats de la partie 1 et 2. La partie 3, elle, vient de manifestes
réellement rendus et d'un cluster réellement installé.*

---

## Ce que cet audit est, et ce qu'il n'est pas

**IL NE REFAIT PAS L'AUDIT DU 21 AOÛT.** Celui-ci reste entier dans
`../2026-08-21-complet/` : ses 162 anomalies ne sont pas résolues, et sa conclusion
tient toujours — 20 événements sur 136 raccordés, 31 RPC sur 116. Le redoubler
aurait produit un second document disant la même chose, et deux documents qui
disent la même chose divergent au premier correctif.

Il couvre trois choses que l'audit du 21 ne pouvait pas voir :

1. **Ce qui reste non implémenté**, vérifié aujourd'hui fichier par fichier — pas
   recopié de l'audit précédent.
2. **Les bugs encore présents**, y compris deux que l'audit du 21 n'avait pas
   relevés et qui touchent de l'argent.
3. **La couche de déploiement** — Kubernetes, Ansible, pare-feu, secrets. Elle
   n'existait pas le 21 août. Elle a produit à elle seule huit défauts en une
   journée, tous du même genre : un fichier écrit, jamais exécuté.

**PORTÉE.** 1 853 fichiers `.cs` lus par quatre analyses parallèles (common,
marketplace, food+delivery, socle+passerelle), plus les manifestes rendus par
`kustomize build` pour les trois environnements.

---

# Partie 1 — Fonctionnalités non implémentées

## 1.1 Quatre services de livraison n'ont aucune base de données

**CONFIRMÉ**, et auto-documenté dans le code.

`dispatch-service/.../DispatchStore.cs:10-12`, `route-service/.../RouteStore.cs:9`,
`tracking-service/.../TrackingStore.cs:9-11`,
`proof-of-delivery-service/.../ProofStore.cs:63-65`

Les quatre gardent tout leur état métier — jobs d'affectation, plans de route,
sessions de suivi, preuves de livraison, défis OTP — dans des
`ConcurrentDictionary` en mémoire de processus. Le code le dit lui-même :
« DISPARAÎT AU REDÉMARRAGE… n'est même pas partagé entre deux réplicas ».

**Ce que ça coûte** : tout redéploiement perd les livraisons en cours et les
preuves déjà collectées. Avec deux réplicas, chacun a son propre état, et un
livreur qui rafraîchit son écran voit deux réalités selon le pod qui répond.

**ET LEURS ÉVÉNEMENTS SONT PERDUS À CHAQUE FOIS.** Les quatre installateurs
enregistrent `IntegrationEventQueue` comme publicateur — mais sans `DbContext`, il
n'y a pas d'outbox, donc **aucun processus ne draine jamais cette file**. Chaque
fichier le documente comme une perte totale et systématique. Un événement publié
par l'un de ces quatre services n'atteint aucun consommateur, jamais.

### RÉSOLU LE 28 AOÛT — TROIS DES QUATRE SERVICES ONT ÉTÉ RETIRÉS

`dispatch-service` est parti en D42, `tracking-service` et
`proof-of-delivery-service` en D43. Le défaut n'a pas été corrigé : les services
ont été **supprimés**, parce que la vérification a montré que personne ne les
appelait et que `delivery-service` tenait déjà les mêmes capacités, avec une base
et des migrations.

Ce qui a été vérifié avant de trancher, service par service : aucune entrée dans
`ServicesOptions` de la passerelle (donc injoignables depuis l'extérieur), aucun
client gRPC enregistré, aucune référence de projet ni `using` hors de leur propre
arborescence, et zéro consommateur de leurs événements — les douze ruptures de
contrat signalées après retrait sont exactement leurs propres événements.

**IL EN RESTE UN, ET IL EST TOUJOURS CASSÉ.** `route-service` a le même profil —
état en mémoire, outbox jamais drainée, zéro appelant — et n'a PAS été retiré.
Voir 1.2 : contrairement aux trois autres, sa capacité n'existe nulle part
ailleurs, elle tourne en mode dégradé, et sa disposition est une décision
distincte.

**CE QUE CE RETRAIT NE RÉPARE PAS.** Le dépôt de la photo de preuve n'a toujours
pas de chemin raccordé : `delivery-service` accepte une référence de stockage,
`media-service` sait recevoir un fichier, et rien ne relie les deux côté
application livreur. Détaillé en D43.

## 1.2 Aucun moteur de routage réel

**CONFIRMÉ.** `route-service/.../IRouteProvider.cs` — interface définie, **aucune
implémentation dans tout le service**. Le calcul effectif est dans
`RouteStore.cs:16-30` : distance à vol d'oiseau (Haversine) divisée par une vitesse
constante de 5,8 m/s. Le champ source vaut littéralement `"FALLBACK_HAVERSINE"`.

**Ce que ça coûte** : toutes les durées annoncées au client ignorent les rues, le
sens de circulation et le trafic. À Cotonou, l'écart entre la ligne droite et le
trajet réel n'est pas une marge, c'est un facteur.

### CORRECTION DU 28 AOÛT — CE CONSTAT DÉSIGNAIT LE MAUVAIS SERVICE

Le constat sur `route-service` est exact et **sans conséquence** : ce service n'a
aucun appelant et aucune entrée dans `ServicesOptions` de la passerelle. Corriger
là n'aurait rien changé pour personne.

**Le Haversine qui compte est dans `delivery-pricing-service`**, appelé en gRPC
par delivery-service, et il ne produit pas une estimation d'affichage :

```csharp
var distance = request.DistanceMeters ?? ServiceabilityPolicy.HaversineMeters(...);
var duration = request.DurationSeconds ?? Math.Max(60, (int)(distance / 5.8));
var breakdown = PricingPolicy.BuildBreakdown(rule, distance, duration, ...);
//   distanceFee = distance/1000 × PerKmFee   ← le prix facturé
//   minuteFee   = duration/60   × PerMinuteFee
```

**Ce que ça coûte vraiment, et l'audit l'avait sous-estimé :**

1. **La plateforme sous-facture chaque course.** Le trajet réel étant toujours
   plus long que la ligne droite, la course est chiffrée sous son coût, et
   l'écart croît avec la distance.

2. **Elle accepte des courses hors zone.** Le plafond de desserte de 25 km est
   comparé à la même ligne droite. Une course de 30 km par la route mais 24 km à
   vol d'oiseau est acceptée, puis effectuée à perte.

3. **Rien ne disait d'où venait le chiffre.** Un devis où l'appelant avait fourni
   sa propre distance et un devis calculé en ligne droite étaient indiscernables.
   Un litige de facturation ne pouvait pas être instruit.

**TRAITÉ EN D44, ET LE DÉFAUT N'EST PAS CORRIGÉ POUR AUTANT.** Les deux
constantes sont sorties du code et validées au démarrage, la provenance est
persistée et rendue jusque dans le contrat, et la durée est déclarée comme un
plancher. Mais le facteur de correction vaut **1,0 par défaut** : le prix produit
est exactement celui d'avant. Le levier est posé, pas tiré — le régler demande de
mesurer l'écart réel à Cotonou, ce qui n'a pas été fait. La sous-facturation et
l'acceptation hors zone subsistent, désormais visibles et réglables.

## 1.3 L'affectation des livreurs propose deux comptes fictifs

**CONFIRMÉ.** `dispatch-service/.../DispatchStore.cs:239-243`

```csharp
private static List<DriverCandidate> BuildCandidates(Guid deliveryId) =>
[
    new DriverCandidate(deliveryId, Guid.Parse("00000000-0000-7000-0000-000000000017"), …),
    new DriverCandidate(deliveryId, Guid.Parse("00000000-0000-7000-0000-000000000018"), …)
];
```

`Pickup`, `Dropoff` et `VehicleRequirement` sont ignorés. Aucun appel vers
driver-service.

**Ce que ça coûte** : n'importe quelle commande, n'importe où, est proposée aux
deux mêmes identifiants fictifs. **Aucun livreur réellement inscrit ne reçoit
jamais d'offre.**

### CORRECTION DU 27 AOÛT, APRÈS VÉRIFICATION — ce constat visait le mauvais service

La première rédaction de ce paragraphe disait qu'il manquait « un appel vers
driver-service ». **C'est faux, et l'erreur valait la peine d'être cherchée.**

`driver-service` ne porte **délibérément** ni disponibilité, ni position, ni course
en cours — son agrégat `DriverAccount` le documente explicitement, et son RPC
`SetBusyState` renvoie `Unimplemented` en désignant l'endroit où cet état vit
réellement.

**LA CAPACITÉ EXISTE DÉJÀ, ET ELLE FONCTIONNE — DANS delivery-service.**

| Ce qui existe | Où |
|---|---|
| table `deliveries.drivers` — position, disponibilité | `DeliveriesDbContext`, 17 migrations |
| `IDriverLocationCache.FindNearbyAsync` | `Infrastructure/Redis/RedisDriverLocationCache.cs` |
| ce qui l'alimente | `POST /api/deliveries/mine/online\|offline\|position` |
| ce qui la consomme | `DispatchDeliveryCommand.cs:94` — **une affectation réelle** |

Autrement dit : **le dépôt contient deux affectations de livreur.** Une vraie,
adossée à une base et à un cache géographique, dans delivery-service. Une fausse,
avec deux GUID codés en dur, dans dispatch-service. La seconde ne peut pas appeler
la première : `FindNearbyAsync` n'est exposée par aucun endpoint HTTP ni aucun RPC.

Le contrat `AddDriversGrpcClient` existe bien dans `shared/contracts/`, mais il
n'offre que des recherches par identifiant — aucune notion de proximité — et
**aucun service du dépôt ne l'enregistre**. Son propre fichier le dit : « ce port
n'a aucun appelant aujourd'hui ».

## 1.4 return-refund : trois adaptateurs gRPC simulés

**CONFIRMÉ.** `InventoryGrpcClient.cs:39-43`, `DeliveryGrpcClient.cs:40-44`,
`MediaGrpcClient.cs:44-49`.

Aucun ne contacte de serveur. Le stock retourné n'est jamais remis en rayon, aucune
course d'enlèvement n'est créée, et une preuve photo est validée sur le seul critère
« non vide ».

Un garde bloque le démarrage en production
(`ReturnRefundModuleInstaller.GuardSimulatedGrpcAdapters:180-251`) — mais il est
**fail-open** si `ASPNETCORE_ENVIRONMENT` est absent ou mal orthographié
(lignes 244-250). Voir le bug 2.1, qui est la même faille de raisonnement.

### RÉSOLU LE 28 AOÛT — ET LE DÉFAUT ÉTAIT PLUS LARGE QUE CE CONSTAT

L'audit décrivait UN garde fail-open. Il y en avait **six**, copies littérales de
la même méthode, dans autant d'installeurs : return-refund, payment, media,
notification, food-cart, et le socle d'infrastructure lui-même. Toutes écrivaient :

```csharp
var env = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"] ?? "";
return string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);
```

**Et c'est l'inverse d'ASP.NET Core.** Quand `ASPNETCORE_ENVIRONMENT` n'est pas
posée, `IHostEnvironment.EnvironmentName` vaut « Production ». Le socle et le
framework se contredisaient sur le cas exact où ça compte le plus : la variable
oubliée.

Ce que ces six copies gardaient : la **clé de chiffrement** des codes de
réinitialisation (sans elle, `AesGcmSecretProtector` retombe sur une clé dérivée
d'une phrase fixe présente dans ce dépôt — c'est le bug 2.1), le refus de
démarrer avec des **adaptateurs gRPC simulés**, et le refus de démarrer avec des
**fournisseurs de paiement simulés**.

Les six délèguent désormais à `EnvironnementDeploiement.EstProduction`, en un seul
exemplaire : absent ou vide → production ; nom explicitement listé hors production
→ pas la production ; **tout autre nom → production**, avec un avertissement qui
nomme les valeurs acceptées.

**CE QUE CETTE CORRECTION NE FAIT PAS.** Les trois adaptateurs gRPC restent
simulés — le garde mord mieux, il ne remplit pas ce qu'il garde. Et « Staging »
reste traité comme hors production, sciemment : une préproduction ne doit pas
encaisser de vrai argent. Conséquence directe, à connaître avant de déployer : un
staging sans `Secrets:Key` utilise la clé de développement publique.

## 1.5 Le résumé des retours d'une commande est une constante

**CONFIRMÉ.** `return-refund-service/.../ReturnQueries.cs:136-147`

```csharp
return new OrderReturnSummaryDto(query.OrderId, 0m, "XOF", 0);
```

Aucune lecture de données. L'écran affichera « 0 remboursé, 0 retour actif » quelle
que soit la réalité.

### RÉSOLU LE 28 AOÛT

`GetOrderSummaryAsync` a été ajouté au dépôt et le handler le lit. Deux décisions
de comptage, écrites dans l'interface :

- **le montant compte l'argent PARTI, pas l'argent promis** — on somme les
  `Refund` en `Succeeded`, pas les `ApprovedRefundAmount`. Un remboursement
  approuvé dont l'exécution échoue chez le fournisseur doit compter zéro : le
  client n'a rien reçu, et c'est précisément le chiffre qu'on lui opposera ;
- **« actif » reprend la définition déjà en place** dans
  `ListOpenQuantitiesByOrderAsync` — les six mêmes statuts terminaux. Deux
  définitions du mot « ouvert » dans le même service divergeraient au premier
  statut ajouté.

**CE QUE ÇA NE COUVRE PAS.** La somme est **monodevise assumée** : les montants
sont additionnés sans regarder la devise, et celle rendue est celle du premier
remboursement trouvé. Le jour où une commande mêle deux devises, ce total sera
faux sans rien dire. Et un remboursement en `Processing` n'apparaît nulle part :
on ne voit pas qu'un versement est en vol.

## 1.6 Le refus vendeur ne déclenche rien

**PLAUSIBLE** — documenté dans le code, non câblé.
`order-service/.../SellerOrderEventHandlers.cs:40-95`

Quand un vendeur refuse ou annule sa part d'une commande **déjà payée**, un
événement d'intégration est publié, puis un `LogWarning`. Aucun consommateur ne
libère le stock, ne rembourse le client, ni ne le notifie.

**Ce que ça coûte** : un client débité pour un article qui ne sera jamais expédié,
sans remboursement automatique ni message.

### VÉRIFIÉ LE 28 AOÛT — PASSE DE « PLAUSIBLE » À **CONFIRMÉ**

`SellerOrderRefusedIntegrationEvent` n'a **aucun consommateur** hors de
order-service. Zéro occurrence dans les 20 services, le socle partagé et la
passerelle. Le `LogWarning` est bien la seule chose qui mette un humain au
courant, et il le dit lui-même : « AUCUN CONSOMMATEUR n'écoute encore cet
événement — reprise MANUELLE requise ».

**NON CORRIGÉ, ET DÉLIBÉRÉMENT.** Fermer ce trou demande trois consommateurs —
libération de stock, remboursement, notification — donc trois décisions métier :
rembourse-t-on la part ou la commande, à quel moment, et qui prévient le client.
Ce n'est pas une correction, c'est une fonctionnalité, et son périmètre revient
au propriétaire du produit.

## 1.7 Les événements Kafka entrants n'ont pas de lettre morte

**PLAUSIBLE** — c'est le comportement écrit, et c'est un trou.
`KafkaIntegrationEventConsumer.cs:291-312` et `:183`

Après trois tentatives, l'événement est journalisé en `Critical` puis **abandonné**,
et l'offset est committé quand même. Le message n'existe plus nulle part.

L'asymétrie est frappante : l'outbox **sortant** a, lui, une vraie table de lettres
mortes. L'entrant n'a rien.

### VÉRIFIÉ LE 28 AOÛT — CONFIRMÉ, ET L'AUDIT SE TROMPAIT SUR LA COMPARAISON

**`/admin/outbox/dead-letters` N'EXISTE PAS.** Aucune route de ce nom n'est montée
nulle part dans le dépôt. Quatre endroits du socle l'annonçaient pourtant, dont —
le pire — le message `LogCritical` émis au moment exact où un événement métier est
définitivement perdu : « Corriger la cause, puis rejouer via
/admin/outbox/dead-letters ». Un exploitant réveillé par cette ligne partait
chercher une surface absente.

Le portail d'administration, lui, avait raison : il classe sa section « Outbox »
comme SANS AMONT, avec la bonne raison — la table est interne au service, et
l'exposer donnerait accès aux charges utiles des événements, dont certaines
portent un secret.

**CORRIGÉ : les messages, pas le manque.** Les quatre mentions décrivent
désormais le geste réel, qui est manuel et en base (`DeadLetteredOnUtc = NULL`,
`AttemptCount = 0`, `NextAttemptAtUtc = NULL` — l'index partiel sur
`DeadLetteredOnUtc IS NOT NULL` existe précisément pour retrouver ces lignes).

**LE TROU ENTRANT RESTE OUVERT.** Le combler suppose une table de lettres mortes
côté consommateur, donc une migration dans les 24 contextes, ou un état
supplémentaire sur `ConsumerInboxEntry` — qui est le candidat naturel. C'est un
lot d'infrastructure à part entière, pas une correction.

## 1.8 Les réservations d'idempotence peuvent rester bloquées à vie

**CONFIRMÉ.** `EfIdempotencyStore.cs:78-81`, `IdempotencyEndpointFilter.cs:110-120`

La clé n'est libérée que si le handler lève une exception **attrapée**. Si le
processus meurt entre la réservation et la complétion — OOM, `kill`, redémarrage de
pod — l'enregistrement reste inachevé pour toujours : ni TTL, ni purge.

**Ce que ça coûte** : la même `Idempotency-Key` renvoie `409 Conflict`
indéfiniment. Un client bloqué en plein paiement n'a aucun recours automatique.

### RÉSOLU LE 28 AOÛT — LE MÉCANISME EXISTAIT DÉJÀ, ÉTEINT

`IdempotencyRecord.ExpiresAtUtc` est déclarée dans l'entité, initialisée à 24 h,
marquée `IsRequired()` dans la configuration, et porte un **index dédié** —
nommé `ix_idempotency_keys_expires_at`, commenté « index de purge » — dans la
migration de **chacun** des sept services concernés. Aucune ligne de code ne
lisait cette colonne.

C'est ce qui rendait le défaut invisible : tout avait l'apparence d'un mécanisme
réglé. Un index dédié dit à qui relit « quelqu'un interroge cette colonne ». Ici,
personne.

Deux changements, **sans aucune migration** — le schéma était déjà là partout :

1. `TryBeginAsync` reprend une réservation inachevée dont l'échéance est passée :
   elle est supprimée, et la contrainte d'unicité arbitre à nouveau. La reprise
   est bornée à **une** par un drapeau explicite, et non par un appel récursif qui
   n'aurait aucune borne prouvable.
2. `IdempotencyPurger` efface les lignes périmées, par tranches, sur un curseur
   d'échéance — `IdempotencyRecord` n'ayant pas de clé simple mais un triplet, on
   ne peut pas copier la mécanique par identifiants d'`OutboxPurger`.

Le magasin et son purgeur s'enregistrent désormais **ensemble**, par
`AddIdempotence<TDbContext>()`. Les poser en deux lignes dans les sept installeurs
aurait reconduit le mécanisme qui a produit le défaut : il aurait suffi qu'un
huitième service ne copie que la première.

**CE QUE ÇA NE COUVRE PAS.** Si la première exécution avait déjà produit son effet
métier avant de mourir — paiement parti, message envoyé — le rejeu après 24 h le
produira **une seconde fois**. L'idempotence HTTP ne remplace pas celle du
domaine ; les opérations qui déplacent de l'argent ont la leur.

## 1.9 Les pièces jointes antérieures à la bascule restent publiques

**CONFIRMÉ.** `notification-service/.../20260817000000_PiecesJointesPrivees.cs:20`,
champ `LegacyUrl` dans `ConversationQueries.cs`

La migration a rendu privés les **nouveaux** envois. Les anciens sont toujours
servis par `LegacyUrl`, qui pointe un bucket public. La recopie vers le stockage
privé, puis la suppression de l'original, n'ont jamais été faites.

**Ce que ça coûte** : toute pièce d'identité, preuve de virement ou photo échangée
avant le 17 août reste lisible sans compte par quiconque possède l'URL — un
historique de navigateur ou un ticket de support suffit.

### VÉRIFIÉ LE 28 AOÛT — CONFIRMÉ, ET NON CORRIGEABLE PAR LE CODE SEUL

`ConversationQueries.ToSummary` sort toujours `a.LegacyUrl`, et le commentaire en
place dit exactement pourquoi masquer ce champ ne fermerait rien : les octets sont
dans un bucket public, les clients ont déjà les URL, et les cacher dans
l'application les rendrait invisibles sans les rendre inaccessibles.

**LE CORRECTIF EST OPÉRATIONNEL, PAS TEXTUEL.** Il faut recopier les fichiers vers
le stockage privé puis effacer les originaux — donc des identifiants de stockage,
un accès réseau au bucket, et une reprise vérifiable. Rien de tout cela n'est
faisable depuis le dépôt, et écrire un travail de fond qui EFFACE des fichiers
sans pouvoir l'exécuter ni le vérifier serait pire que de ne rien écrire.

**ET UNE PRÉCONDITION MANQUE.** `media-service` refuse de démarrer en production
sans stockage objet configuré, et aucune clé `OBJECTSTORAGE__*` n'existe
aujourd'hui dans le Secret ni le ConfigMap. La destination de la recopie n'est
donc pas encore configurée.

## 1.10 Ce que le déploiement révèle comme non implémenté

Trois chiffres, tous vérifiés sur les manifestes rendus et le code de la passerelle :

| Constat | Chiffre du 27 août | **Recompté le 28 août** |
|---|---|---|
| Services sans aucun manifeste Kubernetes | 10 sur 24 | **7 sur 20** |
| Routes de la passerelle visant un service non déployé | 12 sur 19 | **6 sur 19** |
| Sections de la console admin sans amont serveur | 8 sur 29 | **8 sur 29** (inchangé) |

**DEUX CHIFFRES SUR TROIS ÉTAIENT FAUX DÈS LE LENDEMAIN**, et pour deux raisons
différentes.

Le premier a bougé parce que le dépôt a bougé : `dispatch-service` (D42),
`tracking-service` et `proof-of-delivery-service` (D43) ont été retirés. 24 → 20
services portant un Dockerfile, 10 → 7 sans manifeste : les 3 de `food`, plus
`delivery`, `driver`, `delivery-pricing` et `route`.

Le second était **faux au moment où il a été écrit**. « 12 sur 19 » supposait que
douze adresses pointaient dans le vide ; le compte réel est **six** —
`SERVICES__DELIVERY`, `DELIVERYPRICING`, `DRIVERS`, `FOOD`, `FOODCART`,
`FOODORDER`. Les treize autres visent des services qui ont bien un manifeste.
L'exemple donné était lui aussi faux : `SERVICES__CATALOG` et `SERVICES__ORDER`
sont déployés tous les deux.

Ce qui reste vrai, et qui était le point : une requête vers l'un de ces six
traverse la passerelle et meurt sur la résolution DNS. Côté client, un 502 ou un
délai — jamais « ce service n'est pas déployé ».

Le troisième chiffre, lui, tient : le portail se décrit lui-même par trois états,
et `Batir()` déclare 21 sections prêtes, 0 à écrire, 8 sans amont — Marketing,
Taxes, Bannières, Notifications, Fraude, Outbox, Analytics, Monitoring.

**UNE DE CES HUIT EXPLICATIONS ÉTAIT PÉRIMÉE PAR NOTRE PROPRE MÉNAGE.** La section
Monitoring renvoyait vers `infra/docker/compose.monitoring.yml`, supprimé le
27 août. Corrigé : elle pointe désormais `infra/observability/` **et dit que ce
dossier n'a plus aucun lecteur** — les tableaux existent, la pile qui les servait,
non. Deux autres références mortes au même dossier ont été corrigées dans
`InternalRoutes.cs` et `catalog-service/Program.cs`.

---

# Partie 2 — Bugs

## 2.1 La clé de chiffrement de développement peut servir en production

**CONFIRMÉ**, et c'est le plus grave de cet audit.

`HBA.Shared.Infrastructure/DependencyInjection.cs:259-266` (`EstProduction`),
employée par `Security/SecretProtector.cs:131-152`

```csharp
configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"] ?? ""
```

Comparée à `"Production"`. **Si la variable est absente, le résultat est `false`.**

Or ASP.NET Core considère l'environnement comme `Production` par défaut quand cette
variable n'est pas posée — et le même dépôt le sait, puisqu'il emploie correctement
`builder.Environment.IsDevelopment()` ailleurs (`GrpcHostExtensions.cs:59`,
`TokenRevocationExtensions.cs`).

Conséquence d'un oubli de variable : `AesGcmSecretProtector` prend la branche
non-production et chiffre avec la **clé de développement codée en dur**, dérivée de
la phrase `"hba-development-secret-protection-key"`. Un `Console.WriteLine`
remplace l'exception fatale prévue.

**CE QUI LE MASQUE AUJOURD'HUI, ET POURQUOI CE N'EST PAS RASSURANT.**
`k8s/base/services/_service/deployment.yaml` pose explicitement
`ASPNETCORE_ENVIRONMENT: Production` — vérifié sur les manifestes rendus, les huit
déploiements l'ont. Le défaut est donc neutralisé **par une ligne de configuration**,
pas par le code. Retirer cette ligne, ou lancer un service hors de ces manifestes,
et les codes de réinitialisation de mot de passe repartent chiffrés avec une clé
publiée dans le dépôt.

Le garde fail-open de return-refund (1.4) repose sur exactement le même
raisonnement.

### RÉSOLU LE 28 AOÛT (D45)

`EstProduction` délègue à `EnvironnementDeploiement.EstProduction` : une variable
absente ou vide vaut désormais **production**, comme dans ASP.NET Core. Les six
copies de cette méthode ont été remplacées par la même délégation.

**ET UN SECOND POINT A ÉTÉ TROUVÉ EN VÉRIFIANT.** Le commentaire de
`DependencyInjection.cs` annonçait que le service « lève au démarrage » si la clé
manque. C'est faux : `ISecretProtector` est enregistré par une **lambda**, donc
construit à la PREMIÈRE RÉSOLUTION — c'est-à-dire à la première demande de
réinitialisation de mot de passe, pas au boot. Un service déployé sans
`Security:SecretProtection:Key` démarre normalement, passe ses sondes, sert son
trafic, et n'échoue que le jour où un utilisateur demande un code. Le refus est
correct ; ce qui était trompeur, c'était de croire qu'un déploiement réussi valait
vérification de la clé. Corrigé dans le commentaire.

## 2.2 N'importe quel compte connecté peut téléverser sur le dossier d'un autre

**CONFIRMÉ.** `media-service/.../MediaEndpoints.cs:57-67` et `:91-99`

La route `POST /api/v1/media` est montée sur `MapAuthenticatedGroup` — tout compte
connecté. Son propre commentaire affirme « cette route reste réservée aux
administrateurs ». **Aucun `.RequireAdmin()` n'est posé**, et le handler ne vérifie
que la présence d'un jeton.

Entrée précise : un acheteur appelle la route avec `OwnerType=Seller,
OwnerId=<un concurrent>` et rattache un fichier arbitraire au dossier de ce
vendeur.

**Ce que ça coûte** : pollution ou usurpation du dossier média de n'importe quel
acteur, pièces KYB comprises. Et le commentaire dit l'inverse du code — c'est
précisément ce qui fait qu'on ne relit pas la route.

### VÉRIFIÉ ET RESSERRÉ LE 28 AOÛT — LA PORTÉE RÉELLE EST AUTRE

**LA ROUTE N'A PAS ÉTÉ FERMÉE AUX ADMINISTRATEURS, ET ELLE NE DOIT PAS L'ÊTRE.**
L'application vendeur l'appelle directement, avec `OwnerType` valant tour à tour
Product, Seller, MenuItem, Store et Restaurant (`shop_data.dart`,
`catalog_data.dart`, `dish_wizard_screen.dart`, `activity_wizard_screen.dart`).
Poser `.RequireAdmin()` supprimerait le dépôt de photos de tout le portail
vendeur. Le commentaire promettait une garde qui aurait cassé le produit.

**CE QUE LA VÉRIFICATION A ÉTABLI SUR L'ATTAQUE.**

1. L'attaquant peut bien créer une ligne de média portant une appartenance
   mensongère.
2. Il ne peut PAS la rattacher au produit d'un concurrent : la route
   `AddMediaAsync` est gardée par `DenyUnlessProductOwnerAsync`.
3. Il ne peut PAS la faire lire : `ListMediaByOwnerQuery` n'a **aucune route**, et
   `IMediaModuleApi.ListByOwnerAsync` **aucun appelant** — vérifié sur tout le
   dépôt. Le téléchargement, lui, exige déjà d'être le déposant ou administrateur.

**L'exposition actuelle est donc du stockage pollué et des appartenances fausses
en base — pas une usurpation lisible.** Mais la méthode de listage existe, et le
jour où un écran « les pièces de ce vendeur » l'emploie, les fichiers forgés
apparaîtront dans le dossier de leur victime, pièces KYB comprises.

**ET UN DÉFAUT PLUS PROFOND A ÉTÉ TROUVÉ.** Le contrôle d'appartenance
d'`AddProductMediaCommandHandler` comparait `media.OwnerType`/`media.OwnerId` — deux
valeurs **déclarées par l'appelant au téléversement**, que media-service ne vérifie
pas et ne peut pas vérifier (§20). Un contrôle qui compare une valeur fournie par
celui qu'il contrôle ne contrôle rien.

**CE QUI A ÉTÉ FAIT.** `MediaAsset.CreatedByUserId` — déjà persisté depuis la
migration initiale, `IsRequired()`, et invisible de l'extérieur — est désormais
rendu par `MediaView`. Il vient du **jeton** : c'est le seul champ de ce contrat
que l'appelant ne choisit pas. `AddProductMediaCommandHandler` exige que le
déposant du média soit celui qui le rattache, et le paramètre absent vaut REFUS,
pas contrôle désactivé.

**ÉTENDU AU DOSSIER KYB LE MÊME JOUR.** `AddKybDocumentCommandHandler` et
`AddProductMediaCommandHandler` sont les **deux seuls** consommateurs
d'`IMediaModuleApi` du dépôt — vérifié. Le second porte la pièce d'identité d'un
commerçant, c'est-à-dire exactement ce que ce constat citait. Son encadré
annonçait que ses contrôles « ferment les deux exploitations » : c'est exact pour
les deux décrites — aucune ne permet de RÉÉCRIRE l'appartenance d'un média
existant — mais aucun ne ferme la CRÉATION d'un média neuf à l'appartenance
mensongère. L'encadré le dit désormais.

**ET UNE CORRECTION SUR MOI-MÊME.** La première version du contrôle exigeait
« le même compte » (`CreatedByUserId == RequestedByUserId`). C'était faux pour
toute boutique à plusieurs membres : un `SellerMember` téléverse les photos, un
autre monte la fiche, et la route accepte les deux — elle raisonne sur la
CAPACITÉ, pas sur la personne. Le contrôle les aurait départagés en refusant le
second. La règle retenue est que le déposant doit avoir LUI AUSSI le droit sur ce
vendeur : catalog interroge `HasCapabilityAsync`, seller-service résout en local
par `MemberAccessResolver`. Un contrôle de sécurité qui casse un parcours
légitime se fait retirer, et emporte avec lui la propriété qu'il défendait.

**RESTAURANT, BOUTIQUE ET PLAT NE VÉRIFIENT RIEN — ET CE N'EST PAS LE MÊME
DÉFAUT.** Vérifié : `restaurant-service` stocke `LogoMediaId`, `CoverMediaId` et
`ImageMediaId` sans jamais appeler media-service. Il n'y a donc pas de contrôle à
corriger, il n'y en a aucun. Non traité ici.

**CE QUI RESTE OUVERT.** Décider qui a le droit de déclarer quel propriétaire est
une décision de produit : toute règle stricte casse un parcours vendeur existant.
Elle n'est pas prise, et le commentaire de la route le dit maintenant.

## 2.3 Le gain vendeur n'est pas contre-passé à l'annulation d'une commande

**CONFIRMÉ.** `wallet-service/.../SellerEarning.cs:144-147` et
`ReverseEarningsOnOrderCancelledHandler.cs`

Sur un **retour**, `SellerEarning.Reverse(...)` est appelé : le gain sort du circuit
de règlement. Sur une **annulation de commande confirmée**, le handler débite bien
le portefeuille — vendeur, commission, frais PSP, livraison — mais n'appelle jamais
`Reverse`. Le `SellerEarning` reste au statut `Released` avec son montant net
intact.

Le lot de règlement suivant (`ListReleasedInPeriodAsync`) paiera donc ce gain au
vendeur, pour une commande annulée dont le solde a déjà été repris.

**Ce que ça coûte** : de l'argent versé deux fois, découvert au rapprochement
bancaire, sur des fonds déjà partis.

### RÉSOLU LE 28 AOÛT

`ReverseEarningsOnOrderCancelledHandler` appelle désormais `Reverse` sur les quatre
montants RESTANTS de chaque gain, et débite les soldes avec ce que la reprise a
**réellement inscrit** — l'ordre du handler de retour, pour que le grand livre ne
diverge pas du gain quand la reprise est bornée.

Un gain déjà entièrement repris par un retour antérieur est SAUTÉ, avec un
avertissement nommant le vendeur et le montant : le débiter reprendrait au vendeur
de l'argent qu'il n'a pas touché.

**Et le journal de fin de méthode mentait.** Il annonçait « N gain(s)
contre-passé(s) » alors qu'aucun ne l'était — la seule ligne qui aurait pu alerter
affirmait le contraire de ce qui se passait. Il compte maintenant les reprises
réelles, et les refus séparément. L'encadré de `SellerEarning.ReversedGrossAmount`,
qui déclarait ce chemin encore ouvert, a été corrigé dans le même geste.

## 2.4 Renommer une catégorie casse toute sa branche

**CONFIRMÉ.** `catalog-service/.../Categories/Category.cs:158-178` et
`UpdateCategoryCommandHandler.cs`

`Update()` recalcule le `Path` de la catégorie modifiée et d'aucun descendant. Le
commentaire ligne 156 l'admet : « les chemins des descendants ne sont pas répercutés
(évolution future) ».

Entrée précise : renommer « Animaux » (`/animaux`) en « Animaux domestiques ». Son
chemin devient `/animaux-domestiques` ; ses enfants gardent `/animaux/chiens`,
`/animaux/chats`. `ListDescendantsAsync`, qui cherche par préfixe, ne les retrouve
plus.

**Ce que ça coûte** : publication et dépublication en cascade n'atteignent plus la
branche, et les filtres par catégorie la perdent. Silencieusement. À noter que la
méthode nécessaire existe et **est** employée par `PublishCategoryCommandHandler` —
seul le renommage l'oublie.

### RÉSOLU LE 28 AOÛT

`Category.RebasePath` réécrit le chemin d'un descendant par **substitution de
préfixe**, et `UpdateCategoryCommandHandler` l'applique à toute la branche.

**L'ordre est le point délicat, et il est écrit dans le code.** `category.Path` est
la clé de recherche des descendants : muter d'abord et chercher ensuite ne
ramènerait rien — on chercherait sous le nouveau chemin, où personne n'habite
encore. La branche est donc chargée AVANT `Update()`, et les entités rendues sont
suivies par EF, si bien que la réécriture part dans la MÊME transaction que la
racine. Une branche à moitié déplacée serait pire que pas de cascade du tout.

Substitution de préfixe plutôt que reconstruction par `BuildPath` : reconstruire
supposerait de connaître le chemin du parent immédiat de chaque descendant, donc de
traiter la branche par profondeur en tenant une carte des chemins déjà réécrits. La
substitution donne le même résultat sans dépendre de l'ordre.

Aucun contrôle d'unicité n'est refait sur les descendants, et c'est juste : la
structure relative est conservée, donc si la nouvelle racine est libre — ce que
l'appelant vérifie déjà — aucun descendant ne peut entrer en collision. Un
descendant qui refuserait la réécriture fait ÉCHOUER la commande entière : rien
n'ayant encore été persisté, la catégorie reste telle qu'elle était.

## 2.5 Le tarif de livraison ignore ce qu'on lui demande

**CONFIRMÉ.** `delivery-pricing-service/.../EfDeliveryPricingStore.cs:28-31`

La requête filtre sur le statut et les dates, puis prend la priorité la plus haute.
`Scope`, `ServiceLevel` et `VehicleType` sont portés par `PricingRule` **et**
transmis par `CreateQuoteRequest` — et jamais employés dans la sélection.

Entrée précise : `EXPRESS + CAR` et `STANDARD + MOTORBIKE` reçoivent le même prix.

Aucun commentaire ne présente ce comportement comme voulu, contrairement aux autres
compromis du dépôt — c'est ce qui le distingue d'une simplification assumée.

### RÉSOLU LE 28 AOÛT — AVEC UNE RECTIFICATION

**L'audit se trompait sur `Scope`.** Il affirmait que les trois champs sont
« transmis par `CreateQuoteRequest` ». `Scope` ne l'est pas : l'enregistrement ne
porte aucun champ de portée. Il ne peut donc pas entrer dans la sélection, et
l'y faire entrer supposerait d'abord de décider ce qu'une portée désigne — une
zone ? un vendeur ? — puis de la faire remonter jusqu'ici. Non fait, et écrit dans
le code pour qu'on ne le croie pas fait.

**`ServiceLevel` et `VehicleType`, eux, sont désormais employés.** Le niveau de
service doit correspondre exactement — pas de joker, en inventer un poserait une
convention que la console d'administration ne sait pas produire. `VehicleType` est
nullable, et le nul EST le joker : c'est déjà le sens de la colonne. La grille qui
nomme le véhicule passe devant la générique, et `Priority` départage le reste.

**CE QUE ÇA CHANGE POUR L'EXPLOITATION.** Demander un niveau pour lequel aucune
grille active n'existe ne rend plus le prix d'un autre niveau : la création du
devis ÉCHOUE, avec un message qui nomme le niveau manquant. C'est voulu — un prix
emprunté à une autre grille est facturé au client et ne se voit nulle part, tandis
qu'un devis refusé se voit tout de suite. Le seul jeu de données semé ne contient
qu'une grille STANDARD / MOTORBIKE ; aucun appelant n'envoie aujourd'hui de niveau
de service sur une demande de devis, mais la console permet d'en créer, et ces
grilles prendront enfin effet.

## 2.6 Deux administrateurs peuvent payer deux fois le même retrait client

**PLAUSIBLE.** `wallet-service/.../WalletConfigurations.cs:158-232` —
`CustomerWithdrawal` **n'a pas** de `UsePostgresRowVersion()`, contrairement aux
quatre portefeuilles (lignes 43, 78, 125, 267, 430).

`MarkCustomerWithdrawalPaidCommandHandler:361-385` et
`RejectCustomerWithdrawalCommandHandler:405-470` font une lecture-modification-écriture
sans verrou, la garde d'état n'étant vérifiée qu'en mémoire.

Deux opérateurs traitant la même demande à quelques secondes d'écart la lisent tous
deux au statut `Requested` : l'un marque payé — le virement part —, l'autre rejette,
ce qui exécute `wallet.Restore(montant)` et recrédite le client du montant déjà viré.

**Ce que ça coûte** : versement en double, sans aucune exception pour le signaler,
là où les entités voisines sont protégées.

### VÉRIFIÉ ET RÉSOLU LE 28 AOÛT — PASSE DE « PLAUSIBLE » À **CONFIRMÉ**

`CustomerWithdrawal` était bien la seule entité mutable de ce module sans
`UsePostgresRowVersion()` : les quatre portefeuilles l'avaient, et `Withdrawal`
(retrait vendeur) aussi — l'audit ne l'avait pas listé, il est protégé.

**AUCUNE MIGRATION N'A ÉTÉ NÉCESSAIRE, ET C'EST LE POINT QUI SURPREND.** `xmin` est
une colonne **système** de PostgreSQL : elle existe déjà sur chaque ligne de
`customer_withdrawals` et porte le numéro de la transaction qui l'a écrite en
dernier. On ne l'ajoute pas, on la LIT. Seul le snapshot du modèle change — le
schéma de la base ne bouge pas d'un octet.

La traduction en 409 a été vérifiée des deux côtés : `ServiceMiddlewares` pour
HTTP, `TraductionDesErreursServerInterceptor` pour gRPC.

**CE QUE CE VERROU NE COUVRE PAS.** Il fait échouer la seconde écriture, il ne dit
pas laquelle des deux était la bonne : l'opérateur perdant reçoit un 409 et doit
relire la demande. C'est le comportement voulu — la seule alternative serait de
choisir à sa place entre « payé » et « rejeté », ce qu'aucune règle ne permet de
trancher.

**DEUX ENTITÉS RESTENT SANS VERROU, ET C'EST À SAVOIR.** `WalletTransaction` est un
grand livre en insertion seule — le verrou n'y a pas d'objet. `CustomerRefund`, en
revanche, est mutée (`MarkProcessing`, `Complete`, `Fail`) par la réconciliation.
Son profil de risque diffère — création protégée par un index unique sur
`IdempotencyKey`, mutation par un unique travailleur de fond — mais **cela n'a pas
été évalué en profondeur**, et poser un verrou là sans pouvoir l'éprouver
transformerait une course bénigne en remboursement bloqué, faute de reprise
automatique.

---

# Partie 3 — La couche de déploiement

Huit défauts trouvés en une journée sur du code d'infrastructure écrit mais jamais
exécuté. Six sont corrigés, deux restent ouverts.

## Corrigés le 26 et 27 août

| Défaut | Ce qui se serait passé |
|---|---|
| `kubectl apply -k` réécrivait les deux Secrets avec les valeurs vides du dépôt | Les huit pods repartaient avec une clé de signature vide, quelques minutes après une création de secret réussie. `apply` annonçait `secret/hba-platform configured` |
| Aucune identité gRPC dans `k8s/` | Pods `Ready`, sondes vertes, et **chaque appel inter-services** en `FailedPrecondition` |
| Kafka, trois ruptures indépendantes | Aucun `KafkaNodePool` → zéro broker créé ; `kafka:9092` ne résout pas ; pods Kafka sans étiquette, donc invisibles des deux politiques réseau |
| nftables n'acceptait pas les réseaux du cluster | `coredns`, `metrics-server` et `local-path-provisioner` en échec permanent, **nœud `Ready` malgré tout** — kubelet parle à l'API depuis l'hôte, pas depuis un pod |
| `OPENTELEMETRY__ENDPOINT` visait un collecteur inexistant | Échec de connexion toutes les quelques secondes, indéfiniment |
| Le rôle Ansible posait les sysctl sans charger `br_netfilter` | Le playbook échouait six fois pour une cause unique, en désignant un fichier au lieu d'un module |

## Ouverts au 27 août — les deux sont RÉSOLUS le 28, et le premier était pire

**La redondance déclarée en production n'existe pas.** `replicas: 2` est posé par
l'overlay prod, et les HPA restent à `minReplicas: 1`. Le HPA l'emporte : dès qu'il
cible un Deployment, c'est LUI qui écrit `spec.replicas` à chaque réconciliation.
Sous faible charge, les services retombent à un pod en quelques minutes. La
redondance est écrite, relue en revue, et jamais obtenue.

### RÉSOLU LE 28 AOÛT — ET LA LISTE DE L'AUDIT ÉTAIT FAUSSE

L'audit nommait « `api-gateway`, `identity` et `user` ». Le compte réel : l'overlay
prod patchait **dix** services, et `api-gateway` n'en faisait pas partie.

**PIRE : CINQ DE CES DIX PATCHES NE DÉSIGNAIENT RIEN.**

| Nom patché | Ce que la base produit |
|---|---|
| `commerce-service` | `cart-service` |
| `financial-service` | `payment-service` |
| `merchant-service` | `seller-service` |
| `delivery-service` | rien — hors du lot déployé |
| `food-service` | rien — hors du lot déployé |

Kustomize n'échoue PAS sur une cible sans correspondance : le build réussit, le
patch n'est appliqué à rien, et la sortie est identique à celle d'un patch qui a
mordu. Cinq services « critiques » n'ont donc jamais reçu leur second replica —
pas même au premier lancement — et rien ne l'a jamais dit.

**Les trois premiers sont le même défaut, et il a une cause connue.** La
plateforme porte deux vocabulaires : les modules se nomment par domaine
(commerce, financial, merchant), les dossiers de déploiement par dépôt (cart,
payment, seller). `InternalRoutes.cs` documente cet écart depuis des semaines ; il
a fini par produire cinq patches morts dans le fichier qui décide combien de pods
servent la production.

**Ce qui a été fait.** Les trois noms sont corrigés, les deux services hors lot
retirés (à rajouter avec le second lot, sous le nom que produira réellement leur
`namePrefix`), et chaque patch de `replicas` est désormais **collé** à un patch
`minReplicas` sur le HPA du même service — les deux ne peuvent plus diverger. Le
gabarit garde `minReplicas: 1`, qui reste le bon défaut hors production.

**ET UN CONTRÔLE A ÉTÉ AJOUTÉ**, parce que ce défaut est invisible à la lecture :
`scripts/check-k8s.py` résout maintenant chaque cible de patch contre les objets
que la base produit, et refuse celles qui ne désignent rien. Il tourne **sans
kustomize**, ce qui est tout son intérêt : sur un poste qui n'a pas l'outil — le
cas courant — ce fichier ne vérifiait rien du tout auparavant.

Deux autres cibles mortes ont été trouvées par ce contrôle : `Cluster/postgres`
dans dev **et** staging, un patch posant `/spec/instances: 1` sur une base gérée
par CloudNativePG qui n'existe nulle part. Reste du plan abandonné d'une base dans
le cluster ; celle de staging vit sur l'hôte, par apt. Retirés.

*(Le contrôle a d'abord produit un FAUX POSITIF sur `gateway-service` : il ne
regardait que `k8s/base/services/*`, alors que la passerelle vit dans
`k8s/base/apps/gateway/` et inclut le même gabarit. Corrigé avant tout report —
un contrôle qui invente des fautes se fait désactiver, et emporte les vraies.)*

---

**Le PDB rend la maintenance des nœuds impossible.** Les huit PDB posent
`minAvailable: 1`. Sur un service à un seul replica — c'est-à-dire tous, après le
point précédent — une éviction volontaire ferait tomber la disponibilité à zéro :
l'API la refuse, et `kubectl drain` attend indéfiniment sans que rien ne désigne le
PDB.

### RÉSOLU LE 28 AOÛT — ET LE COMMENTAIRE DU FICHIER DISAIT LE CONTRAIRE

Le gabarit `pdb.yaml` reconnaissait le problème puis le désamorçait : « Kubernetes
tranche en faveur du drain après un délai, mais l'opération devient bruyante pour
rien. » **La seconde phrase est fausse**, et c'est elle qui rendait le défaut
acceptable. Kubernetes ne tranche jamais : l'API d'éviction rend 429 tant que le
budget n'est pas satisfait, `kubectl drain` réessaie indéfiniment, et `--timeout`
fait échouer la commande — il ne l'autorise pas.

`minAvailable: 1` est devenu **`maxUnavailable: 1`**. Les deux se calculent
différemment :

    minAvailable: 1   → évictions autorisées = sains − 1
    maxUnavailable: 1 → évictions autorisées = 1 − (voulus − sains)

À **deux** replicas elles sont équivalentes — une éviction à la fois, jamais les
deux derniers pods ensemble, ce qui est la garantie recherchée. À **un** replica
elles divergent : l'ancienne autorisait zéro éviction et bloquait pour toujours, la
nouvelle en autorise une. À **trois et plus**, la nouvelle est même plus stricte.

Une seule modification, dans le gabarit : aucun overlay ne patche les PDB.

## Et un dossier orphelin

`infra/observability/` — sept fichiers de configuration Prometheus, Grafana, Loki,
OTel, Tempo — n'a plus **aucun lecteur** depuis le retrait de `infra/docker/`. Deux
d'entre eux n'en avaient jamais eu : `tempo/tempo.yml`, que le compose ne montait
pas, et `grafana/dashboards/`, qui ne contient qu'un README.

### TRAITÉ LE 28 AOÛT — LE DOSSIER RESTE, IL CESSE DE MENTIR

`infra/README.md` disait déjà la vérité. Le README **du dossier lui-même** ne la
disait pas : il s'ouvrait sur « CE DOSSIER EST UNE CONDITION DE LA DÉCOUPE, PAS UN
CONFORT », ce qui se lit comme une pile en service. C'est pourtant là qu'on lit,
quand on ouvre le dossier.

Il s'ouvre désormais sur l'absence de lecteur, avec un tableau fichier par fichier
disant lequel avait un lecteur et lequel est transférable vers Kubernetes. Réponse
courte : **un seul**, `otel/otel-collector.yml`, dont les pipelines se transposent
dans un ConfigMap. Les autres ne survivent pas au changement d'outil —
kube-prometheus-stack découvre ses cibles par `ServiceMonitor`, pas par
`scrape_configs`, et les charts provisionnent leurs propres sources de données.

**Pas supprimé, et la raison est explicite** : la liste de ce qu'il faut mesurer
est le seul endroit du dépôt où elle est écrite, et elle ne dépend pas de l'outil
qui la lit. La perdre coûterait plus que de la garder ; la garder en prétendant
qu'elle tourne coûterait davantage. Le jour où le lot Helm sera fait, ce dossier
devra être **retiré**, pas migré.

---

# Ce que cet audit a INFIRMÉ

Un constat porté depuis plusieurs jours ne résiste pas à la vérification, et il faut
le dire aussi clairement que les autres.

**« `InvoiceSummary` construit sans ses lignes ».** Aucune classe de ce nom n'existe
dans le dépôt. Les équivalents ont été relus ligne à ligne :
`OrderMapper.ToSummary` et `ToSellerSummary` (`OrderMapper.cs:10-152`) incluent
toujours `Lines` ; `OrderRepository` charge `.Include(o => o.Lines)` partout où un
total est calculé ; `ReturnMappings.ToDto` inclut `request.Items`.

Le constat le plus proche est réel mais différent : ce n'est pas un total sans
lignes, c'est un total **figé à zéro** — voir 1.5.

---

# Ce que cet audit ne couvre pas

**Le comportement à l'exécution.** Aucun test n'a tourné, aucun service n'a démarré.
Tout constat de la partie 1 et 2 vient d'une lecture. Les `PLAUSIBLE` le sont parce
que le chemin d'exécution n'a pas pu être confirmé sans exécuter.

**Le portail admin et les clients.** 8,1 Go de `clients/` n'ont pas été analysés
ici ; seul le décompte des sections sans amont est repris, et il vient du code, pas
d'une exécution.

**Les 162 anomalies du 21 août.** Elles ne sont pas reprises ici. Cet audit ne les
remplace pas et ne les périme pas.

**Les migrations et la base.** `DATABASE_AUDIT.md` du 21 août reste la référence :
23 contextes, 169 migrations. Rien n'a été revérifié de ce côté.
