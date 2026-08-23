using System.Security.Claims;
using HBA.Shared.Hosting.Http;
using HBA.Shared.IntegrationEvents;
using HBA.Tracking.Application;

namespace HBA.Tracking.Api.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE SUIVI EN DIRECT — ISSUE-058.
///
/// TROIS DÉFAUTS, ET ILS SE RENFORÇAIENT.
///
///   1. LE GROUPE ÉTAIT UN `MapGroup` NU. La règle du dépôt, écrite dans
///      `ApiAuthorization`, l'interdit explicitement.
///
///   2. `driverId` ÉTAIT LU DANS LE CORPS. N'importe qui publiait la position de
///      n'importe quel livreur. C'est ISSUE-017/018 — « l'identité vient du
///      jeton, jamais du corps » — refermée à la vague 1 et rouverte ici. Le
///      raisonnement complet est autour des routes `/me` de `FinancialEndpoints`.
///
///   3. `TrackingStore.AddLocationsAsync` OUVRAIT LA SESSION LUI-MÊME si elle
///      n'existait pas. Combiné au point 2, cela suffisait à devenir le livreur
///      d'une course qu'on n'avait jamais acceptée — et le client suivait alors
///      un point qui n'était pas son colis.
///
/// LE JETON DE FLUX A ÉTÉ RETIRÉ, PAS RÉPARÉ. VOILÀ POURQUOI.
///
/// `GET /deliveries/{id}/stream-token` fabriquait `trk_<guid>` et le rendait. Ce
/// jeton n'était vérifié NULLE PART, et pour une raison simple : ce service n'a
/// AUCUN point de terminaison de flux à qui le présenter. Il ne protégeait donc
/// rien — il donnait à la lecture du trajet l'APPARENCE d'être authentifiée, ce
/// qui est pire que rien : c'est ce qui fait passer une relecture.
///
/// Le réparer aurait voulu dire inventer une clé de signature, une durée, une
/// vérification et le flux qui va avec — c'est-à-dire concevoir la fonction, pas
/// corriger un défaut. Elle reviendra avec le service de flux, signée pour de
/// bon. En attendant, la position se lit par `latest`, qui est gardé.
///
/// CE QUE CE LOT NE FERME PAS.
///
/// L'ACHETEUR NE PEUT PAS SUIVRE SA COURSE. `latest` est réservé au livreur de
/// la session et à l'exploitation, faute pour ce service de savoir qui a commandé
/// — il ne connaît ni la course ni son client, seulement une session. C'est un
/// MANQUE assumé et non une fermeture : il vaut mieux une lecture trop étroite
/// qu'une position GPS en direct ouverte à tout inscrit, ce qui était l'état
/// précédent. Le chemin client passera par delivery-service, qui connaît le
/// donneur d'ordre, quand le contrat existera.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class TrackingEndpoints
{
    public static IEndpointRouteBuilder MapTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        var tracking = app.MapAuthenticatedGroup("/api/v1/tracking").WithTags("Tracking");

        tracking.MapPost("/sessions/{deliveryId:guid}/locations", async (
            Guid deliveryId,
            LocationBatchRequest request,
            ClaimsPrincipal user,
            TrackingStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            if (CurrentUserId(user) is not { } livreur)
            {
                return ApiResults.Unauthorized();
            }

            var resultat = await store.AddLocationsAsync(
                deliveryId, livreur, request.Points, publisher, cancellationToken);

            // 404 SUR « PAS AFFECTÉ » COMME SUR « PAS DE SESSION ».
            //
            // Distinguer les deux dirait à l'appelant qu'une session existe pour
            // cette course — donc qu'une livraison est en cours sous cet
            // identifiant. C'est déjà de l'information, et elle suffit à balayer
            // les courses actives de la plateforme. Même discipline que
            // `DenyUnlessOwnDriverAsync` dans payment-service.
            return resultat.Status switch
            {
                LocationBatchStatus.NoSession or LocationBatchStatus.NotAssigned =>
                    Results.NotFound(ApiEnvelope.Fail(
                        "TRACKING_SESSION_NOT_FOUND", "Session de tracking introuvable.")),

                LocationBatchStatus.SessionEnded =>
                    Results.Conflict(ApiEnvelope.Fail(
                        "TRACKING_SESSION_ENDED", "Cette course n'est plus suivie.")),

                _ => Results.Accepted(
                    $"/api/v1/tracking/deliveries/{deliveryId}/latest",
                    ApiEnvelope.Ok(new { accepted = resultat.Accepted }))
            };
        });

        tracking.MapGet("/deliveries/{deliveryId:guid}/latest", (
            Guid deliveryId, ClaimsPrincipal user, TrackingStore store) =>
        {
            if (DenyUnlessOwnSession(deliveryId, user, store) is { } refus)
            {
                return refus;
            }

            return store.TryGetLatest(deliveryId, out var snapshot)
                ? Results.Ok(ApiEnvelope.Ok(snapshot))
                : Results.NotFound(ApiEnvelope.Fail("TRACKING_SNAPSHOT_NOT_FOUND", "Position courante introuvable."));
        });

        // `/internal` RESTE UN `MapGroup` NU, ET C'EST DÉLIBÉRÉ : appels de
        // service à service, sans jeton d'utilisateur, sur un port que la
        // passerelle n'expose pas. C'EST LE PIVOT DE TOUTE LA CORRECTION :
        // `sessions/start` est le SEUL endroit qui décide de qui suit quelle
        // course, et il n'est joignable que de l'intérieur.
        //
        // Ce qui n'est donc pas couvert : quiconque atteint le port interne
        // atteint ces routes. C'est le modèle de tout le dépôt pour `/internal`,
        // et ici il porte plus de poids qu'ailleurs.
        var internalApi = app.MapGroup("/internal/v1/tracking").WithTags("Tracking - Internal");

        internalApi.MapPost("/sessions/start", async (
            StartTrackingSessionRequest request,
            TrackingStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var session = await store.StartAsync(request.DeliveryId, request.DriverId, publisher, cancellationToken);
            return Results.Created($"/api/v1/tracking/sessions/{request.DeliveryId}", ApiEnvelope.Ok(session));
        });

        internalApi.MapPost("/sessions/{deliveryId:guid}/stop", async (
            Guid deliveryId,
            TrackingStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var session = await store.StopAsync(deliveryId, publisher, cancellationToken);
            return session is null
                ? Results.NotFound(ApiEnvelope.Fail("TRACKING_SESSION_NOT_FOUND", "Session de tracking introuvable."))
                : Results.Ok(ApiEnvelope.Ok(session));
        });

        internalApi.MapGet("/deliveries/{deliveryId:guid}/latest", (Guid deliveryId, TrackingStore store) =>
            store.TryGetLatest(deliveryId, out var snapshot)
                ? Results.Ok(ApiEnvelope.Ok(snapshot))
                : Results.NotFound(ApiEnvelope.Fail("TRACKING_SNAPSHOT_NOT_FOUND", "Position courante introuvable.")));

        return app;
    }

    /// <summary>
    /// Refuse la lecture à qui n'est ni le livreur suivi, ni l'exploitation.
    /// </summary>
    /// <remarks>
    /// CE QUI FUITAIT : la POSITION GPS EN DIRECT d'un livreur, et donc le
    /// domicile du destinataire au moment de la remise, pour tout inscrit
    /// connaissant un identifiant de course — glané dans un ticket de support ou
    /// une capture d'écran. C'est mot pour mot la fuite que `MapOperationsGroup`
    /// décrit pour delivery-service.
    ///
    /// `Dispatcher` est accepté avec `Admin` : suivre une course est le métier de
    /// l'exploitation. `Moderator` ne l'est PAS — la modération arbitre des
    /// contenus, elle n'a aucune raison de suivre des livreurs à la trace. C'est
    /// la même frontière que trace `MapOperationsGroup`.
    /// </remarks>
    private static IResult? DenyUnlessOwnSession(Guid deliveryId, ClaimsPrincipal user, TrackingStore store)
    {
        if (user.IsInRole(ApiAuthorization.AdminRole) || user.IsInRole(ApiAuthorization.DispatcherRole))
        {
            return null;
        }

        if (CurrentUserId(user) is not { } utilisateur)
        {
            return ApiResults.Unauthorized();
        }

        return store.DriverOf(deliveryId) == utilisateur
            ? null
            : Results.NotFound(ApiEnvelope.Fail(
                "TRACKING_SNAPSHOT_NOT_FOUND", "Position courante introuvable."));
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
