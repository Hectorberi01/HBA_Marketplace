# Ce qui reste à faire sur seller-service

Audit du 19 août 2026, après `55aa347`. Fait suite à `AUDIT-SELLER.md` (18 août),
dont il reprend le périmètre : le **service**, pas son application Flutter.

Cet audit-ci diffère du premier sur un point : il ne compare pas seulement le code
au §10.3, il suit les **appels sortants** — ce que les autres services reçoivent
réellement de seller-service. C'est là que se trouvent les deux constats les plus
lourds, et ni l'un ni l'autre n'est un manque du cahier.

---

## Ce qui est clos depuis le 18 août

| Constat de l'audit initial | État |
|---|---|
| 1.3 Soumission KYB explicite | ✅ `POST /kyb/submit` (`b23cfcb`) |
| 2.1 `identity.user.registered` non consommé | ✅ Tranché — écart assumé, `UserAnonymized` branché à la place |
| 2.2 `kyc.submitted` / `kyc.approved` non publiés | ✅ Publiés, plus la suspension de boutique |
| 3. Préfixe `/api/v1/`, enveloppe §25, inbox, idempotence, rôle `Seller` | ✅ (`0d92c52`) |
| 4. Aucun test de domaine | ✅ 65 cas unitaires + 8 cas d'intégration |
| 6.1 Boutiques dans la fiche vendeur | ✅ D20 (`55aa347`) |
| 6.2 `ApproveReactivation` | ✅ D19 (`55aa347`) |
| 6.3 Tests d'intégration | ✅ (`6de9bc9`) |

Il ne reste du plan initial que le **lot 5** — membres d'équipe et capacités. Mais
il ne faut pas le traiter en premier, et la suite dit pourquoi.

---

## 1. P0 — LE RETRAIT VENDEUR EST MORT, POUR TOUT LE MONDE

> ✅ **Corrigé le 19 août — voir D21.** Un RPC dédié `GetSellerPayout`, non mis en
> cache, et un type de retour où « vendeur inconnu » et « vendeur sans compte » ne
> se confondent plus. Les trois points de lecture de wallet-service sont migrés.
> Le §5 ci-dessous reste ouvert : cinq autres champs sont encore inventés.
>
> Le constat d'origine est conservé tel quel — il explique pourquoi le §5 n'est
> pas une élégance d'architecture.

**`POST /api/financial/wallets/sellers/{id}/withdrawals` ne peut pas réussir.
Aucun vendeur de la plateforme ne peut sortir son argent.**

Le chemin, en trois fichiers :

```csharp
// wallet-service — WalletCommands.cs:84
var seller = await _sellers.GetSellerAsync(command.SellerId, cancellationToken);
var account = seller?.Payout;
if (account is null || !WalletPayout.IsMobileMoney(account.Provider) || …)
    return Error.Validation("wallet.no_payout_account",
        "Aucun compte de versement Mobile Money configuré.");
```

```csharp
// shared/contracts/HBA.Merchants.Contracts.Grpc/MerchantsGrpc.cs:206
Rating: 0m,
SalesCount: 0,
Payout: null,          // ← ici
KybDocuments: []);
```

```proto
// shared/proto/merchant/v1/merchant.proto:55 — huit champs, et c'est tout
message SellerSummary {
  string seller_id = 1;  string user_id = 2;   string shop_name = 3;
  string status = 4;     string kyb_status = 5; string commission_rate = 6;
  optional string logo_url = 7; optional string description = 8;
}
```

`payment-service` héberge `wallet-service` et enregistre
`AddMerchantsGrpcClient` (`Program.cs:21`). `ISellerModuleApi` s'y résout donc sur
`MerchantsGrpcClient` — jamais sur l'implémentation in-process, qui ne vit que dans
seller-service. `seller.Payout` vaut `null` **quel que soit le vendeur**.

**Ce que voit le vendeur** : « Aucun compte de versement Mobile Money configuré »,
alors que son numéro MTN est bien enregistré et s'affiche sur son propre écran. Le
message l'envoie ressaisir un compte qui est déjà là. Il recommencera.

**Et le second étage est pire.** `ApproveWithdrawalCommandHandler`
(`WalletCommands.cs:200`) lit le même champ et, s'il est nul, appelle
`FailAndRefundAsync` : l'administrateur clique « approuver » et la demande est
**détruite avec un motif faux**. Toute demande déjà en base — créée avant la
régression, ou semée — se solde par un échec au premier geste d'administration.

