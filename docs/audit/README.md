# Audits HBAExpress

Ce dossier rassemble **tous** les audits du dépôt. Il est organisé par date d'exécution,
parce qu'un audit périmé lu comme s'il était courant est plus dangereux qu'un audit absent.

```
docs/audit/
├── 2026-08-27-defauts-et-deploiement/  le plus récent — défauts ouverts et
│                                       couche de déploiement
├── 2026-08-21-complet/          l'audit de référence — 15 rapports, 12 000+ lignes
├── 2026-08-22-console-admin/    l'ordre des pages de la console — encore le seul
│                                document qui explique les 8 sections sans amont
├── 2026-08-23-pages-restantes/  les six lots d'écrans, tous faits — la section
│                                « hors périmètre » reste, elle, d'actualité
├── anterieurs/                  trois audits précédents, gardés parce que d'autres
│                                documents s'appuient dessus
└── IMPLEMENTATION_STATUS.md     (état d'avancement, ce n'est pas un audit)
```

## Ce qui a été retiré le 27 août, et selon quel test

Sept documents sont partis vers `_to_delete/2026-08-27-audits-remplaces/`. Le test
appliqué n'était pas « est-ce ancien » mais **« un autre document s'appuie-t-il
dessus, et son sujet existe-t-il encore »** :

| Retiré | Pourquoi |
|---|---|
| `anterieurs/AUDIT-SAGAS.md`, `AUDIT-SAGAS-3.md` | remplacés par les cinq `SAGA_*.md` ; cités par personne |
| `anterieurs/AUDIT-SELLER.md`, `AUDIT-CATALOG.md` | remplacés par `SERVICES_AUDIT.md` ; cités par personne |
| `anterieurs/AUDIT-VENDEUR-100.md` | remplacé par `SAGA_SELLER.md` ; cité par personne |
| `anterieurs/AUDIT_COMPLET_REEXECUTION_2026_08_20.md` | remplacé par l'audit du 21 ; cité par personne |
| `2026-08-22-bff/AUDIT_BFF.md` | **le BFF n'existe plus dans le dépôt** — l'audit décrivait un composant retiré depuis |

**CE QUI N'A PAS ÉTÉ RETIRÉ, ET C'EST LE POINT IMPORTANT.**

`2026-08-21-complet/` reste **entier**. Ses 162 anomalies ne sont pas résolues : sa
propre conclusion — 20 événements sur 136 raccordés, 31 RPC sur 116 — tient toujours.
C'est le document de travail, pas un souvenir.

`IMPLEMENTATION_STATUS.md` reste aussi, bien qu'il soit périmé — « Last verified :
2026-08-20 », et il parle encore du Client BFF qui n'existe plus. Mais P0.3 à P0.6 y
sont à `TODO` : ce sont des travaux ouverts, et aucun autre document ne les porte.
**À rafraîchir, pas à retirer.**

---

## `2026-08-27-defauts-et-deploiement/` — le plus récent

Un fichier, `AUDIT.md`. **Il ne remplace pas l'audit du 21** : il couvre ce que
celui-ci ne pouvait pas voir.

- **Ce qui reste non implémenté**, revérifié fichier par fichier le 27 — quatre
  services de livraison sans base, aucun moteur de routage, une affectation qui
  propose deux comptes fictifs, trois adaptateurs gRPC simulés.
- **Six bugs**, dont trois touchent de l'argent ou des secrets : la clé de
  chiffrement de développement utilisable en production si une variable manque, un
  téléversement média sans contrôle d'administrateur alors que son commentaire
  l'annonce, un gain vendeur payé deux fois à l'annulation.
- **La couche de déploiement**, qui n'existait pas le 21 août : huit défauts en une
  journée, six corrigés, deux ouverts.

Il contient aussi une section **« ce que cet audit a infirmé »** — un constat porté
plusieurs jours qui ne résiste pas à la vérification.

1 853 fichiers `.cs` lus par quatre analyses parallèles, plus les manifestes rendus
par `kustomize build` pour les trois environnements.

---

## `2026-08-21-complet/` — l'audit de référence

Audit statique complet du 21 août 2026 : architecture, services, communications, sécurité,
base de données, parcours métier des cinq acteurs, machines d'état.
**2 700 fichiers source analysés. 162 anomalies retenues, dont 39 CRITICAL.**

Aucun compilateur .NET n'était disponible : tout constat vient d'une lecture de code,
jamais d'une exécution. Chaque anomalie cite un fichier et une ligne.

### Par où commencer

