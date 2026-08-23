# Audit de seller-service

Audit du 18 août 2026, après la clôture des sept lots du catalogue (`8119c9e`).

Référence : **§10.3 Merchant Service** du cahier des charges Backend v2, plus les
conventions posées par les lots 6 et 7 sur le socle partagé.

**Rappel de vocabulaire (décision D1).** Le cahier dit `merchants` / `outlets` ;
le dépôt dit `Seller` / `Store`, sur 83 fichiers, ses protos, ses topics et sa base.
La décision du 17 août est de **conserver le code et d'aligner la spec**. Cet audit
lit donc `merchants` comme `sellers`, et `outlets` comme `stores`. Les écarts
signalés plus bas sont des écarts de FOND, jamais de vocabulaire.

---

## Ce que le service fait déjà, et bien

83 fichiers, 6 422 lignes. L'agrégat `Seller` porte une machine à états à cinq
statuts — `Pending`, `Active`, `Suspended`, `Closed`, `PendingReactivation` — dont
la fermeture réversible et la demande de réactivation, qui ne sont **pas** dans le
cahier et qui répondent à un vrai besoin : un vendeur qui ferme ne doit pas perdre
son historique.

Le KYB a ses documents, son motif de refus, et un comportement soigné : une pièce
ajoutée à un dossier DÉJÀ vérifié n'invalide pas la vérification, et le motif du
refus précédent est effacé au nouveau dépôt — l'afficher ferait croire au vendeur
que sa correction a déjà été refusée.

Les boutiques ont leurs horaires, leur localisation, leur contact, et leur propre
cycle `Draft → Open → Closed → Suspended`.

**Ce que le lot 7 lui a déjà donné sans qu'il bouge** : télémétrie complète
(traces, métriques, journaux) et documentation OpenAPI sur `/docs`, parce que les
deux sont posées dans `AddHbaService`. C'est exactement l'effet recherché — le
socle a profité aux quatorze services, pas au seul catalogue.

---

## 1. Trois capacités du §10.3 qui n'existent pas

### 1.1 `merchant_members` — les membres d'équipe

Le §10.3 liste la table : `merchant_id, user_id, role, permissions_json`, et
annonce en tête de section « membres d'équipe, rôles métier ». **Aucun fichier du
service ne la mentionne.**

Ce que cela interdit concrètement : une boutique n'a qu'un seul compte. Le
propriétaire ne peut donner à personne l'accès à ses commandes ou à son stock —
ni à un vendeur en boutique, ni à un comptable. Pour une place de marché, ce n'est
pas une commodité : c'est ce qui sépare un commerçant seul d'une entreprise.

La même absence est déjà signalée ailleurs dans le dépôt, côté restaurant :
`GrantFoodPartnerRoleHandler` porte l'encadré « LE PERSONNEL N'EST PAS COUVERT —
seul `OwnerUserId` reçoit le rôle. Un cuisinier ou un caissier ajouté par le §8
n'en obtient aucun, et l'écran de cuisine — qui est fait POUR eux — leur reste
fermé. » C'est le même trou, vu de l'autre bout.

### 1.2 `CheckMerchantCapability` — le RPC absent

Le §10.3 exige trois RPC : `GetMerchant`, `GetOutlet`, `CheckMerchantCapability`.
Le proto en expose cinq, dont `ValidateSeller` — qui n'est **pas** un équivalent :

```proto
message ValidateSellerRequest { string seller_id = 1; }
message ValidateSellerResponse { bool valid = 1; string status = 2; string reason = 3; }
```

Il répond « ce vendeur est-il en règle », pas « ce vendeur a-t-il le droit de
faire CECI ». La différence porte : vendre, encaisser, livrer et gérer une équipe
ne s'ouvrent pas au même moment du parcours KYB, et chaque appelant reconstruit
aujourd'hui sa propre règle à partir de `status` et `kyb_status`. Deux appelants,
deux règles — et elles divergeront.

### 1.3 La soumission du dossier KYB n'est pas un geste

Le §10.3 expose `POST /api/v1/merchants/{id}/kyc/submit`, avec une liste de
`documentIds`. Le service n'a pas cette route. Le passage en revue est un **effet
de bord** de l'ajout d'une pièce :

```csharp
if (KybStatus is KybStatus.NotStarted or KybStatus.Rejected)
{
    KybStatus = KybStatus.InReview;
}
```

