# PRIORITY_FIX_PLAN — HBAExpress

*Ordre de correction proposé. Aucune correction n'a été appliquée : l'audit est en lecture seule.*

---

## Le principe qui gouverne cet ordre

Une remarque d'abord, parce qu'elle change tout le reste du plan.

**La majorité des sagas rompues ont une cause unique : ISSUE-001, le désaccord de nommage des topics Kafka.** Les handlers existent, ils sont corrects, ils sont tous enregistrés en DI. Ils ne reçoivent rien.

La tentation est de corriger ce défaut en premier, puisqu'il débloque tout. **Ce serait une erreur.** Au moment où les messages recommenceront à circuler, ils arriveront sur 90 handlers non idempotents (ISSUE-008). Kafka livre *au moins une fois* : un rejeu de partition recréditera un vendeur, réservera à nouveau du stock, renverra une notification. Le défaut d'idempotence est aujourd'hui **masqué** par celui des topics, et il se révélera à l'instant exact où on lèvera le masque.

D'où la règle du P0 : **l'inbox avant les topics, ou les deux dans la même livraison.** Jamais les topics seuls.

Deuxième principe : quelques défauts se corrigent en minutes et évitent une panne totale (ISSUE-034 : la passerelle ne démarre pas). Ils passent devant, parce que rien ne se teste tant qu'ils sont là.

Troisième principe : deux points ne sont **pas** des décisions techniques et ne doivent pas être tranchés par un développeur seul. Ils sont isolés en §P0-bis.

---

## P0 — Bloquants de production

*Rien ne peut être mis en service tant que ces points ne sont pas traités.*

### P0-0 — Faire démarrer et sécuriser (quelques heures)

| # | Correction | Pourquoi maintenant |
|---|---|---|
| ISSUE-034 | Ajouter `Services:FoodCart` et `Services:FoodOrder` à la configuration de la passerelle | **La passerelle ne démarre pas.** Rien n'est testable avant. `scripts/check-service-addresses.py` couvre déjà ce contrôle |
| ISSUE-010 | Refuser le démarrage si la passerelle de versement réelle n'est pas configurée | `SimulatedPayoutGateway` en production débite le vendeur sans rien virer |
| ISSUE-054, ISSUE-055 | Même traitement pour `InMemoryObjectStorage` et `NullPushSender` | Même classe de défaut : un bouchon qui se tait au lieu de refuser |

Le dépôt applique déjà ce principe ailleurs — `AddXGrpcClient` lève à la construction de l'hôte quand l'adresse manque. C'est la bonne règle : **un bouchon en production doit empêcher le démarrage, pas se contenter d'un avertissement.**

### P0-1 — Les données personnelles et l'argent d'autrui (jours)

| # | Correction |
|---|---|
| ISSUE-021 | URL signées : contrôle de propriété, respect de `Visibility`, plafond serveur sur la durée — **les pièces d'identité passent avant tout le reste** |
| ISSUE-020 | `DELETE /media/{id}` : authentification + propriété ; suppression logique sur les pièces probantes |
| ISSUE-017, ISSUE-018, ISSUE-019 | return-refund : gardes d'appartenance sur les cinq routes vendeur, `sellerId` depuis le jeton, identité obligatoire côté client |
| ISSUE-023 | food-review-service : authentification, identité issue du jeton |
| ISSUE-016 | Les dix services sans authentification : appeler `AddHbaService`, politique admin sur les routes d'administration |
| ISSUE-022 | Appeler `ValidateAccessTokenAsync` : sans elle, une suspension met 15 minutes à mordre |
| ISSUE-024, ISSUE-025 | Le statut du vendeur entre dans l'autorisation ; la suspension retire les offres de la vente |

### P0-2 — L'inbox, puis les topics (jours)

| Ordre | # | Correction |
|---|---|---|
| 1 | ISSUE-008 | Brancher `IConsumerInbox` sur **tous** les handlers à effet de bord. `EfConsumerInbox` existe et fonctionne — c'est du câblage, pas de la conception |
| 2 | ISSUE-001 | Unifier la dérivation du nom de topic : une seule fonction, appelée des deux côtés. Puis rejouer la matrice des 136 événements |
| 3 | ISSUE-002 à ISSUE-006 | Vérifier un par un que les handlers de paiement, de libération de stock, d'ouverture de ticket et de création de course sont bien atteints |

