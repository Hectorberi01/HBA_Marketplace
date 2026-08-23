# Audits HBAExpress

Ce dossier rassemble **tous** les audits du dépôt. Il est organisé par date d'exécution,
parce qu'un audit périmé lu comme s'il était courant est plus dangereux qu'un audit absent.

```
docs/audit/
├── 2026-08-21-complet/     l'audit courant — 15 rapports, 12 000+ lignes
├── anterieurs/             les audits précédents, partiels ou dépassés
└── IMPLEMENTATION_STATUS.md   (état d'avancement, ce n'est pas un audit)
```

---

## `2026-08-21-complet/` — audit courant

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

| Fichier | Date | Portée | Encore valable ? |
|---|---|---|---|
| `AUDIT_COMPLET_REEXECUTION_2026_08_20.md` | 20 août | réexécution des contrôles automatiques | **largement remplacé** par l'audit du 21 |
| `AUDIT-SELLER-RESTE.md` | 19 août | ce qu'il restait à faire côté vendeur | partiellement traité — voir `DECISIONS.md`, sept décisions le corrigent |
| `AUDIT-SELLER.md` | 18 août | service vendeur | remplacé par `SERVICES_AUDIT.md` |
| `AUDIT-CATALOG.md` | 18 août | service catalogue | remplacé par `SERVICES_AUDIT.md` |
| `AUDIT-CONFORMITE.md` | 17 août | conformité des 16 services d'alors | dépassé — le dépôt en compte 31 |
| `AUDIT-VENDEUR-100.md` | 17 août | complétude du parcours vendeur | remplacé par `SAGA_SELLER.md` |
| `AUDIT-APP-VENDEUR.md` | 16 août | application vendeur | périmètre corrigé depuis par `PHASE3-OFFRES.md` |
| `AUDIT-SAGAS-3.md` | 16 août | sagas, troisième passe | remplacé par les cinq `SAGA_*.md` |
| `AUDIT-SAGAS.md` | 15 août | sagas, première passe | remplacé |

Ils sont conservés parce qu'ils portent l'historique du raisonnement — plusieurs décisions
de `DECISIONS.md` ne se comprennent qu'en les lisant. **Ils ne décrivent plus l'état du code.**

---

## Ce qui n'est pas ici

Les revues de cahier des charges et les études de faisabilité restent à la racine de `docs/` :
`FAISABILITE-CAHIER-MEMBRES.md`, `FAISABILITE-CAHIER-PANIER-COMMANDE.md`,
`REVUE-CAHIER-MEMBRES-V2.md`. Elles jugent une spécification, pas du code — ce n'est pas
le même exercice.