Conséquence : le dossier part en validation dès la PREMIÈRE pièce déposée. Le
vendeur qui téléverse sa carte d'identité un lundi et son registre de commerce le
jeudi occupe la file d'attente d'un administrateur pendant trois jours avec un
dossier incomplet — et l'administrateur qui l'ouvre ne peut que le refuser.

Le geste explicite manque aussi côté vendeur : rien ne lui dit quand il a fini.

---

## 2. Deux liens d'intégration rompus

### 2.1 `identity.user.registered` n'est consommé par personne

Le §10.3 l'annonce en toutes lettres : « Consomme : `identity.user.registered` ».
Le service ne déclare **aucun** `IIntegrationEventHandler` — zéro occurrence dans
les 83 fichiers.

C'est le même défaut, à la lettre, que celui qui a coûté cher à user-service :
« identity-service publiait consciencieusement `UserRegisteredIntegrationEvent`
dans Kafka, et personne ne l'écoutait. Le symptôme observé : un compte apparaît
dans `identity.users`, aucune ligne dans `users.profiles`. Rien n'échoue, rien ne
journalise. »

Reste à trancher CE QUE seller-service doit en faire — le cahier ne le dit pas.
C'est une question pour toi, elle est reprise en fin de document.

### 2.2 `merchant.kyc.submitted` et `merchant.kyc.approved` ne sont pas publiés

Le §10.3 les liste parmi les événements publiés. Le service publie onze
événements d'intégration, dont `SellerKybRejectedIntegrationEvent` — mais **ni la
soumission, ni l'approbation**.

L'asymétrie est révélatrice : on prévient quand le dossier est refusé, jamais
quand il est accepté. Notifications ne peut donc annoncer au vendeur que la
mauvaise nouvelle.

`outlet.status.changed` est couvert à moitié : `StoreOpened` et `StoreClosed`
existent, la suspension et la levée de suspension d'une boutique ne produisent
aucun événement. Un service qui filtre sur les boutiques ouvertes continuera donc
d'afficher une boutique suspendue jusqu'à son prochain rechargement complet.

---

## 3. Ce que les lots 6 et 7 ont posé ailleurs et pas ici

Aucun de ces points n'est un défaut du service : ce sont des conventions adoptées
après son écriture. Ils sont regroupés parce qu'ils se traitent d'un geste.

| Convention | État sur seller-service |
|---|---|
| Préfixe `/api/v1/` (D15) | ❌ `/api/merchants` — ce serait le **sixième** service aligné |
| Enveloppe §25 (D16) | ❌ **29** `Results.*`, **0** `ApiResults.*` — le « pire des deux mondes » |
| `consumer_inbox` §19.5 | ❌ absente |
| Idempotence des créations §25 | ❌ absente |
| Rôle `Seller` §22 | ❌ groupes en `MapAuthenticatedGroup` |
| Télémétrie, OpenAPI | ✅ **acquis** par le socle du lot 7 |

### Le piège du rôle `Seller`, à ne pas reproduire ici

Sur le catalogue, poser `MapSellerGroup` était sans danger. **Ici, ce serait une
panne.** `POST /api/merchants` — l'INSCRIPTION vendeur — vit dans le même groupe
authentifié que le reste :

```csharp
var sellers = app.MapAuthenticatedGroup("/api/merchants");
sellers.MapPost("/", RegisterSellerAsync);
```

Or le rôle `Seller` est greffé PAR cette inscription, via
`SellerRegisteredIntegrationEvent` → `GrantSellerRoleHandler`. Exiger le rôle sur
le groupe rendrait donc impossible de jamais le devenir : il faudrait être vendeur
pour pouvoir s'inscrire comme vendeur.

La route d'inscription doit rester ouverte à tout compte authentifié. C'est une
exception à documenter dans le code, pas à découvrir au premier test rouge.

---

## 4. Aucun test de domaine

`tests/HBA.Merchants.AuthorizationTests` existe, avec **5 tests d'autorisation**.
Il n'y a **aucun test unitaire du domaine**.

Or `Seller` porte cinq statuts, deux cycles imbriqués (compte et KYB), une
fermeture réversible et une demande de réactivation. Le catalogue a une machine à
états comparable — et ses tests unitaires ont trouvé, seuls, deux défauts réels :
la transition « correction » manquante du §4, et une publication qui se marquait
elle-même comme remplacée.

Ici, personne n'a jamais posé la question « peut-on suspendre un vendeur déjà
fermé ? » à autre chose qu'au code lui-même.

---

