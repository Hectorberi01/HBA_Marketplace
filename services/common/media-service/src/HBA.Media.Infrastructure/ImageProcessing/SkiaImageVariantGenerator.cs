using HBA.Media.Application.Abstractions;
using HBA.Media.Domain.Assets;
using HBA.Shared.Domain.Results;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HBA.Media.Infrastructure.ImageProcessing;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES VARIANTES D'IMAGE (cahier des charges §11, §12), AVEC SKIASHARP.
///
/// Le choix de la bibliothèque est déjà argumenté dans <c>Directory.Packages.props</c> :
/// ImageSharp est passé en licence commerciale, SkiaSharp reste MIT sans
/// condition. Pour une place de marché, la différence n'est pas théorique.
///
/// TOUT SORT EN WEBP, QUEL QUE SOIT LE FORMAT D'ENTRÉE.
///
/// Le §11 le suggère, et l'économie est réelle : une photo de plat en JPEG de
/// 800 Ko descend sous 200 Ko en WebP à qualité comparable. Sur un réseau mobile
/// béninois, c'est la différence entre une carte qui s'affiche et une carte qu'on
/// abandonne. L'ORIGINAL, lui, n'est jamais réencodé — il reste la source de
/// vérité et la seule chose qu'on puisse retraiter.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class SkiaImageVariantGenerator : IImageVariantGenerator
{
    private const int QualiteWebp = 82;

    /// <summary>
    /// Les tailles du §11. La miniature est CARRÉE et recadrée ; les autres
    /// conservent le rapport d'origine.
    ///
    /// Une miniature qui garderait le rapport produirait des vignettes de
    /// hauteurs différentes, et toute grille d'affichage deviendrait bancale.
    /// </summary>
    private static readonly (MediaVariantType Type, int MaxSide, bool Square)[] Formats =
    [
        (MediaVariantType.Thumbnail, 200, true),
        (MediaVariantType.Small, 480, false),
        (MediaVariantType.Medium, 1024, false),
        (MediaVariantType.Large, 1600, false)
    ];

    private readonly ILogger<SkiaImageVariantGenerator> _logger;

    public SkiaImageVariantGenerator(ILogger<SkiaImageVariantGenerator> logger) => _logger = logger;

    public ImageDimensions? ReadDimensions(byte[] content)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(content);
            return bitmap is null ? null : new ImageDimensions(bitmap.Width, bitmap.Height);
        }
        catch (Exception ex)
        {
            // Une image corrompue est un cas NORMAL — le §28 en fait un scénario de
            // test. On rend null, l'appelant se passera des dimensions.
            _logger.LogDebug(ex, "Dimensions illisibles : contenu probablement corrompu.");
            return null;
        }
    }

    public Task<Result<IReadOnlyList<GeneratedVariant>>> GenerateAsync(
        byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        SKBitmap? original = null;

        try
        {
            original = SKBitmap.Decode(content);

            if (original is null)
            {
                return Task.FromResult(Result.Failure<IReadOnlyList<GeneratedVariant>>(
                    Error.Validation("media.image_unreadable", "Image illisible ou corrompue.")));
            }

            var variantes = new List<GeneratedVariant>();

            foreach (var (type, cote, carre) in Formats)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // ON N'AGRANDIT JAMAIS. Étirer une photo de 200 px en « Large
                // 1600 » fabrique du flou et multiplie le poids réseau pour rien —
                // l'utilisateur télécharge plus pour voir moins bien.
                if (!carre && Math.Max(original.Width, original.Height) <= cote)
                {
                    continue;
                }

                var variante = carre
                    ? RecadrerCarre(original, cote)
                    : Redimensionner(original, cote);

                if (variante is not null)
                {
                    variantes.Add(variante);
                }
            }

            // L'« optimisée » garde les dimensions d'origine et ne change que
            // l'encodage. C'est elle qui sert quand aucune autre taille ne convient.
            var optimisee = Encoder(original, MediaVariantType.Optimized, original.Width, original.Height);
            if (optimisee is not null)
            {
                variantes.Add(optimisee);
            }

            return Task.FromResult(Result.Success<IReadOnlyList<GeneratedVariant>>(variantes));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ON CAPTURE LARGEMENT, ET C'EST DÉLIBÉRÉ.
            //
            // SkiaSharp s'appuie sur du code natif : une image malformée peut
            // produire à peu près n'importe quelle exception. Laisser remonter
            // ferait échouer TOUT l'upload — alors que l'original est déjà stocké
            // et parfaitement servable. Le média passe en « Failed », garde son
            // original, et se retraite plus tard.
            _logger.LogWarning(ex, "Génération des variantes impossible pour un contenu {ContentType}.", contentType);

            return Task.FromResult(Result.Failure<IReadOnlyList<GeneratedVariant>>(
                Error.Failure("media.variants_failed", $"Traitement de l'image impossible : {ex.Message}")));
        }
        finally
        {
            original?.Dispose();
        }
    }

    private static GeneratedVariant? Redimensionner(SKBitmap original, int coteMax)
    {
        var facteur = (double)coteMax / Math.Max(original.Width, original.Height);
        var largeur = Math.Max(1, (int)Math.Round(original.Width * facteur));
        var hauteur = Math.Max(1, (int)Math.Round(original.Height * facteur));

        using var redimensionne = original.Resize(new SKImageInfo(largeur, hauteur), SKFilterQuality.High);
        return redimensionne is null ? null : Encoder(redimensionne, TypePour(coteMax), largeur, hauteur);
    }

    /// <summary>
    /// Vignette carrée : on recadre AU CENTRE avant de réduire.
    ///
    /// Déformer pour remplir le carré donnerait des plats étirés ; recadrer perd
    /// les bords, ce qui est presque toujours le bon compromis sur une photo de
    /// produit ou de plat, dont le sujet est au centre.
    /// </summary>
    private static GeneratedVariant? RecadrerCarre(SKBitmap original, int cote)
    {
        var taille = Math.Min(original.Width, original.Height);
        var x = (original.Width - taille) / 2;
        var y = (original.Height - taille) / 2;

        using var carre = new SKBitmap(taille, taille);
        if (!original.ExtractSubset(carre, new SKRectI(x, y, x + taille, y + taille)))
        {
            return null;
        }

        using var reduit = carre.Resize(new SKImageInfo(cote, cote), SKFilterQuality.High);
        return reduit is null ? null : Encoder(reduit, MediaVariantType.Thumbnail, cote, cote);
    }

    private static GeneratedVariant? Encoder(SKBitmap bitmap, MediaVariantType type, int largeur, int hauteur)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var donnees = image.Encode(SKEncodedImageFormat.Webp, QualiteWebp);

        return donnees is null
            ? null
            : new GeneratedVariant(type, donnees.ToArray(), "image/webp", largeur, hauteur);
    }

    private static MediaVariantType TypePour(int coteMax) => coteMax switch
    {
        <= 480 => MediaVariantType.Small,
        <= 1024 => MediaVariantType.Medium,
        _ => MediaVariantType.Large
    };
}
