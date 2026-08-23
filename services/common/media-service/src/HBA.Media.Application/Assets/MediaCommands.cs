using System.Security.Cryptography;
using HBA.Media.Application.Abstractions;
using HBA.Media.Domain.Assets;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Media.Application.Assets;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// TÉLÉVERSER UN FICHIER (cahier des charges §7 mode A, §14).
///
/// Le flux retenu fait transiter les octets PAR L'API. Le cahier préfère le
/// « presigned » pour le mobile ; ce mode-ci reste indispensable de toute façon
/// aux uploads initiés côté serveur — import, migration, génération de facture —
/// et il est le seul qui permette de valider le contenu avant qu'il n'atteigne le
/// stockage.
///
/// L'ORDRE DES ÉTAPES EST LE CŒUR DE CETTE COMMANDE.
///
///   1. valider (taille, format, cohérence extension) — AVANT tout octet écrit ;
///   2. calculer l'empreinte, et rendre le média existant si c'est un doublon ;
///   3. déposer les octets ;
///   4. enregistrer la métadonnée ;
///   5. générer les variantes, dont l'échec ne perd pas le fichier.
///
/// Inverser 3 et 4 laisserait une ligne désignant un objet inexistant, et chaque
/// lecture échouerait sur un fichier que la base jure présent.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <summary>
/// Ce qu'un dépôt rend à son appelant.
///
/// L'URL EST RENDUE ICI PARCE QU'ELLE EST DÉJÀ CONNUE.
///
/// La commande vient de calculer la clé d'objet : reconstruire l'URL ne coûte
/// rien. Ne rendre que l'identifiant obligerait chaque appelant qui affiche
/// l'image — logo de boutique, pièce jointe de discussion, photo produit — à
/// relire immédiatement le média qu'il vient d'écrire.
///
/// NULLE POUR UN FICHIER PRIVÉ, et pour la raison énoncée sur `MediaView` :
/// une pièce d'identité n'a pas d'URL permanente. L'appelant qui doit la lire
/// demande une URL signée, nommément et pour cinq minutes.
/// </summary>
public sealed record UploadedMedia(Guid MediaId, string? Url);

public sealed record UploadMediaCommand(
    MediaOwnerType OwnerType,
    Guid OwnerId,
    MediaType MediaType,
    string FileName,
    string ContentType,
    byte[] Content,
    Guid CreatedByUserId) : ICommand<UploadedMedia>;

/// <summary>
/// Supprime LOGIQUEMENT (§19). Les octets survivent le temps de la rétention
/// prévue pour cette nature de fichier — dix ans pour une facture, trente jours
/// pour une photo produit.
/// </summary>
public sealed record DeleteMediaCommand(Guid MediaId) : ICommand;

/// <summary>
/// Relance la génération des variantes (§14, <c>/reprocess</c>).
///
/// SA RAISON D'ÊTRE EST L'ÉTAT <c>Failed</c>. Sans elle, une image dont les
/// miniatures ont échoué — service de traitement indisponible dix minutes — reste
/// sans miniatures pour toujours, et personne ne relie l'affichage lourd à un
/// incident d'un mardi matin.
/// </summary>
public sealed record ReprocessMediaCommand(Guid MediaId) : ICommand;

/// <summary>
/// Efface PHYSIQUEMENT les médias dont la rétention est écoulée (§19).
///
/// Appelée par une tâche planifiée. Rend le nombre d'objets réellement effacés —
/// un zéro permanent est le signe que le ménage ne tourne plus, et c'est le genre
/// de panne qui ne se voit que sur la facture de stockage.
/// </summary>
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
        //
        // Le §8 énumère les contrôles. Les faire après le dépôt reviendrait à
        // stocker ce qu'on s'apprête à refuser — et à payer le stockage de
        // fichiers qu'on n'aurait jamais dû accepter.
        var validation = politique.Validate(command.ContentType, command.FileName, command.Content?.LongLength ?? 0);
        if (validation.IsFailure)
        {
            return Result.Failure<UploadedMedia>(validation.Error);
        }

        // ── 2. L'EMPREINTE, ET L'IDEMPOTENCE QU'ELLE OFFRE ──────────────────
        //
        // CE N'EST PAS (SEULEMENT) DE LA DÉDUPLICATION.
        //
        // Un mobile sur un réseau instable réessaie un upload interrompu ; sans ce
        // contrôle, le même fichier est stocké deux fois, la galerie affiche un
        // doublon, et le vendeur en supprime un au hasard. Rendre l'identifiant
        // EXISTANT rend la commande rejouable sans effet de bord.
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
            // L'OBJET DÉPOSÉ DEVIENT ORPHELIN. On tente de le retirer, sans
            // faire dépendre la réponse de ce nettoyage : l'appelant doit lire la
            // vraie erreur, pas celle du ménage.
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
            // Pas de variantes prévues : le fichier est prêt tel quel. Le laisser
            // en « Uploaded » ferait attendre indéfiniment un traitement qui
            // n'arrivera jamais.
            media.Value.CompleteProcessing([]);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Decrire(media.Value);
    }

    /// <summary>
    /// L'identifiant, et l'URL SEULEMENT si le fichier est public.
    ///
    /// LE TEST PORTE SUR LE MÉDIA, PAS SUR LA POLITIQUE DE SA NATURE.
    ///
    /// Les deux coïncident aujourd'hui, mais c'est le média qui décide de sa
    /// visibilité — la politique ne fait qu'en fixer la valeur par défaut.
    /// Interroger la politique laisserait passer une URL publique le jour où un
    /// fichier est restreint individuellement.
    /// </summary>
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

        //ON RELIT L'ORIGINAL DEPUIS LE STOCKAGE. Il n'est pas en base — c'est
        // tout le principe du service — et le retraitement doit repartir des
        // octets réels, pas d'une variante déjà dégradée.
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
            // LES OCTETS D'ABORD, LA LIGNE ENSUITE — l'inverse de l'upload, et
            // pour la même raison retournée : une ligne effacée avant ses objets
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
                // On laisse la ligne : le prochain passage réessaiera. Un stockage
                // momentanément indisponible ne doit pas produire d'orphelins.
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

    /// <summary>
    /// UN ÉCHEC DE VARIANTE NE PERD PAS LE FICHIER.
    ///
    /// L'original est déjà dans le stockage et reste servable — <c>IsUsable</c> le
    /// dit explicitement. Faire échouer tout l'upload parce qu'une miniature n'a
    /// pas pu être calculée perdrait une photo parfaitement valable, et
    /// l'utilisateur recommencerait sans comprendre.
    /// </summary>
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
