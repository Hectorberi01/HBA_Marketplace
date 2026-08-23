# Audit des sagas et de la communication inter-services

*Extraction mécanique sur les treize services — 15 août 2026.*

---

## Ce qui a été vérifié, et comment

Trois questions, trois extractions automatisées plutôt qu'une lecture à l'œil :

1. **Le transport respecte-t-il la règle ?** gRPC pour le synchrone, Kafka pour
   l'asynchrone, rien d'autre entre services.
2. **Chaque appel synchrone trouve-t-il un serveur ?** Un client gRPC enregistré
   sans service en face rend `UNIMPLEMENTED` au premier appel réel — jamais au
   démarrage.
3. **Chaque événement publié trouve-t-il un consommateur ?** Un événement sans
   destinataire ne se plaint pas : le producteur réussit, le courtier stocke, et
   l'effet métier n'a simplement pas lieu.

La troisième question est celle qui rapporte. Les deux premières sont saines.

---

## 1. Transport — conforme

**Aucun appel HTTP entre services.** Les sept `AddHttpClient` recensés visent
tous l'extérieur : webhooks partenaires (delivery), stockage S3 (media), les
cinq PSP dont FedaPay (financial), Resend (communication), rembg (catalog).

**Neuf services gRPC, tous implémentés et tous exposés :**

| Contrat | Servi par |
|---|---|
| `IdentityApi` | identity-service |
| `UsersApi` | user-service |
| `MerchantApi` | merchant-service |
| `CatalogApi` *(porte aussi Products)* | catalog-service |
| `InventoryApi` | inventory-service |
| `CommerceApi` *(panier)* | commerce-service |
| `OrderApi` | order-service |
| `FoodApi` | food-service |
| `MediaApi` | media-service |

**Quatorze appels synchrones déclarés, zéro sans serveur.** Chaque
`AddXGrpcClient` a son `Services:X` renseigné dans le compose — et ces
enregistrements lèvent au démarrage si l'adresse manque, donc la vérification
est doublée à l'exécution.

Un point de vocabulaire à connaître : `AddProductsGrpcClient` parle à
`CatalogApi`, pas à un `ProductApi` qui n'existe pas. Products est hébergé par
catalog-service — trace de la dualité Catalog/Products documentée ailleurs.

---

## 2. Événementiel — 67 événements, 23 sans consommateur

C'est ici que se trouvent toutes les ruptures. Trois catégories, et elles ne se
traitent pas pareil.

### ❌ Ruptures réelles — les deux côtés sont extraits

| Événement | Publié par | Devrait aller à | Effet manquant |
|---|---|---|---|
| `FoodOrderReceived` | food | food, communication | Le restaurateur n'est pas prévenu |
| `FoodOrderAccepted` | food | order, communication | La commande n'avance pas |
| `FoodOrderPreparing` | food | communication | Le client ne voit rien bouger |
| `FoodOrderReadyForPickup` | food | delivery | **Aucune course n'est créée** |
| `FoodOrderPickedUp` | food | order, communication | — |
| `FoodOrderRejected` | food | order, financial | **La commande n'est pas annulée, le paiement pas remboursé** |
| `FoodOrderCancelled` | food | order, financial | idem |
| `DeliveryAssigned` | delivery | communication | **Le livreur n'est pas notifié** de la proposition |
| `PaymentRefunded` | financial | communication | Le client n'est jamais informé d'un remboursement |
| `KybDocumentRemoved` | merchant | media | Le fichier reste dans l'object storage |
| `CartCheckedOut` | commerce | — | À trancher : doublon d'`OrderPlaced` ? |
| `StoreOpened` / `StoreClosed` | merchant | products | Les offres d'une boutique fermée restent en vente |

### ⏳ Attendues — le consommateur appartient à un module non extrait

`ProductCreated`, `ProductDeleted`, `ProductStatusChanged`, `ProductMediaRemoved`,
`ReviewRejected`, `StockReplenished`, `StockReserved`, `BrandCreated`,
`CategoryCreated`, `UserEmailConfirmed` → **Search**, **Products/Offers**.

### Consommés mais jamais publiés — l'inverse, et c'est pire

