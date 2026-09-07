using System.Security.Cryptography;
using HBA.Media.Application.Abstractions;
using HBA.Media.Domain.Assets;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Media.Application.Assets;

/// <summary>TÉLÉVERSER UN FICHIER (cahier des charges §7 mode A, §14).</summary>
/// <summary>Ce qu'un dépôt rend à son appelant.</summary>
public sealed record UploadedMedia(Guid MediaId, string? Url);

public sealed record UploadMediaCommand(
    MediaOwnerType OwnerType,
    Guid OwnerId,
    MediaType MediaType,
    string FileName,
    string ContentType,
    byte[] Content,
    Guid CreatedByUserId) : ICommand<UploadedMedia>;

/// <summary>Supprime LOGIQUEMENT (§19).</summary>
public sealed record DeleteMediaCommand(Guid MediaId) : ICommand;

/// <summary>Relance la génération des variantes (§14, <c>/reprocess</c>).</summary>
public sealed record ReprocessMediaCommand(Guid MediaId) : ICommand;

/// <summary>Efface PHYSIQUEMENT les médias dont la rétention est écoulée (§19).</summary>
public sealed record PurgeExpiredMediaCommand(int Take = 100) : ICommand<int>;

internal sealed class MediaCommandHandler
    : ICommandHandler<UploadMediaCommand, UploadedMedia>,
      ICommandHandler<DeleteMediaCommand>,
      ICommandHandler<ReprocessMediaCommand>,
      ICommandHandler<PurgeExpiredMediaCommand, int>
{
    private readonly IMediaAssetRepository _assets;
    private readonly IObjectStorage _storage;
    private readonly IImageVariantGenerator _variants;
    private readonly IMediaUnitOfWork _unitOfWork;

    public MediaCommandHandler(
        IMediaAssetRepository assets,
        IObjectStorage storage,
        IImageVariantGenerator variants,
        IMediaUnitOfWork unitOfWork)
    {
        _assets = assets;
        _storage = storage;
        _variants = variants;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<UploadedMedia>> Handle(UploadMediaCommand command, CancellationToken cancellationToken)
    {
        var politique = MediaTypePolicy.For(command.MediaType);

        // ── 1. VALIDER AVANT D'ÉCRIRE UN SEUL OCTET ─────────────────────────
        var validation = politique.Validate(command.ContentType, command.FileName, command.Content?.LongLength ?? 0);
        if (validation.IsFailure)
        {
            return Result.Failure<UploadedMedia>(validation.Error);
        }

        // ── 2. L'EMPREINTE, ET L'IDEMPOTENCE QU'ELLE OFFRE ──────────────────
        var empreinte = Convert.ToHexString(SHA256.HashData(command.Content!)).ToLowerInvariant();

        var existant = await _assets.FindByChecksumAsync(
            command.OwnerType, command.OwnerId, empreinte, cancellationToken);

        if (existant is not null && existant.Status != MediaStatus.Deleted)
        {
            return Decrire(existant);
        }

        // ── 3. DÉPOSER LES OCTETS ───────────────────────────────────────────
        var id = MediaAssetId.New();
        var bucket = _storage.BucketFor(politique.DefaultVisibility);
        var cle = MediaAsset.BuildObjectKey(command.MediaType, command.OwnerId, id, command.ContentType);

        var depot = await _storage.PutAsync(
            new ObjectToStore(bucket, cle, command.ContentType, command.Content!), cancellationToken);

        if (depot.IsFailure)
        {
            return Result.Failure<UploadedMedia>(depot.Error);
        }

        // ── 4. ENREGISTRER LA MÉTADONNÉE ────────────────────────────────────
        var media = MediaAsset.Register(
            command.OwnerType, command.OwnerId, command.MediaType, command.FileName,
            bucket, cle, command.ContentType, command.Content!.LongLength,
            empreinte, command.CreatedByUserId, id);

        if (media.IsFailure)
        {
            // L'OBJET DÉPOSÉ DEVIENT ORPHELIN. On tente de le retirer, sans faire
            // dépendre la réponse de ce nettoyage : l'appelant doit lire la vraie
            // erreur, pas celle du ménage.
            await _storage.DeleteAsync(bucket, cle, cancellationToken);
            return Result.Failure<UploadedMedia>(media.Error);
        }

        if (MediaTypePolicy.IsImage(command.ContentType)
            && _variants.ReadDimensions(command.Content!) is { } dimensions)
        {
            media.Value.SetDimensions(dimensions.Width, dimensions.Height);
        }

        await _assets.AddAsync(media.Value, cancellationToken);

        // ── 5. LES VARIANTES, DONT L'ÉCHEC NE PERD PAS LE FICHIER ───────────
        if (politique.GeneratesVariants && MediaTypePolicy.IsImage(command.ContentType))
        {
            await GenerateVariantsAsync(media.Value, command.Content!, command.ContentType, cancellationToken);
        }
        else
        {
            // Pas de variantes prévues : le fichier est prêt tel quel.
            media.Value.CompleteProcessing([]);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Decrire(media.Value);
    }

    /// <summary>L'identifiant, et l'URL SEULEMENT si le fichier est public.</summary>
    private UploadedMedia Decrire(MediaAsset media)
    {
        if (!media.IsPubliclyReadable)
        {
            return new UploadedMedia(media.Id.Value, null);
        }

        var url = _storage.GetPublicUrl(media.Bucket, media.ObjectKey);

        // Une URL publique qui ne se construit pas n'est pas une raison de perdre
        // le dépôt : le fichier est bien là, et `GetAsync` la reconstruira.
        return new UploadedMedia(media.Id.Value, url.IsSuccess ? url.Value : null);
    }

    public async Task<Result> Handle(DeleteMediaCommand command, CancellationToken cancellationToken)
    {
        var media = await _assets.GetByIdAsync(new MediaAssetId(command.MediaId), cancellationToken);
        if (media is null)
        {
            // Idempotent : supprimer ce qui n'existe plus n'est pas une erreur, et
            // un appelant qui réessaie ne doit pas boucler sur un échec.
            return Result.Success();
        }

        var result = media.SoftDelete(DateTime.UtcNow);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(ReprocessMediaCommand command, CancellationToken cancellationToken)
    {
        var media = await _assets.GetByIdAsync(new MediaAssetId(command.MediaId), cancellationToken);
        if (media is null)
        {
            return Result.Failure(Introuvable);
        }

        if (media.Status == MediaStatus.Deleted)
        {
            return Result.Failure(Error.Conflict("media.deleted", "Ce média a été supprimé."));
        }

        if (!MediaTypePolicy.For(media.MediaType).GeneratesVariants)
        {
            return Result.Failure(Error.Conflict(
                "media.no_variants", "Cette nature de média ne produit pas de variantes."));
        }

        // ON RELIT L'ORIGINAL DEPUIS LE STOCKAGE. Il n'est pas en base — c'est tout
        // le principe du service — et le retraitement doit repartir des octets
        // réels, pas d'une variante déjà dégradée.
        var original = await _storage.DownloadAsync(media.Bucket, media.ObjectKey, cancellationToken);
        if (original.IsFailure)
        {
            return Result.Failure(original.Error) ;
        }

        await GenerateVariantsAsync(media, original.Value, media.ContentType, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<int>> Handle(PurgeExpiredMediaCommand command, CancellationToken cancellationToken)
    {
        var maintenant = DateTime.UtcNow;
        var expires = await _assets.ListPurgeableAsync(maintenant, command.Take, cancellationToken);

        var efface = 0;

        foreach (var media in expires)
        {
            // LES OCTETS D'ABORD, LA LIGNE ENSUITE — l'inverse de l'upload, et pour
            // la même raison retournée : une ligne effacée avant ses objets
            // laisserait des octets que PLUS RIEN ne désigne, donc que personne ne
            // saura jamais retrouver ni facturer.
            var toutEfface = true;

            foreach (var cle in media.AllObjectKeys())
            {
                var suppression = await _storage.DeleteAsync(media.Bucket, cle, cancellationToken);
                toutEfface &= suppression.IsSuccess;
            }

            if (!toutEfface)
            {
                // On laisse la ligne : le prochain passage réessaiera.
                continue;
            }

            _assets.Remove(media);
            efface++;
        }

        if (efface > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return efface;
    }

    private static readonly Error Introuvable = Error.NotFound("media.not_found", "Média introuvable.");

    /// <summary>UN ÉCHEC DE VARIANTE NE PERD PAS LE FICHIER.</summary>
    private async Task GenerateVariantsAsync(
        MediaAsset media, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        if (media.Status == MediaStatus.Uploaded)
        {
            media.BeginProcessing();
        }

        var generees = await _variants.GenerateAsync(content, contentType, cancellationToken);

        if (generees.IsFailure)
        {
            media.FailProcessing(generees.Error.Message);
            return;
        }

        var enregistrees = new List<VariantToRecord>();

        foreach (var variante in generees.Value)
        {
            var cle = $"{media.ObjectKey[..media.ObjectKey.LastIndexOf('.')]}_{variante.Type.ToString().ToLowerInvariant()}"
                + $".{MediaTypePolicy.ExtensionFor(variante.ContentType)}";

            var depot = await _storage.PutAsync(
                new ObjectToStore(media.Bucket, cle, variante.ContentType, variante.Content), cancellationToken);

            if (depot.IsFailure)
            {
                media.FailProcessing($"dépôt de la variante {variante.Type} : {depot.Error.Message}");
                return;
            }

            enregistrees.Add(new VariantToRecord(
                variante.Type, cle, variante.ContentType, variante.Width, variante.Height, variante.Content.LongLength));
        }

        media.CompleteProcessing(enregistrees);
    }
}
