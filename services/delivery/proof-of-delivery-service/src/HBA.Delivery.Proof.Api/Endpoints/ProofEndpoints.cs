using System.Security.Claims;
using HBA.ProofOfDelivery.Application;
using HBA.Shared.Hosting.Http;
using HBA.Shared.IntegrationEvents;

namespace HBA.ProofOfDelivery.Api.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA SURFACE DE LA PREUVE DE REMISE — ISSUE-056.
///
/// CE GROUPE ÉTAIT UN `MapGroup` NU, et `CreateProofRequest` portait
/// `DriverId` DANS LE CORPS. C'est la faille ISSUE-017/018 refermée à la vague
/// 1 : L'IDENTITÉ VIENT DU JETON, JAMAIS DU CORPS. Un livreur ouvrait une preuve
/// au nom d'un autre, et c'est ce nom-là qui restait dans l'historique de la
/// course.
///
/// CE QUE CES ROUTES NE PEUVENT PAS ENCORE VÉRIFIER.
///
/// Que le livreur du jeton soit bien CELUI AFFECTÉ À LA COURSE. Ce service ne
/// connaît pas les affectations : elles vivent dans `deliveries.deliveries`, dans
/// delivery-service, et aucun contrat ne les expose ici. Le contrôle posé est
/// donc l'APPARTENANCE DE LA PREUVE (on ne soumet que la sienne), pas
/// l'affectation. Un livreur authentifié peut encore ouvrir une preuve sur une
/// course qui n'est pas la sienne — il ne peut simplement plus le faire au nom
/// de quelqu'un d'autre, ni toucher à la preuve d'autrui.
///
/// Fermer complètement demande le contrat que le lot 5.2 doit poser.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ProofEndpoints
{
    public static IEndpointRouteBuilder MapProofEndpoints(this IEndpointRouteBuilder app)
    {
        var proofs = app.MapAuthenticatedGroup("/api/v1/proofs").WithTags("Proof of Delivery");

        proofs.MapPost("/", (CreateProofRequestBody request, ClaimsPrincipal user, ProofStore store) =>
        {
            if (CurrentUserId(user) is not { } livreur)
            {
                return ApiResults.Unauthorized();
            }

            // LE LIVREUR VIENT DU JETON. Le corps ne le porte plus : c'est la
            // seule forme qui ne se contourne pas.
            var emission = store.Create(new CreateProofRequest(
                request.DeliveryId, request.StopId, request.Type, request.RecipientName, livreur));

            // ON NE RENVOIE PAS `emission.Otp`. Ce point de terminaison est
            // appelé PAR LE LIVREUR ; lui rendre le code viderait la preuve de
            // toute substance — voir l'encadré de `ProofStore.Create`. Le code
            // doit atteindre le DESTINATAIRE, et aucun canal ne le porte encore.
            return Results.Created($"/api/v1/proofs/{emission.Proof.Id}", ApiEnvelope.Ok(emission.Proof));
        });

        proofs.MapPost("/{id:guid}/media/presign", (Guid id, PresignProofMediaRequest request, ClaimsPrincipal user, ProofStore store) =>
        {
            if (DenyUnlessOwnProof(id, user, store) is { } refus)
            {
                return refus;
            }

            var media = store.Presign(id, request);
            return media is null
                ? Results.NotFound(ApiEnvelope.Fail("PROOF_NOT_FOUND", "Preuve introuvable."))
                : Results.Ok(ApiEnvelope.Ok(media));
        });

        proofs.MapPost("/{id:guid}/submit", async (
            Guid id,
            SubmitProofRequest request,
            ClaimsPrincipal user,
            ProofStore store,
            IIntegrationEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            if (DenyUnlessOwnProof(id, user, store) is { } refus)
            {
                return refus;
            }

            var resultat = await store.SubmitAsync(id, request, publisher, cancellationToken);

            // CHAQUE ISSUE A SON CODE, ET CE N'EST PAS DU CONFORT.
            //
            // « code faux », « code expiré » et « trop de tentatives » appellent
            // trois gestes DIFFÉRENTS de la part du livreur : recommencer, faire
            // renvoyer le code, appeler le support. Les confondre sous un 400
            // unique le laisse réessayer un code qui ne marchera jamais.
            return resultat.Status switch
            {
                SubmitStatus.NotFound =>
                    Results.NotFound(ApiEnvelope.Fail("PROOF_NOT_FOUND", "Preuve introuvable.")),

                SubmitStatus.AlreadySubmitted =>
                    Results.Conflict(ApiEnvelope.Fail(
                        "PROOF_ALREADY_SUBMITTED",
                        "Cette preuve a déjà été soumise et ne peut plus être modifiée.")),

                SubmitStatus.OtpInvalid =>
                    Results.Json(
                        ApiEnvelope.Fail("PROOF_OTP_INVALID", "Ce code ne correspond pas."),
                        statusCode: StatusCodes.Status400BadRequest),

                SubmitStatus.OtpExpired =>
                    Results.Json(
                        ApiEnvelope.Fail("PROOF_OTP_EXPIRED", "Ce code a expiré. Faites-en renvoyer un."),
                        statusCode: StatusCodes.Status400BadRequest),

                SubmitStatus.OtpAlreadyUsed =>
                    Results.Conflict(ApiEnvelope.Fail(
                        "PROOF_OTP_ALREADY_USED", "Ce code a déjà servi.")),

                // 423 « Locked » : la ressource est bloquée et le restera tant
                // qu'un humain n'interviendra pas. Un 429 dirait « réessayez plus
                // tard », ce qui serait faux — le temps ne débloque rien ici.
                SubmitStatus.OtpLocked =>
                    Results.Json(
                        ApiEnvelope.Fail(
                            "PROOF_OTP_LOCKED",
                            "Trop de tentatives. La preuve par code est bloquée ; contactez le support."),
                        statusCode: StatusCodes.Status423Locked),

                SubmitStatus.Rejected =>
                    Results.Json(
                        ApiEnvelope.Fail(
                            "PROOF_NOT_VERIFIABLE",
                            "Ni code valide, ni média : rien ne prouve la remise."),
                        statusCode: StatusCodes.Status422UnprocessableEntity),

                _ => Results.Ok(ApiEnvelope.Ok(resultat.Proof))
            };
        });

        proofs.MapGet("/deliveries/{deliveryId:guid}", (Guid deliveryId, ProofStore store) =>
            Results.Ok(ApiEnvelope.Ok(store.ListByDelivery(deliveryId))))
            .RequireAdmin();

        // `/internal` RESTE UN `MapGroup` NU, ET C'EST DÉLIBÉRÉ : ces routes
        // sont appelées de service à service, sans jeton d'utilisateur. Leur
        // protection est le RÉSEAU — port interne, aucune route de passerelle
        // vers ce préfixe. Ce qui n'est donc pas couvert : quiconque atteint le
        // port interne atteint ces routes. C'est le modèle de tout le dépôt.
        var internalApi = app.MapGroup("/internal/v1/proofs").WithTags("Proof of Delivery - Internal");

        internalApi.MapGet("/deliveries/{deliveryId:guid}/dropoff-valid", (Guid deliveryId, ProofStore store) =>
            Results.Ok(ApiEnvelope.Ok(new { deliveryId, valid = store.HasValidDropoffProof(deliveryId) })));

        internalApi.MapGet("/deliveries/{deliveryId:guid}/summary", (Guid deliveryId, ProofStore store) =>
            Results.Ok(ApiEnvelope.Ok(store.ListByDelivery(deliveryId))));

        return app;
    }

    /// <summary>
    /// Refuse si la preuve n'appartient pas au livreur du jeton.
    /// </summary>
    /// <remarks>
    /// 404 ET NON 403 SUR L'APPARTENANCE.
    ///
    /// Un 403 confirmerait qu'une preuve porte cet identifiant, ce qui suffit à
    /// énumérer les remises de la plateforme. « Introuvable » ne dit rien —
    /// c'est la discipline de `DenyUnlessOwnerAsync` dans inventory-service.
    ///
    /// CE QUE CETTE GARDE NE VÉRIFIE PAS : que le livreur soit AFFECTÉ à la
    /// course. Ce service ne connaît pas les affectations. Voir l'encadré du
    /// fichier.
    /// </remarks>
    private static IResult? DenyUnlessOwnProof(Guid proofId, ClaimsPrincipal user, ProofStore store)
    {
        if (user.IsInRole("Admin"))
        {
            return null;
        }

        if (CurrentUserId(user) is not { } livreur)
        {
            return ApiResults.Unauthorized();
        }

        return store.OwnerOf(proofId) == livreur
            ? null
            : Results.NotFound(ApiEnvelope.Fail("PROOF_NOT_FOUND", "Preuve introuvable."));
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// Le corps de la création — SANS `DriverId`.
    ///
    /// IL Y ÉTAIT, ET C'ÉTAIT LA FAILLE. `CreateProofRequest` (le type de la
    /// couche application) le porte encore, parce que le store doit bien le
    /// stocker ; ce qui a changé, c'est qu'il n'est plus RENSEIGNÉ par l'appelant
    /// mais par le jeton, au seul endroit où la requête entre.
    /// </summary>
    public sealed record CreateProofRequestBody(Guid DeliveryId, Guid? StopId, string Type, string? RecipientName);
}