| Ordre | Fichier | Ce qu'on y trouve |
|---|---|---|
| 1 | `ARCHITECTURE_AUDIT.md` | La vue d'ensemble : cible vs réel, écarts structurants, réponses aux quatre questions. **À lire en premier.** |
| 2 | `PRIORITY_FIX_PLAN.md` | L'ordre de correction P0 → P3, et pourquoi cet ordre-là |
| 3 | `PLAN_DE_CORRECTION.md` | Le plan d'exécution : 9 vagues, 34 lots, dépendances, définition de « terminé ». **C'est le document de travail.** |
| 4 | `IMPLEMENTATION_DEFECTS.md` | Les 162 anomalies, ISSUE-001 à ISSUE-075 détaillées, avec preuves et tests requis |

### Rapports de preuve

| Fichier | Portée |
|---|---|
| `SERVICES_AUDIT.md` | Les 31 services, un par un : projets, couches, agrégats, endpoints, événements, défauts |
| `GRPC_MATRIX.md` | Les 116 RPC : serveur, clients, échéance, retry, mapping d'erreurs, RPC morts |
| `KAFKA_EVENT_MATRIX.md` | Les 136 événements : producteurs, consommateurs, topics, outbox, inbox, idempotence |
| `DATABASE_AUDIT.md` | 23 contextes, 169 migrations, index, contraintes, concurrence, types monétaires |
| `SECURITY_AUDIT.md` | RBAC, IDOR, fuite inter-vendeur, permissions inutilisées, PII, upload, secrets |
| `SAGA_CLIENT.md` | Inscription, achat marketplace, commande food |
| `SAGA_SELLER.md` | Intégration, produit, stock, commandes, retours, finances |
| `SAGA_SELLER_MEMBER.md` | Invitation, rôles, permissions, cloisonnement boutique et vendeur |
| `SAGA_DRIVER.md` | Inscription livreur, disponibilité, affectation, enlèvement, livraison, preuve |
| `SAGA_ADMIN.md` | Validation vendeurs, modération, arbitrage, tarification, trace d'audit |
| `STATE_MACHINE_AUDIT.md` | 22 agrégats : transitions autorisées, utilisées, jamais atteintes |

### Ce que l'audit conclut, en trois phrases

L'ossature est bonne — Domain pur, montants en `decimal`, aucun accès SQL croisé,
échéances gRPC systématiques, RBAC membre solide.
Elle n'est pas raccordée : **20 événements sur 136** atteignent un consommateur,
**31 RPC sur 116** sont implémentés et appelés.
Conséquence : **aucun parcours métier de bout en bout n'aboutit aujourd'hui** —
l'acheteur est débité et sa commande reste figée.

---

## `anterieurs/` — audits précédents

Ces documents étaient auparavant **à la racine de `docs/`**. Plusieurs autres documents
les citent encore par leur nom seul (`DECISIONS.md`, `ETAT-ET-PLAN.md`,
`SOCLE-TRANSVERSE.md`, `PHASE3-OFFRES.md`) : les références restent lisibles, seuls
les chemins ont changé.

| Fichier | Date | Portée | Pourquoi il reste |
|---|---|---|---|
| `AUDIT-SELLER-RESTE.md` | 19 août | ce qu'il restait à faire côté vendeur | **six entrées de `DECISIONS.md` s'y réfèrent** par numéro de section : « Corrige `AUDIT-SELLER-RESTE.md` §1 », §2, §3, §4, §5. Sans lui, ces décisions ne se lisent plus |
| `AUDIT-CONFORMITE.md` | 17 août | conformité des 16 services d'alors | `SOCLE-TRANSVERSE.md` s'annonce en première ligne comme sa suite |
| `AUDIT-APP-VENDEUR.md` | 16 août | application vendeur | `PHASE3-OFFRES.md` s'ouvre en corrigeant le périmètre qu'il annonçait |

**CETTE JUSTIFICATION ÉTAIT TROP LARGE, ET C'EST CE QUI A PERMIS LE MÉNAGE.**

Ce paragraphe disait que les neuf documents étaient gardés « parce que plusieurs
décisions ne se comprennent qu'en les lisant ». Vérification faite, six d'entre eux
n'étaient cités par aucun autre fichier du dépôt : la raison valait pour trois, elle
protégeait les neuf.

**Ces trois-là ne décrivent plus l'état du code.** Ils expliquent pourquoi d'autres
documents disent ce qu'ils disent.

---

## Ce qui n'est pas ici

Les revues de cahier des charges et les études de faisabilité restent à la racine de `docs/` :
`FAISABILITE-CAHIER-MEMBRES.md`, `FAISABILITE-CAHIER-PANIER-COMMANDE.md`,
`REVUE-CAHIER-MEMBRES-V2.md`. Elles jugent une spécification, pas du code — ce n'est pas
le même exercice.