**Ne pas inverser 1 et 2.**

### P0-3 — Le stock et l'argent qui ne reviennent pas (jours)

| # | Correction |
|---|---|
| ISSUE-031 | Balayeur de réservations expirées dans inventory |
| ISSUE-032 | Compenser les réservations si `SaveChangesAsync` échoue — même motif dans `OrderLifecycleCommands.cs:223` et `ReturnLifecycleCommands.cs:154` |
| ISSUE-013, ISSUE-014 | Double remboursement : interdire `from == to`, compter les `Pending`, calculer réellement les quantités déjà retournées |
| ISSUE-012 | Implémenter l'exécution du remboursement : aujourd'hui aucun ne part |
| ISSUE-009 | Remboursement chez les quatre fournisseurs ; en attendant, ne plus lever dans le handler |
| ISSUE-011 | Imputer le montant d'un remboursement partiel |
| ISSUE-015, ISSUE-050 | Compensations financières : virement refusé, gain repris sur vente annulée |
| ISSUE-065, ISSUE-066 | Les deux migrations initiales manquantes — sans elles, deux services ne démarrent pas |
| ISSUE-067 | Les deux migrations inertes |

---

## P0-bis — Deux décisions qui ne m'appartiennent pas

Ces deux points bloquent un pan entier du système, et aucun n'est technique. Ils demandent un arbitrage.

### 1. Qui supporte une remise financée par la plateforme ? *(ISSUE-052, ISSUE-033)*

promotion-service n'a **aucune notion de financeur** : `Promotion` porte un périmètre, un type, une valeur et un budget, rien d'autre. Le reste de la plateforme suppose pourtant la distinction — `CartContracts.cs:33` porte `SellerDiscount` **et** `PlatformDiscount`, et wallet calcule le gain du vendeur sur `UnitBasePrice - SellerDiscount` — mais le seul producteur écrit `SellerDiscount: 0m` en dur.

Conséquence si l'on branche promotion-service tel quel : **le vendeur supporte les coupons de la plateforme**, silencieusement, via le calcul des gains. La correction technique tient en une ligne. Elle change ce que les vendeurs touchent.

Deux options :
- **(a)** le vendeur ne supporte que ses propres remises — il faut alors ajouter le financeur au modèle `Promotion` ;
- **(b)** le vendeur supporte tout — statu quo, mais alors il faut l'écrire dans le contrat vendeur.

Tant que ce point n'est pas tranché, la plateforme ne peut faire **aucune promotion**.

### 2. Faut-il construire l'agrégat `SellerOrder` ? *(ISSUE-027, ISSUE-026)*

Il n'existe pas. `OrderingModuleApi.cs:66` renvoie `SellerOrderId: null` en dur. Sans lui : les cinq permissions `ORDER_*` ne gardent rien, le rôle `ORDER_MANAGER` ne peut que lire, le vendeur ne peut ni confirmer ni préparer ni remettre au livreur, et une commande à deux vendeurs n'a pas d'état par vendeur.

C'est le **seul défaut de cet audit qui exige de construire un agrégat**, pas de corriger du code : états, transitions, permissions, événements, migration, découpage à la création de commande. C'est un lot en soi, pas une finition. Il conditionne tout le parcours vendeur et la remise au livreur.

---

## P1 — Requis avant mise en service

| # | Correction |
|---|---|
| ISSUE-028 | Jeton de concurrence sur `Delivery` + unicité en base : deux livreurs ne peuvent plus accepter la même course |
| ISSUE-030, ISSUE-029 | Implémenter driver-service (inscription, documents, vérification) et alimenter le cache de positions — sans quoi aucune course n'est jamais proposée |
| ISSUE-056, ISSUE-057, ISSUE-058 | Preuve de livraison : OTP réel, politique de preuve renseignée, suivi réservé au livreur affecté |
| ISSUE-059, ISSUE-060, ISSUE-061 | Chaîne food : chemin de paiement pour `MealOrder`, clôture du panier, arbitrage administratif |
| ISSUE-035 | Fuite inter-vendeur du carnet de commandes (lignes d'autrui, GPS et téléphone de l'acheteur) |
| ISSUE-036, ISSUE-037 | Éviction de cache sur `SellerRole` ; limiteur de débit derrière `UseForwardedHeaders` |
| ISSUE-038, ISSUE-039 | Gardes de propriété sur les deux routes de paiement |
| ISSUE-042, ISSUE-043 | Activer l'audit sur les contextes sensibles ; enregistrer l'acteur ; corriger `AuditQueries.cs:29-33` qui affirme le contraire |
| ISSUE-045, ISSUE-046, ISSUE-047, ISSUE-048 | Statut de réservation, SKU sans stock, offres en rupture, revalidation prix/publication au paiement |
| ISSUE-049 | Plafond de remboursement calculé depuis la commande, pas depuis la demande |
| ISSUE-040, ISSUE-041 | Transfert de propriété ; fermeture de boutique qui arrête réellement la vente |
| ISSUE-062, ISSUE-063, ISSUE-064 | Chemin OTP, route `/api/auth/*`, routage vers return-refund |
| ISSUE-068 | Socle de tests : paiement, stock, idempotence, transitions d'état, concurrence |

