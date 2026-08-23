# RESTE À FAIRE — dans l'ordre

*Suivi d'exécution du `PLAN_DE_CORRECTION.md`. **Les vagues 0 à 9 sont closes** — la 8 avec une queue nommée (8.4/8.5), la 9 avec trois chantiers nommés dans sa section. Restent les décisions en attente ci-dessous. Les **vingt-deux** contrôles du dépôt passent.*

**LE COMPTE D'ANOMALIES CI-DESSOUS N'EST PLUS RECALCULÉ, ET IL NE FAUT PAS S'Y FIER.**

Il datait de la clôture de la vague 5 : « 63 sur 162 ». Les vagues 6, 7 et 8 en ont fermé
beaucoup, mais le relevé lot par lot a aussi montré que **le décompte lui-même était
faux dans les deux sens** — quatre des dix « index manquants » du lot 8.1 n'en étaient
pas, deux des huit contraintes d'unicité du 8.2 étaient mal énoncées, deux des trois
`CHECK` du 8.7 auraient écrit un bug, et le 8.9 a trouvé une représentation de l'argent
que l'audit ne comptait pas. Un nombre agrégé donnerait une fausse précision.

**Ce qui fait foi, c'est la liste nominative des points ouverts, en fin de document.**
Trois d'entre eux sont plus lourds que tout ce qui reste en vague 9 :

1. ✅ **`DeliveryApi.LookupQuote`** — CRITICAL n° 1, **fermé le 22/08** en option (B) :
   order-service et food-order-service interrogent delivery-pricing directement. Voir la
   section dédiée plus bas, et ce qu'elle laisse ouvert.
2. ✅ **L'identité d'appelant gRPC** (§10.1/§10.2) — **fermée le 22/08** par une signature
   asymétrique P-256 par hôte plus une table d'autorisations engendrée depuis le code
   (**D41**). `RefundPayment` passe de 24 appelants possibles à 1. Ce que cela ne couvre
   pas — le réseau en clair, le rejeu dans la fenêtre de 30 s, la granularité au paquet de
   contrats — est écrit dans D41 et dans la section dédiée plus bas.
3. ✅ **Les réservations de stock** — **fermée le 22/08** par une purge périodique des
   réservations en état terminal, et **non** par un `Include` filtré : voir plus bas
   pourquoi le filtrage aurait rouvert un double décrément de stock.

Un quatrième point, plus lourd que ce qu'il en paraît, reste **ouvert** :

4. **Les espaces de noms ne suivent pas les projets** — 20 paires projet↔espace de noms
   discordantes, 77 noms de types dupliqués, 257 fichiers concernés. Le renommage est
   bloqué par une conséquence qui n'est pas visible depuis le code : `outbox_messages.Type`
   contient le nom **complet** du type, espace de noms inclus, et `OutboxProcessor` le
   résout par `Type.GetType`. Renommer rendrait **irrésolvables toutes les lignes d'outbox
   déjà en base** — c'est-à-dire des paiements, des remboursements et des libérations de
   stock. Le prérequis est posé (`EventTypeName.Resolve` tolère désormais un repli sur le
   nom simple **unique**, et refuse l'ambiguïté en la nommant) ; **le renommage lui-même
   reste à faire**, et il devra être vérifié avec `check-migrations.py`, qui compare
   maintenant le `ModelSnapshot` aux types du code.

**Les vagues 0, 1 et 2 sont closes** — ISSUE-006 exceptée, bloquée en amont (aucun paiement food n'existe), et ISSUE-005 à confirmer par un test.

*(21/08 : ISSUE-071 et le jeton d'invitation vendeur qui lui était rattaché, puis ISSUE-022, 025 et 041.)*

*(28/08 : lot 3.2 — le parcours de remboursement, de bout en bout. ISSUE-009, 011, 012, 013, 014, 049.)*

*(30/08 : lot 3.3 — les compensations financières. ISSUE-015, 050, 051. Puis lot 3.4 — l'appel externe avant persistance. ISSUE-074, 032.)*

*(31/08 : lot 3.5 — le stock. ISSUE-075, 031, 045, 046. **Vague 3 close.**)*

*(01-02/09 : vague 4 — les décisions structurantes. ISSUE-033, 052, 053 (promotions, D28) et ISSUE-026, 027 (`SellerOrder`, D29). **Vague 4 close.**)*

*(03-05/09 : vague 5 — la livraison. ISSUE-069, 070 (D34), 028, 056, 057, 058 (D35), 029, 030 (D36). ISSUE-007 partiellement. **Vague 5 close.**)*

Ce n'est pas un mauvais rendement : les vingt-cinq traitées sont celles qui saignaient sans attendre — fuites de données personnelles, accès anonymes, services qui ne démarraient pas, secrets en clair sur le bus. Le reste est plus profond et, pour l'essentiel, **empilé derrière une seule correction** : le bus d'événements.

---

## Ce qui est fait

| Vague | Lot | Anomalies closes |
|---|---|---|
| 0 | 0.1 | ISSUE-071 — purge d'outbox, puis chiffrement des secrets (voir la dernière ligne) |
| 0 | 0.2 | ISSUE-034 — la passerelle démarre en exécution locale |
| 0 | 0.3 | ISSUE-010, 054, 055 — les bouchons refusent de démarrer en production |
| 0 | 0.4 | ISSUE-065, 066, 067 — migrations manquantes et inertes, `HBA.sln` |
| 0 | 0.5 | les onze squelettes portent un bandeau |
| 1 | 1.1 | ISSUE-020, 021 — médias : propriété et durée de signature |
| 1 | 1.2 | ISSUE-017, 018, 019 — return-refund, vendeur **et** client |
| 1 | 1.3 | ISSUE-016, 023 — dix services authentifiés |
| 1 | 1.4 | ISSUE-037, 022 — limiteur de débit ; révocation vérifiée à la passerelle, échec ouvert et bruyant |
| 1 | 1.5 | ISSUE-024, 036, 025, 041 — suspension effective, éviction de cache, offres retirées de la vente pour un vendeur sanctionné **et** pour une boutique fermée |
| 1 | 1.6 | ISSUE-035, 038, 039 — fuite inter-vendeur, routes de paiement |
| 1 | tests | quatre suites ajoutées — médias, return-refund, révocation de jeton, suspension du catalogue. Portée exacte plus bas |
| 2 | 2.1 | ISSUE-008 — l'inbox de consommation est généralisée : quinze services, garde centrale dans le dispatcher |
| 3 | 3.1 | ISSUE-072, 073 — unicité en base sur les objets financiers : référence PSP, remboursement externe, clé d'idempotence des retours, et une clé posée sur `customer_refunds` qui n'en avait aucune. Plus la traduction 23505 → 409, sans laquelle chaque garde remontait en 500 |
| 2 | 2.4 | KAFKA §11 — contrats additifs (D32) : `EventVersion` cesse de mentir, le consommateur refuse ce qu'il ne sait pas lire, et `check-event-contracts.py` rend toute rupture visible en revue |
| 2 | 2.5 | GRPC §11 — la corrélation métier traverse l'outbox, Kafka et le gestionnaire ; la causalité aussi |
| 2 | 2.3 | ISSUE-002, 003, 004 — le paiement confirme enfin la commande, l'échec libère le stock, la commande food ouvre son ticket. **ISSUE-005 à confirmer par un test**, **ISSUE-006 bloquée** (aucun paiement food n'existe) |
| 2 | 2.2 | ISSUE-001 — un seul catalogue de sujets lu des deux côtés (D31) ; trois événements déclarés en double tranchés ; topics k8s régénérés depuis le code ; `check-kafka-topics.py` |
| 3 | 3.2 | ISSUE-009, 011, 012, 013, 014, 049 — le remboursement existe enfin : il s'exécute (worker), il ne se verse pas deux fois, il est plafonné depuis la commande, et la commande retient ce qu'un retour lui a retiré. **Puis D33 :** l'argent revient au client sur son portefeuille, le virement Mobile Money étant une demande distincte validée à la main |
| 3 | 3.3 | ISSUE-015, 050, 051 — les trois gestes de compensation étaient ÉCRITS et jamais appelés : un virement de lot refusé se compense enfin, un gain remboursé cesse d'être payable, et l'invariant comptable a la contrepartie qui lui manquait pour être appliqué |
| 3 | 3.4 | ISSUE-074, 032 — l'appel externe ne précède plus la persistance : le remboursement client s'inscrit avant que l'argent parte, le checkout compense ses réservations si la base lâche, l'annulation et l'inspection persistent d'abord |
| 3 | 3.5 | ISSUE-075, 031, 045, 046 — la réservation de stock est idempotente, elle porte un statut, les expirées sont enfin balayées, et un SKU sans ligne de stock n'est plus vendable sans limite |
| 4 | 4.1 | ISSUE-033, 052, 053 — les promotions existent enfin : un financeur exprimable en part, une tarification qui appelle vraiment promotion-service, un balayeur de budget, et la garde d'appartenance que l'absence de propriétaire rendait infondable |
| 4 | 4.2 | ISSUE-026, 027 — `SellerOrder` est construit : le vendeur confirme, prépare et déclare prêtes SES commandes, avec les cinq permissions qui ne gardaient rien |
| 5 | 5.4 | ISSUE-069, 070 — le cycle de références entre services livraison est coupé : la cause était cinq fichiers mal classés. `check-dockerfiles.py` passe de 5 manquants à **0**, dernier échec persistant du dépôt |
| 5 | 5.1 · 5.3 | ISSUE-028, 056, 057, 058 — une course ne peut plus être acceptée deux fois, l'OTP n'est plus `123456`, aucune course ne naît sans politique de preuve, le suivi est fermé. **Et `ConcurrencyExceptionHandler`, cité par cinq endroits, n'avait jamais existé : tout verrou optimiste du dépôt ressortait en 500** |
| 5 | 5.2 | ISSUE-029, 030 — driver-service existe : identité, base, dossier, vérification. Le `DefaultDriverId` codé en dur faisait que **tous les livreurs étaient le même livreur**. La position alimente enfin le cache, donc les courses peuvent être proposées |
| — | ISSUE-071 | **fermée par l'option (A)** — les trois secrets qui traversaient le bus sont chiffrés (AES-GCM) : code de vérification, code de réinitialisation, jeton d'invitation vendeur |

---

## Le lot 3.2 est clos — deux de ses trois questions sont tranchées (D33)

**HECTOR a tranché le 28 août 2026**, et la décision est consignée en `docs/DECISIONS.md` § D33 :
le remboursement d'un client **crédite son portefeuille** ; le virement vers son Mobile Money
est une **demande distincte**, exécutée et marquée payée **à la main par un administrateur**,
FedaPay n'exposant aucune API de remboursement.

### Ce que cela a fermé

| Question ouverte | Réponse |
|---|---|
| Le canal manquant entre return-refund et le portefeuille | Il ne passe pas par return-refund : `RefundPaymentCommandHandler` crédite le portefeuille quand `IPaymentGateway.SupportsRefund` est faux. La règle vaut du même coup pour l'annulation de commande et le remboursement administratif direct |
| `Msisdn` et `Provider` absents d'`OrderReturnContext` | La question disparaît : on ne vire plus au moment du remboursement. Le client saisit son numéro **au moment de sa demande de virement**, et il est figé sur la demande |
| `Payments:AllowGatewaysWithoutRefund` doit-il exister ? | Il a disparu, ainsi que le refus de démarrage qu'il contournait. Sa prémisse — « le client ne sera jamais remboursé » — a cessé d'être vraie |

Ce qui a été construit : `CustomerWallet` et `CustomerWithdrawal` (wallet-service), le contrat
`ICustomerWalletApi`, la migration `20260829000100_PortefeuilleClient`, les routes client
`/api/wallet/me…` et la file d'administration `/api/wallet/customer-withdrawals/…`.

### Ce qui reste ouvert

**1 · La dette envers les clients n'est rapprochée de rien.** La somme de
`customer_wallets.AvailableBalance` est de l'argent dû, et rien ne la confronte à la trésorerie
réelle. C'est un rapport d'exploitation à écrire ; il n'est pas dans ce lot. D33 le nomme.

**2 · Aucune règle de péremption des soldes.** Un client qui ne demande jamais son virement garde
son solde indéfiniment. **Ne pas en poser une sans avis juridique** : un solde de portefeuille est
une créance.

**3 · Les retours antérieurs ne sont pas rétroprojetés** (ISSUE-014). Les tables
`ordering.order_return_settlements` naissent vides ; les commandes ayant connu un retour avant la
migration `20260828000600` resteront trop permissives. Le rattrapage exige de lire la base de
return-refund, ce qu'une migration d'order-service ne peut pas faire.
**À trancher :** écrire le script hors migration, ou assumer par écrit le volume d'avant.

**4 · Aucune route d'administration pour consulter le portefeuille d'un client donné.** Seules les
routes `/me` existent, délibérément : c'est ce qui rend la faille ISSUE-017/018 impossible par
construction. Le support la réclamera ; l'ajouter doit être une décision avec sa propre garde
d'appartenance, pas un effet de bord.

**5 · `MarkPaid` fait entièrement confiance à l'administrateur.** La référence externe obligatoire
rend le rapprochement *possible*, jamais automatique. Il n'y a ni double validation ni plafond sur
un virement manuel.

---

## Ce que le lot 3.3 laisse ouvert

Trois anomalies de la même famille : un mécanisme de compensation écrit dans le domaine,
et pas un seul appelant. Trois fils sans courant. Ils en ont un maintenant — mais chacun
laisse une frange nommée.

**1 · `ReverseEarningsOnOrderCancelledHandler` a le même défaut qu'ISSUE-050.** Il débite le
portefeuille du vendeur et **ne touche jamais au statut du gain** : une commande annulée
laisse ses gains payables, qu'un lot ultérieur reversera. `SellerEarning.Reverse(...)` s'y
appliquerait tel quel, en reprise totale. **Ce n'est pas couvert par le lot 3.3 ; cela mérite
une ISSUE à part entière.**

**2 · Le motif d'un virement refusé n'est pas persisté.** `Payout` n'a ni `FailureReason` ni
`FailedAtUtc` : le motif ne vit que dans le journal, et la file d'administration ne peut pas
expliquer un échec.

**3 · Le relevé vendeur rend des montants nets des reprises, sans les montrer.** Un vendeur
voit un total baisser sans qu'aucune ligne ne le dise, tant que la reprise reste partielle.
Le rendre lisible suppose d'ajouter `Reversed*` aux contrats `SellerStatementSummary` et
`SellerStatementLine` — donc de toucher aux applications clientes.

**4 · La contre-passation d'un retour reste hors de l'invariant comptable**, et c'est un
refus motivé, pas un oubli : la borne par montant de `SellerEarning.Reverse` et la part
« frais de port » d'un remboursement le feraient échouer sur des retours parfaitement
normaux. Lever la réserve demande deux changements de modèle — borner la reprise sur le
BRUT et déduire les trois autres, et sortir la part « port » du compte livraison.

**5 · L'opération comptable est facultative.** `WalletMutations.Ouvrir()` est un opt-in :
un NOUVEAU chemin d'écriture peut naître sans contrepartie, et l'invariant ne le verra pas.
Le rendre obligatoire aurait signifié convertir d'un seul geste les quinze sites d'écriture
du module, sans compilateur pour le vérifier.

**6 · Les montants « restants » d'un gain ne sont pas traduisibles en SQL.** Toutes les
sommes qui les utilisent se font en mémoire, après matérialisation. C'était déjà le cas ;
c'est désormais une contrainte — retirer un `ToListAsync` casserait la traduction.

---

## Découvert en corrigeant le lot 3.4 — hors audit

Ces trois constats ne viennent pas de l'audit du 21 août : ils sont apparus en
travaillant. Ils sont consignés ici parce qu'aucun d'eux n'a de numéro d'ISSUE, et
que deux d'entre eux sont plus graves que ce que le lot corrigeait.

### 1 · Deux des quinze contrôles n'avaient JAMAIS lu un seul fichier

`check-grpc-stubs.py` et `check-event-consumers.py` balayaient `ROOT/src` — **un
dossier qui n'existe pas** dans ce dépôt, où le code vit sous `services/`, `shared/`
et `apps/`. `os.walk` sur un dossier absent ne lève pas : il n'itère simplement
jamais. Les deux affichaient donc un compteur à zéro depuis leur écriture, et ce
zéro se lisait « rien à signaler » alors qu'il voulait dire « je n'ai rien regardé ».

Corrigé, et surtout **rendu impossible à refaire** : le module partagé
`scripts/racines_source.py` lève si une racine déclarée manque ou si le balayage
complet rend zéro fichier, et le contrôle sort alors en code 2 avec
« CE CONTRÔLE N'A RIEN PU ANALYSER ». Un contrôle qui ne peut pas contrôler doit le
dire, pas rendre zéro.

Vérification faite sur les treize autres : tous lisent réellement des fichiers.

### 2 · Trois adaptateurs gRPC de return-refund sont des bouchons intégraux

Révélés par le contrôle réparé. Aucun n'a de champ client : ils ne peuvent parler à
personne, par construction.

| Adaptateur | Conséquence métier |
|---|---|
| `InventoryGrpcClient.ProcessReturnedStockAsync` | **la marchandise retournée n'entre jamais en stock** — le retour est inspecté, déclaré remettable en rayon, et rien n'arrive à l'inventaire |
| `DeliveryGrpcClient.CreateReturnDeliveryAsync` | **aucune course d'enlèvement n'est créée** — le client reçoit un numéro de suivi imaginaire, aucun coursier ne vient |
| `MediaGrpcClient.ValidateMediaAsync` | ne vérifie que « chaîne non vide » : ni l'existence du média, ni son propriétaire. N'importe quel identifiant est accepté comme preuve photo, **y compris le média d'un autre client** |

Traités selon la règle que le dépôt s'est déjà donnée aux vagues 0.3 et 3.2 : la
limite est **déclarée** dans le code, elle **refuse le démarrage en production**, et
elle **s'annonce bruyamment** ailleurs. Les vrais appels gRPC ne sont PAS
implémentés — cela demande les protos, les serveurs en face et des décisions de
contrat qui ne sont pas dans ce lot.

**Conséquence à assumer côté déploiement : return-refund-service refuse
désormais de démarrer en production** tant que ces trois adaptateurs sont des
bouchons. C'est l'intention — un service qui prétend remettre du stock en rayon sans
le faire est pire qu'un service arrêté — mais c'est un changement de comportement.

**La liste du garde-fou est écrite à la main** et ne se met pas à jour seule :
implémenter un adaptateur sans retirer sa ligne bloquerait la production pour rien.
`check-grpc-stubs.py` sert de filet, pas de garantie.

### 3 · Le motif « appel externe avant persistance » avait un quatrième site

L'audit en nommait trois (`CustomerRefundCommands`, `PlaceOrderCommandHandler`,
`OrderLifecycleCommands`, `ReturnLifecycleCommands`). `ReturnLifecycleCommands` en
contenait en réalité **deux** — l'expédition de retour et l'inspection. Les deux sont
traités.

---

## Ce que le lot 3.5 laisse ouvert

**1 · ISSUE-047 est le prolongement direct de ce lot, et elle est toujours ouverte.**
`StockDepletedIntegrationEvent` et `StockReplenishedIntegrationEvent` sont publiés
correctement. Leur seul consommateur est une **notification**. **Aucune offre ne passe
jamais `OutOfStock`, ni ne revient en vente.** Le `ReactivateOffersOnStockReplenishedHandler`
que cite un commentaire d'`OfferStatus` **n'existe pas**. Tout est prêt côté catalog —
`IProductOfferRepository.ListBySkuAsync` a même été écrite POUR ce handler, et sa
documentation le dit — mais personne ne l'appelle.

Conséquence concrète : le balayeur d'expiration rend du stock à la vente, et la vitrine
n'en sait rien. Dans l'autre sens, une rupture n'affiche jamais de rupture.

**Un piège à traiter en même temps** : `StockDepleted` est levé par ARTICLE, c'est-à-dire
par couple (SKU, emplacement). Un SKU épuisé à Cotonou mais disponible à Parakou ne doit
pas retirer l'offre de la vente. Le consommateur devra vérifier la disponibilité GLOBALE
avant de marquer, pas se fier à l'événement seul.

**2 · La saisie de stock n'est pas obligatoire à la publication d'une offre.** Depuis la
décision sur ISSUE-046, une offre sans ligne de stock est invendable — mais rien ne le dit
au vendeur au moment où il publie. C'est une correction de catalog-service.

**3 · `stock_reservations` ne décroît plus.** On marque au lieu de supprimer, et les
repositories chargent `Include(Reservations)` en entier pour calculer `Reserved`. Sur un
SKU très vendu, l'agrégat finira par peser à chaque lecture. Il faut une **purge datée** —
que garde-t-on, combien de temps ? C'est une décision d'exploitation.

**4 · Le balayeur ne tient pas la montée en charge.** Pas de `SELECT … FOR UPDATE SKIP
LOCKED` : deux répliques d'inventory-service liraient le même lot. `xmin` empêche la double
écriture, pas le travail en double. Même contrainte que l'outbox.

**5 · `OrderLifecycleCommands` boucle encore ligne par ligne** pour confirmer et libérer.
C'est sûr aujourd'hui — les deux opérations sont idempotentes — mais le regroupement par
`(Sku, emplacement)` appliqué au checkout y serait plus honnête.

**6 · La branche de transition d'ISSUE-046** (`ConfirmReservationAsync` qui laisse passer
un SKU sans stock avec un journal `Critical`) doit être retirée quand la file des commandes
antérieures au déploiement est vidée.

---

## Un garde-fou qui VOYAIT l'anomalie et se taisait (02/09)

`check-braces.py` a rendu « 0 anomalie » sur un fichier que le compilateur a refusé avec
**dix-neuf erreurs**. La cause : un `\n` écrit comme un vrai retour à la ligne à l'intérieur
d'une chaîne non verbatim — CS1010, « saut de ligne dans la constante ».

Ce n'est pas que le contrôle ignorait ce cas : **il le détectait déjà**. Son tokéniseur porte
depuis toujours la ligne `if not verbatim and c == '\n': return i` — écrite pour ne pas avaler
le reste du fichier après une chaîne non terminée. Il rendait la main, proprement, **et ne
disait rien**. Les accolades restaient équilibrées de part et d'autre, donc le comptage ne
pouvait rien voir : une chaîne coupée retire exactement le même contenu qu'une chaîne intacte.

C'est le même mode de défaillance que les deux contrôles aveugles du lot 3.4, en pire : ceux-là
ne regardaient nulle part, celui-ci **regardait au bon endroit et se taisait**.

Corrigé : le cas est désormais signalé, en tête des anomalies puisqu'il est la cause dont le
reste découle. Vérifié sur un fichier fautif fabriqué pour l'occasion — le contrôle le rejette,
et laisse passer la même chaîne écrite en verbatim.

Balayage du dépôt entier après correction : **aucune autre occurrence**.

---

## Quinze tests qui n'éprouvaient rien (22/08)

`HBA.Delivery.UnitTests` : **15 échecs, une seule cause**. La fabrique `UneCourse.Arret`
construisait chaque arrêt avec `« +22997000001 »` — un numéro d'AVANT la migration béninoise
de 2024. `BeninGeography.NormalizePhone` exige `LocalPhoneLength` = **10** chiffres après
l'indicatif et refuse l'ancien format à 8, délibérément : un numéro à 8 chiffres n'aboutit
plus, et l'accepter en silence reviendrait à livrer un colis que personne ne pourra annoncer.

`DeliveryStop.Create` rendait donc un échec, et les quinze tests tombaient sur l'assertion de
la fabrique — **pas sur ce qu'ils prétendaient éprouver**. Acceptation unique, politique de
preuve, session livreur : aucun de ces contrôles n'était réellement exercé. Un test qui échoue
dans sa fabrique ne dit rien du code de production, ni en bien ni en mal.

Corrigé sur la forme déjà utilisée par `DossierLivreurTests` et `SessionLivreurTests`
(`« +2290197000042 »` — ancien numéro préfixé de « 01 »). Deux inventaires de test tombaient
sous la même règle par `Address.Create` et sont corrigés aussi
(`HBA.Order.IntegrationTests`, `HBA.Merchants.IntegrationTests`).

### Ce que ce balayage a révélé, et qui reste ouvert

**Trois règles de téléphone cohabitent dans le dépôt, et la plus permissive est la porte
d'entrée.**

| Règle | Où | Ce qu'elle accepte |
|---|---|---|
| `BeninGeography.NormalizePhone` | livraison, inventaire, carnet d'adresses, dossier livreur | `+229` + **exactement 10** chiffres |
| `PhoneNumber.Create` (identity) | **inscription**, profil | `+` optionnel + **8 à 15** chiffres, tout indicatif |
| `BusinessContact.Create` (vendeur) | contact boutique | **8 à 20 CARACTÈRES**, aucun contrôle de chiffre — `« +++++++++ »` passe |

Conséquence : un acheteur s'inscrit avec son ancien numéro à 8 chiffres — identity l'accepte —
puis **ne peut créer ni adresse de livraison ni course** avec ce même numéro, sans que rien ne
le lui ait dit à l'inscription. Le seed d'identity (`IdentitySeedExtensions.Phone`, défaut
`« +22900000000 »`) est dans ce cas.

La question n'est pas technique : **un ancien numéro à 8 chiffres doit-il bloquer
l'inscription ?** Si oui, identity aligne sa règle sur le socle. Si non, il faut dire où et
quand l'acheteur apprend qu'il devra en saisir un autre. Rien n'est modifié tant que ce n'est
pas tranché — durcir l'inscription en silence fermerait la porte à des comptes existants.

---

## Ce que la vague 4 laisse ouvert

Elle a construit deux choses qui n'existaient pas. Chacune fonctionne de bout en bout **de
son côté**, et chacune s'arrête là où un autre service devrait la relayer. C'est nommé
plutôt que tu.

### Promotions (4.1)

**1 · La remise est CALCULÉE, elle n'est pas CONSOMMÉE.** Personne n'appelle
`ReserveAsync` / `CommitAsync` / `ReleaseAsync` — zéro appelant dans tout le dépôt. Le
budget n'est jamais débité, aucun usage n'est jamais engagé, `coupon.used` n'est jamais
publié. Le maillon manquant est **au checkout**, dans order-service.

**2 · `Order.PromotionCode` est persisté et lu par personne.** Un commentaire nomme un
`RedeemPromotionOnOrderConfirmedHandler` **qui n'existe nulle part** — même classe de
défaut que les gardes fantômes de l'audit, mais invisible de
`check-config-and-guards.py`, dont le motif ne couvre que `Ensure*Async`/`Deny*Async`.

**3 · food-cart-service refuse désormais de démarrer en production.** Son
`NeutralPricingModuleApi` est conservé mais gardé. Le brancher sur promotion-service est
une demi-journée ; le scope `FOOD` existe déjà dans le domaine, sans appelant.

**4 · Une campagne financée par un vendeur ne peut pas se restreindre à ses articles.**
`PromotionScope` ne connaît que GLOBAL/MARKETPLACE/FOOD. Quand une ligne n'appartient pas
au financeur, l'adaptateur fait retomber la part sur la **plateforme** et le journalise :
personne n'est facturé à tort, la plateforme absorbe. La vraie correction est un type de
règle « vendeur ».

**5 · La livraison offerte est inapplicable au panier** : `FREE_DELIVERY` s'évalue avec un
frais de port que le panier ne connaît pas. Cette campagne appartient au checkout.

### `SellerOrder` (4.2)

**6 · Un refus vendeur ne rembourse personne.** L'événement porte tout ce qu'il faut —
lignes, emplacement d'expédition, montant, motif — et **n'a aucun consommateur**. Le point
dur est financial-service : il ne sait rembourser qu'une commande ENTIÈRE, via
`OrderCancelled`. Rembourser une fraction est un contrat à écrire.

**7 · Le frais de port n'est toujours pas réparti dans la vue vendeur.** D29 annonçait que
`SellerOrder` fermerait ce défaut « au passage ». L'agrégat donne le PORTEUR ; il ne donne
pas la RÈGLE. Au prorata du montant ? du poids ? du nombre de colis ? Une commande à deux
vendeurs achète **une** course. Inventer la clé mettrait un chiffre faux dans ce que le
vendeur croit avoir vendu. **Décision produit requise.**

**8 · `ReadyForPickup` ne dépêche personne.** La course marchandise est créée à la
confirmation de la COMMANDE, pas quand le colis est prêt. Brancher l'enlèvement sur l'état
vendeur demanderait de déplacer la création de course pour toute la marketplace.

**9 · Une commande dont TOUS les vendeurs refusent reste `Confirmed`.** Aucune agrégation
« toutes les parts sont tombées → la commande tombe » n'existe : elle ferait piloter
`OrderStatus` par les parts, c'est-à-dire la fusion que ce lot s'interdisait. À trancher.

**10 · `HBA.Order.Contracts` et `HBA.Ordering.Contracts` sont deux doublons**, utilisés par
dix services, et leurs `OrderSummary` divergent désormais de deux champs. Cela mérite un
lot à soi.

---

## Ce que la vague 5 laisse ouvert

**1 · ISSUE-007 n'est fermée qu'au quart.** driver-service a sa base et son processeur d'outbox ;
`dispatch`, `route`, `tracking` et `proof-of-delivery` restent des maquettes en mémoire dont les
événements partent dans une file jamais drainée. Chacun porte, dans son installeur, un encadré à
l'endroit exact où la ligne manquante devra être écrite.

**Corollaire à dire clairement : les corrections de sécurité des lots 5.1 et 5.3 sur l'OTP, la
preuve et le suivi sont posées sur ces maquettes.** Elles sont réelles dans un processus, nulles
entre deux — elles disparaissent au redémarrage et ne sont pas partagées entre réplicas.

**2 · Un livreur suspendu continue de recevoir des propositions.** ✅ **Fermé.**
`WithdrawDriverOnDossierSuspended` consomme désormais `DriverSuspendedIntegrationEvent` et passe la
projection dispatchable en `Offline`. Le gestionnaire lit la disponibilité AVANT de suspendre et
journalise en `Critical` quand le livreur était `Busy` : `Driver.Suspend` ne refuse PAS un livreur
en course, il le met hors ligne.

**Reste ouvert** — la course déjà acceptée n'est ni réassignée ni annulée automatiquement, et la
suspension demeure ASYNCHRONE : une proposition peut encore partir entre la décision et l'événement.
La question de fond reste entière : écarter un livreur pour faute grave par un événement est
discutable ; un appel synchrone serait plus sûr mais inverserait la dépendance.

**3 · L'acheteur ne peut plus suivre sa course.** Fermer le suivi était juste, mais tracking-service
ne sait pas qui a commandé — le contrôle posé est l'appartenance, pas l'affectation. Il faut un
chemin client, et le contrat qui expose l'affectation.

**4 · Personne ne porte l'OTP au destinataire.** ✅ **Fermé pour le PIN de `Delivery`.**
La chaîne manquante est écrite de bout en bout : `DeliveryPickedUpDomainEvent` porte l'`IssuedPin`,
`DeliveryPickedUpDomainEventHandler` le chiffre (AES-GCM, `ISecretProtector`) dans le champ
OPTIONNEL `ProtectedDeliveryPin` de `DeliveryPickedUpIntegrationEvent` (ajout additif D32, validé
par `check-event-contracts`), et `DeliveryPickedUpNotificationHandler` le déchiffre pour le mettre
dans la notification de l'acheteur. Le code est envoyé À LA COLLECTE, pas à la création : avant que
le colis parte, il n'a aucune raison d'être connu.

**Reste ouvert** — aucune SECONDE CHANCE automatique : si la notification n'arrive pas (jeton de
push périmé, appareil éteint), le destinataire doit passer par le support, qui relit le code en
base. Un renvoi à la demande reste à écrire. Et cela ne concerne que le PIN de `Delivery` —
l'`OtpChallenge` de proof-of-delivery, lui, reste sans canal (voir le point 5 : la question n'est
pas de lui en donner un, c'est de choisir lequel des deux mécanismes garder).

