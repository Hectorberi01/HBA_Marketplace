# Audit des pages restantes — console d'administration

**23 août 2026.** État : **18 sections prêtes sur 28**. Ce document couvre les **dix qui
restent**, et ce que la vérification a trouvé au passage.

Méthode : chaque affirmation du panneau a été **confrontée au code d'aujourd'hui**, et non
reprise. Les textes du panneau datent du 22 août ; le dépôt a bougé depuis, et trois
affirmations sur dix se sont révélées fausses ou périmées.

---

## Résumé

| Section | Verdict du panneau | Verdict après vérification |
|---|---|---|
| Factures | à écrire | ⛔ **bloquée à la passerelle**, pas au service |
| Commissions | à écrire | ⛔ **bloquée à la passerelle**, + une garde manquante |
| Marketing | sans amont | ✅ exact |
| Taxes | sans amont | ✅ exact — mais **deux commentaires nomment un module Tax inexistant** |
| Bannières | sans amont | ✅ exact |
| Notifications | sans amont | ✅ exact |
| Fraude | sans amont | ✅ exact — la justification, elle, est fausse |
| Outbox | sans amont | ✅ exact |
| Analytics | sans amont | **trop absolu** : des agrégats existent, la série temporelle non |
| Monitoring | sans amont | 🔴 **le verdict change** : la pile tourne, **seule la passerelle l'alimente** |

Et trois surfaces d'administration **n'apparaissent nulle part dans le panneau** — voir la
dernière section. L'une d'elles **ment** : elle répond 200 et ne persiste rien.

---

## 1. Monitoring — la découverte de cet audit

Le panneau dit : « Prometheus et Grafana tournent déjà. Les recopier dans cette application
n'apporterait rien : un lien vers Grafana est la bonne réponse, pas un écran. »

La première phrase est vraie. La conclusion ne l'est plus, parce qu'**il n'y a presque rien
dans Grafana**.

### Ce que la chaîne fait réellement

`ServiceHostExtensions` appelle `AddHbaTelemetry` pour **tous** les services, avec un
encadré qui explique le choix :

> « Un branchement à faire quatorze fois est un branchement qu'on oublie une fois — et le
> service oublié est muet sans que rien ne le signale. Il démarre, il sert, il passe ses
> tests. On ne s'en aperçoit qu'en cherchant ses traces pendant un incident. »

L'appel est bien centralisé. **L'adresse d'export ne l'est pas.**
`TelemetryExtensions` fait :

```csharp
var hasEndpoint = Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint);
...
if (hasEndpoint) { tracing.AddOtlpExporter(...); }
if (hasEndpoint) { metrics.AddOtlpExporter(...); }
```

Sans endpoint, **rien n'est exporté** — silencieusement, exactement le défaut que l'encadré
prétend avoir écarté.

Or `OPENTELEMETRY__ENDPOINT` n'est posé que dans **`infra/docker/env/gateway.env`**.
`infra/docker/env/` contient **quatorze fichiers `.env`** ; un seul porte la variable. Et
**aucun service n'a de `appsettings.json`** où la déclarer — le fichier n'existe tout
simplement pas dans leurs dossiers.

**Conclusion : seule la passerelle exporte traces, métriques et journaux.** Les treize
autres services décrits par ces fichiers d'environnement sont muets.

### Le commentaire qui le contredit

`infra/observability/prometheus/prometheus.yml` :

> « Le point d'entrée Prometheus du collecteur OTLP : **les métriques des quinze processus y
> arrivent**, chacune portant l'étiquette `service_name` issue de la ressource
> OpenTelemetry. »

Elles n'y arrivent pas. Une seule y arrive.

### Ce que ça change pour la console

Le verdict « un lien vers Grafana suffit » repose sur l'idée que Grafana montre la
plateforme. Il montre la passerelle. **Le lot utile n'est pas un écran, c'est une ligne
d'environnement par service** — ou, mieux, un `env_file` commun ajouté aux dix-huit entrées
de `compose.services.yml`, pour que la prochaine addition ne l'oublie pas à son tour.

Tant que ce n'est pas fait, un incident sur order-service se diagnostique sans trace, sans
métrique et sans journal corrélé.

---

## 2. Factures et Commissions — bloquées au même mur

Les deux sont marquées « à écrire ». Le diagnostic est incomplet dans les deux cas : ce ne
sont pas les **services** qui manquent, c'est la **route de passerelle**.

`appsettings.json` de la passerelle ne porte que trois préfixes `financial` :
`settlements`, `wallets`, `payments`. Ni `/api/financial/invoices`, ni
`/api/financial/commissions`. La console ne parle qu'à la passerelle : vue d'ici, les deux
répondent 404.

**Et percer le mur ne suffit pas.**