| Événement | Consommé par | Publié par |
|---|---|---|
| `ShipmentShipped` | communication | **personne** |
| `ShipmentDelivered` | communication, financial | **personne** |
| `ShipmentReadyForPickup` | — | **personne** |
| `ReturnRefunded` | communication, financial | **personne** |
| `ReturnRefundApproved` | communication | **personne** |

Shipping et Returns n'ont jamais été extraits. Des gestionnaires attendent des
événements que plus rien n'émet : ils passent tous les contrôles de compilation
et de démarrage, et ne se déclencheront jamais.

---

## 3. Saga Client — marketplace

```
panier          commerce ──gRPC──▶ catalog (produit) , inventory (stock)     ✓
checkout        commerce ──gRPC──▶ order                                     ✓
commande        order    ──gRPC──▶ commerce (panier valorisé)                ✓  [rétabli]
                order    ──gRPC──▶ inventory (réservation)                   ✓
                order ──OrderPlaced──▶ commerce (vider le panier)            ✓
                                  ▶ communication (accusé)                   ✓
paiement        financial ──PaymentCaptured──▶ order (confirmer)             ✓
                financial ──PaymentFailed──▶ order (annuler) + communication ✓
                order ──OrderConfirmed──▶ financial (gain vendeur)           ✓
                                       ▶ communication (client + vendeur)    ✓
livraison       ???                                                          ❌
                delivery ──DeliveryCompleted──▶ order                        ✓  [rétabli]
                order ──OrderDelivered──▶ financial (escrow + versement)     ✓
avis            engagement ──ReviewPublished──▶ communication                ✓
```

