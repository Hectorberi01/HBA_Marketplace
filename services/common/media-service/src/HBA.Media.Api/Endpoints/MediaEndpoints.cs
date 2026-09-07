using System.Security.Claims;
using HBA.Media.Application.Assets;
using HBA.Media.Contracts;
using HBA.Media.Domain.Assets;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Media.Api.Endpoints;

/// <summary>API DU SERVICE MÉDIA (cahier des charges §14).</summary>
public static class MediaEndpoints
{
    // Durées de signature, en secondes.
    private const int DureeSignatureParDefaut = 300;
    private const int DureeSignatureMin = 30;
    private const int DureeSignatureMax = 900;

    private static readonly Error Introuvable =
        Error.NotFound("media.not_found", "Média introuvable.");

    // Le refus se présente comme une absence — voir le commentaire de
    // DownloadUrlAsync.
    private static readonly Error Interdit = Introuvable;

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        // `/api/v1/media`, ET LA PASSERELLE GARDE UNE COQUILLE POUR `/api/media`.
        var media = app.MapAuthenticatedGroup("/api/v1/media").WithTags("Media");

        // PAS DE `RequireIdempotency()` ICI, ET C'EST DÉLIBÉRÉ.
        media.MapPost("/", UploadAsync).WithName("UploadMedia").DisableAntiforgery();
        media.MapGet("/{id:guid}", GetAsync).WithName("GetMedia");
        media.MapGet("/{id:guid}/download-url", DownloadUrlAsync).WithName("GetMediaDownloadUrl");
        media.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteMedia");
        media.MapPost("/{id:guid}/reprocess", ReprocessAsync).WithName("ReprocessMedia");

        return app;
    }

    /// <summary>Upload via l'API (§7 mode A).</summary>
    private static async Task<IResult> UploadAsync(
        IFormFile? file,
        string ownerType,
        Guid ownerId,
        string mediaType,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken ct)
    {
        if (UserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (!Enum.TryParse<MediaOwnerType>(ownerType, ignoreCase: true, out var proprietaire))
        {
            return Results.BadRequest(new
            {
                error = "invalid_owner_type",
                value = ownerType,
                expected = Enum.GetNames<MediaOwnerType>()
            });
        }

        if (!Enum.TryParse<MediaType>(mediaType, ignoreCase: true, out var nature))
        {
            return Results.BadRequest(new
            {
                error = "invalid_media_type",
                value = mediaType,
                expected = Enum.GetNames<MediaType>()
            });
        }

        // LE CONTRÔLE DES OCTETS, AVANT TOUT LE RESTE.
        var controle = await UploadValidation.CheckDocumentAsync(file, ct);
        if (controle.Error is { } refus)
        {
            return refus;
        }

        using var flux = new MemoryStream();
        await file!.CopyToAsync(flux, ct);

        var result = await sender.Send(
            new UploadMediaCommand(
                proprietaire,
                ownerId,
                nature,
                file.FileName,

                // LE TYPE RÉEL, jamais `file.ContentType`.
                controle.ContentType!,
                flux.ToArray(),
                userId),
            ct);

        return result.Match(depot => Results.Ok(new { mediaId = depot.MediaId, url = depot.Url }));
    }

    private static async Task<IResult> GetAsync(Guid id, IMediaModuleApi media, CancellationToken ct)
    {
        var vue = await media.GetAsync(id, ct);
        return vue is null ? Results.NotFound() : Results.Ok(vue);
    }

    /// <summary>URL signée de courte durée (§10).</summary>
    private static async Task<IResult> DownloadUrlAsync(
        Guid id,
        int? expiresIn,
        ClaimsPrincipal user,
        ISender sender,
        IMediaModuleApi media,
        CancellationToken ct)
    {
        if (UserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var acces = await sender.Send(new GetMediaAccessQuery(id), ct);
        if (acces.IsFailure)
        {
            return acces.Match(_ => Results.NoContent());
        }

        if (!PeutAcceder(acces.Value, userId, user))
        {
            // 404 et non 403 : l'identifiant désigne une RESSOURCE, pas un vendeur
            // (règle §29).
            return Result.Failure(Interdit).Match(() => Results.NoContent());
        }

        // LA DURÉE EST BORNÉE PAR LE SERVEUR, PAS PAR L'APPELANT.
        var duree = Math.Clamp(expiresIn ?? DureeSignatureParDefaut, DureeSignatureMin, DureeSignatureMax);

        var url = await media.CreateSignedUrlAsync(id, duree, ct);
        return url is null ? Results.NotFound() : Results.Ok(url);
    }

    /// <summary>CE CONTRÔLE EST ÉTROIT, ET C'EST ASSUMÉ.</summary>
    private static bool PeutAcceder(MediaAccess acces, Guid userId, ClaimsPrincipal user)
    {
        if (acces.IsDeleted)
        {
            return false;
        }

        return acces.IsPublic
            || acces.CreatedByUserId == userId
            || user.IsInRole(ApiAuthorization.AdminRole);
    }

    /// <summary>SUPPRESSION RÉSERVÉE AU DÉPOSANT ET À L'ADMINISTRATEUR.</summary>
    private static async Task<IResult> DeleteAsync(
        Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (UserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var acces = await sender.Send(new GetMediaAccessQuery(id), ct);
        if (acces.IsFailure)
        {
            return acces.Match(_ => Results.NoContent());
        }

        if (acces.Value.CreatedByUserId != userId && !user.IsInRole(ApiAuthorization.AdminRole))
        {
            return Result.Failure(Interdit).Match(() => Results.NoContent());
        }

        return (await sender.Send(new DeleteMediaCommand(id), ct)).Match(() => Results.NoContent());
    }

    private static async Task<IResult> ReprocessAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ReprocessMediaCommand(id), ct)).Match(() => Results.NoContent());

    private static Guid? UserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