**5 · Deux mécanismes de PIN cohabitent** : `Delivery.IssuedPin` + `FailedProofAttempts`
(persistés) et l'`OtpChallenge` de proof-of-delivery (mémoire). La question n'est pas comment
persister le second, c'est **lequel garder**.

**6 · `IsCashOnDelivery` n'est pas persisté** sur la course, alors que le livreur doit savoir qu'il
encaisse — et il est câblé à `false` chez les deux producteurs. Vrai aujourd'hui (la course naît
après encaissement), **faux en silence** le jour du paiement à la livraison.

**7 · Le rôle `Driver` n'est attribué par personne.** `DriverVerifiedIntegrationEvent` devrait le
faire poser côté Identity ; aucun consommateur, et aucune route n'exige ce rôle. La seule garde des
routes livreur est donc l'appartenance.

**8 · Le livreur parle à deux services** (dossier et session). `driver-bff` existe et est vide.

---

## Les quatre décisions sont tranchées

**Prises le 21 août 2026, consignées en `docs/DECISIONS.md` D27 à D30.**

| | Décision | Débloque |
|---|---|---|
| **D27** | Révocation vérifiée **à la passerelle**, échec **ouvert** avec journal `Critical` et cache court | ISSUE-022 |
| **D28** | Une remise porte son **financeur** ; la part vendeur se calcule sur `UnitBasePrice - SellerDiscount` | ISSUE-033, 052, 053 — et la garde d'appartenance sur `/api/v1/merchant/promotions` |
| **D29** | **`SellerOrder` est construit** | ISSUE-026, 027 — et la répartition du frais de port dans la vue vendeur |
| **D30** | Domaine livraison **fini** (driver, dispatch, tracking, route, proof) · quatre squelettes food **retirés** | vague 5 en entier, lot 6.4 |

Le détail du raisonnement de chaque décision — y compris ce qu'elle ne tranche PAS —
est dans `DECISIONS.md`.

**D33** (28 août) tranche le remboursement client : il crédite le portefeuille du
client, et le virement Mobile Money est une demande distincte validée à la main.
Ce qu'elle laisse ouvert est listé plus bas.

### Ancien texte des décisions, pour mémoire

Ces quatre points **bloquent des pans entiers** et ne sont pas techniques. Tant qu'ils ne sont pas tranchés, les lots qui en dépendent restent en attente.

### D-4 · Où brancher la révocation de jeton, et comment elle échoue ? *(ISSUE-071 est indépendante — voir plus bas)*

`ValidateAccessTokenAsync` est écrite, complète, et n'a **aucun appelant**. Une suspension met 15 minutes à mordre.

