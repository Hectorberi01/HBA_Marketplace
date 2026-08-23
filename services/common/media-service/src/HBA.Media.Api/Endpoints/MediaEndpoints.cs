using System.Security.Claims;
using HBA.Media.Application.Assets;
using HBA.Media.Contracts;
using HBA.Media.Domain.Assets;
using HBA.Shared.Domain.Results;
using HBA.Shared.Hosting.Http;
using MediatR;

namespace HBA.Media.Api.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// API DU SERVICE MÉDIA (cahier des charges §14).
///
/// LE TYPE MIME TRANSMIS AU MODULE EST CELUI DES OCTETS, PAS CELUI DU CLIENT.
///
/// C'est le point le plus important de ce fichier, et il n'est pas nouveau : ce
/// dépôt a déjà corrigé le défaut ailleurs. <c>IFormFile.ContentType</c> vient de
/// l'en-tête multipart, il est écrit par l'appelant, et
/// « curl --form 'file=@payload.bin;type=image/png' » suffit à le forger.
///
/// <c>UploadValidation</c> lit les MAGIC BYTES et rend le type réel. C'est celui-là
/// qui part vers <c>UploadMediaCommand</c> — le domaine valide donc ce que le
/// fichier EST, pas ce qu'il prétend être. Le cahier le demande d'ailleurs
/// nommément (§8 : « signature fichier — vérifier magic bytes »).
///
/// AUCUNE ROUTE ANONYME ICI, CONTRAIREMENT À LA VITRINE FOOD.
///
/// Téléverser coûte du stockage et de la bande passante. Une route d'upload
/// ouverte, c'est un disque rempli par un inconnu en une nuit — et la facture
/// arrive avant l'alerte.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class MediaEndpoints
{
    // Durées de signature, en secondes. Le défaut reste celui d'avant (5 min) ;
    // le plafond est ce qui manquait.
    private const int DureeSignatureParDefaut = 300;
    private const int DureeSignatureMin = 30;
    private const int DureeSignatureMax = 900;

    private static readonly Error Introuvable =
        Error.NotFound("media.not_found", "Média introuvable.");

    // Le refus se présente comme une absence — voir le commentaire de DownloadUrlAsync.
    private static readonly Error Interdit = Introuvable;

    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        // `/api/v1/media`, ET LA PASSERELLE GARDE UNE COQUILLE POUR `/api/media`.
        //
        // Le service ne connaît plus que le chemin versionné (D3). Les clients déjà
        // installés continuent d'appeler l'ancien : c'est la passerelle qui le
        // réécrit, pas ce fichier. Router les deux ICI aurait été plus simple et
        // aurait rendu la dépréciation impossible à mesurer — deux chemins servis
        // par le même hôte ne se distinguent dans aucune télémétrie.
        var media = app.MapAuthenticatedGroup("/api/v1/media").WithTags("Media");

        // PAS DE `RequireIdempotency()` ICI, ET C'EST DÉLIBÉRÉ.
        //
        // `UploadMediaCommand` déduplique déjà sur le SHA-256 du CONTENU : un
        // mobile qui réessaie un upload interrompu retombe sur le média existant
        // et reçoit son identifiant. C'est une garantie plus forte qu'un en-tête
        // `Idempotency-Key`, puisqu'elle tient même quand le client oublie de
        // l'envoyer — et le filtre exigerait en plus une table `idempotency_keys`
        // dans le schéma media, donc une migration, pour ne rien ajouter.
        media.MapPost("/", UploadAsync).WithName("UploadMedia").DisableAntiforgery();
        media.MapGet("/{id:guid}", GetAsync).WithName("GetMedia");
        media.MapGet("/{id:guid}/download-url", DownloadUrlAsync).WithName("GetMediaDownloadUrl");
        media.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteMedia");
        media.MapPost("/{id:guid}/reprocess", ReprocessAsync).WithName("ReprocessMedia");

        return app;
    }

    /// <summary>
    /// Upload via l'API (§7 mode A).
    ///
    /// LE PROPRIÉTAIRE EST DÉCLARÉ PAR L'APPELANT, ET CE N'EST PAS VÉRIFIÉ ICI.
    ///
    /// Media ignore ce qu'est un produit, un restaurant ou un vendeur : le §20 le
    /// pose explicitement. Un client qui déclarerait « OwnerType=Product,
    /// OwnerId=celui d'un concurrent » rattacherait donc son fichier à autrui.
    ///
    /// C'est pourquoi cette route est destinée aux BFF et aux services, PAS aux
    /// applications clientes. Le raccordement de Catalog, Sellers et Food fera
    /// passer chaque upload par la route métier correspondante, qui connaît le
    /// propriétaire et vérifie qu'il appartient à l'appelant. Tant que ce n'est
    /// pas fait, cette route reste réservée aux administrateurs — voir l'audit.
    /// </summary>
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
        //
        // On demande la validation la plus large — images ET documents — parce que
        // la restriction fine appartient à `MediaTypePolicy` : c'est elle qui sait
        // qu'une facture est un PDF et un avatar une image. Deux listes blanches à
        // deux endroits divergeraient au premier ajout de format.
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

                // LE TYPE RÉEL, jamais `file.ContentType`. Continuer à passer le
                // type déclaré après avoir vérifié les octets, ce serait vérifier
                // une identité puis recopier le faux nom.
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

    /// <summary>
    /// URL signée de courte durée (§10).
    ///
    /// CETTE ROUTE NE VÉRIFIE PAS LE DROIT MÉTIER, et c'est sa limite connue.
    ///
    /// Le §20 décrit la chaîne complète : vérifier l'identité, puis le couple
    /// (OwnerType, OwnerId), puis la permission métier. Les deux premiers points
    /// sont ici ; le troisième appartient au service propriétaire, que Media ne
    /// connaît pas.
    ///
    /// Tant que les routes métier ne sont pas raccordées, celle-ci reste donc un
    /// accès trop large — c'est noté dans l'audit, et c'est la raison pour laquelle
    /// la migration de Sellers (pièces KYB) doit venir tôt.
    /// </summary>
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
            // (règle §29). Répondre 403 confirmerait à un inconnu que ce média
            // existe — sur une pièce d'identité, c'est déjà une fuite.
            return Result.Failure(Interdit).Match(() => Results.NoContent());
        }

        // LA DURÉE EST BORNÉE PAR LE SERVEUR, PAS PAR L'APPELANT.
        //
        // `expiresIn` arrivait tel quel du client. Rien n'empêchait de demander
        // une URL signée valable un an sur une pièce KYB — l'URL, elle, circule
        // ensuite dans un historique de navigateur, un en-tête `Referer`, un
        // journal de mandataire. Une durée courte est ce qui rend une URL signée
        // acceptable ; sans plafond, la signature ne protège plus rien.
        var duree = Math.Clamp(expiresIn ?? DureeSignatureParDefaut, DureeSignatureMin, DureeSignatureMax);

        var url = await media.CreateSignedUrlAsync(id, duree, ct);
        return url is null ? Results.NotFound() : Results.Ok(url);
    }

    /// <summary>
    /// CE CONTRÔLE EST ÉTROIT, ET C'EST ASSUMÉ.
    ///
    /// Media ignore ce qu'est un vendeur ou un produit : il ne peut pas répondre
    /// à « cet utilisateur a-t-il le droit sur CE dossier ». Il sait deux choses,
    /// et elles suffisent à refermer l'accès général : qui a déposé le fichier,
    /// et si le fichier est public.
    ///
    /// Un média PUBLIC (photo de produit, vitrine de boutique) se signe pour tout
    /// compte authentifié : il est déjà lisible par son URL publique, refuser ici
    /// n'ajouterait aucune protection et casserait les vitrines.
    ///
    /// Un média PRIVÉ — pièce KYB, preuve de livraison, document de retour — ne
    /// se signe que pour son déposant, ou pour un administrateur.
    ///
    /// Le contrôle métier complet du §20 reste à faire, côté service propriétaire,
    /// quand les uploads passeront par les routes métier.
    /// </summary>
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

    /// <summary>
    /// SUPPRESSION RÉSERVÉE AU DÉPOSANT ET À L'ADMINISTRATEUR.
    ///
    /// La route était authentifiée mais ne vérifiait rien d'autre : tout compte
    /// inscrit effaçait n'importe quel média, y compris une pièce KYB ou une
    /// preuve de livraison — des éléments de preuve, pas des illustrations.
    ///
    /// Le caractère PUBLIC d'un média ne donne aucun droit de suppression :
    /// une photo de produit est lisible par tous, elle n'appartient pas à tous.
    /// C'est la seule différence avec l'URL signée ci-dessus, et elle compte.
    /// </summary>
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
