using HBA.Shared.Domain.Results;
using HBA.Catalog.Application.Abstractions;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace HBA.Catalog.Infrastructure.Media;

/// <summary>Détourage LOCAL via un service rembg (u2net) auto-hébergé.</summary>
public sealed class RembgImageProcessor : IImageProcessor, IImageProcessingAvailability
{
    public const string ClientName = "rembg-image-processor";

    /// <summary>Côté maximal du rendu, en pixels.</summary>
    private const int MaxSide = 2000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly RembgOptions _options;
    private readonly RembgHealth _health;
    private readonly ILogger<RembgImageProcessor> _logger;

    public RembgImageProcessor(
        IHttpClientFactory httpClientFactory,
        RembgOptions options,
        RembgHealth health,
        ILogger<RembgImageProcessor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _health = health;
        _logger = logger;
    }

    /// <summary>
    /// Reflète la santé OBSERVÉE, pas la simple présence d'une configuration : un
    /// conteneur arrêté ne doit pas faire promettre un détourage aux interfaces.
    /// </summary>
    public bool IsAvailable => _health.IsHealthy;

    public async Task<Result<ProcessedImage>> RemoveBackgroundWhiteAsync(
        string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ClientName);

            // On ne passe PAS `bgc` : le service rendrait un PNG opaque pleine
            // couleur, bien plus lourd à transférer et à décoder, pour un fond
            // blanc que nous reposons de toute façon nous-mêmes.
            var url = $"{_options.BaseUrl.TrimEnd('/')}/api/remove";

            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            // Le champ s'appelle « file » — c'est le nom du paramètre côté serveur.
            form.Add(file, "file", string.IsNullOrWhiteSpace(fileName) ? "upload" : fileName);
            // `EffectiveModel`, jamais `Model` : la liste blanche de licences
            // s'applique ici.
            form.Add(new StringContent(_options.EffectiveModel), "model");
            // Lissage du masque. Il érode légèrement les contours fins : c'est un
            // compromis, pas une amélioration gratuite.
            form.Add(new StringContent("true"), "ppm");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, _options.TimeoutSeconds)));

            using var response = await client.PostAsync(url, form, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("rembg : détourage échoué ({Code}) : {Body}", (int)response.StatusCode, Trim(body));
                _health.MarkFailure();
                return Error.Failure("image.process.render_failed",
                    $"Détourage indisponible ({(int)response.StatusCode}).");
            }

            var cutout = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (cutout.Length == 0)
            {
                _health.MarkFailure();
                return Error.Failure("image.process.empty", "Le service de détourage a renvoyé une image vide.");
            }

            // L'orientation se lit sur l'ORIGINAL : le PNG produit par rembg n'a
            // plus d'EXIF (voir `ReadOrientation`).
            var jpeg = ToOpaqueJpeg(cutout, ReadOrientation(content), _options.JpegQuality);
            if (jpeg is null)
            {
                _health.MarkFailure();
                return Error.Failure("image.process.encode_failed", "L'image détourée n'a pas pu être réencodée.");
            }

            _health.MarkSuccess();
            return new ProcessedImage(jpeg, "image/jpeg");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Annulation venue de l'APPELANT (client parti) : on la laisse
            // remonter, et on ne l'impute pas au service — il n'y est pour rien.
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("rembg : délai de traitement dépassé ({Seconds} s).", _options.TimeoutSeconds);
            _health.MarkFailure();
            return Error.Failure("image.process.timeout", "Le détourage a pris trop de temps.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "rembg : traitement d'image en erreur.");
            _health.MarkFailure();
            return Error.Failure("image.process.error", "Le service de détourage est injoignable.");
        }
    }

    /// <summary>Orientation EXIF de l'image d'ORIGINE.</summary>
    private static SKEncodedOrigin ReadOrientation(byte[] original)
    {
        try
        {
            using var data = SKData.CreateCopy(original);
            using var codec = SKCodec.Create(data);
            return codec?.EncodedOrigin ?? SKEncodedOrigin.TopLeft;
        }
        catch
        {
            // Orientation illisible : on ne tourne rien.
            return SKEncodedOrigin.TopLeft;
        }
    }

    /// <summary>
    /// Aplatit le PNG détouré sur du blanc OPAQUE, applique l'orientation, réduit
    /// si nécessaire, et réencode en JPEG.
    /// </summary>
    private static byte[]? ToOpaqueJpeg(byte[] png, SKEncodedOrigin origin, int quality)
    {
        using var data = SKData.CreateCopy(png);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        // DIMENSIONS LUES AVANT DÉCODAGE.
        var source = codec.Info;
        if (source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        var longest = Math.Max(source.Width, source.Height);
        var scale = longest > MaxSide ? (float)MaxSide / longest : 1f;
        var scaled = scale < 1f ? codec.GetScaledDimensions(scale) : new SKSizeI(source.Width, source.Height);

        // Alpha PREMUL au décodage : c'est ce qui permet à Skia de composer le
        // sujet sur le blanc.
        var decodeInfo = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = SKBitmap.Decode(codec, decodeInfo);
        if (bitmap is null)
        {
            return null;
        }

        // Un quart de tour échange largeur et hauteur.
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var targetWidth = swap ? bitmap.Height : bitmap.Width;
        var targetHeight = swap ? bitmap.Width : bitmap.Height;

        var surfaceInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var surface = SKSurface.Create(surfaceInfo);
        if (surface is null)
        {
            return null;
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.SetMatrix(OrientationMatrix(origin, bitmap.Width, bitmap.Height));
        canvas.DrawBitmap(bitmap, 0, 0);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));
        return encoded?.ToArray();
    }

    /// <summary>Transformation remettant l'image d'aplomb selon son orientation EXIF.</summary>
    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        // a b c d e f
        SKEncodedOrigin.TopLeft => Affine(1, 0, 0, 0, 1, 0),
        SKEncodedOrigin.TopRight => Affine(-1, 0, w, 0, 1, 0),   // miroir horizontal
        SKEncodedOrigin.BottomRight => Affine(-1, 0, w, 0, -1, h),   // 180°
        SKEncodedOrigin.BottomLeft => Affine(1, 0, 0, 0, -1, h),   // miroir vertical
        SKEncodedOrigin.LeftTop => Affine(0, 1, 0, 1, 0, 0),   // transposition
        SKEncodedOrigin.RightTop => Affine(0, -1, h, 1, 0, 0),   // 90° horaire
        SKEncodedOrigin.RightBottom => Affine(0, -1, h, -1, 0, w),   // transposition + miroir
        SKEncodedOrigin.LeftBottom => Affine(0, 1, 0, -1, 0, w),   // 270° horaire
        _ => Affine(1, 0, 0, 0, 1, 0),
    };

    private static SKMatrix Affine(float a, float b, float c, float d, float e, float f)
        => new(a, b, c, d, e, f, 0, 0, 1);

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}
