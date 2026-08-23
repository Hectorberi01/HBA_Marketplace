# Socle transverse — mise en conformité §5, §18 et §19

17 août 2026 · fait suite à `AUDIT-CONFORMITE.md`

Ce document décrit ce qui a été ajouté dans `shared/`, comment le brancher dans un
service, et ce qui reste à faire. **Rien n'a été compilé** : ni la machine ni le
conteneur d'exécution n'ont accès au SDK .NET ou à NuGet. Lancer `make build` avant
de brancher quoi que ce soit.

## Ce qui a été ajouté

Tout est **additif** : aucun `.csproj` ni `HBA.sln` n'a été modifié, donc le graphe de
dépendances est inchangé. Deux fichiers existants seulement ont été touchés.

| Fichier | Projet | Rôle |
|---|---|---|
| `Results/ErrorCodes.cs` | Domain | Les 5 codes normalisés du §5 + `ServiceCodes` pour les `*_SERVICE_NOT_FOUND` |
| `Results/Error.cs` *(modifié)* | Domain | Ajout de `ErrorType.BusinessRule` (422) et `DependencyUnavailable` (503) |
| `Context/HbaRequestContext.cs` | Application | Contexte propagé du §18 : requestId, correlationId, traceId, actor, idempotencyKey, locale, tenantId |
| `Kafka/HbaEventEnvelope.cs` | Infrastructure | Enveloppe canonique du §19.1, champ pour champ |
| `Kafka/HbaEventAttribute.cs` | Infrastructure | Déclare `domaine.agrégat.action` + version sur un événement |
| `Kafka/HbaEventNaming.cs` | Infrastructure | Topic `hba.<env>.<domaine>.<agrégat>.v<major>`, DLQ, schéma (§19.2) |
| `Inbox/*` | Infrastructure | Table `consumer_inbox` du §19.5 + contrat + implémentation EF |
| `Idempotency/*` | Infrastructure | Table `idempotency_keys` du §5 + contrat + implémentation EF |
| `Http/ApiEnvelope.cs` | Hosting | Enveloppes succès/erreur/pagination du §5 |
| `Http/ApiResults.cs` *(modifié)* | Hosting | Rend l'enveloppe au lieu de RFC 7807 ; mapping status du §5 |
| `Http/RequestContextMiddleware.cs` | Hosting | Remplit le contexte depuis les en-têtes, le renvoie |
| `Http/IdempotencyEndpointFilter.cs` | Hosting | Applique `Idempotency-Key` sur un endpoint ou un groupe |

## Les deux ruptures de contrat à assumer

### 1. La forme des réponses d'erreur change partout

`ApiResults.Match(...)` rendait du RFC 7807 (`title`, `detail`, `status`). Il rend
maintenant l'enveloppe du §5 (`success`, `error.code`, `error.message`, `meta.requestId`).
Les deux formes n'ont **aucun champ en commun**.

Tout client web ou mobile lisant `detail` ou `title` casse. Ils doivent être livrés
**avec** cette version, pas après.

### 2. Le succès n'est enveloppé que sur les endpoints migrés

`Results.Ok(dto)` continue de compiler et de rendre la ressource nue. Pour envelopper,
il faut passer à `ApiResults.Ok(dto)`. C'est volontaire — sinon la compilation cassait
d'un coup sur des centaines d'endpoints — mais cela crée un état transitoire où un
endpoint non migré rend l'ancienne forme en succès et la nouvelle en erreur.

**C'est le pire des deux mondes et il ne doit pas durer.** À suivre explicitement.

## Brancher un service

### 1. Le contexte propagé

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseHbaRequestContext("food-order-service");   // APRÈS l'authentification
```

Placé avant `UseAuthentication`, tout se remplit sauf l'acteur — et une absence
d'acteur ne lève aucune erreur, elle se voit des semaines plus tard dans un journal
d'audit vide.

### 2. Les tables `consumer_inbox` et `idempotency_keys`

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());
    modelBuilder.ApplyConfiguration(new IdempotencyConfiguration());
}
```

```csharp
services.AddScoped<IConsumerInbox, EfConsumerInbox<FoodOrderDbContext>>();
services.AddScoped<IIdempotencyStore, EfIdempotencyStore<FoodOrderDbContext>>();
```

Puis `dotnet ef migrations add AddInboxAndIdempotency` dans le service.

### 3. L'idempotence sur les endpoints du §5

```csharp
group.MapPost("/checkout", CheckoutAsync).RequireIdempotency();   // obligatoire
group.MapPatch("/{id}", UpdateAsync).AllowIdempotency();          // honorée si fournie
```

`RequireIdempotency` rend 400 `VALIDATION_ERROR` sans en-tête. Sans
`IIdempotencyStore` enregistré, le filtre **laisse passer** en journalisant une erreur :
un filtre mal câblé ne doit pas rendre l'endpoint inutilisable, mais l'absence de
protection ne doit pas passer inaperçue non plus.

### 4. Les codes d'erreur

```csharp
return Result.Failure<FoodOrderDto>(
    Error.NotFound(ErrorCodes.NotFound(ServiceCodes.FoodOrder), "Commande introuvable."));

return Result.Failure(
    Error.BusinessRule(ErrorCodes.BusinessRuleViolation, "Le restaurant est fermé."));
```

### 5. Un événement conforme au §19

```csharp
[HbaEvent("food", "order", "accepted", Version = 1, AggregateType = "FoodOrder")]
public sealed record FoodOrderAcceptedIntegrationEvent : IntegrationEvent { ... }
```

L'attribut remplace la dérivation depuis le nom de classe. Sans lui, l'événement
reste publié à l'ancienne — la bascule est donc **événement par événement**, et
l'attribut est la preuve qu'elle a eu lieu.

## Ce qui reste à faire

1. **Le publisher et le consumer Kafka ne lisent pas encore `[HbaEvent]`.**
   `HbaEventNaming` et `HbaEventEnvelope` existent, mais `KafkaIntegrationEventPublisher`
   et `KafkaIntegrationEventConsumer` continuent d'utiliser `KafkaEventNaming`. C'est la
   prochaine étape, et la plus délicate : elle change les topics de production.
2. **Aucun événement ne porte encore `[HbaEvent]`.** 90 lignes d'événements à annoter,
   domaine par domaine.
3. **Aucun endpoint n'est passé en `/api/v1/<domaine>`.** 61 endpoints (cause racine 1
   de l'audit), mécanique mais à faire d'un bloc par service.
4. **`ApiResults.Ok` n'est utilisé nulle part** : la migration des succès reste entière.
5. **Le mapping `IIdempotencyStore` n'est enregistré dans aucun service.**
6. **`consumer_inbox` n'est utilisée par aucun consumer** : le contrat existe, la
   séquence du §19.5 (vérifier → traiter → tracer → committer → ACK) reste à câbler
   dans `KafkaIntegrationEventConsumer`.
