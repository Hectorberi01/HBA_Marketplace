using System.Collections.Concurrent;
using HBA.Media.Application.Abstractions;
using HBA.Media.Domain.Assets;
using HBA.Shared.Domain.Results;
using Microsoft.Extensions.Options;

namespace HBA.Media.Infrastructure.ObjectStorage;

/// <summary>STOCKAGE EN MÉMOIRE, POUR LE DÉVELOPPEMENT HORS LIGNE.</summary>
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

    /// <summary>CE N'EST PAS UNE VRAIE SIGNATURE, et le préfixe le dit.</summary>
    public Result<string> CreateSignedGetUrl(string bucket, string objectKey, int expiresSeconds = 300)
        => $"memory://{bucket}/{objectKey}?unsigned-development-only&expires={expiresSeconds}";

    private static string Cle(string bucket, string objectKey) => $"{bucket}/{objectKey}";
}
