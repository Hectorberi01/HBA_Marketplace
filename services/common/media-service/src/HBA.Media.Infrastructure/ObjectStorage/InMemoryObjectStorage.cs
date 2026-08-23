using System.Collections.Concurrent;
using HBA.Media.Application.Abstractions;
using HBA.Media.Domain.Assets;
using HBA.Shared.Domain.Results;
using Microsoft.Extensions.Options;

namespace HBA.Media.Infrastructure.ObjectStorage;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// STOCKAGE EN MÉMOIRE, POUR LE DÉVELOPPEMENT HORS LIGNE.
///
/// Substitut retenu quand aucune configuration de stockage n'est fournie. Le
/// dépôt a le même motif ailleurs — <c>SimulatedMediaStorage</c> côté catalogue,
/// <c>SimulatedKybStorage</c> côté vendeurs — et pour la même raison : un
/// développeur sans identifiants S3 doit pouvoir lancer l'application et créer un
/// produit.
///
/// LE CHOIX EST JOURNALISÉ AU DÉMARRAGE, PAS SILENCIEUX.
///
/// Un substitut qui s'installe sans le dire, c'est une préproduction qui perd
/// tous ses fichiers au redémarrage pendant trois semaines avant que quelqu'un ne
/// comprenne. Voir <c>MediaModuleInstaller</c>.
///
/// IL NE SIGNE RIEN. Les URL qu'il rend sont locales et ne protègent rien.
/// C'est acceptable en développement, et c'est pourquoi il ne doit jamais être
/// sélectionné en production — la configuration présente suffit à l'écarter.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class InMemoryObjectStorage : IObjectStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _objets = new(StringComparer.Ordinal);
    private readonly ObjectStorageOptions _options;

    public InMemoryObjectStorage(IOptions<ObjectStorageOptions> options) => _options = options.Value;

    public string BucketFor(MediaVisibility visibility)
        => visibility == MediaVisibility.Public ? _options.PublicBucket : _options.PrivateBucket;

    public Task<Result<string>> PutAsync(ObjectToStore obj, CancellationToken cancellationToken = default)
    {
        _objets[Cle(obj.Bucket, obj.ObjectKey)] = obj.Content;
        return Task.FromResult(GetPublicUrl(obj.Bucket, obj.ObjectKey));
    }

    public Task<Result<byte[]>> DownloadAsync(
        string bucket, string objectKey, CancellationToken cancellationToken = default)
        => Task.FromResult(_objets.TryGetValue(Cle(bucket, objectKey), out var contenu)
            ? Result.Success(contenu)
            : Result.Failure<byte[]>(Error.NotFound("media.storage.not_found", "Objet introuvable.")));

    public Task<Result> DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        // Comme le vrai : effacer ce qui n'existe plus est un succès.
        _objets.TryRemove(Cle(bucket, objectKey), out _);
        return Task.FromResult(Result.Success());
    }

    public Result<string> GetPublicUrl(string bucket, string objectKey)
        => $"memory://{bucket}/{objectKey}";

    /// <summary>
    /// CE N'EST PAS UNE VRAIE SIGNATURE, et le préfixe le dit.
    ///
    /// Une URL « signée » indistinguable d'une vraie ferait croire à un
    /// développeur que le chemin privé fonctionne, alors qu'il ne protège rien.
    /// </summary>
    public Result<string> CreateSignedGetUrl(string bucket, string objectKey, int expiresSeconds = 300)
        => $"memory://{bucket}/{objectKey}?unsigned-development-only&expires={expiresSeconds}";

    private static string Cle(string bucket, string objectKey) => $"{bucket}/{objectKey}";
}
