# Plan pour terminer seller-service

Établi le 18 août 2026, après les lots 1 à 4 (`b23cfcb` et la suite).

Ce qui reste : deux lots du plan initial, trois points plus petits qu'aucun lot ne
couvrait, et une vérification qui n'a jamais eu lieu.

---

## Le constat qui réorganise le plan

**Les lots 5 et 6 sont une seule conception, pas deux.**

L'audit les avait séparés — `merchant_members` d'abord, `CheckMerchantCapability`
ensuite — au motif que le second n'a de sens qu'une fois les membres en place.
C'est vrai, mais l'inverse l'est tout autant, et c'est ce que la vérification a
montré.

**Cinq services hors de seller-service résolvent « le vendeur de ce compte »** par
`GetSellerByUserIdAsync` : catalog, inventory, order, payment, plus huit usages
internes. Tous supposent la même chose sans l'écrire : **un compte = un vendeur, et
ce compte peut tout faire**.

Ajouter des membres casse la seconde moitié de cette hypothèse. Un comptable membre
d'une boutique doit voir les commandes et pas toucher au catalogue — or catalog ne
sait poser qu'une seule question, « quel est le vendeur de ce compte ? », et en
déduit un droit total. C'est exactement le trou que `CheckMerchantCapability` est
fait pour combler.

Livrer les membres sans la capacité donnerait donc à chaque membre les pleins
pouvoirs de son propriétaire, sur cinq services, sans que rien ne le signale.

---

## Décision à prendre avant tout le reste

**Un compte peut-il appartenir à PLUSIEURS vendeurs ?**

### Option A — un compte, un vendeur (recommandée)

Un utilisateur est propriétaire OU membre d'exactement un vendeur.
`GetSellerByUserIdAsync` garde sa signature et se met simplement à répondre aussi
pour les membres.

- **Portée** : les cinq services appelants ne changent pas d'une ligne. Les membres
  gagnent l'accès le jour où la table existe.
- **Ce qu'on renonce à faire** : un comptable qui tient les comptes de trois
  boutiques a besoin de trois comptes utilisateurs.
- **Réversible** : passer à B plus tard reste possible ; l'inverse, non.

### Option B — un compte, plusieurs vendeurs

`GetSellerByUserIdAsync` devient `ListSellersForUserAsync`, et chaque appel doit
dire POUR QUEL vendeur il agit — un en-tête ou un segment d'URL, sur les cinq
services et dans l'application.

- **Portée** : cinq services, la passerelle, l'app vendeur, et une migration de
  contrat gRPC.
- **Quand cela vaut le coup** : quand des groupes multi-boutiques existent
  réellement. Rien dans le dépôt ne l'indique aujourd'hui.

**Recommandation : A.** Le besoin de B n'est pas démontré, et son coût est réel et
immédiat. La formulation « un compte, un vendeur » sera écrite dans le code, pas
supposée — c'est ce qui manquait jusqu'ici.

---

## Lot 5 — les membres d'équipe et leurs capacités

Un seul lot, parce que les deux moitiés ne tiennent pas séparées.

### 5.1 Le domaine

`SellerMember` : `SellerId`, `UserId`, `MemberRole`, ajouté/retiré par le
propriétaire. Un `MemberRole` **énuméré**, pas un `permissions_json` libre.

**Le §10.3 dit `permissions_json`. C'est un écart, et il est délibéré.** Un
document JSON de permissions libres n'est vérifiable par personne : ni le
compilateur, ni un test, ni l'appelant distant qui doit en déduire un droit. Trois
rôles nommés — `Owner`, `Manager`, `Accountant` — couvrent le besoin décrit et
donnent une réponse *stable* à `CheckMerchantCapability`. Le jour où un rôle
supplémentaire manque, on l'ajoute ; le jour où un JSON libre est mal interprété
par un service, personne ne le voit.

Invariants à tenir, et à tester : le propriétaire ne peut pas se retirer lui-même ;
un membre ne peut pas s'ajouter de membres ; retirer un membre ne ferme pas son
compte utilisateur.

### 5.2 La capacité, et les cinq appelants

`CheckMerchantCapability(sellerId, capability)` au proto, et son pendant dans
`ISellerModuleApi`. Les capacités viennent du croisement **rôle du membre × état du
vendeur** — vendre exige un KYB vérifié, pas seulement un rôle.

Les cinq appelants passent de « quel est le vendeur de ce compte » à « ce compte
a-t-il le droit de faire CECI pour ce vendeur ». C'est le gros du travail, et c'est
ce qui empêche un comptable de publier un produit.

### 5.3 Le rôle `Seller` pour les membres

Le rôle est greffé à l'inscription par `GrantSellerRoleHandler`. Un membre ajouté
n'en reçoit aucun, et se heurterait au `MapSellerGroup` posé au lot 3.

Il faut donc un `SellerMemberAddedIntegrationEvent` qu'identity consomme — le même
chemin que `SellerRegistered`. **C'est exactement le trou déjà documenté côté
restaurant** dans `GrantFoodPartnerRoleHandler` : « seul `OwnerUserId` reçoit le
rôle… l'écran de cuisine, qui est fait POUR eux, leur reste fermé ». Le refermer
ici donne le gabarit pour l'y refermer aussi.

### 5.4 Ce que ça coûte

Le plus gros lot de la série : une table, un agrégat, un RPC, cinq services
appelants, un événement consommé par identity, et l'app vendeur pour l'écran de
gestion d'équipe. À découper en livraisons si la première ne passe pas d'un coup.

---

## Lot 6 — les trois points restés en dehors