**La rupture est au milieu.** Rien ne crée la course pour une commande
marketplace. Dans le monolithe c'était Shipping qui posait la référence
`SHIP-…` ; Shipping n'est pas extrait, et le nouveau pont attend `ORDER-…` que
personne ne pose (tâche #157).

Conséquence en chaîne : la commande n'atteint jamais « livrée », donc l'escrow
n'est jamais libéré, donc **le vendeur n'est jamais payé**.

Deuxième rupture, silencieuse : **`PaymentRefunded` n'a aucun consommateur**. Un
remboursement aboutit en base et le client ne l'apprend jamais.

---

## 4. Saga Vendeur — marketplace

```
inscription     merchant ──SellerRegistered──▶ identity (rôle Seller)        ✓
                                            ▶ communication                  ✓
KYB (admin)     merchant ──SellerKybRejected──▶ communication                ✓
activation      merchant ──SellerActivated──▶ communication                  ✓
suspension      merchant ──SellerSuspended / SuspensionLifted──▶ communic.   ✓
fermeture       merchant ──SellerClosed / SellerDeleted──▶ catalog           ✓
boutique        merchant ──StoreOpened / StoreClosed──▶ ???                  ❌
pièces KYB      merchant ──KybDocumentRemoved──▶ ???                         ❌
commandes       order ──OrderConfirmed──▶ communication (vendeur prévenu)    ✓
versement       financial ──PayoutPaid──▶ communication                      ✓
stock           inventory ──StockDepleted──▶ communication                   ✓
```

Le cycle de vie vendeur est le mieux câblé de la plateforme. Deux trous, tous
deux liés à des modules non extraits : fermer une boutique ne retire pas ses
offres de la vente, et supprimer une pièce KYB laisse le fichier dans MinIO.

---

## 5. Saga Vendeur — food

```
candidature     food ──(REST)──▶ soumission du dossier                       ✓
validation      food ──RestaurantApproved──▶ identity (rôle FoodPartner)     ✓
                                          ▶ communication                    ✓
refus           food ──RestaurantRejected──▶ communication                   ✓
suspension      food ──RestaurantSuspended / Reopened──▶ communication       ✓
─────────────────────────────────────────────────────────────────────────────
commande reçue  food ──FoodOrderReceived──▶ ???                              ❌
acceptation     food ──FoodOrderAccepted──▶ ???                              ❌
préparation     food ──FoodOrderPreparing──▶ ???                             ❌
prêt à enlever  food ──FoodOrderReadyForPickup──▶ ???                        ❌
enlevé          food ──FoodOrderPickedUp──▶ ???                              ❌
refus           food ──FoodOrderRejected──▶ ???                              ❌
annulation      food ──FoodOrderCancelled──▶ ???                             ❌
```

**Le cycle de vie du restaurant fonctionne. Le cycle de vie d'une commande de
repas est entièrement muet.** Les sept événements sont publiés et aucun n'est
écouté.

Concrètement : un client commande un repas, le restaurant l'accepte, le prépare,
le déclare prêt — et rien ne se passe. Aucune course n'est créée, la commande
reste figée, le client n'est jamais informé. Si le restaurant refuse, la commande
n'est pas annulée et le paiement n'est pas remboursé.

Dans le monolithe, ces sept événements étaient consommés par deux fichiers de la
composition root : `FoodDeliveryBridgeHandlers.cs` et `FoodOrderBridgeHandlers.cs`.
Ils n'ont pas suivi l'extraction.

C'est la rupture la plus grave de l'audit.

---

## 6. Saga Livreur

```
inscription     delivery ──(REST)──▶ dossier livreur                         ✓
vérification    delivery ──DriverVerified──▶ identity (rôle Driver)          ✓
proposition     delivery ──DeliveryAssigned──▶ ???                           ❌
acceptation     delivery ──DeliveryAccepted──▶ delivery (webhook partenaire) ✓
collecte        delivery ──DeliveryPickedUp──▶ delivery (webhook)            ✓
remise          delivery ──DeliveryCompleted──▶ order + webhook              ✓
annulation      delivery ──DeliveryCancelled──▶ delivery (webhook)           ✓
aucun livreur   delivery ──DeliveryNoDriverAvailable──▶ delivery (webhook)   ✓
gains           ???                                                          
```

**`DeliveryAssigned` n'a aucun consommateur.** Le livreur dispose de
quarante-cinq secondes pour accepter une course dont **rien ne l'avertit**. Le
contrat de l'événement documente lui-même que son unique consommateur devait
envoyer une notification poussée.

**Et communication-service ne consomme AUCUN événement de livraison.** Ni
`DeliveryAccepted`, ni `DeliveryPickedUp`, ni `DeliveryCompleted`. L'acheteur
n'est jamais informé de l'avancement de sa livraison — alors que c'est
l'information qu'il regarde le plus.

Le crédit du livreur à la remise (`DriverWallet`) est branché côté financial via
le domaine, mais aucun gestionnaire d'intégration ne le déclenche depuis
`DeliveryCompleted` — à confirmer sur pièce.

---

## 7. Saga Admin

```
KYB vendeur     merchant (REST admin) ──▶ approve / reject                   ✓
                merchant ──SellerKybRejected──▶ communication                ✓
validation resto food (REST admin) ──▶ approve / reject / suspend            ✓
                food ──RestaurantApproved / Rejected──▶ communication        ✓
livreurs        delivery (REST ops) ──▶ verify / suspend / block             ✓
                delivery ──DriverVerified──▶ identity                        ✓
modération avis engagement ──ReviewRejected──▶ ???                           ⏳
comptes         identity (REST admin) ──▶ rôles, verrouillage                ✓
```

Le parcours admin est complet côté commande ; ses effets dépendent des sagas
qu'il déclenche, dont les ruptures sont listées plus haut.

---

## Synthèse — par gravité

| # | Rupture | Effet |
|---|---|---|
| 1 | Sept événements `FoodOrder*` sans consommateur | Le parcours Food ne fonctionne pas au-delà de la prise de commande |
| 2 | Rien ne crée de course pour une commande marketplace | Aucune commande n'atteint « livrée » → **aucun vendeur n'est payé** |
| 3 | Aucun événement de livraison vers communication | L'acheteur ne suit jamais sa livraison |
| 4 | `DeliveryAssigned` sans consommateur | Le livreur n'est pas prévenu des propositions |
| 5 | `PaymentRefunded` sans consommateur | Un remboursement n'est jamais annoncé |
| 6 | `StoreOpened/Closed` sans consommateur | Fermer une boutique ne retire pas ses offres |
| 7 | `KybDocumentRemoved` sans consommateur | Fichiers orphelins dans l'object storage |
| 8 | Cinq événements consommés que personne ne publie | Gestionnaires morts (Shipping, Returns) |

Les sept premières sont des ponts perdus à l'extraction, pas des défauts de
conception : le monolithe les avait, dans sa composition root, et le déménagement
des modules a laissé les fichiers qui les reliaient.

---

# Second passage — après les corrections

*Même jour, après SAGA-1, SAGA-2 et SAGA-3.*

## Ce qui a été fermé

| # | Rupture | État |
|---|---|---|
| 1 | Sept `FoodOrder*` sans consommateur | ✅ Le parcours Food va du paiement au livreur |
| 3 | Aucun événement de livraison vers communication | ✅ L'acheteur suit sa livraison |
| 4 | `DeliveryAssigned` sans consommateur | ✅ Le livreur est prévenu de ses propositions |
| 5 | `PaymentRefunded` sans consommateur | ✅ Et surtout : **personne ne remboursait du tout** |

**38 → 45 événements consommés. 23 → 1 sans consommateur.**

## Trois causes que l'audit initial n'avait pas vues

**La conversion gRPC des commandes mentait.** Le message proto portait sept
champs quand le contrat C# en porte dix-sept ; le client comblait les trous en
inventant `Kind = "Goods"`. Toute commande de repas lue par un autre service
revenait en commande de marchandise. Aucun pont Food n'aurait pu fonctionner,
quels que soient les gestionnaires branchés.

**Neuf événements étaient déclarés dans deux espaces de noms.** L'enveloppe Kafka
ne porte que le nom court : le consommateur en résolvait un au hasard de l'ordre
de chargement des assemblies. Un gestionnaire enregistré sur l'autre n'était
jamais appelé, sans erreur. `PaymentCaptured` en faisait partie — **aucune
commande n'était confirmée après paiement**.

**Personne ne remboursait.** `OrderCancelled` avait deux consommateurs — reprise
des gains vendeur, notification — et aucun ne rendait l'argent. Le monolithe le
faisait dans un helper de sa composition root ; le geste s'est perdu à la découpe.
Cela valait pour TOUTE annulation, pas seulement Food.

## Correction à l'audit initial : la couche synchrone n'est pas saine

Le premier passage concluait « neuf contrats gRPC, tous implémentés et tous
exposés, zéro appel sans serveur ». **Il vérifiait l'existence du serveur, pas le
fait que le client lui parle.**

Sept méthodes de clients gRPC ne contactent jamais le serveur — elles rendent
`Task.FromResult(null)` en dur.

| Client | Méthode | Effet |
|---|---|---|
| Products | `GetOfferAsync` | **Aucun article ne peut entrer dans un panier** |
| Products | `GetOffersAsync`, `ListPurchasableOffersAsync`, `ListOffersBySkuAsync` | Sans appelant inter-services connu |
| Food | `GetRestaurantByOwnerAsync`, `GetStaffMembershipAsync`, `GetOrderAsync` | Latents — appelés seulement depuis food-service, en processus |

`GetOfferAsync` est appelé par `AddItemToCartCommandHandler`. C'est le premier
geste du parcours client, avant le checkout et le paiement.

Un bouchon est pire qu'une `NotImplementedException` : celle-ci se voit au
premier appel. Celui-là rend « cette offre n'existe pas », et l'appelant conclut
que l'offre n'existe pas.

Deux bouchons du même genre avaient déjà été trouvés à la main dans la journée —
`InventoryGrpcClient.GetLocationAsync` (l'adresse d'enlèvement, donc aucune
course possible) et le contrat de commande amputé. Le contrôle
`check-grpc-stubs.py` existe désormais pour ne plus les chercher à l'œil.

## Reste à traiter

| Priorité | Point | Effet |
|---|---|---|
| 1 | `GetOfferAsync` et ses trois sœurs (#165) | Le panier est inutilisable |
| 2 | Rien ne crée de course marketplace (#157) | Aucune commande colis n'atteint « livrée » → vendeurs non payés |
| 3 | Bouchons Food (#166) | Latents, mais ils mentent |
| 4 | Trois interfaces `*ModuleApi` en double (#164) | Bruyant au démarrage, donc non urgent |
| 5 | `KybDocumentRemoved` (#158) | Fichiers orphelins dans l'object storage |

Les quatorze consommateurs « attendus » restent liés aux modules non extraits —
Search, Disputes, Shipping, Products/Offers.

## Reproduire cet audit

```bash
./scripts/check-all.sh                     # les six contrôles
python3 scripts/check-event-consumers.py   # consommateurs perdus + noms ambigus
python3 scripts/check-grpc-stubs.py        # clients qui ne parlent à personne
```