- **Commissions** : `commissions.MapGet("/", ListCommissionRulesAsync)` n'a **pas** de
  `.RequireAdmin()`, contrairement aux cinq écritures voisines. La liste porte les règles de
  portée `Seller` — le **taux négocié vendeur par vendeur**. C'est exactement la donnée que
  le commentaire de `ComputeCommissionAsync` décrit comme la fuite qu'il vient de refermer :
  « tout inscrit calculait la commission d'un concurrent […] la donnée sur laquelle on décide
  de casser un prix ». La relayer telle quelle rouvrirait la fuite par une autre porte.
- **Factures** : même nature. Une liste plateforme expose le chiffre d'affaires commissionné
  vendeur par vendeur, et il n'existe aujourd'hui **aucune** requête de liste — seulement
  `GET /{id}` et `GET /seller/{id}`.

**Ordre des corrections, dans les deux cas :** la garde d'abord, la route de passerelle
ensuite. Jamais l'inverse.

Le moteur de commission, lui, est sain et mérite d'être noté : résolution
`Seller > Category > Global` par `Priority => (int)Scope`, départage par `EffectiveFromUtc`
décroissante, et **repli sur le taux par défaut plutôt que sur zéro** — un gestionnaire
antérieur recopiait le résolveur et rendait `0` quand rien ne correspondait, si bien que
l'aperçu annonçait « commission : 0 » pendant que la comptabilisation prélevait 10 %.

---

## 3. Les six « sans amont » confirmés

Vérifiés un par un contre le code d'aujourd'hui.

