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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE STOCKAGE OBJET, DERRIÈRE UNE SEULE PORTE (cahier des charges §17).
///
/// « Cette abstraction permet de changer de fournisseur sans modifier Product,
/// Food, Seller ou Delivery Service. » — et c'est déjà arrivé dans ce dépôt : il
/// existait DEUX implémentations S3 séparées, une pour les images du catalogue,
/// une pour les pièces KYB des vendeurs, chacune avec sa propre signature AWS V4.
/// Deux copies d'un algorithme cryptographique, corrigées une seule fois le jour
/// où l'une aura un défaut.
///
/// CETTE INTERFACE NE CONNAÎT NI MediaAsset NI AUCUNE RÈGLE MÉTIER.
///
/// Elle dépose, lit, signe, efface. Savoir si un fichier a le droit d'exister,
/// quelle taille il peut faire et qui peut le lire appartient au domaine. Un
/// stockage qui validerait des types MIME serait un stockage qu'on ne peut plus
/// remplacer sans réécrire les règles.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IObjectStorage
{
    /// <summary>
    /// Dépose les octets. Rend l'URL publique quand le bucket l'est.
    ///
    /// APPELÉE AVANT LA CRÉATION DE LA MÉTADONNÉE. Une ligne « Uploaded »
    /// désignant un objet inexistant échouerait à chaque lecture ; un objet sans
    /// ligne se ramasse au ménage.
    /// </summary>
    Task<Result<string>> PutAsync(ObjectToStore obj, CancellationToken cancellationToken = default);

    /// <summary>
    /// URL de lecture SIGNÉE, de courte durée (§10).
    ///
    /// Le seul chemin d'accès aux fichiers privés. Cinq minutes par défaut : assez
    /// pour ouvrir un document, trop peu pour qu'une URL copiée dans un ticket de
    /// support serve encore le lendemain.
    /// </summary>
    Result<string> CreateSignedGetUrl(string bucket, string objectKey, int expiresSeconds = 300);

    /// <summary>URL permanente. N'a de sens que pour un bucket public.</summary>
    Result<string> GetPublicUrl(string bucket, string objectKey);

    Task<Result> DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

    Task<Result<byte[]>> DownloadAsync(
        string bucket, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Le bucket où ranger cette nature de fichier.
    ///
    /// DÉRIVÉ DE LA VISIBILITÉ, jamais choisi par l'appelant. Le §9 l'impose :
    /// « les documents privés doivent être stockés dans des buckets privés ; ils
    /// ne doivent jamais être accessibles via une URL publique permanente ». Le
    /// laisser au choix, c'est attendre le jour où une CNI part dans le bucket du
    /// CDN.
    /// </summary>
    string BucketFor(MediaVisibility visibility);
}

/// <summary>Une image dérivée, telle que produite par le générateur.</summary>
public sealed record GeneratedVariant(
    MediaVariantType Type, byte[] Content, string ContentType, int Width, int Height);

/// <summary>Les dimensions d'une image, lues sans la décoder entièrement.</summary>
public sealed record ImageDimensions(int Width, int Height);

/// <summary>
/// La fabrique de variantes (§11, §12).
///
/// SÉPARÉE DU STOCKAGE, délibérément : redimensionner et déposer sont deux
/// pannes différentes. Un service de traitement indisponible ne doit pas empêcher
/// un fichier d'être stocké et servi — c'est précisément ce que traduit l'état
/// <c>Failed</c>, qui laisse l'original utilisable.
/// </summary>
public interface IImageVariantGenerator
{
    /// <summary>Rend <c>null</c> si le contenu n'est pas une image lisible.</summary>
    ImageDimensions? ReadDimensions(byte[] content);

    /// <summary>
    /// Produit les déclinaisons demandées.
    ///
    /// NE PRODUIT JAMAIS UNE VARIANTE PLUS GRANDE QUE L'ORIGINAL. Agrandir une
    /// photo de 200 px en « Large 1600 » fabrique du flou et multiplie le poids
    /// réseau pour rien.
    /// </summary>
    Task<Result<IReadOnlyList<GeneratedVariant>>> GenerateAsync(
        byte[] content, string contentType, CancellationToken cancellationToken = default);
}