Petits, indépendants, livrables ensemble.

### 6.1 `GET /merchants/{id}` doit rendre ses boutiques

Le §10.3 les montre imbriquées ; `SellerSummary` ne les porte pas, et le client
fait un second appel. Écart au contrat que l'audit n'avait pas relevé.

`SellerSummary` traverse le proto gRPC : l'y ajouter touche les cinq appelants.
Voir si une projection HTTP séparée ne vaut pas mieux qu'un champ de plus sur le
contrat inter-services — la vitrine et le back-office n'ont pas les mêmes besoins.

### 6.2 `ApproveReactivation` sans demande préalable

La méthode accepte un compte simplement `Closed`, alors que son nom et son code
d'erreur affirment qu'une demande est requise. Ce n'est pas nécessairement faux — un
administrateur peut vouloir rouvrir un compte fermé par erreur — mais **le code et
son nom doivent dire la même chose**. Deux issues : refuser sans demande, ou
renommer et corriger le message. Une question de cahier, pas de code.

### 6.3 Les tests d'intégration

seller-service n'en a aucun. La fixture Testcontainers du catalogue est le gabarit ;
il n'y a qu'à la reprendre.

Trois parcours valent le déplacement, et aucun n'est couvrable en unitaire :

1. **Le départ à froid des migrations.** Jamais rejoué sur ce service. Neuf
   migrations, dont deux fraîches.
2. **Le consommateur RGPD du lot 4.** Il n'a jamais tourné contre un vrai courtier,
   et c'est le seul consommateur du dépôt SANS idempotence naturelle : sa garde
   d'inbox est load-bearing, et rien ne l'a encore éprouvée.
3. **Le parcours KYB de bout en bout** — dépôt, soumission, validation, activation —
   avec la vérification que les événements sortent bien de l'outbox.

---

## Ordre proposé

| # | Contenu | Pourquoi là |
|---|---|---|
| **0** | Trancher A ou B | Rien ne peut commencer avant : la réponse dessine le contrat |
| **1** | Tests d'intégration (6.3) | **Avant** les membres, pas après. Le lot 5 touche cinq services et le contrat gRPC ; le faire sans filet, c'est refaire à l'aveugle ce qui a déjà cassé deux fois cette session |
| **2** | Les deux petits points (6.1, 6.2) | Rapides, indépendants, et ils dégagent le terrain |
| **3** | Membres + capacité (lot 5) | Le gros morceau, avec le filet posé |

**Le déplacement des tests d'intégration devant les membres est le seul vrai
changement par rapport au plan d'audit.** La raison est empirique : sur ce dépôt,
les deux défauts les plus coûteux de la session — l'espace de noms d'`order-service`
et le `CatalogClient` qui contournait la passerelle — ont été trouvés par un build
ou par une relecture, pas par un test. Le lot 5 modifie cinq services d'un coup ;
c'est précisément le cas où un build vert ne prouve rien.

---

## Ce que ce plan ne couvre pas

- **Rien de ce service n'a jamais tourné contre une vraie base.** Les migrations
  sont générées, pas rejouées. C'est l'objet du point 1, et c'est aussi pourquoi
  aucune estimation ici n'est fiable tant qu'il n'est pas fait.
- L'application vendeur Flutter : l'écran de gestion d'équipe du lot 5 lui
  appartient, et il n'est pas chiffré ici.
- La bascule KYB dépréciée reste en place jusqu'à ce que l'app envoie
  `POST /kyb/submit`. La condition de retrait est écrite dans le code.

---

## État d'avancement

*Mis à jour le 19 août 2026.*

| # | Contenu | État |
|---|---------|------|
| **0** | Trancher A ou B | ✅ **A** — un compte, un vendeur |
| **1** | Tests d'intégration (6.3) | ✅ Livré (`tests/HBA.Merchants.IntegrationTests`) |
| **2** | Les deux petits points (6.1, 6.2) | ✅ Livré — voir **D19** et **D20** |
| **3** | Membres + capacité (lot 5) | ⬜ Reste |

### Ce que le point 1 a rapporté au-delà de son objet

Un défaut latent **hors seller-service** : `La_documentation_openapi_est_servie_sans_jeton`,
côté catalogue, ne pouvait pas passer — `UseHbaOpenApi` n'ouvre la page qu'en
Development ou sur `OpenApi:Enabled`, et la fixture tourne en `Testing`. Le test
n'avait jamais été exécuté pour le dire : il porte `[Trait("Docker","true")]`, et
`make test` filtre dessus. C'est le prix du découpage, et il faut le connaître —
**`make test-integration` doit être lancée, pas seulement écrite.**

### Le point 2, tranché

- **6.1** — `GET /merchants/{id}` rend ses boutiques par une projection HTTP
  séparée (`SellerDetail`), sans toucher au contrat gRPC. **D20**.
- **6.2** — `ApproveReactivation` exige désormais la demande préalable : c'est le
  nom qui avait raison. **D19**.

### Ce qui reste, et ce qu'il faut savoir avant de commencer

Le lot 5 est le plus gros de la série — une table, un agrégat, un RPC, **cinq
services appelants**, un événement consommé par identity, et l'écran de gestion
d'équipe de l'application vendeur. Le filet est maintenant posé : c'était toute la
raison de faire passer le point 1 devant.

À découper en livraisons si la première ne passe pas d'un coup. L'ordre naturel :
le domaine et sa table d'abord (5.1), la capacité et les cinq appelants ensuite
(5.2), le rôle greffé aux membres en dernier (5.3) — c'est celui des trois qui
touche un autre service.