Un troisième point, silencieux : `ListPendingWithdrawalsQueryHandler`
(`WalletQueries.cs:173`) se rabat sur `seller?.Payout?.Provider` pour les demandes
antérieures au figeage de destination. La file d'administration affiche donc ces
lignes **sans opérateur ni numéro** — l'admin approuve un virement dont il ne voit
pas la destination, ce qui est exactement le défaut que le figeage venait corriger.

> **La correction n'est pas dans wallet-service.** Elle est dans le proto et dans
> le mappeur. Voir le §5 : c'est le même défaut que les deux constats suivants.

---

## 2. P0 — LA PIÈCE D'IDENTITÉ D'UN AUTRE VENDEUR, ET LA SUPPRESSION ARBITRAIRE

> ✅ **Corrigé le 19 août — voir D22.** Contrôle posé aux deux bouts :
> seller-service vérifie propriétaire, nature et état avant de rattacher ;
> media-service vérifie ce qu'il s'apprête à détruire avant de le détruire. Le
> script de peuplement téléverse désormais un vrai fichier, et le faux média des
> tests est pilotable.
>
> **Reste ouvert et non couvert ici** : la route `POST /api/v1/media` est dans
> `MapAuthenticatedGroup` alors que son propre encadré affirme qu'elle « reste
> réservée aux administrateurs ». N'importe quel inscrit peut donc téléverser en
> déclarant le propriétaire de son choix. Ce n'est plus une fuite — un fichier
> qu'on dépose est le sien — mais c'est un remplissage de disque non borné, et le
> commentaire ment sur l'état réel de la route.

`Seller.AddKybDocument(type, mediaId)` porte cet encadré :

> « L'existence et l'appartenance du média ne sont pas vérifiées ici. C'est
> l'appelant — la couche qui voit les deux — qui contrôle que le média est de
> nature `SellerDocument` et qu'il appartient à CE vendeur. »

**Aucune couche ne le fait.** `AddKybDocumentCommandHandler` n'injecte que
`ISellerRepository` et `ISellerUnitOfWork`. seller-service n'a **aucun** client
média : ni `HBA.Media.Contracts.Grpc` dans un `.csproj`, ni `AddMediaGrpcClient`
dans `Program.cs`. L'« appelant » désigné est le **BFF Vendeur**, dont le
`Program.cs` annonce lui-même : « SQUELETTE — L'HÔTE DÉMARRE ET RÉPOND, MAIS
N'EXPOSE AUCUN CAS D'USAGE. »

Le dépôt le sait déjà, dans `scripts/seed-accounts.sh:486` : « …le domaine renvoie
ce contrôle à *la couche qui voit les deux*, et **aucune couche ne le fait
aujourd'hui**. […] en production, c'est un trou. »

**Deux exploitations, l'une et l'autre à la portée d'un vendeur inscrit :**

1. **Lire les papiers d'identité d'un concurrent.** `POST /kyb/documents` avec le
   `mediaId` d'autrui → accepté, la pièce est sur SON dossier, donc rendue par son
   propre `GET /merchants/{id}`. Il demande ensuite l'URL signée à media-service,
   dont la route dit explicitement qu'elle ne vérifie pas le droit métier — « le
   troisième [contrôle] appartient au service propriétaire », c'est-à-dire celui
   que personne n'a écrit. **C'est mot pour mot la faille que le passage de
   `FileUrl` à `MediaId` devait fermer.**
2. **Effacer n'importe quel média de la plateforme.** Rattacher puis retirer :
   `DELETE /kyb/documents/{id}` lève `KybDocumentRemovedDomainEvent(…, MediaId)`,
   que media-service transforme en `DeleteMediaCommand(e.MediaId)` **sans aucune
   comparaison de propriétaire**. Photos produit d'un concurrent, visuels de
   restaurant, dossier KYB d'autrui.

**Le gabarit existe déjà**, à cinq lignes près, dans catalog-service
(`AddProductMediaCommandHandler.cs:67`) : `_media.GetAsync(mediaId)`, puis refus si
`OwnerType`/`OwnerId` ou `MediaType` ne correspondent pas. Il manque ici une
`ProjectReference`, un `AddMediaGrpcClient`, et ce bloc.

