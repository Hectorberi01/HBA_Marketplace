# §5 — l'ordre, réécrit sur l'état réel

Le §5 du document `shared/` proposait cinq étapes. Quatre sont faites. La
première ne l'est pas, et c'est la seule qui compte encore.

---

## Ce que §5 disait, et où ça en est

| # | Étape proposée | État |
|---|---|---|
| 1 | **Compiler ce qui est déjà là** | **NON FAITE** — 29 commits non poussés, aucune compilation depuis |
| 2 | `Idempotency/` dans chaque service | faite — `2803f4b`, 25 dossiers `Idempotency/` |
| 3 | Les 14 contrats chez leur propriétaire | faite — `6b04597` (12 projets) ; il reste `HBA.Pricing.Contracts` (§4) |
| 4 | Les trois fichiers HTTP mal rangés | **faite, et elle ne déplaçait rien** — voir plus bas |
| 5 | Le tri du mort | faite pour `HBA.Shipping.Contracts` (`ae08e56`) ; les trois serveurs gRPC sans appelant restent à trancher |

`shared/contracts/` ne contient plus qu'un projet et un README.

---

## Le §1.3 s'était trompé deux fois sur trois

Vérifié par les **appelants**, pas par le nom du type :

| Fichier | Verdict §1.3 | Réel |
|---|---|---|
| `StepUpAuthentication` | descend chez identity | **reste** — 5 projets appellent `HasRecentAuthentication()` |
| `IdempotencyEndpointFilter` | descend avec le lot 1.2 | **reste** — 7 projets annotent `AllowIdempotency()` |
| `UploadValidation` | descend chez media | **reste** — deux services reçoivent un `IFormFile` |
| `FileSignature` | à déplacer | **déjà fait** (`d1022e9`) — la seule vraie anomalie |
| `AuthRateLimiter` | non listé | **reste** — `ServiceHostExtensions` enregistre la politique pour 24 hôtes |

Le lot n'a déplacé aucun fichier. Il a trouvé autre chose :
`CatalogEndpoints.ProcessProductImageAsync` passait `file.ContentType` — la
déclaration du client — au processeur d'image. Fermé par `dd0fd8f`, avec un
contrôle `televersements` qui refuse la prochaine.

---

## Le substitut de compilation, faute de SDK

Pas de `dotnet` sur ce poste. J'ai fait ce qu'un compilateur fait **en premier**
et qui ne demande pas de compiler : résoudre les références.

| Axe | Résultat |
|---|---|
| `ProjectReference` vers un fichier absent | 0 |
| Entrée de solution vers un `.csproj` absent (MSB5023) | 0 |
| `NestedProjects` orphelin | 0 |
| `PackageReference` avec `Version=` (NU1008) | 0 |
| Nom de projet en double | 0 |
| `using HBA.*` sans espace de noms déclaré | 0 |
| `<Protobuf>` sans `Access="Internal"` | 0 sur 67 déclarations |
| **Projet du dépôt absent de `HBA.sln`** | **3 — corrigés (`016d496`)** |

Les trois manquants — `HBA.Drivers.Contracts`, `HBA.Routes.Contracts`,
`HBA.DeliveryPricing.Contracts` — se construisaient quand même (MSBuild suit les
`ProjectReference`), mais échappaient à tout outillage qui énumère la solution.
`SolutionControle` point 5 les refusait déjà : le contrôle existait, il n'avait
pas été passé.

**CE QUE CE PASSAGE NE REMPLACE PAS.** Aucune erreur de type, d'accessibilité ou
de surcharge ne peut sortir d'ici. Les sept lots empilés n'ont toujours pas vu de
compilateur, et c'est le seul risque non mesuré du dépôt.

---

## L'ordre, maintenant

1. **`dotnet build` puis `dotnet test`.** Sept lots structurels et 29 commits
   attendent ça. Chaque round d'erreurs de cette semaine était un défaut de mes
   générateurs ; il n'y a aucune raison de croire que le huitième n'en aura pas.
2. **Pousser.** Tout ceci n'existe que sur ton disque.
3. **`dotnet ef migrations add` par service** — trois entités déplacées
   (`AuditEntry`, `OutboxMessage`/`ConsumerInboxEntry`, `IdempotencyRecord`),
   diff de schéma vide, mais les instantanés les référencent par chaîne.
4. **Vérifier `consumer_inbox.consumer_name` en base**, avant toute remise à zéro
   d'offsets.
5. **La rotation des 24 secrets de production circulés en clair.** Toujours en
   attente, et c'est le seul point de cette liste qui se dégrade avec le temps.

---

## Les arbitrages qui t'attendent, pas moi

- **Trois serveurs gRPC sans aucun appelant** : `user.proto`, `route.proto`,
  `driver.proto` sont compilés par un seul projet chacun — leur propre serveur.
  Vérifié indépendamment sur les 67 déclarations `<Protobuf>`. Supprimer serveur
  + `MapInternalGrpcService` + compilation `Server`, ou garder en sachant que
  c'est une surface exposée que personne n'appelle.
- **`HBA.Pricing.Contracts`** — trois consommateurs, aucun propriétaire évident.
  `HBA.Pricing.Promotion` est parti chez promotion-service ; celui-ci ne l'a pas
  suivi.
- **`PlatformPricing` et `BeninGeography`** — de la donnée de référence. Ni
  format, ni port, ni règle. Elles appartiennent probablement à un service qui
  les EXPOSE, pas à une bibliothèque qui les recopie.
- **`Migrations/` sous `Persistence/`** — touche `dotnet ef --output-dir`, pas
  seulement des fichiers. Et 5 services n'ont pas de dossier `Migrations/`.
- **Kafka** : le nommage canonique reste gelé tant que la fenêtre de double
  lecture n'est pas écrite ; les `.dlq` n'ont ni consommateur ni alerte.