- **Où** — à la passerelle (un point, un cache, un client ; tout le trafic externe y passe) ou dans le socle partagé (quatorze services, identity devient une dépendance dure de chaque requête). *Je recommande la passerelle.*
- **Comment elle échoue** — identity injoignable : fermé (une panne d'identity devient une panne totale) ou ouvert bruyamment (un compte suspendu garde ses droits le temps de la panne). *Je recommande ouvert, journal critique, cache court.*

Débloque : **ISSUE-022**.

### D-1 · Qui supporte une remise financée par la plateforme ?

promotion-service n'a aucune notion de financeur. Le brancher tel quel fait supporter aux vendeurs les coupons de la plateforme, silencieusement, via le calcul des gains.

Débloque : **ISSUE-033, 052, 053** — et donc **toute possibilité de promotion**.

### D-2 · Faut-il construire l'agrégat `SellerOrder` ?

Il n'existe pas. Sans lui : les cinq permissions `ORDER_*` ne gardent rien, `ORDER_MANAGER` ne peut que lire, le vendeur ne peut ni confirmer ni préparer ni remettre au livreur.

Débloque : **ISSUE-026, 027**, et la répartition du frais de port dans la vue vendeur.

### D-3 · Que fait-on des onze squelettes ?

Finir, retirer, ou assumer. Ils portent désormais un bandeau qui dit ce qu'ils sont — c'est le minimum, ce n'est pas une décision.

Débloque : la **vague 5** en entier, et le lot 6.4.

---

## Vague 7 — lot 7.1 : la trace d'audit (22/08)

### L'énoncé de l'audit était périmé sur un point, exact sur le reste

« Vrai sur 3 contextes sur 23, et l'un des trois n'a pas de table ». Vérification :
**24** contextes de module, pas 23 — et le troisième **a** sa table depuis
`20260821000100_InitialReturnRefund`, dont l'en-tête décrit le défaut au passé. Le lot part
donc de **3 sur 24, toutes trois avec table**.

### Ce qui a été fait

**1 · Le journal se journalisait lui-même.** `RecordAuditTrail` n'excluait que `AuditEntry` et
`OutboxMessage` — les deux sans lesquels la boucle serait infinie. `ConsumerInboxEntry` et
`IdempotencyRecord` sont mappés dans les **mêmes** contextes et écrits à chaque message Kafka
consommé, à chaque requête idempotente. Chacun produisait une ligne de journal à acteur nul.
Sur les trois schémas qui journalisaient, ce bruit était déjà majoritaire. **Corrigé en
premier** : l'allumer ailleurs sans cela aurait transformé treize tables d'audit en flux Kafka
déguisés.

Ce qu'on perd : la trace « un message a été consommé ». Elle n'a jamais appartenu à ce journal
— `AuditEntry` répond à « qui a touché à quoi », et la réponse était toujours « personne ». Le
suivi d'un message passe par `CorrelationId` et `TraceParent`. **L'effet métier, lui, reste
tracé** : un consommateur qui marque son inbox ET confirme une commande écrit toujours la ligne
de la commande.

**2 · Deux commentaires affirmaient quatre journaux. Il y en avait un.** `AuditQueries.cs`
annonçait « un journal par schéma : catalog, inventory, order et celui-ci », `SellersDbContext`
renchérissait : « order, inventory et catalog journalisent ce qui arrive aux marchandises ».
Aucun des trois n'a jamais surchargé `KeepsAuditTrail`, aucun n'a de table. Le mensonge n'était
pas décoratif : il répondait par avance à « qui a modifié ce prix ? » en promettant « ce sera
une route de catalog-service » — donc une route à écrire sur une table qui n'existait pas. Les
deux encadrés disent maintenant ce qui est vrai, et ce qui ne l'était pas.

**3 · Dix contextes allumés**, chacun avec sa migration écrite à la main et son bloc de
snapshot :

| Schéma | Ce qui ne laissait aucune trace |
|---|---|
| `identity` | attribution/retrait d'un rôle **plateforme**, permissions d'un rôle, suspension d'un compte |
| `payments` | **capture** et **remboursement** — deux routes admin qui déplacent de l'argent réel |
| `settlement` | **approbation et refus d'un retrait** — le geste par lequel l'argent quitte la plateforme |
| `catalog` | approbation/refus d'une fiche produit, modération des marques, **changement de prix** |
| `reviews` | signalement, rejet, restauration d'un avis |
| `food` | approbation, refus, suspension d'un **établissement** |
| `deliveries` | annulation de course, retrait d'affectation |
| `delivery_pricing` | création, édition, activation d'une **règle de tarification** |
| `drivers` | vérification d'un dossier, **suspension d'un livreur** |
| `ordering` | remboursement après arbitrage, reprise, annulation par l'exploitation |

**13 schémas sur 24 tiennent désormais un journal.** `check-migrations` rejoue les 24 contextes
à froid : 0 incohérence.

**4 · Un dix-septième contrôle : `scripts/check-audit-trail.py`.** Pour chaque contexte qui
journalise il exige une migration créant `<schema>.audit_entries`, le bloc `AuditEntry` dans le
snapshot, et l'appel à `base.OnModelCreating` — c'est là que `AuditConfiguration` est appliquée,
et un override qui l'oublie mappe tout sauf le journal, **en silence**. Il refuse aussi le sens
inverse : une table `audit_entries` sans surcharge est une table que personne n'alimente.

### Ce que le lot 7.1 laisse ouvert

**1 · La rétention n'est pas décidée, et c'est à trancher (D37).** `audit_entries` est
append-only et **n'a aucun purgeur**, là où `OutboxPurger` existe depuis longtemps. Passer de 3
à 13 schémas journalisés est un engagement de croissance : `ordering` et `deliveries` sont les
deux contextes les plus écrits du dépôt, et chaque transition y écrit désormais une ligne de
plus, dans la même transaction.

Je n'ai **pas** écrit de purgeur, délibérément : la durée de conservation est une décision
**juridique et métier**, pas technique, et elle diffère par domaine — un journal de paiement se
garde probablement des années, un journal de course des semaines. Écrire un purgeur avec une
valeur par défaut inventée aurait effacé des preuves sur une règle que personne n'a choisie.
**Question à trancher : combien de temps garde-t-on quoi ?**

**2 · `ActorType` n'est pas une taxonomie, c'est le premier rôle du jeton.**
`RequestContextMiddleware` pose `Type = roles[0].ToUpperInvariant()`. Un compte portant
`["Seller","Admin"]` sera journalisé « SELLER » ou « ADMIN » selon l'ordre des revendications —
c'est-à-dire selon rien de stable. La question « qu'a fait cet administrateur » est donc
répondable par `ActorUserId`, pas par `ActorType`.

**3 · Le journal dit QUI et QUOI, jamais AVANT/APRÈS.** C'est un choix documenté d'`AuditEntry`,
pas un oubli. « Qui a changé ce prix » a une réponse ; « de combien à combien » n'en a pas. Si
cette seconde question doit être répondable, c'est un autre mécanisme.

**4 · `KeepsAuditTrail` est tout ou rien.** C'est une propriété du modèle : il n'existe pas de
réglage « journaliser seulement les gestes d'exploitation ». Sur `ordering` et `deliveries`, le
choix était donc entre tout journaliser et ne rien journaliser. J'ai retenu tout — une course
annulée sans trace est précisément le litige qu'on ne sait pas arbitrer — et le coût est écrit
dans les deux migrations concernées.

**5 · La route `GET /members/audit` ne lit toujours que `sellers`**, et ne montre que les gestes
des MEMBRES de ce vendeur : une suspension décidée par la plateforme n'y apparaît pas. C'est le
bon comportement pour un vendeur ; il n'existe en revanche **aucune** console d'exploitation qui
lise les treize journaux.

---

## Vague 7 — lot 7.2 : le transfert de propriété vendeur (22/08)

### Trois gardes renvoyaient à une opération qui n'existait pas

Le domaine refusait déjà, en trois endroits, tout ce qui aurait pu déplacer le rôle système
`OWNER` — et chacun des trois messages disait au vendeur de faire un transfert de propriété :

| Garde | Message rendu |
|---|---|
| `SetSellerRoles` | « Le rôle de propriétaire **se transfère**, il ne se retire pas. » |
| `EnsureCanAssign` | « Le rôle de propriétaire **ne s'attribue que par un transfert de propriété**. » |
| `EnsureNotLastOwner` | « Le dernier propriétaire ne peut pas être retiré : **transférez la propriété d'abord**. » |

Aucune des trois phrases ne désignait quoi que ce soit d'écrit. `OWNERSHIP_TRANSFER` était
déclarée — critique, réservée au propriétaire, refusée à tout rôle personnalisé — et **ne
gardait aucune route**.

**Ce que cela coûtait :** un dossier dont le propriétaire disparaît devenait *définitivement*
inadministrable. `SELLER_CLOSE`, `PAYOUT_CONFIGURE` et `SELLER_REACTIVATE` ne sont portés par
aucun autre rôle — `SELLER_ADMIN` est semé avec `All.Where(p => !p.IsOwnerOnly())` — et
`EnsureCanAdminister` interdit à quiconque n'est pas propriétaire de toucher un propriétaire.
Le commerçant ne pouvait donc ni fermer son dossier, ni changer son compte de reversement, ni
reprendre la main. Un administrateur plateforme peut fermer le dossier, mais **aucun chemin
admin ne compose l'équipe d'un commerçant** — délibérément.

### Ce qui a été écrit

`POST /api/v1/merchants/{sellerId}/members/{memberId}/ownership`

- **`SellerMember.TransferOwnership(cedant, beneficiaire, acteur)`** — statique, parce qu'elle
  mute deux membres à la fois : une moitié appliquée seule laisserait le dossier sans
  propriétaire, ou avec deux.
- **`Seller.TransferOwnership(userId)`** — l'autre moitié. `Seller.UserId` est la clé de
  `GetByUserIdAsync`, par laquelle *toutes* les routes vendeur résolvent « quel dossier ce
  jeton administre-t-il ». Le laisser derrière donnerait deux sources de vérité qui se
  contredisent.
- **Le handler tient les deux dans une seule transaction**, sous le verrou consultatif
  `LockSellerAsync` — le même que `MuterAsync`, et pour la même raison : `xmin` est un jeton
  **par ligne**, deux transferts simultanés écriraient deux lignes différentes sans jamais
  entrer en conflit.

**Les gardes retenues, et pourquoi :**

- **On ne transfère que SA PROPRE propriété** (`acteur.Id == cedant.Id`). Sans elle,
  `OWNERSHIP_TRANSFER` permettrait à un propriétaire d'en dépouiller un autre — et comme le
  rôle ne s'attribue par aucun autre chemin, la victime ne pourrait jamais le reprendre. C'est
  la seule escalade que cette opération pouvait ouvrir.
- **Le bénéficiaire doit être ACTIF.** Transférer vers un membre suspendu produirait un dossier
  dont le propriétaire ne peut pas agir — donc que plus personne ne peut débloquer.
- **Le bénéficiaire ne doit pas déjà posséder un dossier.** `IX_sellers_UserId` est unique ; la
  question est posée avant, pour rendre un refus qui s'explique plutôt qu'un 409 opaque.
- **Le cédant ne reste pas sans rôle** : s'il n'en portait aucun autre, il reçoit
  `SELLER_ADMIN` — tout sauf les six permissions réservées au propriétaire, c'est-à-dire
  exactement ce qu'il vient de céder.
- **Le step-up est porté par la route**, pas hérité. Le groupe `members` ne passe
  délibérément pas par `DenyUnlessOwnSellerAsync` — or c'est là que vit la réauthentification
  des permissions critiques. Sans cette ligne, le geste le plus irréversible du module aurait
  été **le seul geste critique sans step-up**.

### Un défaut de cache trouvé en écrivant, et refermé

`SellersDbContext` construisait la clé `sellers:by-user:{id}` depuis la valeur **courante** de
`Seller.UserId`. Tant que ce champ n'était écrit qu'à l'inscription, cela suffisait. Avec le
transfert, il bouge — et la clé de l'**ancien** propriétaire serait restée en cache dix
minutes, à répondre qu'il administre encore ce dossier. Ce n'est pas une lenteur : c'est un
droit révoqué qui continue de s'exercer, sur le chemin que toutes les routes vendeur empruntent.
La boucle lit désormais `OriginalValues` et fait tomber les deux clés.

### Ce que le lot 7.2 laisse ouvert

**1 · Le cas que ce lot NE règle PAS : le propriétaire réellement disparu.** Le transfert
suppose le cédant joignable — c'est lui qui l'exécute. Un dossier dont le propriétaire a
supprimé son compte ou est parti sans transmettre reste bloqué. Personne d'autre ne porte
`OWNERSHIP_TRANSFER`, et l'administration plateforme ne compose pas les équipes. **Le
rattrapage de ce cas demande une décision produit** : soit une route d'exploitation qui désigne
un propriétaire (et alors la plateforme touche à l'équipe d'un commerçant, ce que le module
refuse aujourd'hui par principe), soit une procédure hors ligne. Question ouverte.

**2 · Sept permissions ne gardent toujours rien** : `INVENTORY_TRANSFER`, `STOCK_MOVEMENT_VIEW`
(lot 7.3), les six `RETURN_*` (ISSUE-017/018/019), `REVIEW_VIEW`, `ROLE_ASSIGN` — doublon
fonctionnel de `MEMBER_ASSIGN_ROLE` —, `BANK_ACCOUNT_UPDATE` et `SECURITY_POLICY_UPDATE`, cette
dernière sans objet tant que `seller_security_policies` n'existe pas.

**3 · Le step-up n'a pas été vérifié.** `HasRecentAuthentication()` est appelé aux bons
endroits ; je n'ai pas lu son implémentation et ne peux pas affirmer qu'il ne retombe pas
toujours vrai. À vérifier avant de compter dessus.

---

## Le troisième endroit où une adresse de service doit exister (22/08)

Le lot 6.1 a donné à payment-service un client gRPC vers food-order-service. L'adresse a été
posée dans `docker-compose.dev.yml`, `check-service-addresses.py` est passé au vert — et les
**cinquante-neuf tests d'autorisation financiers** sont tombés d'un coup :

    System.InvalidOperationException : Services:FoodOrder est absent.
       at FoodOrdersGrpcRegistration.AddFoodOrdersGrpcClient(…)
       at Program.<Main>$(…)

`tests/Shared/AuthorizationTestFactory.cs` démarre les **vrais** `Program.cs` par
`WebApplicationFactory`. Les mêmes `Add<X>GrpcClient` y lèvent donc de la même façon — sauf que
l'échec ne ressemble à rien de connu : la levée précède la première décision d'autorisation, et
la pile désigne un fichier de contrats partagés, jamais la fabrique.

**Le défaut n'est pas l'oubli, c'est l'absence de lien.** La liste de la fabrique était tenue
à la main, au rythme des besoins — on y ajoutait une clé le jour où un test échouait. Rien ne la
reliait au catalogue des clients qui la réclament. Le contrôle vérifiait le compose **et** le
configmap Kubernetes ; il ne regardait pas ce troisième endroit.

Comptage après coup : **20 clés réclamées** par une extension d'enregistrement, **14 posées**
par la fabrique. Neuf manquaient — `FoodCart`, `FoodOrder`, `Promotion`, `Drivers`, `Dispatch`,
`Tracking`, `Routes`, `ProofOfDelivery`, `DeliveryPricing`. Une seule a mordu ; les huit autres
étaient des mines à retardement identiques.

**Corrigé** : les vingt clés sont posées, et `check-service-addresses.py` exige désormais que la
fabrique porte **toute** clé du catalogue — y compris celles qu'aucun test n'emploie encore. Le
compose est vérifié service par service (chacun n'a besoin que de ce qu'il appelle) ; la
fabrique, non, puisqu'elle démarre n'importe lequel des `Program.cs` du dépôt.

Vérifié en retirant volontairement `Services__FoodOrder` : le contrôle sort en code 1 et nomme
le fichier. Restauré, il repasse à 0.

---

## Un contrôle qui s'est trompé exactement comme le code qu'il vérifiait (22/08)

Le retrait des quatre squelettes food a supprimé leurs blocs `Project` et leurs lignes de
configuration dans `HBA.sln` — **mais pas** leurs vingt lignes d'imbrication. MSBuild s'arrête
net : `MSB5023`, « un projet répertorié comme imbriqué sous … mais il n'existe pas dans la
solution ». Zéro fichier C# en cause, zéro erreur de compilation : **les quinze contrôles
passaient tous et rien ne se construisait**.

**Le plus instructif n'est pas l'oubli, c'est la vérification.** Le retrait cherchait ces
lignes avec `^\t\{` — UNE tabulation. Elles en portent DEUX. La vérification écrite dans la
foulée utilisait **le même motif**, a donc trouvé « zéro orphelin », et a confirmé une
suppression qui n'avait pas eu lieu. Un contrôle qui partage l'hypothèse fausse du code qu'il
contrôle ne contrôle rien.

C'est la troisième occurrence de ce mode de défaillance dans ce dépôt : `check-braces.py`
détectait CS1010 et se taisait, `check-grpc-stubs.py` parcourait un `ROOT/src` inexistant.

**Corrigé** : `scripts/check-solution.py`, seizième contrôle, placé **en tête** de
`check-all.sh` — une solution incohérente ne compile rien, le reste est sans objet. Il ne
suppose aucune indentation (`\s*` partout), vérifie que tout GUID cité est déclaré, que tout
parent d'imbrication existe, que `Project`/`EndProject` et `GlobalSection`/`EndGlobalSection`
s'équilibrent, et que chaque `.csproj` référencé est sur le disque.

### Ce qu'il a trouvé en s'exécutant la première fois

**24 `.csproj` existent sur le disque et ne sont dans aucune solution** — donc compilés par
aucune intégration continue, au mieux transitivement par une `ProjectReference` :

- les **trois BFF** (`client-bff`, `driver-bff`, `seller-bff`), 12 projets ;
- **12 projets de contrats** des services de livraison : `DeliveryPricing`, `Dispatch`,
  `Drivers`, `ProofOfDelivery`, `Routes`, `Tracking`, chacun avec son jumeau `.Grpc`.

Ce n'est pas une erreur de build (le contrôle les signale en ⓘ, pas en ❌) : c'est une zone du
dépôt que personne ne compile volontairement. À trancher — les inscrire, ou dire pourquoi ils
n'y sont pas.

---

## La vague 6 — ce qu'elle a vraiment trouvé (22/08)

Le plan annonçait quatre lots et quatre anomalies. La chaîne restauration en portait
**sept ruptures**, dont six issues d'une seule cause que l'audit n'avait pas nommée.

### La cause commune : `FoodOrder.OrderId` portait DEUX choses

Le ticket de cuisine de restaurant-service naît de deux ponts — `OrderConfirmed` (une
commande order-service dont une ligne est un plat) et `MealOrderConfirmed` (une `MealOrder`
de food-order-service). Les deux écrivaient dans le **même champ**, sans discriminant. Le
commentaire de ce champ disait encore « la commande commerciale, chez Ordering » : vrai pour
la moitié des tickets.

Six gestionnaires inter-services lisaient ce champ **nu** et interrogeaient leur propre base
avec :

| Gestionnaire | Service | Ce que ça cassait |
|---|---|---|
| `CreateDeliveryOnFoodOrderReadyHandler` | restaurant | **Aucune course n'était JAMAIS créée** pour un repas du nouveau parcours. Le sac était prêt, le gestionnaire levait, les reprises Kafka s'épuisaient, aucun livreur n'était cherché |
| `FoodOrderReadyNotificationHandler` (×4) | notification | Le client du nouveau parcours ne recevait **aucun suivi** — acceptation, préparation, prêt, récupéré. Échec avalé en `Warning` : silence complet |
| `HoldOrderOnDeliveryCancelledHandler` | order | Envoyait un identifiant de `MealOrder` à order-service pour la mise en arbitrage |
| `CancelOrderOnFoodOrderRejected` / `Cancelled` / `MarkOrderDeliveredOnFoodOrderDelivered` | order | Chacun a un jumeau dans food-order-service abonné au **même** événement. Pour chaque ticket, l'un des deux jeux travaillait forcément sur un identifiant étranger — `SagaOutcome` en faisait une alerte `Critical` sur un fonctionnement normal |

**Corrigé** : `FoodOrderOrigin { Marketplace = 0, Food = 1 }` sur l'agrégat, migration
`20260827000000_OrigineDuTicketDeCuisine` (défaut `0`, exact pour tout l'existant),
`ux_food_orders_order` passe de `OrderId` à `(Origin, OrderId)`, l'origine voyage sur les
huit événements de domaine et les huit contrats d'intégration (ajouts **optionnels**, D32,
validés par `check-event-contracts`), et les six gestionnaires filtrent dessus.

### Les lots

| Lot | État | Ce qui a été fait |
|---|---|---|
| **6.1 — Paiement food** ✅ | ISSUE-059 | `InitiatePaymentCommand` gagne `OrderType` (défaut « Marketplace », additif). `IPayableOrderReader` lit la commande chez Ordering **ou** chez FoodOrders — un seul chemin de paiement, pas deux, pour ne pas dupliquer la garde de propriété, la garde « déjà en cours » et la réconciliation PSP. `PaymentOrderType.Food` est enfin **produit** : `ConfirmMealOrderOnPaymentCapturedHandler` existait, était enregistré, filtrait sur `"FOOD"` — et aucun paiement ne portait jamais cette valeur. `IPaymentRepository.GetByOrderAsync` prend désormais l'univers (l'index `ix_payments_order` porte `(OrderType, OrderId)` depuis le début ; la lecture filtrait sur `OrderId` seul). **Et** `ReleaseEscrowOnMealOrderDeliveredHandler` : `MealOrderDeliveredIntegrationEvent` était publié **sans aucun consommateur** — l'escrow d'un repas n'était jamais levé, le restaurateur jamais reversable |
| **6.2 — Panier food** ✅ | ISSUE-060 | **Déjà fermé** par un lot antérieur, vérifié pas à pas : `CloseFoodCartOnMealOrderPlacedHandler` clôt le panier sur `MealOrderPlaced` (et vérifie qu'il est encore `Active`, contre le rejeu) ; `GetActiveByBuyerAsync` filtre sur `Status == Active`, donc un panier neuf s'ouvre après la commande ; `HbaTopics` abonne tous les services à tous les sujets, le message arrive bien. La garde `GetByCartAsync` + l'index unique sur `CartId` restent, et ne bloquent plus rien puisque chaque panier neuf a un identifiant neuf |
| **6.3 — Arbitrage** ✅ | ISSUE-061 | `HoldMealOrderOnDeliveryCancelledHandler` : la porte d'entrée qui n'existait pas. `PutMealOrderUnderReviewCommand`, son gestionnaire, `MarkUnderReview` et ses quatre gardes, la colonne `ReviewReason`, l'index partiel `ix_meal_orders_under_review` — tout était écrit, **rien** n'envoyait jamais cette commande. Les deux routes d'administration qui SORTENT de l'arbitrage répondaient donc 409 `not_under_review` à tous les coups |
| **6.4 — Chaîne dupliquée** ✅ | D-3 / D30 | `POST /api/commerce/cart/food-items` **retirée** : aucun appelant dans `clients/` ni `apps/`, et elle prenait le **prix unitaire dans le corps de la requête** — un client commandait à son prix. Les quatre squelettes food (`menu`, `availability`, `kitchen-prep`, `food/review`) déplacés dans `_to_delete/squelettes-food-D30/`, retirés de `HBA.sln` (230 projets, cohérence des GUID vérifiée) et de `docker-compose.dev.yml` |

### Trouvé au passage : la passerelle ne relayait **aucune** route `/api/admin/*`

Seize groupes `MapAdminGroup` existent. Ceux sous un préfixe déjà relayé
(`/api/identity`, `/api/inventory`, `/api/v1/merchants`, `/api/food/admin`) fonctionnaient par
ricochet. Les deux sous `/api/admin` — commandes et arbitrage food — étaient joignables
**uniquement depuis l'intérieur du réseau**. Routes `admin-orders` et `admin-food-orders`
ajoutées, politique `Authenticated` (le rôle est exigé par le service, pas dupliqué à la
passerelle).

**Quatre groupes restent injoignables, et pas faute de route** : `/api/v1/admin/returns`,
`/api/v1/admin/return-policies`, `/api/v1/admin/drivers` et `/api/v1/admin/delivery-pricing`
visent return-refund-service, driver-service et delivery-pricing-service, dont **aucun cluster
n'existe** dans la passerelle. C'est le lot 7.5.

### Ce que la vague 6 laisse ouvert

**1 · Le reste de la chaîne marketplace→food n'est pas parti, seulement fermé.**
`AddFoodItemToCartCommand`, `Cart.AddFoodItem`, `CartLineKind.Food`, `OrderLineKind.Food`,
`CartItemOption` et leurs colonnes restent : des paniers et des commandes EXISTANTS les
portent, et les effacer réécrirait l'histoire de commandes déjà livrées. Plus rien de
l'extérieur n'y entre — c'est ce que ce lot garantit. Le retrait complet demande une reprise
de données, pas un lot de code.

**2 · `ReceiveFoodOrderOnOrderConfirmedHandler` reste enregistré**, délibérément : une
commande déjà passée et pas encore confirmée doit encore pouvoir ouvrir son ticket. Il pose
maintenant `FoodOrderOrigin.Marketplace`, donc il ne trouble plus personne. À retirer quand
plus aucune commande de ce type n'est en vol.

**3 · Le repas mis en arbitrage refroidit.** `HoldMealOrderOnDeliveryCancelledHandler` OUVRE
le dossier ; il ne relance aucune course. Tant que personne ne tranche, rien ne bouge. Une
relance automatique — une nouvelle course avant d'appeler un humain — reste à décider.

**4 · Aucune seconde chance sur la création de course.** Si les reprises Kafka s'épuisent
(restaurant fermé, lieu de collecte sans téléphone, adresse sans repère), le sac reste sur le
passe et seul un journal `Error` le dit. Il n'existe pas de file de rattrapage.

**5 · Les quatre squelettes sont dans `_to_delete/`, pas supprimés.** `device_bash` ne peut
pas effacer sur cette machine. Le dossier `_to_delete/squelettes-food-D30/` est à retirer à
la main.

**6 · `MealOrderPlaced` clôt le panier AVANT le paiement.** Si le client abandonne le
paiement, la commande reste en `AwaitingPayment` indéfiniment et le panier est perdu — il
faut tout ressaisir. Aucune expiration n'existe. Ce n'est pas bloquant (un panier neuf
s'ouvre), mais c'est une commande fantôme de plus à chaque abandon.

---

## Vague 7 — lots 7.3 à 7.6 (22/08)

### 7.3 · Le stock vendeur n'avait pas de mémoire

Deux permissions du catalogue promettaient des gestes qui n'existaient pas. Le rôle
`INVENTORY_MANAGER` annonce « Stocks, ajustements, transferts » ; le mot « transfert »
n'apparaissait nulle part dans inventory-service, et rien ne gardait la trace d'un
mouvement. Une quantité changeait, l'ancienne valeur disparaissait, et personne ne pouvait
dire qui avait fait quoi ni pourquoi.

**Écrit** : `StockMovement` (agrégat + configuration + dépôt + migration
`20260901000000_JournalDesMouvementsDeStock`), cinq natures de mouvement, `TransferStockCommand`,
`StockMovementQueries`, deux routes vendeur.

**Quatre signatures du domaine ont changé.** `Receive`, `AdjustOnHand` et
`ConfirmReservation` rendent désormais le mouvement enregistré au lieu de `Result` nu, et
`Transfer` est un nouveau point d'entrée statique. C'est délibéré : rendre le mouvement
oblige l'appelant à le persister, là où un `Result` nu laissait le journal facultatif — donc
vide.

**Le transfert lit `Available`, pas `OnHand`.** Transférer du stock réservé pour une
commande en cours ferait échouer la commande à la confirmation, dans un autre service, sans
lien visible avec le transfert. Ce qui n'est pas couvert : deux transferts concurrents
depuis le même emplacement ne sont pas sérialisés entre eux — c'est le verrou optimiste de
l'article qui tranche, et le second reçoit un conflit à écrire, pas un refus métier.

**`ConfirmReservation` peut rendre `null`.** Un rejeu idempotent ne produit PAS un
second mouvement. Le journal compte les gestes réels, pas les messages reçus.

### 7.4 · La classe citée par un commentaire n'avait jamais existé

`OfferStatus.cs:80` renvoyait le lecteur à `WithdrawOffersOnStockDepletedHandler` pour
savoir comment une offre repasse `OutOfStock`. Cette classe n'existait pas. Aucune offre
n'est jamais sortie de la vente pour rupture, ni n'y est revenue.

**Écrit** : `StockCatalogHandlers.cs` (retrait et remise en vente), filtrés sur
`ShipFromLocationId` et sur le statut — seuls `Active` et `OutOfStock` sont concernés, une
offre suspendue ou dépubliée ne doit pas se réactiver toute seule sous prétexte qu'un carton
est arrivé.

**Et la revalidation avant paiement.** `PlaceOrderCommandHandler` accepte le prix envoyé par
le client. Il interroge maintenant le catalogue pour chaque ligne Goods :

| Constat | Erreur rendue |
|---|---|
| l'offre a disparu | `ordering.offer_unavailable` |
| l'offre n'est plus achetable | `ordering.offer_not_purchasable` |
| le prix a changé | `ordering.price_changed` |

**Il REFUSE, il ne retarifie pas.** Recalculer silencieusement débiterait le client d'un
montant qu'il n'a jamais vu. Le client doit revoir son panier. Ce qui n'est pas couvert : le
prix peut encore changer entre cette validation et la capture — la fenêtre est réduite, pas
fermée. La fermer demande un gel de prix côté catalogue, avec sa durée de vie et son
expiration.

### 7.5 · Trois services entiers étaient injoignables depuis Internet

return-refund-service (21 routes), driver-service et delivery-pricing-service n'avaient
**aucun cluster** dans la passerelle. Le `docker-compose` fournissait pourtant leurs
adresses : elles pointaient vers rien. Trois adresses, trois clusters, huit routes, trois
`SERVICES__*` en configmap. La passerelle porte désormais 19 clusters et 54 routes.

**Le volet permissions du même lot a produit le quatrième contrôle qui se trompait comme son
code.** Voir la section dédiée ci-dessous.

### 7.6 · `/api/auth/*` rendait 404, et le commentaire disait le contraire

La transformation de la passerelle réécrivait vers `/api/identity/auth/...`. identity-service
sert `/api/v1/auth`, et n'a jamais servi `/api/identity/auth`. **Toutes** les routes
d'authentification passant par la passerelle étaient donc mortes. Le commentaire de
`HBA.Identity.Api/Program.cs` qui a causé cette écriture affirmait l'inverse ; il a été
remplacé par la liste des quatre groupes réellement servis.

---

## Ce que les lots 7.3 à 7.6 laissent ouvert — deux décisions pour HECTOR

### A · Le chemin OTP est complet à un détail près : personne ne reçoit le code

`IssueOtpChallengeCommandHandler` génère le code, le hache, le stocke, applique le plafond de
tentatives, ne le journalise pas — et finit sur `_ = code;`. Le code en clair est **jeté**.
Le commentaire juste au-dessus dit « le code EN CLAIR ne sort pas d'ici autrement que par le
canal choisi » : il n'y a pas de canal.

Deuxième moitié du problème : `verify-otp` rend `OtpVerificationDto(bool Verified, string Channel)`.
**Aucun jeton.** Même si le code était livré, le vérifier n'ouvrirait aucune session.

**Le patron d'envoi existe déjà et il est éprouvé.** identity publie un événement
d'intégration portant le secret sous `Protected*`, notification-service le consomme et le
déchiffre au dernier moment — c'est exactement ce que font `SendEmailVerificationHandler` et
le handler de réinitialisation de mot de passe. Brancher l'OTP par e-mail est un lot court,
sans conception nouvelle.

**Ce qui demande une décision, ce n'est pas l'e-mail, c'est le SMS.** `MfaChannels.All` vaut
`[SMS, EMAIL]`, `SMS` est le canal par défaut, et il n'existe **aucun** fournisseur SMS dans
le dépôt — seulement `IEmailSender` (Resend + un envoi de développement). Au Bénin, un second
facteur par e-mail sur un parcours mobile a peu de sens.

| Option | Ce qu'elle coûte | Ce qu'elle laisse ouvert |
|---|---|---|
| **1 · E-mail seul** | court, le patron existe | `SMS` reste au catalogue et par défaut ; il faut le retirer de `MfaChannels.All` ou le refuser explicitement, sinon la route rend un défi qui n'arrivera jamais |
| **2 · E-mail + fournisseur SMS** | un adaptateur, un contrat, un compte opérateur, la facturation | le vrai parcours, mais c'est un choix de fournisseur et un budget, pas un lot de code |
| **3 · Retirer la route** | le plus honnête à court terme | il faut aussi retirer les 4 routes de passerelle et le limiteur `otp`, sinon la surface reste exposée |

**Aucune de ces options n'est écrite tant que la décision n'est pas prise** : les trois
divergent dès la première ligne, et la troisième défait ce que les deux autres construisent.

### B · Trois BFF sont construits, démarrés, et joignables par personne

`apps/client-bff`, `apps/seller-bff`, `apps/driver-bff` :

- **absents de `HBA.sln`** — aucune intégration continue ne les compile ;
- **construits et démarrés par `docker-compose.dev.yml`** — trois conteneurs qui tournent ;
- **aucune route de passerelle** ne vise leur préfixe (`/api/v1/client`) ;
- `client-bff` porte 13 routes dont **9 rendent 501** ; `/home` est écrit en dur ;
- `seller-bff` et `driver-bff` ne contiennent qu'une sonde de santé.

**Et la passerelle porte son PROPRE BFF, complet et testé.**
`HBA.Gateway.Application/Bff/` contient les agrégateurs client express, client food, marchand,
livreur et restaurant, avec six suites de tests. C'est celui-là qui fonctionne.

La question n'est donc pas « finir les BFF » mais **lequel des deux est le BFF**. Deux
réponses cohérentes :

1. **La passerelle EST le BFF.** Alors les trois `apps/*-bff` partent dans `_to_delete/`,
   sortent du `docker-compose`, et il reste à remplir `Bff:Screens` — les deux sections
   `client.express.home` et `client.food.home` sont des tableaux **vides**, donc l'écran
   d'accueil agrège zéro source. Le commentaire de configuration le dit déjà : « sections
   VIDES tant qu'aucun microservice n'expose de contrat ».
2. **Les BFF sont des services.** Alors il faut y déplacer les agrégateurs de la passerelle,
   les inscrire dans la solution, leur donner adresse-propriété-branche-clé-cluster-route
   (les cinq endroits), et retirer les contrôleurs `Bff/` de la passerelle.

**Ne pas trancher a un coût qui court** : la passerelle porte déjà DEUX générations de
contrôleurs pour les mêmes écrans — `Controllers/Bff/Client*` en `/api/v1/bff/client/...` et
`Controllers/Client/*BffController` en `/api/bff/client/...`, ce dernier marqué `[Obsolete]`.
Trois implémentations pour un écran d'accueil.

---

## Un contrôle qui s'est trompé comme son code — la quatrième fois (22/08)

`scripts/check-permissions.py`, dix-neuvième contrôle, a été écrit pour fermer le volet
permissions du lot 7.5 : croiser les 57 permissions du catalogue vendeur avec tous les
appels du dépôt, et refuser un droit que personne n'interroge.

**Il a annoncé cinq `RETURN_*` sans garde. Elles gardaient dix routes.**

`SellerReturnsEndpoints` recopiait les codes en chaînes littérales dans ses propres
`private const string`. Le contrôle cherchait `MerchantCapabilities.X` : un littéral lui est
invisible. La correction « évidente » — inscrire les cinq dans `SansGardeAssumee` — aurait
gravé dans le dépôt le contraire de la vérité, et un lecteur aurait planifié un lot déjà
fait. C'est exactement ce que faisait `AuditQueries` pour les journaux d'audit.

**Corrigé des deux côtés, et c'est le point** :

- **le code** : les cinq alias pointent vers `MerchantCapabilities.*` au lieu de recopier les
  chaînes. Une faute de frappe dans un littéral compile, et `Can("RETURN_VEIW")` est faux
  pour **tout le monde** — une garde qui refuse tout le monde est aussi cassée qu'une garde
  absente, et rien ne le signale avant le premier vendeur bloqué ;
- **le contrôle** : il voit désormais les littéraux (donc il ne ment plus sur la couverture)
  **et il les refuse** (donc la forme fragile ne revient pas). Il retire les commentaires
  avant analyse — sans quoi le bandeau qui cite `"RETURN_VIEW"` pour EXPLIQUER le problème
  passerait pour la garde elle-même.

Vérifié par trois essais négatifs, pas par une ligne verte : littéral réintroduit → refus ;
garde réellement retirée → refus ; décommenteur éprouvé sur une chaîne contenant `//`.

**Le compte réel** : 57 permissions, 52 gardées, 5 sans garde et assumées comme telles dans
`MerchantPermissions.SansGardeAssumee`, chacune avec sa justification écrite —
`BANK_ACCOUNT_UPDATE` (doublon de `PAYOUT_CONFIGURE`), `RETURN_DISPUTE_VIEW` (aucune notion
de litige n'existe), `REVIEW_VIEW` (la lecture est ouverte à tout compte authentifié),
`ROLE_ASSIGN` (doublon de `MEMBER_ASSIGN_ROLE`), `SECURITY_POLICY_UPDATE` (sans objet).

Les trois premières occurrences du même mode de défaillance, pour mémoire : `check-braces.py`
détectait CS1010 et se taisait, `check-grpc-stubs.py` parcourait un `ROOT/src` inexistant,
et la vérification du retrait dans `HBA.sln` réutilisait le motif faux du retrait lui-même.

### Trouvé au passage, et refermé

**Les quatre squelettes food étaient partis du code, pas de l'outillage.** Leur retrait au
lot 6.4 avait couvert `HBA.sln` et `docker-compose.dev.yml`, mais `scripts/dev-up.sh` les
construisait encore et `scripts/dev-doctor.sh` les diagnostiquait encore.

**Un diagnostic qui interroge des services absents masque les vraies pannes** : `dev-doctor`
rapportait quatre indisponibilités permanentes, dans lesquelles une cinquième — réelle — se
serait perdue. Les deux listes sont à jour ; `check-kafka-topics` ne signale plus aucun
producteur déclaré sans code.

---

## Cinquième occurrence, et la deuxième fois qu'une correction n'a fermé qu'un cinquième du trou (22/08)

Le lot 7.4 a branché `AddProductsGrpcClient` dans `HBA.Order.Api/Program.cs`. Trois tests
d'intégration sont tombés — pas sur une assertion, **à la construction de l'hôte** :

    System.InvalidOperationException : Services:Catalog est absent.
       at ProductsGrpcRegistration.AddProductsGrpcClient(…)
       at Program.<Main>$(String[] args)

C'est le même défaut que les cinquante-neuf échecs du lot 6.1, à ceci près qu'il tombe
ailleurs — et c'est cela qui est instructif.

### Ce que la correction précédente avait manqué

Après les 59, `check-service-addresses.py` a été étendu à `tests/Shared/AuthorizationTestFactory.cs`.
Le contrôle est resté vert pendant que trois tests tombaient, parce qu'il y a **cinq** fabriques
qui démarrent de vrais `Program.cs`, chacune avec sa liste d'adresses tenue à la main :

| Fabrique | Hôte démarré |
|---|---|
| `tests/Shared/AuthorizationTestFactory.cs` | n'importe lequel — d'où l'exigence du catalogue entier |
| `tests/HBA.Order.IntegrationTests/OrderIntegrationFixture.cs` | `HBA.Order.Api` |
| `tests/HBA.Catalog.IntegrationTests/CatalogIntegrationFixture.cs` | `HBA.Catalog.Api` |
| `tests/HBA.Merchants.IntegrationTests/MerchantsIntegrationFixture.cs` | `HBA.Merchants.Api` |
| `apps/api-gateway/tests/…/GatewayFactory.cs` | `HBA.Gateway.Api` |

**Corriger l'occurrence qu'on a sous les yeux n'est pas corriger le défaut.** C'est la
deuxième fois dans ce chantier : le retrait des squelettes food avait couvert `HBA.sln` et le
compose, en laissant `dev-up.sh` et `dev-doctor.sh` (voir D38). Le réflexe à prendre est de
chercher **combien d'endroits portent la même hypothèse** avant de réparer le premier.

### Ce que le contrôle fait maintenant

Il compare chaque fabrique à **son** hôte, déduit de la `ProjectReference` vers un
`*.Api.csproj` — la seule liaison que le compilateur garantit, donc la seule sur laquelle un
contrôle peut s'appuyer sans partager une hypothèse avec le code qu'il vérifie. Exigence
exacte, pas le catalogue entier : leur demander toutes les clés produirait un bruit que
personne ne corrigerait, et le bruit finit par masquer le vrai manque.

Éprouvé en retirant `Services__Catalog` de la fabrique order : le contrôle nomme la fabrique,
la clé, et le `Program.cs` qui la réclame.

### Et l'adresse seule n'aurait pas suffi — c'est la partie qu'aucun contrôle statique ne voit

Depuis le lot 7.4, le catalogue n'est plus seulement **exigé au démarrage** : il est **appelé
à chaque commande**, pour revalider prix et achetabilité. Poser l'adresse vers un port fermé
aurait remplacé trois erreurs de construction par une erreur d'appel dans chaque test qui
commande — plus lente, plus bruyante, et pointant vers le réseau au lieu du manque.

`CatalogueDeTest` a donc été écrit, sur le modèle des trois doubles existants. Deux points
valent d'être retenus :

**1 · Le prix des deux doubles est une seule constante.** `PanierDeTest.PrixUnitaire`, lue
aussi par le catalogue. Deux valeurs recopiées séparément feraient refuser CHAQUE commande de
la suite avec `ordering.price_changed` — un refus parfaitement correct du code de production,
pour un désaccord qui n'existe que dans les tests.

**2 · Les offres inconnues sont rendues achetables, pas absentes.** Le panier tire ses
identifiants d'offre au hasard : le double ne peut pas les connaître d'avance. Ce que ce choix
ne masque pas : les trois refus restent éprouvables en les demandant explicitement sur un
identifiant lu dans `PanierDeTest.Offres`.

### Le lot 7.4 porte enfin ses tests

`RevalidationDuPrixTests` — cinq cas : le nominal (sans lui, les quatre autres passeraient sur
un checkout qui refuse toujours), offre disparue, offre invendable, prix en hausse, prix en
baisse.

**Ils lisent `error.details[].reason`, pas `error.code`.** `ApiResults.Problem` normalise :
`error.code` porte un code de FAMILLE, et le code du domaine est rangé dans les détails sous le
champ `reason`. Asserter sur `error.code` aurait rendu les trois refus **indistinguables** — ce
sont tous des 409 — et le test serait vert sur la mauvaise cause. La première rédaction de ce
lecteur essayait cinq formes de corps au hasard ; c'est la même faute que celles que ce
chantier retire, et elle a été remplacée par la lecture de la seule forme qui existe, avec un
refus explicite sur toute autre.

**Ce qu'ils ne prouvent pas** : le catalogue est un double. Ils éprouvent la DÉCISION du
checkout face à une réponse donnée, pas la fidélité de catalog-service à la produire. La
correspondance entre `OfferStatus` et `IsPurchasable` appartient à `SuspensionDuCatalogueTests`.

---

## D37bis · Le chemin OTP est livré (22/08, tranché par HECTOR : e-mail + fournisseur SMS)

Le code était généré, haché, stocké, plafonné en tentatives — puis **jeté** (`_ = code;`).
Le commentaire juste au-dessus affirmait qu'il « ne sort pas d'ici autrement que par le canal
choisi » : il n'existait aucun canal. Et `verify-otp` rendait `(bool, string)`, donc vérifier
n'ouvrait rien même si le code était arrivé. L'endpoint était décoratif de bout en bout.

### Ce qui a été écrit

| Où | Quoi |
|---|---|
| `HBA.Identity.Contracts` | `OtpChallengeIssuedIntegrationEvent` — code sous `ProtectedCode`, canal, adresse ET numéro, échéance |
| identity, émission | le `_ = code;` remplacé par une publication **avant** `SaveChangesAsync` — l'outbox et le défi dans une seule transaction |
| identity, vérification | `OtpVerificationDto` porte les jetons ; `OuvrirLaSessionAsync` rejoue les gardes de `LoginCommandHandler` |
| notification-service | `ISmsSender` + `SmsMessage`, `DevelopmentSmsSender`, `SmsOptions`, deux gardes de démarrage |
| notification-service | `SendOtpCodeHandler` — déchiffre au dernier moment, aiguille SMS/e-mail, **lève** sur canal inconnu |
| gabarits | `OneTimeCode` (e-mail) et `OneTimeCodeSms` — durée **calculée**, pas recopiée de `MfaChallenge.Lifetime` |

**Aucun adaptateur SMS de production n'est écrit, et c'est la décision qui reste.** Choisir
un agrégateur est un contrat commercial, un compte opérateur, une facturation au message et un
expéditeur à faire homologuer auprès des opérateurs béninois. Écrire un adaptateur pour un
fournisseur arbitraire aurait produit du code plausible, jamais exécuté — exactement ce que ce
chantier passe son temps à retirer. Ce qui est écrit : le port, l'adaptateur de développement,
et **deux gardes de démarrage** —

1. `Notifications:Sms` renseigné mais aucun adaptateur enregistré → **refus de démarrer**.
   Quelqu'un a ouvert un compte et croit que les SMS partent ; retomber sur l'adaptateur de
   développement écrirait les codes dans la console d'un serveur, sans que personne ne le voie.
2. Production sans `Notifications:Sms` → **refus de démarrer**. `SMS` est le canal par défaut :
   sans lui, tout défi est émis, stocké, et n'atteint personne.

Brancher le fournisseur retenu, c'est une classe qui implémente `ISmsSender` et une ligne dans
`NotificationsModuleInstaller`.

### Ce chemin a forcé à durcir le step-up du dépôt entier — et c'est le plus important du lot

`StepUpAuthentication.HasRecentAuthentication` ne lisait QUE `auth_time`. Son propre encadré
annonçait pourtant, depuis le premier jour : « ce compte a-t-il saisi son **MOT DE PASSE** il y
a moins de cinq minutes ». Il ne le vérifiait pas.

L'écart n'a jamais rien coûté **parce que tout chemin d'émission de jetons passait par un mot de
passe** — `ByPassword` et `ByPasswordAndOtp` portent l'une comme l'autre `pwd`. `verify-otp` est
le premier qui n'en exige aucun. Sans correction, la livraison de l'OTP aurait ouvert ceci :

> qui reçoit un SMS obtient un jeton « fraîchement authentifié », et franchit les **six** gardes
> de step-up du dépôt — virement, compte bancaire, transfert de propriété vendeur, mouvements de
> stock. Une carte SIM suffisait à vider un portefeuille.

Le prédicat exige désormais `pwd` dans l'`amr`, et refuse un `amr` absent — même raisonnement
fail-closed que pour un `auth_time` absent. **Aucun chemin existant ne change** : tous portent
`pwd`, y compris `TestTokens.Create` des suites d'autorisation. Seule la session OTP est exclue,
et son porteur passe par `POST /auth/reauthenticate`, qui rejoue le mot de passe.

**La leçon est la même que celle du contrôle des permissions, deux sections plus haut** : un
texte qui affirme une garde, et un code qui ne la porte pas. Ici l'hypothèse était JUSTE — elle
n'était simplement écrite nulle part d'exécutable. Elle l'est maintenant, avec trois tests dans
`StepUpTests` qui l'éprouvent (`otp` seul refusé, `pwd` seul et accompagné acceptés, `amr`
absent refusé).

**Et la fabrique de test portait la même hypothèse muette.** `StepUpTests.Jeton(...)`
produisait un jeton **sans `amr`** quand on ne précisait pas de méthode, et les tests de fenêtre
passaient quand même — puisque le prédicat n'en lisait aucune. Chaque test portait donc le
présupposé « un jeton sans méthode déclarée est valable », précisément ce que le step-up refuse.
Le défaut est passé à `pwd` ; l'absence d'`amr` se demande explicitement et a son propre test.

### Ce que le chemin OTP laisse ouvert

**1 · C'est une connexion SANS MOT DE PASSE, et il faut le savoir.** Qui reçoit le code entre.
La sécurité du compte devient celle du canal — boîte e-mail ou carte SIM. C'est le modèle assumé
d'un OTP de connexion ; le plafond de cinq tentatives, le code unique vivant et l'expiration à
dix minutes ne sont donc pas décoratifs.

**2 · Un mauvais code ne verrouille pas le compte.** Il consomme une des cinq tentatives DU
DÉFI, et rien de plus — contrairement à un mot de passe faux, qui incrémente le compteur du
compte. Demander cinquante défis successifs coûte cinquante SMS et ne verrouille rien. La seule
limite est le limiteur `otp` de la passerelle, donc **par IP** : une protection de débit, pas
une protection de compte.

**3 · Aucun repli si le canal échoue.** Le canal demandé est le seul essayé ; l'événement porte
pourtant adresse ET numéro, précisément pour qu'un repli puisse se décider là où l'échec d'envoi
est constaté, le jour où il sera voulu.

**4 · Un compte inconnu reçoit toujours un défi** (anti-énumération, déjà en place) — et rien
n'est publié pour lui. Même chose pour un canal demandé qu'aucune coordonnée ne sert : le défi
existe, le message ne part pas, et c'est indiscernable de l'extérieur. C'est le prix assumé de
ne pas offrir un annuaire de comptes.

---

## D38 · La passerelle EST le BFF (22/08, tranché par HECTOR)

`apps/client-bff`, `apps/seller-bff` et `apps/driver-bff` sont dans `_to_delete/bff-D38/`,
retirés de `docker-compose.dev.yml`, de `scripts/dev-up.sh`, de `scripts/dev-doctor.sh` et du
`README`. Aucun manifeste k8s ni aucune CI ne les mentionnait — ils n'étaient nulle part
ailleurs, ce qui est précisément le problème qu'ils posaient.

Le BFF réel reste `HBA.Gateway.Application/Bff/` : agrégateurs client express, client food,
marchand, livreur et restaurant, couverts par six suites de tests.

**`BFF_SERVICES` reste déclaré, VIDE, dans `dev-up.sh`.** Les profils `bff` et `bff-only`
démarrent donc la passerelle seule — ce qu'il faut pour travailler le BFF. Supprimer le
tableau ferait échouer ces deux profils sur une variable inconnue.

### Ce que D38 ne fait PAS, et c'est le travail qui reste

**1 · `Bff:Screens` est toujours vide.** Les deux sections `client.express.home` et
`client.food.home` sont des tableaux `[]` : l'écran d'accueil agrège **zéro source**. Retirer
les squelettes n'a pas créé l'agrégation, cela a supprimé l'illusion qu'elle existait
ailleurs. Le commentaire de configuration dit la condition — « sections VIDES tant qu'aucun
microservice n'expose de contrat » — et il faut maintenant la lever écran par écran.

**2 · Deux générations de contrôleurs cohabitent dans la passerelle.**
`Controllers/Bff/Client*` sert `/api/v1/bff/client/…`, `Controllers/Client/*BffController`
sert `/api/bff/client/…` et porte déjà `[Obsolete]`. Les routes sont distinctes, donc rien ne
casse — mais deux implémentations d'un même écran finissent toujours par diverger. À retirer
en vague 9, une fois les clients migrés sur `/api/v1/`.

**3 · Les dossiers sont dans `_to_delete/bff-D38/`, pas supprimés** — `device_bash` ne peut
pas effacer sur cette machine.

---

## VAGUE 2 — Le bus d'événements
**~6 jours. C'est le verrou du système. 22 anomalies, et elles en débloquent une trentaine d'autres.**

**L'ordre interne est une contrainte dure, pas une préférence.**

| Ordre | Lot | Anomalies | Pourquoi là |
|---|---|---|---|
| 1 | **2.1 — Inbox généralisée** | ISSUE-008 | `EfConsumerInbox` existe et sert dans 6 handlers sur 96. C'est du câblage, pas de la conception. **Avant les topics** : sinon le premier rejeu de partition recrédite un vendeur et réserve deux fois du stock |
| 2 | **2.2 — Unifier le nommage des topics** | ISSUE-001 | Une seule fonction de dérivation, appelée des deux côtés. Au passage : `[HbaEvent]`, `HbaEventNaming` et `HbaEventEnvelope` sont du code mort ; trois événements sont déclarés deux fois et résolus par ordre alphabétique ; les 14 topics du k8s ne correspondent pas à ce qui est émis |
| 3 | **2.3 — Vérifier les cinq chaînes** | ISSUE-002, 003, 004, 005, 006 | paiement → commande payée · échec → stock libéré · commande food → ticket cuisine · repas prêt → course · repas livré/annulé → séquestre |
| 4 | **2.4 — Suspension et fermeture effectives** | ISSUE-025, 041 | *Reportées de la vague 1* : `SellerSuspended` et les cinq événements `Store*` n'ont pas de consommateur côté catalog et inventory. Écrites avant, elles ne recevraient rien |
| 5 | **2.5 — Versionnement** | KAFKA §11 | `EventVersion` codé en dur à 1, jamais lu. À trancher avant que les contrats se figent |
| 6 | **2.6 — Corrélation** | GRPC §11 | `x-correlation-id` perdu sur tout le flux événementiel. Sans lui, un incident traversant trois services n'est pas reconstituable |

**Terminé quand** : un test de contrat vérifie, pour chaque `IntegrationEvent`, que le topic calculé côté producteur égale celui côté consommateur ; chaque handler à effet de bord a son test « double livraison → un seul effet » ; les cinq chaînes ont leur E2E.

---

## VAGUE 3 — L'argent et le stock
**CLOSE — les cinq lots sont faits.**

| Lot | Anomalies | Objet |
|---|---|---|
| **3.1 — Unicité en base** ✅ | ISSUE-072, 073 + DATABASE §5 | `payments.ProviderReference` (le webhook encaisse l'un des deux au hasard) · `customer_refunds` sans clé d'idempotence (**double versement Mobile Money**) · `payment_refunds.ExternalRefundId` · `refunds.IdempotencyKey` |
| **3.2 — Les remboursements aboutissent** ✅ | ISSUE-009, 011, 012, 013, 014, 049 | `ExecuteRefundCommand` sans émetteur (aucun remboursement n'est jamais versé) · `Success:false` en dur chez 4 fournisseurs, et le handler lève donc rejeu infini · webhook partiel compté comme total · `from == to` autorisé · quantités déjà retournées en dur à 0 · plafond autoréférentiel |
| **3.3 — Compensations financières** ✅ | ISSUE-015, 050, 051 | virement de lot refusé jamais compensé · gain non repris sur vente annulée · invariant comptable §10.13 écrit, testé, jamais appelé |
| **3.4 — Appel externe avant persistance** ✅ | ISSUE-074, 032 | le remboursement client appelle le PSP **avant tout `SaveChangesAsync`** : un incident laisse l'argent parti et aucune ligne. Même motif dans `OrderLifecycleCommands.cs:223` et `ReturnLifecycleCommands.cs:154` |
| **3.5 — Stock** ✅ | ISSUE-075, 031, 032, 045, 046 | `ReserveStock` non idempotent alors qu'`order_id` est dans le proto · réservations expirées jamais libérées · compensation manquante · `StockReservation` sans statut · SKU sans ligne réputé disponible sans limite |

---

## VAGUE 4 — Les décisions structurantes
**CLOSE — les deux lots sont faits.**

| Lot | Anomalies | Objet |
|---|---|---|
| **4.1 — Promotions** ✅ | ISSUE-033, 052, 053 | `NeutralPricingModuleApi` est la seule implémentation d'`IPricingModuleApi` : tout coupon refusé, toute remise à 0. promotion-service expose un contrat complet que personne n'appelle |
| **4.2 — `SellerOrder`** ✅ | ISSUE-026, 027 | Le seul point de l'audit qui demande de **construire un agrégat** : états, transitions, migration, découpage à la création de commande, puis raccordement des cinq routes `ORDER_*` |

---

## VAGUE 5 — La livraison
**CLOSE — les quatre lots sont faits. Reste ISSUE-007 pour quatre services encore en maquette.**

| Lot | Anomalies | Objet |
|---|---|---|
| **5.1 — Concurrence sur la course** ✅ | ISSUE-028 | Deux livreurs peuvent accepter la même mission : `AssignAsync` écrase sans relire, `Delivery` n'a aucun jeton de concurrence, aucun index unique sur `AssignedDriverId` |
| **5.2 — driver-service réel** ✅ | ISSUE-029, 030 | Inscription, documents, vérification. `IDriverLocationCache.SetAsync` n'a aucun appelant : **aucune course n'est jamais proposée à personne** |
| **5.3 — Preuve et suivi** ✅ | ISSUE-056, 057, 058 | OTP universel `"123456"`, `submit` rejouable · `RequiredProof` renseigné par personne · suivi non réservé au livreur affecté |
| **5.4 — Découpler et fiabiliser** ✅ | ISSUE-007, 069, 070 | Références `.csproj` croisées (les Dockerfiles le prouvent déjà) · `Driver` déclaré trois fois · cinq services publient sans processeur d'outbox |

---

## VAGUE 6 — La chaîne food
**~4 jours. Dépend de la vague 2.**

| Lot | Anomalies | Objet |
|---|---|---|
| **6.1 — Paiement d'une commande food** | ISSUE-059 | Aucun chemin n'existe : `InitiatePayment` lit la commande marketplace et fige `PaymentOrderType.Marketplace` |
| **6.2 — Panier food** | ISSUE-060 | Jamais clos + idempotence par `CartId` → **le client ne peut plus jamais commander de repas** après une première tentative |
| **6.3 — Arbitrage** | ISSUE-061 | `UnderReview` inatteignable, les deux routes admin échouent toujours |
| **6.4 — Sort des quatre squelettes food** | D-3 | Et retirer le parcours restauration dupliqué dans order-service (`Kind = Food`) |

---

## VAGUE 7 — Vendeur, membre et administration
**CLOSE — les six lots sont écrits, et les deux décisions ouvertes ont été tranchées par
HECTOR le 22/08 : D37bis (OTP par e-mail + fournisseur SMS) et D38 (la passerelle EST le
BFF).** Il reste un choix de fournisseur SMS, qui n'est pas un lot de code. Détail plus haut.

| Lot | Anomalies | Objet |
|---|---|---|
| **7.1 — Trace d'audit** ✅ | ISSUE-042, 043 | `KeepsAuditTrail` vrai sur 3 contextes sur 23. Rôles, suspensions, captures, remboursements, retraits, modération, tarification : aucune trace. Et `AuditQueries.cs:29-33` affirme le contraire |
| **7.2 — Transfert de propriété** ✅ | ISSUE-040 | `OWNERSHIP_TRANSFER` n'a aucune route : le dossier devient inadministrable si le propriétaire disparaît |
| **7.3 — Stock vendeur** ✅ | ISSUE-044 | Aucun journal de mouvements, aucun transfert, alors que deux permissions les promettent |
| **7.4 — Catalogue** ✅ | ISSUE-047, 048 | Aucune offre ne passe `OutOfStock` ni ne revient en vente · prix et publication jamais revalidés entre panier et paiement |
| **7.5 — Routes admin absentes** ✅ | SECURITY §3, SAGA_ADMIN §9.3 | Validation des livreurs (aucune route) · les 18 à 23 permissions qui ne gardent rien : les brancher ou les retirer |
| **7.6 — Parcours client résiduels** ✅ | ISSUE-062, 063, 064 | `/api/auth/*` en 404, routes vers return-refund, chemin OTP livré (D37bis) et BFF tranché (D38). Reste à CHOISIR le fournisseur SMS — le port et les gardes sont écrits, l'adaptateur non |

---

## Vague 8 — ce que le relevé de l'audit dit encore vrai (22/08)

L'énoncé du 21/08 a quatre vagues de retard. Avant d'écrire quoi que ce soit, l'état réel a
été relevé fichier par fichier. Trois constats changent le travail :

**1 · `IX_outbox_messages_ProcessedOnUtc` est déjà retiré.** L'audit le décrit comme « index
mort créé par 15 migrations initiales, qui coûte une écriture à chaque message ». Il était créé
par **14** migrations initiales, et **supprimé** par les 14 migrations `AddOutboxRetryTracking`
du 14/07. Les 14 `CreateIndex` qui subsistent sont dans les `Down()` — du rollback, jamais joué
en avant. Mieux : `ix_outbox_messages_pending` est un index **partiel**
(`WHERE ProcessedOnUtc IS NULL AND DeadLetteredOnUtc IS NULL`), donc strictement supérieur à
l'ancien — une ligne traitée en sort et ne coûte plus rien. **Rien à faire.**

**2 · Deux des huit index du lot 8.1 existent déjà** — `commission_rules` en a deux
(`BillingConfigurations.cs:29-30`), et les six `return_*.ReturnId` sont créés par EF via leurs
clés étrangères. Un troisième est mal énoncé : il y a **deux** tables de retrait, et c'est
`customer_withdrawals` qui avait déjà son index sur `Status`. Celle des vendeurs — l'argent qui
part vers un compte Mobile Money — ne l'avait pas.

**3 · Le coupon « une fois par compte » du lot 8.2 ne doit PAS être corrigé comme énoncé.**
Le modèle porte `Coupon.PerUserLimit`, une valeur **N** configurable. Un index unique
`(CouponId, UserId)` serait donc FAUX : il interdirait le second usage d'un coupon qui en
autorise trois. La contrainte réelle n'est pas exprimable en base telle quelle. L'audit suppose
une sémantique que le modèle ne porte pas.

### Le séquencement retenu, et pourquoi il diffère du plan

Les lots 8.1, 8.2, 8.3 et 8.6 sont tous de la DDL sur les mêmes tables. Écrits lot par lot,
ils produiraient jusqu'à quatre migrations par contexte — donc quatre éditions manuelles du
même instantané, quatre fois le risque, pour aucun bénéfice. **Ils sont groupés par CONTEXTE :
une migration chacun.** 14 contextes sont concernés.

---

## Lot 8.6 · Ce qui a été vendu, versé, remboursé ou contesté ne s'efface plus (22/08)

**15 relations basculées de `Cascade` à `Restrict`, dans 5 contextes.**

| Contexte | Relations | Ce qui disparaissait |
|---|---:|---|
| `PaymentsDbContext` | 1 | l'historique des remboursements d'un paiement |
| `ReturnRefundDbContext` | 7 | remboursements, tentatives PSP, photos, inspections, expéditions, historique |
| `WalletDbContext` | 1 | le détail de ce qui a été versé à chaque vendeur dans un lot |
| `BillingDbContext` | 1 | le détail d'une facture |
| `OrderingDbContext` | 5 | lignes, options, imputations de retour, lignes vendeur |

**`ON DELETE CASCADE` ne se voit pas dans le code : il vit dans la base.** Aucun `Remove`,
aucun `RemoveRange`, aucun `ExecuteDeleteAsync` du dépôt ne supprime l'un de ces agrégats. Le
danger n'était donc pas dans le code — il était dans la main qui écrit un `DELETE` en psql pour
nettoyer des données de test, et qui emporte au passage la preuve qu'un client a été remboursé.
Le client garde son relevé Mobile Money ; la plateforme n'a plus rien à lui opposer.

### Quinze relations, alors que l'audit en nommait six

`return_requests` en porte six ; l'audit en protégeait deux. `orders` en porte cinq ; l'audit en
protégeait deux. Ne protéger qu'une partie produit **le pire des trois états possibles** : un
`DELETE` mal ciblé échoue sur les tables protégées APRÈS avoir effacé les autres. La transaction
est annulée, certes — mais une protection par moitié ne tient que tant que l'effacement est
transactionnel, et ce n'est pas une hypothèse à prendre sur une donnée comptable ou un dossier
de litige. Un dossier se protège entier ou pas du tout.

### Le piège que trois configurations annonçaient elles-mêmes

`SettlementBatchConfiguration`, `InvoiceConfiguration` et `OrderConfiguration` portaient toutes
trois le même avertissement : « retirer le `OnDelete(Cascade)` — geste anodin en apparence —
ferait RÉELLEMENT basculer cette relation en sévérance ». Autrement dit, un enfant retiré de la
collection serait mis à `NULL` au lieu d'être supprimé.

C'est exact **sans** `IsRequired()`. Il est posé sur les cinq relations concernées, et le
NOT NULL est en base : EF lève au lieu de sévrer. Et vérification faite avant de toucher, aucune
de ces collections n'est jamais mutée par retrait — `_payouts`, `_lines`, `_options` sont
seulement lus et alimentés. Le basculement est donc sans effet sur le code existant.

Le commentaire disait vrai, et il a servi exactement à ce pour quoi il avait été écrit :
faire vérifier avant d'agir.

### Ce que le lot 8.6 laisse ouvert

**1 · Aucune suppression logique n'a été ajoutée, contrairement à ce que suggérait l'audit.**
Rien ne supprime ces agrégats. Des colonnes `IsDeleted`/`DeletedAtUtc` sans un seul appelant
seraient du code mort à maintenir, et une colonne de plus à oublier dans chaque requête.

**2 · Une purge légitime est désormais impossible sans procédure.** C'est le but : sur une
donnée comptable, l'effacement doit être un geste délibéré qui dit ce qu'il efface. Si la
rétention légale ou le RGPD l'exigent un jour, il faudra l'écrire — et ce sera visible.

**3 · Un `DELETE` visant directement une table fille reste possible.** Rien ne protège une
table de sa propre suppression. Ce qui est fermé, c'est l'effacement INVISIBLE — celui qu'on
déclenche en croyant n'agir que sur le parent.

**4 · 235 `DeleteBehavior.Cascade` subsistent dans le dépôt**, et c'est normal : la grande
majorité relie un agrégat à des enfants possédés sans valeur probante. Seules les relations
portant une preuve — d'argent, d'expédition ou de litige — ont été reprises.

---

## Lots 8.1 et 8.3 · Quatre index, six jetons — et quatre gestes NON faits (22/08)

### 8.1 · Ce qui a été posé, et surtout ce qui ne l'a pas été

| Index | Ce qu'il sert |
|---|---|
| `withdrawals.Status` | la reprise périodique des retraits en cours, un balayage complet à chaque tour |
| `settlement_batches.CreatedAtUtc` | les deux seules lectures de liste, qui trient dessus sur toute la table |
| `reviews.(SellerId, Status)` | la note moyenne du vendeur, affichée sur chaque fiche produit |
| `stock_reservations.ExpiresAtUtc` **partiel** | le balayage des réservations expirées, sur une table qui ne décroît jamais |

**Quatre des huit gestes demandés n'ont PAS été faits, et c'est le résultat du lot.**

- `commission_rules` a déjà deux index, et les six `return_*.ReturnId` sont créés par EF via
  leurs clés étrangères. L'audit décrivait un état antérieur.
- `IX_outbox_messages_ProcessedOnUtc` est retiré depuis le 14/07 et remplacé par un index
  **partiel** strictement meilleur.
- **`conversations` n'est filtré par AUCUNE requête.** Les quatre lectures du dépôt passent par
  la clé primaire ou par `conversation_participants.UserId`, qui a déjà son index. `ContextType`,
  `ContextId` et `Status` ne sont interrogés nulle part. Poser un index ici serait un coût
  d'écriture à chaque message pour servir zéro requête — exactement le reproche que l'audit
  faisait à l'index d'outbox, à trois pages d'écart.
- `deliveries.QuoteId` n'est filtré par aucune requête non plus. Son index ne se justifie **que**
  comme contrainte d'unicité — c'est le lot 8.2, pas le 8.1.

**Un index qu'aucune requête n'emprunte n'est pas une précaution : c'est une écriture de plus
à chaque insertion, pour rien.**

### 8.3 · Six jetons, aucune ligne de DDL

`withdrawals`, `invoices`, `carts`, `meal_orders`, `drivers`, `promotions`. `xmin` est une
colonne **système** que chaque ligne PostgreSQL porte déjà : rien n'est créé, le changement vit
dans la configuration et dans l'instantané.

**L'encadré d'`UsePostgresRowVersion` exige une vérification avant chaque pose** : « un jeton
n'est évalué que dans un `UPDATE` ; si l'opération n'écrit que des lignes ENFANTS, il est
totalement inerte ». Elle a été faite pour les six, et elle a changé la conclusion sur l'un
d'eux.

**`carts` — le jeton ne protège PAS ce que l'audit croyait.** `AddItem` ajoute à `_items` ou
incrémente une ligne existante, et n'écrit **rien** sur l'en-tête du panier : sur ce chemin, le
jeton est inerte. Ce qu'il protège réellement, c'est `MarkCheckedOut` — deux validations
simultanées du même panier produisaient **deux commandes à partir d'un seul panier**. Et
l'ajout de lignes n'en a pas besoin : `ux (CartId, OfferId) WHERE Kind = 'Goods'` le tient déjà.

**Ce qui reste ouvert sur les paniers** : les lignes FOOD. Leur unicité tient à la
combinaison plat + options, vérifiée **en mémoire** par `CartItem.MatchesFood` — elle ne
s'exprime pas en une colonne, donc aucun index ne la porte. Deux ajouts concurrents du même plat
avec les mêmes options peuvent encore produire deux lignes. Il faudrait une empreinte des options
stockée en colonne : c'est un changement de modèle, pas un réglage.

### `drivers` — le jeton a créé un problème qu'il a fallu fermer dans le même geste

Il a été posé pour la DISPONIBILITÉ, écrite par le dispatch, par le livreur et par la course,
qui ne s'attendent pas. Mais un jeton s'applique à **tout** `UPDATE` de la ligne — recopie de
position comprise.

Un battement GPS qui croiserait un changement de statut aurait rendu **un 409 à l'application du
livreur**, pour une écriture sans importance : Redis a déjà reçu la position, et c'est la seule
source que le dispatch lit. La recopie en base n'est qu'un instantané de confort, refait au plus
tard dans cinq minutes.

`IDeliveryUnitOfWork.TrySaveChangesAsync` a donc été ajouté — il rend `false` au lieu de lever,
**et n'avale que le conflit de concurrence**. Pas un `try/catch` dans le handler : la couche
Application ne référence pas EF Core, règle du dépôt rappelée en toutes lettres dans
`ExecuteRefundCommandHandler`. La tolérance est déclarée dans le contrat, implémentée dans
`DeliveriesDbContext`, et éprouvée par `Un_conflit_sur_la_recopie_laisse_le_battement_reussir` —
sans quoi elle serait une branche que personne n'exécute jamais.

### `promotions` — le jeton lève une contrainte de déploiement écrite

`ExpireCouponHoldsWorker` portait ceci depuis sa rédaction : « les deux écritures concurrentes
sur `Promotion.BudgetConsumed` ne sont pas protégées par un jeton de concurrence dans ce module.
Avant de mettre promotion-service à l'échelle horizontale, il faut soit le verrou de ligne, soit
un jeton de version sur la campagne. C'est une contrainte de déploiement, pas une opinion. »

C'est ce jeton. **Son encadré a été corrigé dans le même geste** — laisser un texte qui annonce
une limite levée est exactement le défaut que ce chantier passe son temps à retirer.

---

## Lot 8.2 · Trois contraintes posées, une refusée, et une fusion de données (22/08)

### Ce qui a été fait

| Contrainte | Forme | Risque de reprise |
|---|---|---|
| un panier marketplace actif par acheteur | `ux (BuyerId) WHERE Status='Active'` | **fusion des doublons** |
| un panier repas actif par acheteur | idem sur `food_carts` | **fusion des doublons** |
| un devis ne paie qu'une course | `ux (QuoteId) WHERE QuoteId IS NOT NULL` | **échec bruyant assumé** |
| un jeton de rafraîchissement unique | `IX_refresh_tokens_TokenHash` rendu unique | aucun |

**Le code supposait déjà la règle des paniers ; la base ne la tenait pas.**
`GetActiveCartAsync` fait un `FirstOrDefault` **sans tri** : avec deux paniers actifs, l'acheteur
en voyait un AU HASARD — ses articles apparaissaient et disparaissaient d'une requête à l'autre.
Et la création est un « récupérer-ou-créer » non atomique, donc deux ajouts simultanés
produisaient deux paniers.

### La première migration du chantier qui écrit des lignes métier

Décision de HECTOR : **fusionner**, pas écarter. La migration marketplace procède en sept étapes
ensemblistes — désigner le survivant (le plus fourni, départagé par `Id`), cumuler les quantités
des offres déjà présentes, supprimer les lignes reportées, **collapser les doublons entre
paniers absorbés** (sans quoi l'étape suivante violerait `ux (CartId, OfferId)`), déplacer le
reste, abandonner les absorbés, poser la contrainte.

**Trois cas ne sont pas fusionnables et sont seulement abandonnés** : devises différentes,
natures différentes (`Goods` / `Food` — le panier ne peut pas être mixte), et côté repas,
**restaurants différents** — c'est le cas le plus probable là-bas, un client qui hésite entre
deux restaurants. Rien n'est supprimé : le panier passe en `Abandoned` et ses lignes restent
lisibles.

**Le survivant ne peut pas être « le plus récent ».** `carts` et `food_carts` n'ont **aucune
colonne d'horodatage** — rien en base ne dit lequel a été créé en premier. C'est un manque à part
entière (lot 8.7) ; ici il a fallu faire sans, d'où le critère « le plus fourni ».

**Ce que la fusion ne résout pas** : les lignes food en double. Leur unicité tient à la
combinaison plat + options, vérifiée **en mémoire** — aucune colonne ne la porte, donc aucun SQL
ne peut la reconstituer. Deux paniers fusionnés peuvent laisser deux lignes du même plat.
L'acheteur les voit et peut en retirer une : c'est visible et réparable, contrairement à l'état
d'avant.

### Le devis : l'index n'est que le filet, la cause était ailleurs

`ConsumeQuoteAsync` lisait le devis, testait `Status == "ACTIVE"`, puis écrivait `CONSUMED`.
Entre le test et l'écriture, rien ne tenait la ligne : deux courses concurrentes passaient toutes
deux, et **la plateforme payait deux livraisons pour un devis**.

Corrigé par un `UPDATE … WHERE "Status" = 'ACTIVE'` atomique — le test et l'écriture dans le même
ordre à la base, le perdant l'apprenant par une valeur de retour et non par une exception.

**Et il a fallu ouvrir une transaction explicite.** `ExecuteUpdateAsync` s'exécute
IMMÉDIATEMENT, sans attendre `SaveChanges` : sans transaction, la consommation du devis aurait
été validée AVANT que l'outbox ne reçoive son message. Un incident entre les deux aurait laissé
un devis consommé et aucun événement — exactement la panne que l'outbox existe pour empêcher.

**Second défaut fermé au passage** : l'événement était publié AVANT le `SaveChanges`, donc
**par le perdant aussi**. Deux `DeliveryQuoteConsumed` partaient pour un seul devis, désignant
deux courses différentes, et les consommateurs en aval n'avaient aucun moyen de savoir lequel
comptait.

L'index unique sur `deliveries.QuoteId` garde l'autre bout : il refuse deux courses citant le
même devis, quel que soit le chemin qui les écrit. **Il peut faire échouer le déploiement**, et
c'est voulu — décision de HECTOR. Un devis consommé deux fois est une anomalie financière ; la
résoudre automatiquement serait décider seul du sort d'un versement à un livreur, en silence.
La requête de détection est dans l'en-tête de la migration.

### La cinquième contrainte est REFUSÉE, et c'est le résultat

L'audit demande « coupon *une fois par compte* contournable par deux paniers simultanés ». Le
modèle porte `Coupon.PerUserLimit`, une valeur **N** configurable. Un index unique
`(CouponId, UserId)` interdirait donc le second usage d'un coupon qui en autorise trois : suivre
l'énoncé aurait écrit un bug.

**La course existe pourtant, et elle est ailleurs.** `Coupon.Reserve` compte les usages
**en mémoire** sur l'agrégat chargé : deux réservations concurrentes comptent toutes deux N−1 et
passent. Ce n'est pas exprimable en contrainte — il faut sérialiser sur la ligne du coupon. Le
dépôt a déjà le motif (`pg_advisory_xact_lock`)… et c'est en allant le reprendre qu'on a trouvé
le défaut ci-dessous.

---

## Le verrou consultatif du dépôt ne verrouille rien (22/08)

`SellersDbContext.LockSellerAsync` exécute `SELECT pg_advisory_xact_lock(clé)`. Son encadré
explique — correctement — que la variante `_xact_` se relâche au `COMMIT`, puis ajoute :

> « Sans transaction ouverte, il ne sert à rien … En production, **l'intercepteur de transaction
> du module** encadre la commande — c'est là que le verrou mord. »

**Cet intercepteur n'existe pas.** Il n'y a pas un seul `BeginTransactionAsync` dans tout le
dépôt. L'instruction s'exécute donc dans sa propre transaction implicite, qui valide aussitôt et
**relâche le verrou avant même que le handler ait lu quoi que ce soit**.

Trois appelants en dépendent, et le troisième a été écrit pendant ce chantier :

- `MemberCommands.cs:574` et `:690` — les décomptes de membres ;
- `OwnershipCommands.cs:85` — **le transfert de propriété vendeur (lot 7.2)**, dont l'entrée
  d'`IMPLEMENTATION_DEFECTS` annonce « sous verrou consultatif ».

C'est la cinquième occurrence du motif — un texte qui affirme une garde, un code qui ne la porte
pas — et cette fois j'en suis l'auteur : j'ai bâti le lot 7.2 sur une protection que j'avais lue
sans la vérifier.

### Ce qui a été fait (22/08)

`LockSellerAsync` **n'existe plus**. Il est remplacé par
`ISellerUnitOfWork.ExecuteUnderSellerLockAsync(sellerId, operation, ct)` — déclaré en Application,
implémenté dans `SellersDbContext`, la couche Application ne pouvant pas nommer
`IDbContextTransaction` (règle du dépôt). Même forme que `IDeliveryUnitOfWork.TrySaveChangesAsync`
posé au lot 8.3.

**REMPLACÉ, PAS ACCOMPAGNÉ — ET C'EST LE POINT.** Ajouter une méthode « qui marche » à côté
d'une méthode piégée laisse le piège en place pour le prochain appelant, qui ne peut pas voir
depuis son propre fichier que le verrou ne tient rien. Le verrou n'est plus prenable séparément :
il enveloppe l'opération ou il n'existe pas.

**ET LA CORRECTION EST DOUBLE.** Le verrou est désormais réellement tenu — et il couvre
maintenant des lectures qui lui échappaient. Les trois appelants résolvaient l'acteur et
chargeaient le membre AVANT de verrouiller : on décidait qui agit sur une lecture hors verrou,
puis on verrouillait pour lire le reste. Même réparé sur place, l'ancien appel aurait laissé la
moitié de la décision exposée.

| Appelant | Ce qui est passé sous verrou |
|---|---|
| `MuterAsync` (suspension, réactivation, révocation) | la résolution de l'acteur, le chargement du membre, les deux décomptes |
| `LeaveSellerCommand` | le chargement de l'appartenance et les décomptes |
| `TransferSellerOwnershipCommand` | la résolution de l'acteur, cédant, bénéficiaire, dossier |

**Un échec annule la transaction.** Les trois appelants rendent leur refus AVANT d'écrire —
l'annulation ne leur retire rien, et le contrat devient net : une opération refusée ne laisse
aucune trace. Un futur appelant qui voudrait persister ET refuser ne doit pas passer par là.

**Hors PostgreSQL, ni verrou ni transaction** — les tests en base mémoire n'ont pas la course
à fermer. Les suites d'intégration tournent sur un vrai PostgreSQL (testcontainers) : c'est là
que le verrou mord.

### Ce qui reste ouvert

**1 · Le seul autre verrou consultatif du dépôt est SAIN.**
`SingleRunnerLock` (delivery) emploie `pg_try_advisory_lock` — la variante de SESSION — avec son
`pg_advisory_unlock` explicite, et son encadré explique pourquoi ce n'est PAS `_xact_`. Quelqu'un
avait compris la distinction ; elle s'est perdue entre deux fichiers.

**2 · La course du coupon reste ouverte** (lot 8.2). `Coupon.Reserve` compte les usages en
mémoire ; deux réservations concurrentes comptent toutes deux N−1. Le motif à reprendre est
celui-ci, appliqué à `coupons` — c'est en allant le chercher qu'on a trouvé ce défaut.

**3 · Trois tests le prouvent désormais** — `VerrouVendeurTests`, contre un vrai PostgreSQL :

- deux opérations sur le MÊME vendeur sont sérialisées (le test qui aurait attrapé le défaut :
  avec l'ancienne écriture, la seconde entrait immédiatement) ;
- deux vendeurs DIFFÉRENTS ne s'attendent pas — contre-épreuve indispensable, un verrou global
  passerait le premier test et mettrait la plateforme à genoux ;
- un échec relâche le verrou, ce qui est toute la raison d'être de la variante `_xact_`.

Ils tournent au niveau de `ExecuteUnderSellerLockAsync`, pas d'un parcours métier. La règle
« un vendeur garde au moins un propriétaire actif » se prouverait mieux par deux révocations
concurrentes, mais son échec serait ambigu — le verrou ? le décompte ? la garde du domaine ? Ici
la seule question qui manquait est posée seule : ce verrou bloque-t-il un second appelant ?

Et ils exigent une VRAIE base : `ExecuteUnderSellerLockAsync` ne pose ni verrou ni transaction
hors PostgreSQL. En base mémoire, ces tests n'éprouveraient rien — et seraient verts.

---

## Lots 8.4 / 8.5 · Première passe — les cinq cas nommés, et deux que l'audit n'avait pas vus (22/08)

**Ici, contrairement aux lots précédents, l'énoncé de l'audit tient ligne à ligne.** Les cinq
cas nommés étaient tous encore présents. Les quatre vagues ont travaillé **autour** d'eux — index
`(SellerId, Status)` sur les avis, `AsSplitQuery` sur les commandes, verrou optimiste sur le
reversement, slug redirigé vers les révisions publiées. De vraies corrections, sur d'autres
défauts. Deux des cinq avaient même reçu un commentaire qui **documentait** le problème sans le
corriger.

### Ce qui a été corrigé dans cette passe

| Cas | Avant | Après |
|---|---|---|
| `GetSellerSalesCountAsync` | tout l'historique du vendeur, lignes et options comprises, **en boucle sur les vendeurs, à chaque confirmation** | un `SUM` SQL |
| `ListPayoutsBySellerAsync` | **tous** les lots de la plateforme avec **tous** leurs versements, filtre en mémoire | une lecture ciblée, bornée à 200 |
| notes produit et vendeur | tous les avis chargés pour en faire une moyenne | un `GROUP BY` rendant **cinq lignes au plus** |
| recherche de slug | jusqu'à **100 requêtes** par création, **écrite deux fois** | 1 requête au cas courant, 2 au pire, et un seul fichier |
| `ListLowStockAsync` | table de stock entière + toutes les réservations | filtre en base, borné à 200 |

### La pire n'était pas dans la liste de l'audit — c'était leur composition

`SellerSalesCountHandler` itère sur les vendeurs d'une commande confirmée et appelle
`GetSellerSalesCountAsync` pour chacun. Celui-ci appelait `ListBySellerAsync` — **tout**
l'historique du vendeur. Une commande à trois vendeurs relisait donc trois historiques complets,
**à chaque confirmation de commande**.

Le coût croissait avec le succès de chaque vendeur : plus la plateforme marche, plus elle
ralentit. C'est la forme la plus déplaisante d'un défaut de performance, parce qu'elle ne se
manifeste qu'une fois qu'il est trop tard pour la reproduire en recette.

### Deux traductions impossibles, et ce qu'elles ont imposé

**Les notes.** L'audit demandait un `AVG()` SQL. `Rating` est un objet-valeur adossé à un
**convertisseur** : EF sait traduire la propriété, il ne sait pas traduire `r.Rating.Value`. Un
`Sum(r => r.Rating.Value)` aurait échoué à la traduction — c'est exactement la « traduction
fragile » que l'ancien commentaire redoutait, et il avait raison.

La sortie est un `GROUP BY "Rating"`, qui EST traduisible et qui rend **une ligne par note
distincte, donc cinq au plus, quel que soit le nombre d'avis**. Ce n'est pas une borne qu'on
impose — c'est une borne que le domaine porte (`Rating` va de 1 à 5), donc une qu'on ne peut pas
oublier de maintenir. La moyenne se reconstitue exactement : Σ(note × compte) ÷ Σ(compte).

**Le slug.** Même mur : `StartsWith` sur la chaîne d'un `Slug` converti n'est pas traduisible.
D'où `ListTakenSlugsAsync(candidats)` — on demande « lesquels de ceux-ci sont pris » plutôt que
« ceux qui commencent par ». Plus exact qu'un motif, et traduisible en `IN (…)`.

**Et le chemin courant reste à UNE requête.** La grande majorité des créations trouvent leur
slug de base libre : il est testé seul d'abord, et les cent candidats ne sont construits que si
ce premier essai échoue. Envoyer cent paramètres à chaque création pour servir le cas rare aurait
remplacé un défaut par un autre.

### Un filtre SQL qui recopie une propriété C# est une duplication — donc un risque

`IsLowStock` vaut `Available <= ReorderThreshold`, et `Available` vaut `OnHand - Reserved`, où
`Reserved` somme les réservations actives. Pour filtrer en base, il a fallu **réécrire ce calcul
en sous-requête corrélée**. Si la définition de `Reserved` changeait sans que ce prédicat suive,
les deux diraient des choses différentes.

D'où un second filtre, en mémoire, juste après la requête : il ne peut que **retirer** des
lignes, jamais en ajouter. Une divergence produirait donc un manque visible — un article sous
seuil non listé — jamais un faux positif silencieux. C'est le sens dans lequel on veut se
tromper.

### Seconde passe (22/08) — quatorze lectures bornées, deux boucles fermées

| Lecture | Borne |
|---|---|
| messagerie d'un utilisateur | 50, plafond serveur 200 |
| historique acheteur / vendeur / parts vendeur | 50, plafond 200, **même borne pour les deux jointes** |
| commandes repas par acheteur et par restaurant | 100 |
| retraits vendeur : par vendeur, par statut | 100 |
| retraits client : par client, par statut | 100 |
| remboursements client en cours | 100 |
| avis par produit, par vendeur | 100 |
| offres d'une boutique (affichage) | 200 |
| factures d'un vendeur | 100 |

Et deux N+1 : `ListBySellersAsync` charge en UNE lecture les portefeuilles de tous les vendeurs
d'un lot de reversement — **à la création comme à l'annulation**. L'audit n'avait vu que la
première ; la seconde fait la même chose, et c'est celle qui REND l'argent.

### Deux lectures NON bornées, délibérément

`ListAllBySellerForUpdateAsync` et `ListAllByStoreForUpdateAsync` figurent au relevé des
lectures non bornées, et c'est exact. Mais elles servent la **suspension d'un vendeur** et la
**fermeture d'une boutique** : elles doivent rendre TOUTES les offres, parce que l'appelant les
retire de la vente.

Y poser un `Take` laisserait, après suspension d'un vendeur au gros catalogue, une partie de ses
offres **en vente** — silencieusement, et précisément celles que la borne aurait coupées. Une
sanction appliquée à moitié est pire qu'une requête lente : la première se voit sur la vitrine,
la seconde dans les journaux.

La vraie réponse, le jour où ce volume posera problème, est un traitement **par lots** — lire
mille offres, les retirer, recommencer. C'est un changement du handler et il demande de rendre
l'opération reprenable. Ce lot ne le fait pas ; il refuse seulement la correction qui aurait
l'air d'en être une.

### Ce que les bornes coûtent, et il faut le dire

Un acheteur ou un vendeur qui dépasse la borne **ne voit plus ses commandes les plus anciennes**.
C'est une régression fonctionnelle assumée. La vraie réponse reste la **pagination des routes**,
qui change le contrat de `GET /api/orders` et `GET /api/v1/seller/orders` et se décide avec les
clients web et mobile.

Entre « tronqué et visible » et « illimité et lent », le premier est réparable. Les routes
acceptent `?take=`, mais c'est un souhait : le plafond serveur ne se contourne pas, sans quoi un
client rouvrirait le balayage que ce lot ferme.

### Ce qui reste du lot 8.4 / 8.5

**1 · La pagination des routes de commande** — la décision ci-dessus.

**2 · Neuf lectures encore non bornées**, les plus notables : les gains d'un vendeur
(`ListSellerEarningsAsync`, `GetSellerStatementAsync`, `ListReleasedBySellerAsync`), les lots de
règlement (`ListAsync`), les gains d'une période (`ListReleasedInPeriodAsync`,
`ListAccruedInPeriodAsync`, `ListByBatchAsync`) et le catalogue d'un vendeur
(`ListBySellerAsync`, `ListBySellerForUpdateAsync`). Les trois dernières du portefeuille
alimentent le lot de reversement lui-même : les borner demande d'en faire un traitement par
lots, pas d'y poser un `Take`.

**3 · Sept N+1 restantes**, toutes d'ordre de grandeur modeste (1 à 30 tours) : les plats d'une
commande repas, les positions Redis d'un dispatch (un `MGET` suffirait), les vendeurs d'une
notification, les campagnes d'un balayage de coupons.

**4 · Les réservations de stock ne sont jamais purgées** — et ce n'est pas une requête à
borner, c'est une donnée à faire décroître. `StockReservation` documente que la suppression a été
retirée volontairement, pour garder l'historique. Tant qu'aucune purge datée n'existe, **six**
`Include(i => i.Reservations)` du service se dégradent linéairement avec le temps, y compris
ceux qui sont par ailleurs correctement bornés. C'était déjà signalé au lot 3.5 (ISSUE-045) et
ça n'a pas bougé.

---

## Lot 8.7 · Horodatage et contraintes d'état (22/08)

**Deux des trois contraintes `CHECK` demandées par l'audit auraient écrit un bug.** Elles ont
été refusées et remplacées par ce que le code tient réellement.

### La contrainte « restaurants » visait un statut qui n'existe pas

L'audit demandait `CHECK (Status <> 'Submitted' OR "PayoutSellerId" IS NOT NULL)`.
`RestaurantStatus` vaut **Draft / PendingApproval / Active / Suspended / Closed** — il n'y a pas
de `Submitted`. `Submit()` est le **geste**, `PendingApproval` l'**état** qui en résulte. Écrite
telle quelle, la contrainte aurait été **toujours vraie** : décorative, donc pire qu'absente —
elle aurait fait croire la règle tenue.

Posée sur `PendingApproval`. **`Active` en est exclu délibérément** : la migration
`20260820000000_DossierDeReversementDuRestaurant` a créé la colonne nullable en assumant que les
établissements *déjà en service* continuent sans dossier. L'y étendre aurait contredit une
décision prise deux jours plus tôt et mis hors la loi des lignes laissées ainsi exprès.

### La contrainte « deliveries » aurait rejeté des courses légitimes

L'audit demandait qu'une course livrée ait un prix et un gain livreur. `Delivery.MarkDelivered`
dit l'inverse, en toutes lettres : *« Mettre zéro serait exact arithmétiquement et faux dans les
faits : le livreur a bien roulé. On laisse donc NUL. »* Une course sans devis — course interne,
reprise manuelle, partenaire hors tarification — est livrée sans montant. La contrainte demandée
aurait **fait échouer la remise du colis** pour imposer une règle que le code refuse.

Remplacée par les deux cohérences que le code tient vraiment :

- `ck_deliveries_price_has_currency` — un montant sans devise n'est ni facturable ni versable ;
  `AttachQuote` pose toujours les deux ensemble ;
- `ck_deliveries_earning_has_basis` — un gain existe toujours **avec** le prix et le taux dont il
  dérive (`MarkDelivered` ne le calcule que dans `if (Price is { } price)`). Un gain orphelin est
  un montant que personne ne peut recalculer ni contester, et c'est de l'argent dû à quelqu'un.

### La troisième était juste — et elle valait pour quatre statuts, pas un

`ck_orders_paid_requires_payment`. `Paid` est le **premier** état post-paiement, pas le dernier :
`Confirmed`, `Delivered` et `UnderReview` ne s'atteignent que depuis lui. Ne contraindre que
`Paid` aurait laissé passer une commande **livrée** sans paiement — le même défaut un cran plus
loin. `Cancelled` et `Failed` en sont exclus : on annule aussi bien avant qu'après le paiement.

**Ce piège n'a pas de parade automatique.** Ajouter un état post-paiement à `OrderStatus` sans
l'ajouter à la contrainte le laisse hors contrôle, en silence — exactement comme les index
partiels de `deliveries`. La liste est écrite en toutes lettres dans `OrderConfiguration` pour
qu'on la relise.

### L'horodatage : neuf tables, un mécanisme partagé

`HorodatageExtensions.HorodateLesModifications()` (sur le modèle de `UsePostgresRowVersion`) +
un quatrième temps dans `ModuleDbContext.SaveChangesAsync`. Posé sur `orders`, `payments`,
`deliveries`, `withdrawals`, `customer_refunds`, `seller_earnings`, `invoices`, `coupons`,
`reviews` — les agrégats que l'audit nomme, ceux dont un état intermédiaire *qui dure* est un
incident.

- **Propriété fantôme**, pas propriété de domaine : c'est une donnée d'exploitation, et le
  domaine ne doit pas pouvoir fonder un invariant sur l'heure d'un `UPDATE`.
- **Nullable, sans défaut** : `NULL` = ligne antérieure à la colonne. `DEFAULT now()` aurait fait
  dire à chaque ligne ancienne qu'elle a été touchée à la seconde du déploiement.
- **Estampillée aussi à l'INSERT**, sans quoi `NULL` voudrait dire deux choses.
- `restaurants` n'en reçoit pas : elle porte déjà `UpdatedOnUtc`, rempli par `Restaurant.Touch()`.
  Deux colonnes de même sens sur une table sont un piège de lecture.

### Ce que le lot 8.7 ne couvre PAS

1. **Les 73 tables sans aucun `Created*Utc`** — `inventory_items`, `carts`, `drivers`,
   `conversations`, `food_orders`, `product_variants`, `categories`, `roles`, `devices`… Rien
   n'est fait ici : chacune demande sa migration, et poser la colonne sans savoir ce qu'on veut
   en lire ne ferait qu'élargir le schéma.
2. **Les ~32 autres tables avec `Created*Utc` mais sans `Updated*Utc`.** Les neuf traitées sont
   celles que l'audit nomme comme financières ou de cycle de vie ; les autres attendent une
   raison d'être interrogées.
3. **Une ligne parente que seul un enfant fait changer n'est pas estampillée.** Si une
   écriture ne touche que des lignes ENFANTS, EF n'émet aucun `UPDATE` sur le parent, l'entrée
   reste `Unchanged`, et la colonne ne bouge pas. **Ajouter une ligne de commande ne change donc
   pas `orders.UpdatedAtUtc`.** C'est le même angle mort que le jeton `xmin`, et il a le même
   remède si un jour il gêne : salir une colonne du parent, comme `InventoryItem.StockVersion`.
4. **Les valeurs sentinelles de `order_lines`** (§9.1) — `RestaurantId` / `MenuItemId`
   `IsRequired()` et remplis de `'00000000-…'` pour une ligne de marchandise. La colonne garde
   une contrainte qui ne dit plus rien. Le commentaire l'assume ; **corriger demanderait de
   scinder la table ou de rendre les colonnes nullables**, ce qui est un geste de modélisation,
   pas d'horodatage.
5. **Aucune de ces contraintes n'est `NOT VALID`.** Sur une base contenant déjà des lignes
   fautives, la migration ÉCHOUE et le service ne démarre pas — les migrations sont appliquées
   avant l'ouverture du port. C'est voulu : un `NOT VALID` aurait laissé ces lignes en place pour
   toujours et la contrainte aurait menti sur ce qu'elle garantit. **Les trois requêtes de
   repérage à passer avant déploiement sont dans l'en-tête de chaque migration.**

---

## Lot 8.8 · gRPC — le disjoncteur qui était annoncé partout et n'existait nulle part (22/08)

**Trois commentaires du dépôt décrivaient ce disjoncteur comme acquis** —
`PromotionGrpcClient` (« la politique de résilience — délai, reprise, disjoncteur — se pose à
l'enregistrement du client »), `MediaGrpcClient`, et `promotion.proto` (« ouvrirait le
DISJONCTEUR de l'appelant »). Il n'y en avait aucun. C'est la sixième fois de cette campagne
qu'un commentaire décrit un mécanisme absent.

### Le geste évident aurait produit un disjoncteur aveugle

`AddGrpcClient` rend un `IHttpClientBuilder` : poser `AddResilienceHandler` dessus, comme le fait
la passerelle, était le réflexe. Il n'aurait presque rien vu, pour deux raisons :

1. **Un échec gRPC est un HTTP 200.** Le statut voyage dans les en-têtes ou les bandes-annonces
   HTTP/2. `Internal`, `Unavailable`, `ResourceExhausted` arrivent tous en « 200 OK » au
   gestionnaire de messages : un `ShouldHandle` écrit sur `HttpResponseMessage.StatusCode` — le
   réflexe suivant — n'en compte aucun.
2. **Un dépassement d'échéance y est indiscernable d'une annulation par l'appelant.** Les deux
   arrivent en `OperationCanceledException` sur le même jeton lié. Compter les deux ferait ouvrir
   le disjoncteur quand un utilisateur ferme son onglet ; n'en compter aucune laisserait le cas
   le plus fréquent — le service **lent** — hors du compte.

D'où `DisjoncteurClientInterceptor`, au niveau gRPC, où `RpcException.StatusCode` est explicite.
L'état est consulté **avant** d'appeler `continuation` : un disjoncteur qui laisse partir l'appel
avant de constater qu'il est ouvert ne protège personne.

Un disjoncteur **par service appelé** (clé : `Method.ServiceName`) — un disjoncteur global
transformerait une panne en panne générale. Seuil : 50 % d'échecs sur 30 s, minimum 10 appels,
coupure 15 s.

### Pas de réessai, et ce n'est pas un oubli

En HTTP, la passerelle ne réessaie que les GET et HEAD : rejouer un POST dont la réponse s'est
perdue débite deux fois. En gRPC, **rien ne dit si un RPC est sûr** — `GetSellerPayout` et
`RefundPayment` ont la même forme. Un réessai générique rembourserait deux fois.

### Les statuts : trois corrections, et une qui revient sur une décision argumentée

| Où | Avant | Après | Pourquoi |
|---|---|---|---|
| Clé interne non configurée | `Unavailable` | `FailedPrecondition` | L'erreur est **permanente** jusqu'au redéploiement. Et depuis le disjoncteur, `Unavailable` est compté comme une panne : une variable d'environnement oubliée aurait ouvert les disjoncteurs de tous les appelants |
| Clé absente ou fausse | `NotFound` | `Unauthenticated` | voir ci-dessous |
| Exception non gérée côté serveur | `Unknown` (par défaut) | `Internal` / `Aborted` / `AlreadyExists` / `Cancelled` | Nouveau `TraductionDesErreursServerInterceptor` |

**Le passage de `NotFound` à `Unauthenticated` renverse une décision qui était argumentée** —
« un appelant sans secret n'a pas à apprendre que ce service expose une API gRPC ». Le
raisonnement d'origine est **conservé dans le code**, avec ce qu'il ne pesait pas : ce qu'il
protège est déjà connu de quiconque atteint le port interne, tandis que `NotFound` est **aussi**
le code d'une ressource absente. Une clé mal déployée produisait donc une `RpcException(NotFound)`
non rattrapée par le seul appelant qui filtre les statuts, remontant brute dans
`CreateDeliveryCommand` : un incident d'**authentification** déguisé en défaut de **domaine**.

### Le tri des exceptions compte double depuis qu'il y a un disjoncteur

Un conflit de concurrence optimiste est **une garde qui a fonctionné**. Laissé en `Unknown`, il
serait compté comme une panne : dix écritures concurrentes sur une même commande — un jour de
soldes — auraient ouvert le disjoncteur de tous les appelants. La garde aurait fait tomber le
service qu'elle protège. Traduit en `Aborted`, il ne compte pas. Même chose pour la violation
d'unicité → `AlreadyExists`, qui existe précisément pour qu'un rejeu échoue.

C'est ce que vérifient les **six tests** de `DisjoncteurGrpcTests`.

### Le code d'erreur normalisé survit enfin au saut gRPC

`reason` empaquetait deux informations dans une chaîne — et pas de la même façon selon le service :
`« code — message »` côté delivery, `« code:message »` côté payment. **Personne ne reparsait ni
l'un ni l'autre.** Champ `reason_code` ajouté (D32, additif) sur `FinancialOperationResponse`,
`CreateDeliveryResponse` et `CancelDeliveryResponse`.

**Et il fallait que quelqu'un le lise, sans quoi le champ n'aurait été qu'un ornement.** Deux
appelants s'en servent :

- `PaymentGrpcClient` : « déjà intégralement remboursé », « montant supérieur au remboursable » et
  « paiement introuvable » rendaient le **même** code local `payment_refund_failed`. Ils sont
  maintenant distincts, préfixés `return_refund.payment.<code>`.
- `CreateDeliveryOnOrderConfirmedHandler` : le repli « retenter sans devis » se déclenchait sur
  **n'importe quel motif de refus** — téléphone invalide, commune inconnue, quota partenaire — en
  journalisant « Devis payé refusé » alors que le devis n'y était pour rien. Il est désormais
  restreint à `pricing.quote_not_usable` et `pricing.quote.malformed`.

**Changement de comportement assumé** : une **panne** de delivery-pricing (`pricing.grpc_*`) ne
déclenche plus le repli. Sans devis, la course était créée **sans prix** (`AttachQuote` n'est pas
appelé) — donc impossible à facturer et sans gain livreur calculable. On lève désormais, l'outbox
rejoue. Coût : un délai de livraison pendant la panne. Bénéfice : aucune course impayable.

### Le `new HttpClient()` de l'audit était pire que ce que l'audit disait

`MobileMoneyPaymentGateway.GetAccessTokenAsync` était **du code mort** — jamais appelé, la
simulation ne demandant aucun jeton — qui cumulait trois défauts : `new HttpClient()` par appel,
**des identifiants écrits en dur** (« apiuser », « GetApiKey », « GetSubscriptionKey ») dans un
fichier de simulation, et aucun cache de jeton. Et la vraie version existait déjà à côté, correcte :
`Real/MtnMomoHttpGateway` lit ses options, tire son client de la fabrique, met en cache et protège
le renouvellement par un sémaphore. Supprimé : garder une seconde version approximative ne pouvait
servir qu'à ce qu'on branche la mauvaise.

### Ce que le lot 8.8 ne couvre PAS

1. **§10.1 et §10.2 — l'identité d'appelant. C'est le vrai trou du maillage gRPC, et il reste
   ouvert.** Une clé unique et symétrique pour les 20+ services ; l'intercepteur n'atteste **pas**
   quel service appelle. **Tout service compromis peut appeler n'importe quel RPC en affirmant
   n'importe quelle identité** — dont `GetSellerPayout` (numéro Mobile Money de n'importe quel
   vendeur, énumérable), `RefundPayment`, `ReleaseReservation`, `CancelDelivery`. Ce n'est pas un
   défaut de robustesse, c'est de la sécurité, et il demande mTLS ou une clé par service avec
   liste blanche de RPC. **Rien de ce lot ne l'entame.**
2. **§11 — `x-correlation-id` perdu sur tout le flux événementiel.** L'intercepteur client lit la
   corrélation dans `HttpContext.Items` ; un appel gRPC émis depuis un consumer Kafka ou un
   `BackgroundService` n'a pas de `HttpContext`, et la corrélation est omise sans journal. Cela
   concerne la majorité des appels **mutants** du dépôt.
3. **Les 40 RPC sans corps de serveur et les 45 RPC morts** — lot 9.1.
4. **Les 13 copies de `.proto`** — lot 9.1. `delivery.proto` a été resynchronisée avec son
   original dans ce lot pour ne pas introduire de divergence ; **rien n'empêche la prochaine.**
5. **§10.0 — trois services (`notification`, `review`, `return-refund`) appellent `AddHbaGrpc()`
   sans publier de service gRPC** et ouvrent un port HTTP/2 inutile. Ils en ont besoin pour les
   intercepteurs **clients** qu'elle enregistre : séparer les deux moitiés toucherait la
   configuration Kestrel et les ports de `docker-compose`. Non fait.
6. **Aucun réessai** (voir plus haut) — et la décision d'en poser un devra être prise **RPC par
   RPC**, jamais globalement.
7. **§12.1 — les 13 clients HTTP de la passerelle doublant des contrats gRPC** ne sont pas
   supprimés : c'est le rôle légitime d'une passerelle nord-sud. Le défaut relevé — « c'est la
   seule couche qui a un disjoncteur » — est clos par ce lot, l'autre couche en a un maintenant.

---

## Lot 8.9 · L'argent — trois représentations, pas deux (22/08) → **D39**

L'audit demandait « documenter, pas corriger à l'aveugle », et comptait **deux**
représentations. Il en manquait une, et c'est la seule qui mordait.

| # | Représentation | Où |
|---|---|---|
| 1 | `decimal` / `numeric(18,2)` | 65 colonnes — le cœur du dépôt |
| 2 | `long` / `bigint` | `promotions` et `delivery_pricing` — le franc CFA n'a pas de sous-unité |
| 3 | **`string`** | **~50 champs monétaires gRPC** — protobuf n'a pas de type décimal |

**Aucune conversion ne multiplie ni ne divise par cent**, dans tout le dépôt — vérifié.
Le risque que l'audit pressentait n'était pas là où il le cherchait. Les deux frontières
`decimal ⇄ long` (panier↔promotions, delivery_pricing→delivery) sont correctes et déjà
argumentées sur place.

### Le défaut réel : sept lecteurs sur huit rendaient ZÉRO sans rien dire

Huit fonctions lisaient un montant venu du fil. **Sept s'écrivaient
`TryParse(…) ? valeur : 0m`** ; une seule refusait. Un champ `string` de protobuf 3 vaut
la **chaîne vide** quand l'émetteur ne le pose pas — il n'y a pas de « non renseigné ».
Un chemin de code qui oublie une affectation, un producteur plus ancien qui ne connaît
pas un champ ajouté (D32) : le lecteur lisait **zéro franc**.

**Le cas le plus cher est démontrable.** `plafondCommande = CapturedAmount −
AlreadyRefundedAmount` : un zéro silencieux sur `AlreadyRefundedAmount` **remonte le
plafond de remboursement** — exactement le défaut qu'**ISSUE-014** a fermé, rouvrable
sans qu'une seule ligne fautive n'apparaisse nulle part.

`MontantSurLeFil` : `Ecrire` / `Lire` (refuse) / `LireOuAbsent` (vide ⇒ `null`, illisible
⇒ refus), `NumberStyles.Number` et jamais `Any` (deux lecteurs l'utilisaient — « 1E3 »
aurait valu mille francs). Le nom du champ est rempli par le compilateur
(`[CallerArgumentExpression]`) : **aucun des ~60 sites d'appel n'a changé**.

### Trouvé en faisant ce relevé — le CRITICAL n° 1 est toujours ouvert, et il est PIRE que décrit

**`DeliveryApi.LookupQuote` n'a aucun corps de serveur.** Appelé par
`PlaceOrderCommandHandler` **et** `PlaceMealOrderCommand` → `UNIMPLEMENTED` non rattrapé.
Le devis étant obligatoire pour un repas, **aucune commande de repas ne peut être passée**.

Et l'audit sous-estime : `delivery-service` a sa **propre** table `delivery_quotes`
(`numeric(12,2)`) qu'**aucune route n'écrit et qu'aucune ne lit** — `GetQuote` n'a pas
non plus de corps, et il n'existe aucune route HTTP de devis dans ce service. Le seul
magasin vivant est celui de **delivery-pricing** (en `long`), et c'est bien lui que
`CreateDeliveryCommand` consomme. **Implémenter `LookupQuote` contre la table de
delivery-service ne marcherait donc pas non plus : elle est vide.**

Deux voies, à trancher — **(A)** delivery-service relaie vers delivery-pricing (une seule
façade pour les appelants) ; **(B)** order et food-order interrogent delivery-pricing
directement (un saut réseau et une table fantôme en moins). Voir D39.

### Ce que le lot 8.9 ne couvre PAS

1. **Une devise à sous-unité casserait la représentation n° 2.** `Currency` est un texte
   libre de trois lettres et **rien ne vérifie qu'il vaut XOF**. Une campagne en euros
   stockerait « 10 » pour dix euros.
2. **Le nombre de décimales n'est contraint nulle part** : `numeric(18,2)` accepte
   0,01 franc, qui n'existe pas. Aucun `CHECK`.
3. **Aucun contrôle automatique n'empêche un neuvième lecteur d'écrire `? valeur : 0m`.**
   La règle est écrite dans D39 et dans l'en-tête de `MontantSurLeFil` ; elle n'est pas
   outillée.
4. Les 26 colonnes `double` — coordonnées, distances, scores, facteur routier — **aucune
   n'est de l'argent**, vérifié colonne par colonne.

---

## VAGUE 8 — Robustesse et performance
**Tout est fait sauf la queue de 8.4/8.5.** La VAGUE 8 est close.
Détail des trois lots clos plus haut — y compris les quatre gestes délibérément NON faits.

**Quatre des dix « index manquants » n'en étaient pas**, et deux des huit « contraintes
d'unicité » sont mal énoncées. L'audit date du 21/08 ; il faut relire l'état réel avant chaque
lot, pas après.

| Lot | Volume | Objet |
|---|---:|---|
| **8.1 — Index manquants** ✅ | 10 | `reviews.SellerId`, `deliveries.QuoteId`, `withdrawals.Status`, `conversations`… et retirer `IX_outbox_messages_ProcessedOnUtc`, index mort qui coûte une écriture à chaque message |
| **8.2 — Contraintes d'unicité** ✅ | 8 | Paniers actifs en double · devis consommé par deux courses · coupon « une fois par compte » contournable |
| **8.3 — Jetons de concurrence** ✅ | 8 | `withdrawals`, `meal_orders`, `drivers`, `carts`, `invoices`, `promotions.BudgetConsumed` |
| **8.4 — Requêtes non paginées** | 13 | 94 `ToListAsync()` sans borne. `ListLowStockAsync` remonte toute la table de stock avec ses réservations |
| **8.5 — N+1** | 5 | Jusqu'à 99 requêtes pour trouver un slug libre · un aller-retour par vendeur dans le lot de reversement |
| **8.6 — Cascade sur données financières** ✅ | 6 | Un `DELETE` mal ciblé efface la preuve qu'un client a été remboursé |
| **8.7 — Nullable et horodatage** ✅ | 8 | 73 tables sur 131 sans `Created*Utc` · 41 avec un `Created*Utc` sans jamais d'`Updated*Utc`, dont `orders`, `payments`, `deliveries` |
| **8.8 — gRPC** ✅ | 21 | Aucun disjoncteur · **aucun** code de statut métier utilisé, donc refus et panne indiscernables · 13 clients HTTP doublonnant des contrats gRPC |
| **8.9 — Deux représentations de l'argent** ✅ | 1 | `numeric(18,2)` partout sauf promotions et delivery_pricing en entier. **À documenter, pas à corriger à l'aveugle** |

---

## CRITICAL n° 1 · `LookupQuote` — fermé (22/08, tranché par HECTOR : option B)

**Aucune commande de repas ne pouvait être passée.** `DeliveryApi.LookupQuote` était appelé
par les deux checkouts — marchandise et repas — et **n'avait aucun corps de serveur** :
`UNIMPLEMENTED`, non rattrapé. Le devis étant obligatoire pour un repas, tout le parcours
food était mort.

### Ce que l'audit ne disait pas, et qui a changé la solution

`delivery-service` **n'a plus de domaine de tarification**. Le namespace
`HBA.Deliveries.Domain.Pricing` est **introuvable dans tout le dépôt** — pas de dossier, pas
de classe, pas de `DbSet`. Sa table `delivery_quotes` n'était donc écrite par personne et lue
par personne. **Implémenter `LookupQuote` chez lui aurait interrogé une table vide.**

`GetQuote` n'avait pas de corps non plus, et il n'existe aucune route HTTP de devis dans ce
service : le seul magasin de devis vivant est celui de **delivery-pricing** — c'est déjà lui
que `CreateDeliveryCommand` consomme.

### Ce qui a été fait (option B)

- **`DeliveryPricingApi.LookupQuote`** — nouveau RPC, avec les quatre vérifications que le
  checkout doit faire et que `ValidateQuote` ne couvrait pas : expiré **et** consommé rendus
  séparément (deux gestes opposés côté client), les quatre coordonnées du trajet, le niveau
  de service. Expiration et consommation **décidées par le serveur** : `GetQuoteAsync` périme
  déjà un devis `ACTIVE` échu et l'écrit.
- **`IDeliveryQuoteLookup`** dans `HBA.DeliveryPricing.Contracts`, avec `DeliveryQuoteDetails`
  **déplacé** depuis `HBA.Deliveries.Contracts` — le laisser là-bas en aurait fait un type
  sans producteur, et deux types de même nom pour qui référence les deux contrats.
- **order-service et food-order-service** interrogent delivery-pricing. Dans les deux, la
  dépendance `IDeliveryDispatchApi` ne servait **qu'à** la relecture : elle est remplacée, pas
  doublée.
- **`RequestQuoteAsync` / `LookupQuoteAsync` retirés** de `IDeliveryDispatchApi` et de son
  client gRPC ; **`GetQuote` et `LookupQuote` retirés** de `delivery.proto` avec leurs quatre
  messages. `delivery.proto` resynchronisée avec sa copie morte de `services/`.
- **`SERVICES__DELIVERYPRICING`** ajouté pour les deux services et pour la fabrique de test —
  `check-service-addresses.py` a signalé les trois manques **avant** le build, dont celui de
  la suite d'intégration. C'est exactement ce pour quoi il a été étendu au lot 7.
- **`DevisDeTest`** : un double qui **lève**. Rendre un devis complaisant court-circuiterait
  la garde qui empêche l'acheteur de fixer ses propres frais de livraison — un faux qui dit
  toujours oui rendrait vert un service ayant reperdu cette garde.

### Trois aggregates fantômes dans le ModelSnapshot

`DeliveryQuote`, `DeliveryZone` et `PricingRule` étaient déclarés dans
`DeliveriesDbContextModelSnapshot` **sans exister dans le code**. Conséquence : le prochain
`dotnet ef migrations add` sur ce contexte aurait généré, tout seul, une migration
**supprimant quatre tables** — au milieu d'un diff portant sur autre chose.

Snapshot nettoyé, et la suppression écrite **à la main, délibérément**
(`20260906000000_TarificationRendueAuServiceQuiLaTient`) plutôt que subie plus tard par
surprise. Le `Down` recopie les blocs de création **à l'identique** de la migration d'origine :
écrire une forme « suffisante » de mémoire aurait produit des tables au bon nom et à la
mauvaise structure — un retour en arrière qui a l'air d'avoir marché.

**La migration détruit des données** : les lignes restantes sont antérieures à la
séparation des services et n'ont plus de lecteur (`deliveries.QuoteId` n'a jamais porté de clé
étrangère vers `delivery_quotes`, seulement un index). Les requêtes d'archivage sont dans
l'en-tête de la migration.

### Ce qui reste ouvert

1. **La garde `PartnerId` de `PlaceOrderCommandHandler` ne protège rien.** Elle refuse un
   devis dont le `PartnerId` n'est pas nul — mais **aucun devis du système n'en porte** :
   delivery-pricing ne connaît pas ce champ, et il n'existe aucune route de devis partenaire
   dans le dépôt. La garde est conservée telle quelle, et le manque est écrit à trois endroits
   (proto, contrat, client). **À trancher** : porter `partner_id` jusqu'à delivery-pricing, ou
   retirer la garde.
2. **Aucun contrôle ne compare le ModelSnapshot au MODÈLE.** C'est ce qui a laissé trois
   aggregates fantômes passer — `check-migrations.py` rejoue les migrations entre elles, il ne
   lit pas le modèle. Nommé au **lot 9.4**, non fait.
3. **Le second RPC mort de `delivery.proto` est parti avec le premier**, mais les **43 autres**
   restent — lot 9.1.

---

## Lots 9.1 et 9.4 · Un RPC mort n'est pas inerte (22/08)

### Le troisième `UNIMPLEMENTED` vivant de la journée

`OrderApi.ListOrdersBySeller` **n'a aucun corps de serveur**, et
`SellerSalesCountHandler` l'appelle — via `GetSellerSalesCountAsync` — à **chaque commande
confirmée**. L'exception partait **avant** `MarkProcessedAsync` : le message était donc rejoué
jusqu'à épuisement, et `SalesCount` restait à **zéro pour tous les vendeurs**. C'est-à-dire
exactement le défaut que ce handler avait été écrit pour fermer.

Corrigé par un RPC **dédié** `GetSellerSalesCount`, pas en implémentant `ListOrdersBySeller` :

- rendre TOUTES les commandes d'un vendeur pour n'en tirer qu'une somme d'entiers est ce que
  le lot 8.4 a retiré côté in-process — un vendeur à dix mille ventes faisait remonter dix
  mille agrégats, et une commande à trois vendeurs le faisait trois fois ;
- le filtre « une vente est une vente PAYÉE » était **recalculé par le client**. Même
  interface, deux réponses selon le côté du réseau. Le serveur rend maintenant le nombre.

Les **deux** clients `IOrderingModuleApi` du dépôt sont corrigés — le second n'a pas
d'appelant, mais il portait le même `UNIMPLEMENTED` en embuscade.

### 21ᵉ contrôle — `check-grpc-rpc.py`

Il aurait attrapé `LookupQuote`, `ListOrdersBySeller` **et** `GetProducts`. Les trois
compilaient et passaient les vingt autres contrôles : `protoc` génère une base serveur dont
les membres non surchargés lèvent **à l'exécution**, et il n'existait aucun moment, entre
l'éditeur et la production, où quelque chose s'en apercevait.

État : **112 RPC déclarés, 0 appelé sans corps de serveur**, 34 latents, 21 morts. Preuve
négative faite (on recasse `GetSellerSalesCount` → code 1, on restaure → code 0).

**Sa limite est écrite dans son en-tête** : une enveloppe de `*.Contracts.Grpc` compte
comme un appelant même si personne n'appelle l'enveloppe. Le sens de l'erreur est le bon — le
RPC reste à brancher ou à retirer.

### `CatalogApi.GetProducts` — retiré, pas implémenté

Personne ne l'appelait. L'implémenter correctement demande un lot batché **et** une sémantique
de cache par identifiant (`GetProductAsync` passe par le cache et charge cinq `Include`). Le
retrait est argumenté sur place : **un contrat déclaré sans serveur n'est pas une avance,
c'est un piège qui compile.** Le lot des OFFRES reste — il est implémenté et appelé.

Au passage, j'ai supprimé `GetProductsResponse` **encore utilisé** par
`ListProductsBySeller`, et je l'ai remis : deux RPC partagent ce message.

### Les 10 copies de `.proto` — et l'une avait déjà divergé

L'audit en comptait 13 ; il en restait **10** (les quatre squelettes food sont partis au lot
6.4). Il disait « aucune divergence aujourd'hui, mais aucun mécanisme ne l'empêche demain ».
**Demain était arrivé** : `services/food/restaurant-service/proto/food.proto` différait de son
original. Deux marqueurs `` — sans conséquence, et c'est bien le problème : la dérive
commence toujours par quelque chose de sans conséquence.

Les 10 sont dans `_to_delete/protos-dupliques-9.1/`. Aucune n'était compilée : la seule
directive `<Protobuf Include=…>` du dépôt pointe vers `shared/proto`. Vérifié qu'aucun
Dockerfile, `.csproj`, compose ou script ne les référence.

### 9.4 — le contrôle des migrations réparé sur deux points

**`check_sql_identifier_case` ne voyait que 19 blocs SQL sur 232.** Il ne connaissait que
la forme à triple guillemet ; le dépôt écrit son SQL brut à **146 exemplaires en littéral
verbatim** et 11 en littéral ordinaire. Il passait à côté de 94 % de ce qu'il prétendait lire.
Les trois formes n'échappent pas les guillemets de la même façon — et ce sont précisément des
identifiants ENTRE GUILLEMETS que l'on cherche. Preuve négative : une casse fautive dans un
littéral verbatim est maintenant signalée ; elle ne l'était pas.

**`check_snapshot_entities` compare le ModelSnapshot au CODE** — le contrôle qui manquait ce
matin. Preuve négative faite en réintroduisant un agrégat fantôme.

**Le premier jet de ce contrôle a produit un faux positif de son propre fait** : il écartait
tout fichier dont le NOM contenait « Snapshot », donc `PolicySnapshot.cs`, un value object
parfaitement vivant. Corrigé par un filtre sur le SUFFIXE, et l'anecdote est dans le code.

**L'audit disait « il ne vérifie que les tables, jamais les colonnes » — c'était périmé** :
le rejeu suit les colonnes depuis une vague antérieure. Ce qui manquait était ailleurs.

### Reste de la vague 9

**9.2, 9.3 et 9.5 ne sont pas faits.** Et pour 9.1 : **34 RPC latents et 21 RPC morts**
restent déclarés. Les retirer est une chirurgie de contrats à faire par service, pas d'un
bloc — le contrôle les compte désormais à chaque exécution, donc l'inventaire ne se reperd
plus.

---

## Lot 9.2 · Les états inatteignables (22/08) → **D40**

**L'instruction était « les atteindre ou les retirer ». Il ne faut, presque partout, faire ni
l'un ni l'autre** — et le chiffre de 83 ne mesure pas ce qu'il prétend mesurer.

| Mesure | Résultat | Ce qui cloche |
|---|---:|---|
| « jamais affectée » | **256** | Compte `MerchantPermission` **57/57**. Une permission ne s'affecte jamais : elle se **lit** d'une charge utile |
| « jamais produite hors comparaison » | **78** | Proche des 83 — mais compte `DeliverySource.HbaFood`, posé **en chaîne** (`Source: "HbaFood"`). Toute énumération qui traverse un contrat en texte y échappe |
| « ni produite ni citée en littéral » | **~12** | Le vrai compte, et il porte sur les **états d'agrégat** |

**La première mesure était la mienne, et elle partageait l'hypothèse fausse de ce qu'elle
mesurait** — septième occurrence de ce motif dans la campagne.

Les vraies valeurs inatteignables sont des **fonctions déclarées et non construites** :
paniers jamais abandonnés (aucun balayeur — et depuis `ux_carts_active_buyer`, le panier actif
d'un acheteur est le sien **pour toujours**), campagnes qui n'expirent jamais (**et continuent
d'être accordées**), acceptation automatique jamais activable (exigence du cahier §3/§14),
remboursement partiel qui se lit `Failed`, « vendu pour pièces » qu'aucune route n'accepte.

**Toutes annotées sur place, aucune retirée.** Retirer effacerait le seul endroit où
l'exigence est écrite : le lecteur suivant conclurait « ce n'était pas prévu ».

**Et pour trois d'entre elles, retirer casserait des données** : `MemberStatus`,
`StoreEnforcement` et `OrderAcceptanceMode` sont stockés en **entier**, alors que tout le
reste du dépôt stocke ses statuts en texte — précisément ce que le commentaire de
`PromotionConfigurations` déconseille. `MemberStatus.Invited` vaut **zéro** : la retirer
ferait de `default(MemberStatus)` un état qui ne nomme plus rien.

**Doublon trouvé au passage** : `ReturnResolution.Refund` et `RefundOnly`, deux noms pour
la même chose dans une énumération dont **aucune des cinq valeurs** n'est jamais posée. À
trancher avant le premier usage — après, un écran qui compte les remboursements en oubliera
la moitié.

**Aucun contrôle automatique, et c'est un choix argumenté** (voir D40) : mes deux premières
mesures ont sur-signalé, l'une d'un facteur vingt.

### Trouvé en faisant ce lot : le README de promotion-service mentait

Il annonçait encore « **Etat : squelette** — les quatre projets sont créés et compilent, mais
ils sont **vides** : aucune entité, aucun cas d'usage, aucun endpoint métier », et « ce
service n'est **pas** déclaré dans `docker-compose.dev.yml` ni dans `HBA.sln` ». Les quatre
affirmations étaient fausses depuis le lot 4.1 : le service porte `Promotion`, `Coupon`, son
contrat gRPC, ses migrations, son schéma — et il est dans la solution (7 occurrences) comme
dans le compose (8).

C'est le cas **symétrique** du lot 0.5, qui avait posé des bandeaux « squelette » parce
qu'« un audit les a d'abord comptés comme faits ». Ici : un service fait, compté comme à
faire. Réécrit, y compris ses « étapes pour l'activer » — une liste de tâches accomplies
laissée telle quelle finit par être refaite.

---

## Lot 9.3 · Quatre dossiers qui ont la forme d'un service et n'en sont pas (22/08)

`billing`, `wallet`, `recommendation` et `wishlist` ont **quatre projets, un domaine, une
base, des migrations, un schéma** — et **ni `Program.cs`, ni `Dockerfile`, ni entrée dans
`docker-compose.dev.yml`**. Ils s'exécutent dans le processus d'un autre :

| Dossier | Hôte | Base | Schéma |
|---|---|---|---|
| `billing-service/` | payment-service | `hba_financial` | `billing` |
| `wallet-service/` | payment-service | `hba_financial` | `settlement` |
| `recommendation-service/` | review-service | `hba_engagement` | `recommendations` |
| `wishlist-service/` | review-service | `hba_engagement` | `wishlist` |

**Aucun des quatre n'avait de README.** Rien, nulle part, ne disait qu'ils n'étaient pas
des services — et l'audit les a comptés comme quatre services de plus, c'est-à-dire **deux
fois chacun** : une fois comme service à déployer, une fois comme module déjà fourni. C'est
le défaut **symétrique** des bandeaux « SQUELETTE » du lot 0.5.

Les quatre portent maintenant un bandeau en tête, avec ce que leur statut implique
(processus, base, schéma, port, déploiement) et **ce qu'il faudrait pour en faire un
service**. Les **deux hôtes** disent en tête ce qu'ils portent — sans quoi le lien ne se
lisait que dans un sens.

**La règle 1 n'est pas violée, et il fallait le vérifier avant de l'écrire.** « Une base
par service, pas de schéma partagé » : ce ne sont pas des services, leur `DbContext` est
distinct, leur schéma est distinct, et `ModuleDbContext` interdit déjà la jointure
inter-schéma. Ce qui est partagé, c'est la **chaîne de connexion** — donc le serveur et la
base, pas les tables.

**Le module `wallet` est celui qui manipule le plus d'argent du dépôt, et il n'a pas de
processus à lui** : une panne de payment-service emporte les deux. C'est le premier argument
pour l'en extraire, le jour où l'on s'y mettra — et il est maintenant écrit dans son README.

### Trois documents corrigés au passage, tous du même motif

- **`services/README.md`** annonçait « les treize services » et « 8 / 3 / 1 / 1 ». Le dépôt
  a **27 dossiers pour 23 processus**. Aucun des cinq nombres n'était juste. Corrigé, avec le
  critère qui tranche : **le `Program.cs`, pas le suffixe `-service`**.
- Son tableau des 29 modules nomme encore `merchant-service`, `food-service`,
  `commerce-service`, `financial-service`, `engagement-service` — **cinq dossiers qui
  n'existent pas**. Conservé, mais requalifié en tableau de **cible**, pas d'état.
- **`payment-service/README.md` et `review-service/README.md`** pointaient leurs « modules
  actuels » vers `src/Modules/…` — un chemin du monolithe, **qui n'existe plus**. Leur
  section « à savoir avant d'extraire » décrivait au futur une extraction déjà faite ; le
  raisonnement qu'elle contient est exact et explique précisément pourquoi billing et wallet
  sont **toujours** dans ce processus — il est conservé sous ce titre-là.

---

## Lot 9.5 · Sondes, schémas, nommage (22/08) — **la vague 9 est close**

### Une sonde de disponibilité qui ne pouvait pas échouer, sur le chemin de chaque checkout

`delivery-pricing-service` répondait `/health/ready` par un `Results.Ok` **constant**. Et
l'encadré de son `Program.cs` expliquait pourquoi :

> « ce service n'a pas de base à sonder ni de domaine à câbler. Le jour où il en aura une, il
> devra passer à `AddHbaService<TDbContext>` »

**C'était faux.** Il a `DeliveryPricingDbContext`, le schéma `delivery_pricing`, sa migration
initiale depuis le lot 0.4, et `EfDeliveryPricingStore` qui écrit chaque devis. « Le jour où
il en aura une » était déjà passé quand la phrase a été écrite.

**Une sonde qui ne peut pas échouer est pire qu'une sonde absente : elle AFFIRME.** Base
injoignable, l'orchestrateur laisse l'instance en rotation, et chaque appel échoue en 500 au
lieu que le trafic soit détourné. **Et ce service est sur le chemin critique de chaque passage
en caisse depuis ce matin**, la relecture de devis lui ayant été branchée au lot 9.1.

Corrigé par la sonde réelle (`AddDbContextCheck`, tag `ready`) plutôt que par une adoption de
`AddHbaService<TDbContext>` — celui-ci exige un `IModuleInstaller`, du MediatR et une couche
Application que ce service n'a pas. `live` reste une constante, et c'est correct : y sonder la
base ferait **redémarrer** le conteneur à chaque hoquet de PostgreSQL.

Les **quatre squelettes** de `delivery/` gardent leur sonde constante — ils n'ont rien à
sonder — mais chacun porte désormais la note qui dit à quelle condition elle deviendra un
mensonge.

### `shared/kafka-schemas/` — vide, et il doit le rester

Ses trois promesses existent ailleurs depuis D31/D32 : `docs/contrats-evenements.json`
(**140 événements**, comparés par `check-event-contracts.py`), l'attribut `[HbaEvent]` +
`HbaTopics` pour le nom stable, et la règle additive tenue par le contrôle.

Le dossier n'est **pas supprimé** : son README est désormais le panneau qui envoie au bon
endroit. Le supprimer laisserait la question ouverte, et le prochain qui cherche « où sont
les schémas ? » en recréerait — c'est-à-dire un quatrième endroit, exactement le défaut
d'ISSUE-001.

### Le nommage : mesuré, documenté, **non renommé**

| Mesure | Valeur |
|---|---:|
| Fichiers dont le namespace ne partage pas deux segments avec son projet | **257** |
| Couples projet ↔ namespace désalignés | **20** |
| Types déclarés dans **plusieurs** namespaces | **77** |

Les pires : `HBA.Delivery.Core.*` → `HBA.Deliveries.*` (92 fichiers), `HBA.Order.*` →
`HBA.Orders.*` (73), `HBA.Food.Cart.*` → `HBA.FoodCarts.*`, `HBA.Delivery.Driver.*` →
`HBA.Drivers.*`.

**Ce n'est pas cosmétique, et ça a coûté du temps aujourd'hui même.** Le dépôt a **deux**
`IOrderingModuleApi`, dans deux namespaces, implémentés par deux clients gRPC du même
fichier. Au lot 9.1, il a fallu remonter les deux pour savoir lequel `SellerSalesCountHandler`
utilisait avant de pouvoir corriger l'`UNIMPLEMENTED`. Les deux ont dû être corrigés. Même
motif pour `IPaymentsModuleApi`, `IPayoutModuleApi`, et `OrderReturnContext` — celui-là dans
**trois** namespaces.

**Les deux `IOrderingModuleApi` portent maintenant un encadré qui nomme l'autre.**

**Le renommage n'est pas fait, et ce n'est pas un report par confort** : il touche 257
déclarations de namespace plus chaque `using` du dépôt, sans aucun gain fonctionnel, et **je
n'ai pas de compilateur ici**. Une substitution massive à l'aveugle sur des noms qui se
chevauchent (`HBA.Order.` est un préfixe de `HBA.Orders.`) est le genre de geste qui compile
et déplace un type dans le mauvais assemblage. À faire dans un IDE, en une fois, avec un build
entre chaque projet.

### Métriques métier : aucune, et c'est nommé

Le seul `Meter` du dépôt est celui de la passerelle (`bff_request_duration`,
`bff_dependency_failure_total`…). **Aucun service n'émet de compteur métier** — ni commande
passée, ni paiement capturé, ni course créée, ni retrait approuvé. L'instrumentation
technique, elle, est complète (`AddHbaTelemetry` sur tous les services : ASP.NET Core, HTTP,
Npgsql, runtime).

Ce n'est pas un lot d'hygiène : c'est un chantier d'observabilité qui demande de décider
**quoi** compter avant d'écrire une ligne — un compteur qu'on ajoute « pour voir » ne se
retire jamais. Non fait, nommé ici.

### Ce que la vague 9 laisse ouvert

1. **34 RPC latents et 21 RPC morts** — la chirurgie de contrats, service par service.
   `check-grpc-rpc.py` les recompte à chaque exécution : l'inventaire ne se reperd plus.
2. **Le renommage des 257 namespaces**, ci-dessus.
3. **Les métriques métier**, ci-dessus.
4. **`ReturnResolution.Refund` / `RefundOnly`** — le doublon à trancher avant le premier usage
   (lot 9.2).
5. Les balayeurs manquants nommés au lot 9.2 : paniers jamais abandonnés, campagnes qui
   n'expirent pas — **et les réservations de stock jamais purgées**, ouvert depuis le lot 3.5.

---

---

## Identité d'appelant gRPC et purge des réservations (22/08) → **D41**

### 1. L'identité d'appelant — ce qui a été fait, et ce que cela ne ferme pas

`Internal:ApiKey` est **une** chaîne, la même sur les vingt-quatre hôtes. Elle atteste
l'appartenance au réseau, pas l'identité : un service compromis la lit dans son
environnement et appelle n'importe quel RPC en se présentant comme n'importe qui.

**Une clé par service ne suffisait pas**, et c'est le point qui décide de tout le reste :
avec un secret **symétrique**, celui qui vérifie doit connaître le secret de celui qui
signe. financial-service détiendrait la clé d'order-service pour le vérifier — le
compromettre rendrait donc toutes les clés qu'il vérifie. D'où une signature
**asymétrique** P-256, une paire par hôte : compromettre un service permet d'usurper
ce service-là, et rien d'autre.

L'attestation est liée à la **méthode** appelée et expire en **trente secondes**.

| Fichier | Rôle |
|---|---|
| `HBA.Shared.Hosting/Grpc/IdentiteInterne.cs` | frappe et vérification, registre des clés publiques |
| `HBA.Shared.Hosting/Grpc/AutorisationsGrpc.cs` | **engendré** — 24 appelants, 289 autorisations |
| `scripts/check-autorisations-grpc.py` | 22ᵉ contrôle — échoue dans les **deux** sens |
| `scripts/generer-identites-internes.sh` | engendre les paires, écrit un `identites.env` |

**Ce que la table ferme :** `RefundPayment` 24 → **1** appelant ; `ReleaseCoupon` 24 → 2 ;
`ReleaseReservation` 24 → 5 ; `GetSellerPayout` 24 → 10 ; six hôtes de livraison n'ont plus
aucun droit d'appel sortant.

**Ce que cela NE couvre PAS** — à lire avant de considérer §10.1 comme clos :

- **Le réseau reste en clair.** Un attaquant en coupure lit les charges utiles et peut
  **rejouer** une attestation pendant sa fenêtre. Le modèle de menace fermé est « un service
  compromis », pas « un observateur du réseau ». **mTLS reste à faire.**
- **`GetSellerPayout` reste ouvert à dix appelants** parce que l'enveloppe
  `MerchantsGrpcClient` est **une** classe qui appelle les vingt-six RPC de merchant. La
  découper par interface est le seul geste qui descendrait à un — **c'est un lot en soi.**
- **En développement les identités ne sont pas signées** (`Internal:IdentitesNonSignees`).
  `AddHbaGrpc` refuse de démarrer si le drapeau est posé hors `Development`.

**Deux pannes trouvées en câblant ceci, invisibles à la compilation :**

- **order-service n'avait aucune `Internal__ApiKey`** dans `compose.services.yml`. Il ne
  pouvait ni servir ses RPC ni en appeler un — le parcours de commande était mort sur cette
  pile. Les douze autres services l'avaient.
- **La passerelle non plus** (`compose.gateway.yml`) : la révocation de jeton (D27) échouait
  à la première requête authentifiée. Et il lui manquait `DisjoncteurClientInterceptor` au
  conteneur — oublié au lot 8.8, parce qu'elle n'appelle pas `AddHbaGrpc`.

### 2. La purge des réservations — pourquoi PAS un `Include` filtré

Le geste évident était de filtrer `Include(i => i.Reservations.Where(r => r.IsActive))`.
Il aurait **rouvert un double décrément de stock**.

`InventoryItem.Confirm` établit son idempotence en cherchant
`_reservations.Any(r => r.OrderId == orderId && r.Status == Confirmed)`. Une réservation
confirmée n'est plus « active » : un `Include` filtré la ferait disparaître de la
collection, et une confirmation **rejouée** — ce que l'inbox rend possible — ne trouverait
plus la trace de la première. Le stock serait décrémenté deux fois.

D'où une **purge** : `PurgeStockReservationsWorker` supprime les réservations en état
terminal au-delà de la rétention. **La rétention doit rester supérieure à toute fenêtre
de rejeu** — 90 jours par défaut, réglable par
`Inventory:ReservationPurge:{IntervalHours|RetentionDays|BatchSize}`.

La suppression se fait en **deux requêtes** (identifiants avec `Take`, puis
`ExecuteDeleteAsync`) parce qu'EF refuse de combiner `ExecuteDelete` avec `Take`.

## VAGUE 9 — Hygiène
**~3 jours. À tout moment. 24 anomalies.**

- **9.1** — Supprimer les 13 copies de `.proto` non compilées ; retirer ou brancher les 45 RPC morts.
- **9.2** — Les **83 valeurs d'énumération jamais assignées** : les atteindre ou les retirer.
- **9.3** — Documenter l'écart assumé : billing, wallet, recommendation, wishlist sont des **modules hébergés**, pas des services autonomes.
- **9.4** — Corriger `scripts/check-migrations.py` : il ne vérifie que les tables, jamais les colonnes ; `check_sql_identifier_case` est **totalement mort**.
- **9.5** — Restes de nommage `HBA.Deliveries.*` · `shared/kafka-schemas/` vide · métriques métier et sondes de disponibilité.

---

## Transverse, à ne pas reporter

**ISSUE-071 — les jetons en clair. ✅ FERMÉE, option (A).**

Les secrets ne traversent plus l'outbox ni Kafka en clair. `ISecretProtector`
(`HBA.Shared.Application/Abstractions`) déclare l'opération ; `AesGcmSecretProtector`
(`HBA.Shared.Infrastructure/Security`) l'implémente en AES-GCM — chiffrement
**authentifié**, nonce aléatoire de 12 octets par message, format versionné
`v1.<nonce>.<étiquette>.<chiffré>`.

Trois champs étaient concernés, tous renommés pour que le clair ne puisse plus
être écrit par inadvertance :

| Événement | Avant | Après |
|---|---|---|
| `EmailVerificationRequestedIntegrationEvent` | `VerificationToken` | `ProtectedVerificationToken` |
| `PasswordResetRequestedIntegrationEvent` | `ResetToken` | `ProtectedResetToken` |
| `SellerMemberInvitedIntegrationEvent` | `InvitationToken` | `ProtectedInvitationToken` |

Cinq producteurs chiffrent (quatre dans identity-service, un dans seller-service),
trois consommateurs déchiffrent dans notification-service, au dernier moment.

**La clé doit être identique de part et d'autre.** `Security:SecretProtection:Key` —
32 octets en base64. Elle est dans le secret `hba-platform` (k8s), monté par tous les
déploiements, et dans l'ancre `x-dev-auth` de `docker-compose.dev.yml`. Une divergence
ne se voit pas au démarrage : elle se voit quand plus aucun e-mail ne part.
`AesGcmSecretProtector` **refuse de démarrer en production** sans clé.

Ce qui n'est pas couvert : quelqu'un qui a la clé — donc une compromission d'identity
ou de notifications. Ce n'était pas l'objectif ; ces services doivent lire le secret
pour l'envoyer. Ce qui est couvert, c'est exactement la surface qui posait problème :
dump de base, sauvegarde, réplica analytique, consommateur du topic.

**Le volet inventory d'ISSUE-025 n'est PAS un oubli.** L'audit demandait de consommer l'événement « côté catalog et inventory ». Seul catalog le fait, délibérément : `InventoryItem` ne connaît ni vendeur ni boutique — il ne porte qu'un SKU et un `LocationId` — et `FulfillmentLocation` a bien un `OwnerId` mais aucun état, donc rien à basculer. Rendre inventory sensible aux vendeurs demande une migration et un nouvel état à maintenir, pour une défense en profondeur : ce qui retire réellement de la vente, c'est l'offre, et `OfferStatusTransitions.IsPurchasable` ne rend vrai que pour `Active`.

**ISSUE-068 — les tests. Ce qui est couvert, et ce qui ne l'est pas.**

Cinq suites d'autorisation préexistaient — catalog, financial, food, merchants, order, quarante cas — et couvrent notamment les deux fuites inter-vendeur du lot 1.6. Quatre suites ont été ajoutées avec la clôture de la vague 1 :

| Suite | Ce qu'elle prouve |
|---|---|
| `HBA.Media.AuthorizationTests` | propriété et durée de signature : le refus d'un média privé d'autrui, l'indiscernabilité du refus et de l'inexistence, les trois bornes du plafond `expiresIn` |
| `HBA.ReturnRefund.AuthorizationTests` | le rôle du groupe sur les huit routes vendeur, et surtout qu'un `sellerId` glissé en query string ne rouvre plus le carnet d'un concurrent |
| `TokenRevocationTests` (passerelle) | le refus d'un jeton révoqué, l'absence de divulgation de la cause, l'échec **ouvert** quand identity est injoignable, et la mémorisation par jeton |
| `SuspensionDuCatalogueTests` | quelles offres sont retirées, lesquelles sont épargnées, et dans quel état chacune revient |

**Ce que ces suites ne prouvent pas, et il faut le savoir.** Elles démarrent l'hôte sans base : un contrôle qui s'exécute DANS le handler, après lecture en base, n'y est pas atteignable. Concrètement, la garde d'appartenance de sept des huit routes vendeur de return-refund (le cœur d'ISSUE-017), la garde de propriété côté client (ISSUE-019) et le refus d'un demandeur nul dans `CreateReturnCommand` restent **couverts par la relecture, pas par un test**. Chaque suite le dit en tête, en toutes lettres. Il faut pour cela des tests d'intégration avec base, sur le modèle de `HBA.Catalog.IntegrationTests`.

**Et chaque lot des vagues suivantes porte ses tests dans sa définition de terminé** — sinon ils ne seront jamais écrits.

---

## Chemin critique, en une lecture

```
D-4 ─→ ISSUE-022 (révocation)

2.1 inbox ─→ 2.2 topics ─→ 2.3 chaînes ─→ VAGUE 3 (argent, stock)
                        └─→ 2.4 suspension/boutique
                        └─→ VAGUE 6 (food)

D-1 ─→ 4.1 promotions
D-2 ─→ 4.2 SellerOrder ──→ 7.x routes de commande
D-3 ─→ 5.2 driver-service ─→ 5.1, 5.3, 5.4

VAGUES 8 et 9 : en parallèle, à partir de la vague 3.
```

**De l'ordre de 5 à 7 semaines** pour le chemin critique, hors décisions et hors finition des squelettes. Repère de séquencement, pas engagement : les vagues 0 et 1 ont compilé du premier coup, mais rien n'a encore été exécuté contre une base.