---

## P2 — Requis rapidement

- ISSUE-044 — journal des mouvements de stock et transferts, ou retrait des permissions qui les promettent.
- ISSUE-051 — appeler l'invariant comptable dans le chemin d'écriture.
- ISSUE-053 — balayeur d'expiration des réservations de budget promotionnel.
- ISSUE-069, ISSUE-070 — découpler les quatre services delivery ; une seule déclaration de `Driver`.
- Disjoncteur sur les clients gRPC, et codes de statut métier (`NotFound`, `FailedPrecondition`, `AlreadyExists`, `PermissionDenied`) : aujourd'hui **aucun** n'est utilisé, donc l'appelant ne peut pas décider s'il doit réessayer.
- Index et contraintes d'unicité manquants (`DATABASE_AUDIT.md` §4-§5).
- Les 83 valeurs d'énumération jamais assignées : les atteindre ou les retirer. Une valeur inatteignable promet un état que le système n'a pas.

---

## P3 — Amélioration technique

- Supprimer les 13 copies de `.proto` non compilées, ou les remplacer par un lien vers `shared/proto`.
- Retirer les 45 RPC morts, ou les brancher.
- Documenter l'écart assumé : billing, wallet, recommendation et wishlist sont des **modules hébergés**, pas des services autonomes. Ce choix est défendable ; ne pas l'écrire ne l'est pas.
- Décider du sort des onze squelettes : les finir, ou les retirer du dépôt. Aujourd'hui ils portent un README qui ne dit pas qu'ils sont provisoires — un lecteur conclut qu'ils sont finis.
- Restes de nommage `HBA.Deliveries.*` après renommage.
- Observabilité : métriques métier, sondes de disponibilité.
- Corriger `scripts/check-migrations.py` : il ne vérifie que les tables, jamais les colonnes, et ne voit pas les migrations dépourvues d'attributs (les deux défauts les plus dangereux du dépôt lui échappent). `check_sql_identifier_case` est **totalement mort** — il ne cherche que dans des chaînes `"""…"""`, et aucune migration n'en utilise.

---

## Ordre optimal, en une lecture

1. **Faire démarrer** (ISSUE-034) et **faire refuser les bouchons** (ISSUE-010, 054, 055).
2. **Fermer les fuites de données personnelles et d'argent d'autrui** (ISSUE-016 à 025).
3. **Inbox** (ISSUE-008), **puis** topics (ISSUE-001). Jamais l'inverse.
4. **Compensations** : stock, remboursements, gains (ISSUE-031, 032, 009 à 015).
5. **Trancher les deux décisions du P0-bis** — sans elles, promotions et parcours vendeur restent bloqués.
6. **Livreur** (ISSUE-028 à 030) et **chaîne food** (ISSUE-059 à 061).
7. **Tests** (ISSUE-068) — à mener *en parallèle* des étapes 3 à 6, pas après : chaque correction ci-dessus mérite son test de non-régression au moment où elle est faite.

---

## Méthode pour la suite

Si la correction commence, elle doit suivre les règles déjà en vigueur sur ce dépôt :

- une anomalie à la fois, ou un lot cohérent restreint ;
- un test qui échoue avant, qui passe après ;
- pas de refactorisation massive non nécessaire ;
- compatibilité des contrats publics préservée ;
- toute migration nécessaire écrite à la main, avec son snapshot, selon la convention maison ;
- commentaires et prose en français, dans le style « POURQUOI CECI EXISTE » du dépôt.