**Marketing** ✅ — promotion-service n'expose que deux groupes, tous deux
`MapAuthenticatedGroup` : `/api/v1/promotions` (validation d'un code) et
`/api/v1/merchant/promotions` (le vendeur gère les siennes). Aucun groupe d'administration.
Une campagne plateforme n'a ni agrégat ni route.

**Bannières** ✅ — aucun service de contenu éditorial. Le mot `Banner` n'apparaît dans aucun
fichier C# hors migrations. L'application cliente déclare le même manque sous le nom
`content` dans `not_migrated.dart`, ligne 55.

**Notifications** ✅ — les quatre groupes du service sont tous `MapAuthenticatedGroup`, et
leur surface est celle du **destinataire** : lire ses notifications, marquer lu, gérer ses
préférences, enregistrer un appareil, et la messagerie de ses propres conversations. Aucun
envoi de masse, aucun gabarit administrable, aucune vue d'exploitation.

**Outbox** ✅ — `outbox_messages` reste interne. Aucune route ne l'expose, et c'est
délibéré : les charges utiles portent des données personnelles.

**Fraude** ✅ sur le fond — aucun service, aucun score, aucune règle.
**La justification du panneau est fausse.** Elle dit « le mot n'apparaît que dans un
commentaire de merchant-service ». Il apparaît en réalité dans **quatre** fichiers :
`MerchantEndpoints.cs`, `Withdrawal.cs` (wallet), `ApiAuthorization.cs` et
`IdentityIntegrationEvents.cs`. Aucun ne porte de mécanisme — mais un signal existe et
mérite d'être cité : `ProcessingWithdrawalView.Anomaly` porte l'incident d'un versement en
cours, et c'est le seul indice de risque exposé par une API du dépôt. Il est déjà affiché
par l'écran Retraits.

**Taxes** ✅ sur le fond — aucun service, aucune route, aucun agrégat. `TaxRule` n'existe
nulle part.
**Mais l'affirmation « le mot n'apparaît nulle part dans les services » est fausse**, et
ce qu'elle rate est plus intéressant que le mot : `CommissionCommands.cs` contient **deux
commentaires qui nomment `UpdateTaxRuleCommand` et `UpdateTaxRuleCommandHandler`** comme des
références établies —

> « C'est le contrat qu'applique déjà le module Tax (`UpdateTaxRuleCommand`), et il n'y a
> aucune raison que deux tables de règles datées se comportent différemment. »

**Ni l'un ni l'autre n'existe.** C'est la classe de défaut que ce dépôt traque partout : un
commentaire qui décrit un mécanisme absent, et qui fait passer la relecture. Le contrôle
`check-config-and-guards.py` refuse déjà qu'un nom en `Ensure*Async`/`Deny*Async` soit cité
sans exister ; l'étendre aux `*Command`/`*CommandHandler` attraperait celui-ci.

---

## 4. Analytics — le verdict est juste, l'énoncé est trop large

Le panneau ouvre par « Aucune agrégation côté serveur », puis précise « la série temporelle
des ventes […] ni service HBA ni route BFF ne la rend ». La seconde phrase est exacte ; la
première la contredit et elle est fausse :

- `GET /api/payments/stats` rend `PaymentStatsSummary` — comptes et montants capturés,
  remboursés — et l'écran Paiements l'affiche déjà ;
- `GetAdminQueuesHandler` agrège cinq files pour le tableau de bord ;
- `ListUsersQuery`, `ListForAdminAsync` et `ListForModerationAsync` rendent chacune un
  **comptage par statut** dans `meta.facets`.

Ce qui manque est précis : **une série temporelle**. Aucune requête ne groupe par jour, par
semaine ou par mois, sur aucun agrégat. Le même manque bloque l'écran `analytics` de
l'application vendeur, qui le déclare ligne 85 de son `not_migrated.dart`.

Reformulation proposée pour le panneau : « Les agrégats ponctuels existent (stats de
paiement, files, facettes). **Aucune série temporelle** : rien ne groupe par période, sur
aucun agrégat. C'est cela qu'un écran d'analytique demanderait d'abord. »

---

## 5. Trois surfaces d'administration absentes du panneau

Le panneau a été dérivé du menu de l'ancienne console. Une surface que l'ancienne console
n'avait pas est donc **invisible**, même si elle existe. Le balayage des seize préfixes
`MapAdminGroup` du dépôt en révèle trois qu'aucune page ne consomme et qu'aucune entrée ne
mentionne.

### `/api/v1/admin/return-policies` — 🔴 elle répond, et elle ne stocke rien

C'est la trouvaille la plus sérieuse après le monitoring.

```csharp
var group = app.MapAdminGroup("/api/v1/admin/return-policies").WithTags("Return Policies");

group.MapGet("/", () => ApiResults.Ok<IReadOnlyList<ReturnPolicyDto>>([
    new("default", "2026.08.1", 14, true, true, true, true, 0m)
]));

group.MapPost("/", (UpsertReturnPolicyDto request) => ApiResults.Created(
    new ReturnPolicyDto($"{request.ScopeType}:{request.ScopeId}", "2026.08.1", ...)));
```

Deux lambdas sans la moindre dépendance injectée : pas d'`ISender`, pas de dépôt, pas de
`DbContext`. **Le GET rend une politique écrite en dur ; le POST renvoie la requête et
n'écrit rien.**

La route est relayée par la passerelle (`admin-return-policies`). Un écran construit
dessus afficherait « politique enregistrée », et la fenêtre de retour de la plateforme
resterait à quatorze jours pour tout le monde, indéfiniment.

**Ce n'est pas une page à écrire : c'est un service à écrire, ou une route à retirer.** La
laisser en l'état est le pire des trois, parce qu'elle a l'air de marcher.

### `/api/engagement/recommendations` (POST admin) — réelle, sans écran

`UpsertRecommendationAsync` passe par `ISender` et persiste. Le commentaire du service dit
l'enjeu :

> « ÉCRIRE UNE RECOMMANDATION, C'EST ÉCRIRE LA PAGE D'ACCUEIL. La route acceptait la commande
> brute dans le corps : n'importe quel inscrit choisissait les produits mis en avant sur la
> fiche d'un concurrent. »

Une surface qui décide de la mise en avant produit, sans page pour la piloter. **Il manque
une route de liste** — comme pour les avis avant ce lot — mais l'écriture, elle, fonctionne.

### `/api/v1/merchants/{sellerId}/stores` (suspend / lift-suspension) — réelle, sans écran

Suspendre une **boutique** n'est pas suspendre un **vendeur** : un vendeur peut en tenir
plusieurs. La page Vendeurs & KYB agit sur le vendeur ; rien n'agit sur la boutique. Deux
routes, aucun bouton.

---

## 6. Ce que je ferais ensuite, par rapport valeur / coût

1. **`OPENTELEMETRY__ENDPOINT` dans les treize `.env` qui ne l'ont pas.** Une ligne par
   service — ou mieux, un `env_file` commun ajouté aux entrées de `compose.services.yml`,
   pour que la prochaine addition ne l'oublie pas à son tour. C'est le meilleur rapport
   valeur/coût du lot, et de loin.
2. **Trancher `return-policies`** : l'implémenter ou la retirer. Une route qui simule est
   une dette qui se paie en incident.
3. **`.RequireAdmin()` sur `GET /api/financial/commissions`, puis les deux routes de
   passerelle** — Commissions et Factures débloquées ensemble, avec la liste de factures à
   écrire pour la seconde.
4. **Corriger les trois textes du panneau** (Fraude, Taxes, Analytics) et le commentaire de
   `prometheus.yml`. Un panneau qui décrit faux vaut moins qu'un panneau vide.
5. **Étendre `check-config-and-guards.py`** aux noms en `*Command` / `*CommandHandler` cités
   sans exister. Il aurait attrapé `UpdateTaxRuleCommand` tout seul.
6. Les deux surfaces sans écran — recommandations et suspension de boutique — quand le reste
   est fait. Elles ne bloquent personne aujourd'hui.