**Le second effet de bord de l'exploitation 2 n'est pas dans media-service** :
`DeleteMediaOnKybDocumentRemovedHandler` fait confiance à l'émetteur. Refermer le
trou côté seller-service ne suffit donc que tant que seller-service est le seul à
émettre cet événement.

---

## 3. P1 — LE LIEU LOGISTIQUE D'UN AUTRE VENDEUR

> ✅ **Corrigé le 19 août — voir D23.** Le handler interroge
> `IInventoryModuleApi.GetLocationAsync` et exige `OwnerId == sellerId`. Un
> entrepôt plateforme est refusé : son `OwnerId` est nul par construction, et
> l'accepter rendrait la garde inopérante.

Même forme, même délégation dans le vide. `StoreCommands.cs:119` :

> « L'APPARTENANCE DU LIEU AU VENDEUR N'EST PAS VÉRIFIÉE ICI. […] Le contrôle
> est fait par l'appelant, qui voit les deux modules — **voir la route du BFF
> Vendeur**. »

Le BFF Vendeur est le squelette ci-dessus. seller-service n'a pas non plus de
client inventory.

`PUT /merchants/{id}/stores/{storeId}/location` accepte donc n'importe quel GUID :
le `SellerAddress` d'un concurrent, un `PlatformWarehouse`, ou un identifiant qui
n'existe pas. `Store.Open()` accepte ensuite la boutique, et l'identifiant part
vers delivery, qui construit un enlèvement coursier sur une adresse que le vendeur
ne contrôle pas. Le GUID inexistant, lui, ne se manifeste qu'**après le paiement
de l'acheteur**, sur la jambe coursier.

---

## 4. P1 — `IsSelling` EST TOUJOURS FAUX À DISTANCE (mine dormante)

> ✅ **Corrigé le 19 août — voir D23.** `bool is_selling` au proto : le serveur
> connaît la réponse, il l'envoie. Corriger la chaîne aurait marché aujourd'hui et
> se serait recassé au premier renommage d'énumération.

```csharp
// MerchantsGrpc.cs:219
IsSelling: string.Equals(store.Status, "Active", StringComparison.OrdinalIgnoreCase),
```

`StoreStatus` vaut `Draft | Open | Closed | Suspended`. **`"Active"` n'existe
pas.** Le prédicat réel est `Store.IsSelling => Status == StoreStatus.Open`.

Personne ne lit `IsSelling` à distance aujourd'hui — c'est pour cela que c'est un
P1 et non un P0. Le jour où catalog ou order le lira, **toutes les boutiques de la
plateforme s'éteindront pour lui**, sans une erreur, sans un journal.

La confusion vient d'un vrai piège : `"Active"` EST une valeur légitime… pour
`SellerStatus`. Le même mappeur traite les deux vocabulaires, et l'un a été
appliqué à l'autre.

---

## 5. La cause commune des §1, §2, §3 et §4

> ✅ **Corrigé le 19 août — voir D24.** Option **C** retenue : `SellerSummary` ne
> porte plus que les huit champs du proto, la vue riche vit dans `SellerDetail` et
> ne sort jamais du service. Le mappeur gRPC n'invente plus rien. La divergence de
> `SellerModuleApi.Map` — qui oubliait `KybRejectionReason` — disparaît par
> construction.

**Une interface, deux sémantiques.** `ISellerModuleApi` a deux implémentations de
production, et rien — ni le type, ni la DI — ne les distingue :

| Champ | `SellerModuleApi` (in-process) | `MerchantsGrpcClient` (distant) |
|---|---|---|
| ~~`Payout`~~ | le vrai compte | ✅ RPC dédié depuis D21 |
| `KybDocuments` | la liste | **`[]`** |
| `Rating` / `SalesCount` | les vraies valeurs | **`0`** |
| `Metadata`, `KybRejectionReason` | réels | **`null`** (par défaut de paramètre) |
| `Store.OpeningHours` | triés, lundi d'abord | **`[]`** |
| `Store.LogoUrl` / `Description` / `StatusReason` | réels | **`null`** |
| `Store.CreatedOnUtc` | la vraie date | **`DateTime.MinValue`** |
| ~~`Store.IsSelling`~~ | `Status == Open` | ✅ transporté depuis D23 |

