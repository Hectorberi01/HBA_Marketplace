using HBA.Media.Domain.Assets;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Results;

namespace HBA.Media.Application.Abstractions;

/// <summary>Unit of Work propre au module Media (évite la collision DI inter-modules).</summary>
public interface IMediaUnitOfWork : IUnitOfWork
{
}

/// <summary>Un objet à déposer : le contenu en mémoire, sa clé, son type.</summary>
public sealed record ObjectToStore(string Bucket, string ObjectKey, string ContentType, byte[] Content);

/// <summary>LE STOCKAGE OBJET, DERRIÈRE UNE SEULE PORTE (cahier des charges §17).</summary>
public interface IObjectStorage
{
    /// <summary>Dépose les octets. Rend l'URL publique quand le bucket l'est.</summary>
    Task<Result<string>> PutAsync(ObjectToStore obj, CancellationToken cancellationToken = default);

    /// <summary>URL de lecture SIGNÉE, de courte durée (§10).</summary>
    Result<string> CreateSignedGetUrl(string bucket, string objectKey, int expiresSeconds = 300);

    /// <summary>URL permanente. N'a de sens que pour un bucket public.</summary>
    Result<string> GetPublicUrl(string bucket, string objectKey);

    Task<Result> DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

    Task<Result<byte[]>> DownloadAsync(
        string bucket, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>Le bucket où ranger cette nature de fichier.</summary>
    string BucketFor(MediaVisibility visibility);
}

/// <summary>Une image dérivée, telle que produite par le générateur.</summary>
public sealed record GeneratedVariant(
    MediaVariantType Type, byte[] Content, string ContentType, int Width, int Height);

/// <summary>Les dimensions d'une image, lues sans la décoder entièrement.</summary>
public sealed record ImageDimensions(int Width, int Height);

/// <summary>La fabrique de variantes (§11, §12).</summary>
public interface IImageVariantGenerator
{
    /// <summary>Rend <c>null</c> si le contenu n'est pas une image lisible.</summary>
    ImageDimensions? ReadDimensions(byte[] content);

    /// <summary>Produit les déclinaisons demandées.</summary>
    Task<Result<IReadOnlyList<GeneratedVariant>>> GenerateAsync(
        byte[] content, string contentType, CancellationToken cancellationToken = default);
}
