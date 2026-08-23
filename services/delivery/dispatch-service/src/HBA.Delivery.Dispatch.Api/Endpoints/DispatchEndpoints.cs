using HBA.Dispatch.Application;
using HBA.Shared.Hosting.Http;
using HBA.Shared.IntegrationEvents;

namespace HBA.Dispatch.Api.Endpoints;

public static class DispatchEndpoints
{
    public static IEndpointRouteBuilder MapDispatchEndpoints(this IEndpointRouteBuilder app)
    {
        // ══════════════════════════════════════════════════════════════════════
        // CE GROUPE ÉTAIT UN `MapGroup` NU — ISSUE-028.
        //
        // La règle du dépôt est écrite dans `ApiAuthorization` : « tout nouveau
        // groupe de l'API part de `MapAdminGroup` ou `MapAuthenticatedGroup`.
        // Jamais de `MapGroup` nu. » Ce groupe l'était.
        //
        // Depuis que `Program.cs` appelle `AddHbaSecurity`, la politique de repli
        // exige au moins un compte, si bien que le groupe n'était plus tout à fait
        // ouvert. Mais « au moins un compte » n'est pas un contrôle sur
        // `manual-assign` : cette route AFFECTE UNE COURSE À UN LIVREUR. Tout
        // acheteur inscrit pouvait donc s'attribuer — ou attribuer à n'importe qui
        // — la course de n'importe qui, et le corps de la requête choisissait le
        // livreur.
        //
        // `MapOperationsGroup` (Admin / Dispatcher) est le bon niveau : affecter à
        // la main est un geste d'EXPLOITATION, pas d'administration de contenu. Ce
        // groupe exclut délibérément `Moderator` — voir son encadré.
        //
        // CE QUE ÇA NE COUVRE PAS : un dispatcheur peut toujours affecter
        // n'importe quelle course à n'importe quel livreur. C'est son métier ; il
        // n'y a pas de notion d'« appartenance » à opposer ici. La garde qui
        // manque encore est ailleurs — c'est la vérification que le livreur
        // désigné est disponible et vérifié, et elle appartient au lot 5.2 qui
        // construira driver-service.
        // ══════════════════════════════════════════════════════════════════════
        var dispatch = app.MapOperationsGroup("/api/v1/dispatch").WithTags("Dispatch");

        dispatch.MapGet("/jobs/{deliveryId:guid}", (Guid deliveryId, DispatchStore store) =>
            store.TryGetJob(deliveryId, out var job)
                ? Results.Ok(ApiEnvelope.Ok(job))
                : Results.NotFound(ApiEnvelope.Fail("DISPATCH_JOB_NOT_FOUND", "Demande de dispatch introuvable.")));

        dispatch.MapPost("/{deliveryId:guid}/retry", async (
            Guid deliveryId,
            DispatchStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var job = await store.RetryAsync(deliveryId, publisher, cancellationToken);
            return Results.Accepted($"/api/v1/dispatch/jobs/{deliveryId}", ApiEnvelope.Ok(job));
        });

        dispatch.MapPost("/{deliveryId:guid}/manual-assign", async (
            Guid deliveryId,
            ManualAssignRequest request,
            DispatchStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            // ON LIT CE QUE `AssignAsync` REND. La version d'origine ignorait
            // le résultat et rendait 200 dans tous les cas : le SECOND livreur
            // recevait « affecté » exactement comme le premier. Voir l'encadré de
            // `DispatchStore.AssignAsync`.
            var (assigned, assignment) = await store.AssignAsync(
                deliveryId, request.DriverId, "MANUAL", publisher, cancellationToken);

            return assigned
                ? Results.Ok(ApiEnvelope.Ok(assignment))
                : Results.Conflict(ApiEnvelope.Fail(
                    "DISPATCH_ALREADY_ASSIGNED",
                    "Cette course est déjà affectée à un autre livreur."));
        });

        // `/internal` RESTE UN `MapGroup` NU, ET C'EST DÉLIBÉRÉ ICI.
        //
        // Ces routes sont appelées de service à service, sans jeton d'utilisateur.
        // Leur protection est le RÉSEAU — port interne, pas de route de passerelle
        // vers ce préfixe — comme pour `MapInternalGrpcService`. Poser une
        // politique d'utilisateur dessus casserait l'appel amont sans rien fermer.
        //
        // CE QUI N'EST DONC PAS COUVERT : quiconque atteint le port interne
        // atteint ces routes. C'est le modèle de tout le dépôt pour `/internal`,
        // pas une exception prise ici — mais il faut le savoir en le lisant.
        var internalApi = app.MapGroup("/internal/v1/dispatch").WithTags("Dispatch - Internal");

        internalApi.MapPost("/request", async (
            RequestDispatchRequest request,
            DispatchStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var job = await store.RequestAsync(request, publisher, cancellationToken);
            return Results.Accepted($"/api/v1/dispatch/jobs/{request.DeliveryId}", ApiEnvelope.Ok(job));
        });

        internalApi.MapPost("/{deliveryId:guid}/cancel", (Guid deliveryId, DispatchStore store) =>
        {
            store.Cancel(deliveryId);
            return Results.NoContent();
        });

        internalApi.MapGet("/{deliveryId:guid}/assignment", (Guid deliveryId, DispatchStore store) =>
            store.TryGetAssignment(deliveryId, out var assignment)
                ? Results.Ok(ApiEnvelope.Ok(assignment))
                : Results.NotFound(ApiEnvelope.Fail("ASSIGNMENT_NOT_FOUND", "Affectation introuvable.")));

        return app;
    }
}