Dans seller-service, `ISellerModuleApi` veut dire « le vendeur ». Ailleurs, il veut
dire « un objet en forme de vendeur, dont l'argent, les pièces d'identité, les
horaires et l'état ouvert/fermé ont été remplacés par des valeurs plausibles ».
`WalletCommands` a manifestement été écrit contre la première et a reçu la seconde
le jour où wallet a été replié dans payment-service.

**L'ironie mérite d'être notée** : `SellerSummary` porte depuis hier un encadré
expliquant qu'il ne faut PAS y ajouter les boutiques, parce que « le mappeur gRPC
laisserait VIDE : un appelant distant en conclurait que le vendeur n'a aucune
boutique » (D20). Le raisonnement était juste — et cinq champs souffraient déjà du
mal qu'il décrivait.

**Et le second fil** : trois encadrés du domaine délèguent un contrôle à « la
couche qui voit les deux », c'est-à-dire au BFF Vendeur, qui est un squelette. Une
délégation à un destinataire inexistant se lit comme une décision d'architecture ;
c'est un trou.

### Trois façons de fermer ça, à trancher

| | Ce que c'est | Ce que ça coûte |
|---|---|---|
| **A** | Compléter le proto (payout, rating, horaires, `IsSelling` corrigé) | Le contrat grossit ; le RIB et les pièces d'identité circulent entre services |
| **B** | Un RPC dédié `GetSellerPayout`, et faire ÉCHOUER les champs non transportés au lieu de les inventer | Le plus sûr : un champ absent lève au lieu de mentir. Touche les appelants qui lisent aujourd'hui du vide |
| **C** | Séparer les contrats : `SellerSummary` (local, complet) et `SellerRemoteSummary` (distant, seulement ce qui voyage) | Le compilateur empêche la confusion pour toujours. Le plus de travail, une seule fois |

**Recommandation : B pour le retrait (urgent), puis C.** B remet l'argent en
circulation cette semaine ; C empêche le prochain champ inventé. A seul reproduit
le problème au champ suivant.

---

## 6. P2 — La file de modération charge tout le fichier vendeurs

> ✅ **Corrigé le 19 août — voir D25.** Paginée, filtrable (`search`, `kybStatus`,
> `status`), avec facettes — et `SellerListItem` ne porte plus ni compte de
> retrait, ni pièces, ni informations légales.

```csharp
// SellerRepository.cs:35
=> await _dbContext.Sellers.Include(s => s.KybDocuments).OrderBy(s => s.ShopName).ToListAsync();
```

`GET /api/v1/merchants` (admin) est l'unique entrée de la file de validation KYB.
Elle rend **tous** les vendeurs, **avec toutes leurs pièces**, sans pagination et
sans filtre — pas même sur `KybStatus`, qui est pourtant la seule chose que le
modérateur cherche.

`ApiResults.Page` et `PagedResult` existent depuis le lot 6 ; cette route est la
seule liste du service à ne pas s'en servir. Une place de marché à mille vendeurs
rend une réponse de plusieurs mégaoctets, et le modérateur y cherche à l'œil les
quatre dossiers en revue.

---

## 7. P2 — `Rating` et `SalesCount` ne sont jamais alimentés

> ✅ **Corrigé le 19 août — voir D26.** Dénormalisés sur `Seller`, alimentés par
> `SellerRatingRecomputedIntegrationEvent` (review-service) et
> `OrderConfirmedIntegrationEvent` (order-service). On POSE une valeur recalculée,
> jamais un delta.
>
> **Deux choses restent ouvertes, et elles sont nommées ici pour ne pas être
> oubliées :**
>
> 1. **L'annulation ne fait pas redescendre le compteur.**
>    `OrderCancelledIntegrationEvent` ne porte aucun vendeur, et son producteur ne
>    tient pas les lignes en main. Le compteur reste trop haut jusqu'à la prochaine
>    vente confirmée du vendeur. Refermer cela demande d'enrichir l'événement côté
>    order-service — lot distinct.
> 2. **La vitrine publique n'existe toujours pas.** Aucun acheteur ne voit ces deux
>    valeurs : `GetStoreShowcaseAsync` rend « non implémenté » en dur, et
>    `SellerPublicSummary` / `ToPublic()` attendent un appelant. Portée retenue
>    pour ce lot : les compteurs seulement.