## Plan proposé

| Lot | Contenu | Pourquoi dans cet ordre |
|---|---|---|
| **1** | Tests du domaine `Seller` et `Store` : les deux machines à états, la fermeture réversible, le KYB | On n'aligne pas un service qu'on ne peut pas casser bruyamment. C'est le filet qui rend les lots suivants sûrs, et il trouvera probablement des défauts par lui-même |
| **2** | Soumission KYB explicite (§10.3) + les deux événements manquants (`submitted`, `approved`) + suspension de boutique | Un défaut fonctionnel ouvert : la file d'attente des administrateurs se remplit de dossiers incomplets |
| **3** | `/api/v1/merchants` + enveloppe §25 + idempotence + rôle `Seller` (avec l'exception d'inscription) | Transverse, à faire d'un coup, exactement comme le lot 6 du catalogue |
| **4** | `consumer_inbox` + consommation de `identity.user.registered` | Les deux vont ensemble : on ne branche pas un consommateur sans sa garde d'idempotence |
| **5** | `merchant_members` : table, agrégat, routes, et rôles d'équipe | Le plus gros morceau, et le seul qui ajoute une capacité entière. Il profite de tout ce qui précède |
| **6** | `CheckMerchantCapability` | Il n'a de sens qu'une fois les membres et leurs permissions en place — sans eux, il ne ferait que reformuler `ValidateSeller` |

---

## Deux décisions à prendre avant le lot 4

1. **Que doit faire seller-service de `identity.user.registered` ?** Le cahier dit
   qu'il le consomme, pas ce qu'il en fait. Trois lectures possibles : créer un
   marchand en brouillon pour tout nouveau compte (probablement faux — la plupart
   des inscrits sont des acheteurs) ; mémoriser le compte pour accélérer une
   inscription vendeur ultérieure ; ou ne rien faire, et corriger la spec.

2. **Le rôle des membres d'équipe est-il un rôle de la plateforme ou du
   marchand ?** `permissions_json` suggère un rôle interne au marchand, distinct
   des rôles JWT d'`identity-service`. Les deux modèles se défendent ; ils ne
   donnent pas la même surface d'API ni le même point d'application des
   autorisations.

---

## Écart assumé au §10.3 — `identity.user.registered` n'est pas consommé

**Tranché au lot 4.** Le §10.3 annonce que ce service consomme
`identity.user.registered`. Il ne le fait pas, et ce n'est pas un oubli.

L'inscription vendeur valide déjà le compte par un appel **gRPC synchrone** à
Identity — existence et e-mail confirmé compris. Le faire en asynchrone
reviendrait à accepter une inscription avant de savoir si le compte existe. Quant
à créer un marchand en brouillon pour chaque nouveau compte, la grande majorité
des inscrits sont des acheteurs : la table se remplirait de coquilles vides.

**Ce qui a été branché à la place, et qui manquait vraiment** :
`UserAnonymizedIntegrationEvent`. Il n'était consommé que par user-service, alors
que seller-service détient ce que la plateforme a de plus sensible —
`kyb_documents` pointe vers des cartes d'identité, des registres de commerce et
des documents fiscaux, déposés dans le bucket privé.

Un vendeur exerçait donc son droit à l'effacement, Identity anonymisait son
compte, user-service purgeait son profil — et sa pièce d'identité restait,
indéfiniment, sans que plus rien ne la relie à une personne identifiable.
C'est-à-dire dans l'état exact où plus personne ne peut la retrouver pour
l'effacer.

Le consommateur ferme le compte vendeur et fait nommer chaque pièce à effacer par
`MarkForDeletion`. Il est le premier de ce dépôt à n'avoir **aucune idempotence
naturelle** — réémettre relancerait un effacement déjà fait — ce qui rend sa garde
d'inbox load-bearing, et non préventive comme celles du catalogue.

---

## Ce que cet audit n'a pas vérifié

- **Rien n'a été exécuté.** Aucune requête n'a été passée au service, aucune base
  n'a été montée. Les constats viennent de la lecture du code et de son
  croisement avec le §10.3.
- Le comportement réel des routes de gouvernance — elles sont couvertes par 5
  tests d'autorisation, qui vérifient qui entre, pas ce qui se passe ensuite.
- L'application vendeur Flutter, déjà couverte par `AUDIT-APP-VENDEUR.md` et
  `AUDIT-VENDEUR-100.md`. Cet audit-ci regarde le SERVICE, pas son client.