Les deux colonnes existent, sont persistées, et figurent dans
`SellerPublicSummary` — la **vitrine** d'une boutique. `Seller.UpdateRating` n'a
**aucun appelant** dans tout le dépôt, et rien n'incrémente `SalesCount`.

Toutes les boutiques affichent donc `0` vente et `0/5`. Un vendeur qui a écoulé
trois cents commandes est présenté à l'acheteur comme n'ayant jamais vendu — la
preuve sociale sur laquelle repose une place de marché est constamment fausse, et
elle l'est dans le sens qui décourage l'achat.

La donnée existe pourtant : order-service expose `GetSellerSalesCountAsync`. Il n'y
a ni consommateur d'événement de note, ni tâche de recalcul, ni appel.

---

## 8. Le lot 5 — membres d'équipe et capacités (inchangé)

`SellerMember`, `merchant_members`, `CheckMerchantCapability` : **aucune
occurrence** dans le dépôt, ni en C# ni dans les protos. Le §10.3 les demande.

Le contenu et les arbitrages sont décrits dans `PLAN-SELLER-FIN.md` (option A
retenue : un compte, un vendeur ; `MemberRole` énuméré plutôt que
`permissions_json`). Rien n'a bougé depuis.

**Une remarque nouvelle, née du §5** : le lot 5 ajoute un RPC au proto
merchant. Le faire AVANT de trancher A/B/C ci-dessus, c'est ajouter un étage à un
contrat dont on sait qu'il ment sur cinq champs — et `CheckMerchantCapability` est
précisément le RPC qu'on ne peut pas se permettre de voir répondre « non » par
défaut de remplissage.

---

## 9. Dettes datées, avec leur condition de retrait

| Dette | Condition de retrait, déjà écrite dans le code |
|---|---|
| Bascule KYB automatique au premier dépôt (`Seller.cs:206`) | Quand l'app vendeur enverra `POST /kyb/submit`. Un test la garde vivante exprès |
| `KybDocument.LegacyFileUrl` + sa colonne (`KybDocumentConfiguration.cs:18`) | Quand les pièces d'avant la bascule auront été reversées dans media-service |
| BFF Vendeur squelette | Bloque les §2 et §3 tant qu'aucune autre couche ne prend le relais |

Aucun `TODO`, `FIXME` ni `HACK` dans le service. Les seuls `Ecart_` restants sont
des **rappels historiques** dans les encadrés de tests, plus aucun test actif.

---

## Ordre proposé

| # | Contenu | Pourquoi là |
|---|---|---|
| **1** | Le retrait vendeur (§1), par un RPC de payout dédié | De l'argent que personne ne peut sortir. Rien ne justifie d'attendre |
| **2** | La propriété du média KYB (§2) — des deux côtés | Deux exploitations à la portée d'un inscrit, dont une lecture de pièces d'identité |
| **3** | `IsSelling` (§4) + la propriété du lieu (§3) | Petits, et le premier est une mine qu'on désamorce à froid plutôt qu'en incident |
| **4** | Trancher A/B/C et refermer le contrat (§5) | Ce qui empêche les §1, §2 et §4 de revenir sous une autre forme |
| **5** | Pagination et filtre de la file de modération (§6) | Rapide, et c'est l'écran que l'exploitation ouvre tous les jours |
| **6** | Membres + capacité (§8) | Après §4, pas avant : il ajoute un RPC au contrat qu'on vient de réparer |
| **?** | `Rating` / `SalesCount` (§7) | Décision produit avant décision technique : d'où vient la note, et à quelle fraîcheur |

---

## Ce que cet audit n'a pas vérifié

- **Rien n'a été exécuté.** Les tests d'intégration écrits hier n'ont pas encore
  tourné ici : les constats viennent de la lecture du code, du proto et des
  enregistrements de DI.
- Le §1 est établi par lecture de trois fichiers et de la table
  d'enregistrement DI. Il se confirme en une requête, dès qu'un environnement
  tourne — et il devrait l'être avant tout correctif, pour que la correction soit
  vérifiable.
- L'application vendeur Flutter, couverte par `AUDIT-APP-VENDEUR.md`.
- Les performances au-delà du §6 : aucun plan de requête n'a été lu.
