using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using HBA.Media.Application.Abstractions;
using HBA.Media.Domain.Assets;
using HBA.Shared.Domain.Results;
using Microsoft.Extensions.Options;

namespace HBA.Media.Infrastructure.ObjectStorage;

/// <summary>
/// Configuration d'un stockage compatible S3 : MinIO, AWS S3, Cloudflare R2.
///
/// DEUX BUCKETS, PAS UN. Le §9 l'impose : « les documents privés doivent être
/// stockés dans des buckets privés ; ils ne doivent jamais être accessibles via
/// une URL publique permanente ». Un seul bucket avec des ACL par objet marche
/// jusqu'au jour où quelqu'un se trompe d'ACL — et personne ne s'en aperçoit,
/// puisque rien n'échoue.
/// </summary>
public sealed class ObjectStorageOptions
{
    public const string SectionName = "Media:Storage";

    /// <summary>Point d'entrée S3, sans bucket : « https://xxx.r2.cloudflarestorage.com ».</summary>
    public string? Endpoint { get; set; }

    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }

    /// <summary>« auto » chez R2, « us-east-1 » chez MinIO par défaut.</summary>
    public string Region { get; set; } = "auto";

    public string PublicBucket { get; set; } = "hba-public";
    public string PrivateBucket { get; set; } = "hba-private";

    /// <summary>
    /// Domaine servant les objets publics — CDN ou domaine personnalisé (§25).
    ///
    /// Distinct de l'<c>Endpoint</c> : on ne sert pas les images du catalogue par
    /// l'API S3 signée. S'il est absent, l'URL publique retombe sur l'endpoint.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey);
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE STOCKAGE OBJET, EN SIGNATURE AWS V4 (cahier des charges §17).
///
/// CE FICHIER REMPLACE DEUX IMPLÉMENTATIONS EXISTANTES.
///
/// Le dépôt en contenait déjà deux — <c>CloudflareR2MediaStorage</c> pour les
/// images du catalogue, <c>CloudflareR2KybStorage</c> pour les pièces des
/// vendeurs. Deux copies du même algorithme cryptographique, dans deux modules
/// qui s'ignorent : une correction de l'une n'aurait jamais atteint l'autre, et
/// c'est le genre de divergence qu'on ne découvre que le jour où une signature
/// est refusée en production.
///
/// C'est la raison d'être du service transverse, bien avant les miniatures.
///
/// AUCUN SDK AWS. La signature V4 tient en quarante lignes ; embarquer le SDK
/// pour trois verbes ajouterait des dizaines de dépendances transitives à un
/// monolithe qui en compte déjà assez. Le protocole, lui, est figé depuis 2012.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class S3CompatibleObjectStorage : IObjectStorage
{
    private const string Algorithme = "AWS4-HMAC-SHA256";
    private const string Service = "s3";

    private readonly HttpClient _http;
    private readonly ObjectStorageOptions _options;

    public S3CompatibleObjectStorage(HttpClient http, IOptions<ObjectStorageOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>
    /// LE BUCKET DÉCOULE DE LA VISIBILITÉ, ET L'APPELANT NE CHOISIT PAS.
    ///
    /// C'est la seule garantie structurelle que le §9 soit respecté. Laisser le
    /// bucket en paramètre, c'est attendre le jour où une CNI part dans celui que
    /// sert le CDN — et rien n'échouera pour le signaler.
    /// </summary>
    public string BucketFor(MediaVisibility visibility)
        => visibility == MediaVisibility.Public ? _options.PublicBucket : _options.PrivateBucket;

    public async Task<Result<string>> PutAsync(ObjectToStore obj, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return Error.Failure("media.storage.not_configured", "Stockage objet non configuré.");
        }

        var uri = new Uri($"{_options.Endpoint!.TrimEnd('/')}/{obj.Bucket}/{obj.ObjectKey}");

        using var requete = new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = new ByteArrayContent(obj.Content)
        };

        requete.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(obj.ContentType);
        Sign(requete, obj.Content);

        try
        {
            using var reponse = await _http.SendAsync(requete, cancellationToken);

            if (!reponse.IsSuccessStatusCode)
            {
                var corps = await reponse.Content.ReadAsStringAsync(cancellationToken);
                return Error.Failure(
                    "media.storage.put_failed",
                    $"Dépôt refusé par le stockage ({(int)reponse.StatusCode}). {Truncate(corps)}");
            }

            return GetPublicUrl(obj.Bucket, obj.ObjectKey).Value;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // ON NE LAISSE PAS FUIR L'EXCEPTION. Une panne de stockage est un
            // cas NORMAL du point de vue métier — l'upload échoue proprement, et
            // la commande n'écrit aucune métadonnée. Une exception non capturée
            // remonterait en 500 sans que l'appelant sache s'il peut réessayer.
            return Error.Failure("media.storage.unreachable", $"Stockage injoignable : {ex.Message}");
        }
    }

    public async Task<Result<byte[]>> DownloadAsync(
        string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return Error.Failure("media.storage.not_configured", "Stockage objet non configuré.");
        }

        var uri = new Uri($"{_options.Endpoint!.TrimEnd('/')}/{bucket}/{objectKey}");

        using var requete = new HttpRequestMessage(HttpMethod.Get, uri);
        Sign(requete, []);

        try
        {
            using var reponse = await _http.SendAsync(requete, cancellationToken);

            if (!reponse.IsSuccessStatusCode)
            {
                return Error.NotFound("media.storage.not_found", "Objet introuvable dans le stockage.");
            }

            return await reponse.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Error.Failure("media.storage.unreachable", $"Stockage injoignable : {ex.Message}");
        }
    }

    public async Task<Result> DeleteAsync(
        string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            return Result.Failure(Error.Failure("media.storage.not_configured", "Stockage objet non configuré."));
        }

        var uri = new Uri($"{_options.Endpoint!.TrimEnd('/')}/{bucket}/{objectKey}");

        using var requete = new HttpRequestMessage(HttpMethod.Delete, uri);
        Sign(requete, []);

        try
        {
            using var reponse = await _http.SendAsync(requete, cancellationToken);

            // 404 EST UN SUCCÈS. Effacer ce qui n'existe plus, c'est l'état
            // recherché — et la purge doit pouvoir rejouer sans se bloquer sur un
            // objet déjà parti lors d'un passage interrompu.
            return reponse.IsSuccessStatusCode || reponse.StatusCode == System.Net.HttpStatusCode.NotFound
                ? Result.Success()
                : Result.Failure(Error.Failure(
                    "media.storage.delete_failed", $"Suppression refusée ({(int)reponse.StatusCode})."));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result.Failure(Error.Failure("media.storage.unreachable", $"Stockage injoignable : {ex.Message}"));
        }
    }

    public Result<string> GetPublicUrl(string bucket, string objectKey)
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
        {
            return $"{_options.PublicBaseUrl.TrimEnd('/')}/{objectKey}";
        }

        if (!_options.IsConfigured)
        {
            return Error.Failure("media.storage.not_configured", "Stockage objet non configuré.");
        }

        return $"{_options.Endpoint!.TrimEnd('/')}/{bucket}/{objectKey}";
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// URL SIGNÉE DE LECTURE (§10), EN « QUERY STRING » V4.
    ///
    /// LA DURÉE EST BORNÉE DES DEUX CÔTÉS.
    ///
    /// Trop courte, le document se ferme pendant qu'on le lit ; trop longue, une
    /// URL collée dans un ticket de support sert encore la semaine suivante. Une
    /// heure au maximum : au-delà, ce n'est plus une URL temporaire, c'est une
    /// fuite à retardement.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result<string> CreateSignedGetUrl(string bucket, string objectKey, int expiresSeconds = 300)
    {
        if (!_options.IsConfigured)
        {
            return Error.Failure("media.storage.not_configured", "Stockage objet non configuré.");
        }

        var duree = Math.Clamp(expiresSeconds, 30, 3600);

        var maintenant = DateTime.UtcNow;
        var amzDate = maintenant.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateCourte = maintenant.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scope = $"{dateCourte}/{_options.Region}/{Service}/aws4_request";

        var hote = new Uri(_options.Endpoint!).Host;
        var chemin = $"/{bucket}/{Uri.EscapeDataString(objectKey).Replace("%2F", "/", StringComparison.Ordinal)}";

        // Les paramètres DOIVENT être triés par nom : la signature porte sur la
        // chaîne canonique, et un ordre différent produit une signature différente.
        var parametres = string.Join('&',
            $"X-Amz-Algorithm={Algorithme}",
            $"X-Amz-Credential={Uri.EscapeDataString($"{_options.AccessKeyId}/{scope}")}",
            $"X-Amz-Date={amzDate}",
            $"X-Amz-Expires={duree}",
            "X-Amz-SignedHeaders=host");

        var requeteCanonique = string.Join('\n',
            "GET", chemin, parametres, $"host:{hote}\n", "host", "UNSIGNED-PAYLOAD");

        var aSigner = string.Join('\n',
            Algorithme, amzDate, scope, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requeteCanonique))).ToLowerInvariant());

        var signature = Convert.ToHexString(HmacSha256(SigningKey(dateCourte), aSigner)).ToLowerInvariant();

        return $"{_options.Endpoint!.TrimEnd('/')}{chemin}?{parametres}&X-Amz-Signature={signature}";
    }

    // ── Signature V4, en en-têtes ───────────────────────────────────────────

    private void Sign(HttpRequestMessage requete, byte[] corps)
    {
        var maintenant = DateTime.UtcNow;
        var amzDate = maintenant.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateCourte = maintenant.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var scope = $"{dateCourte}/{_options.Region}/{Service}/aws4_request";

        var empreinteCorps = Convert.ToHexString(SHA256.HashData(corps)).ToLowerInvariant();

        // ═════════════════════════════════════════════════════════════════════
        // `Authority` ET NON `Host` : LE PORT FAIT PARTIE DE LA SIGNATURE.
        //
        // `Uri.Host` rend « minio », `Uri.Authority` rend « minio:9000 ». Or
        // HttpClient envoie un en-tête `Host: minio:9000` — il n'omet le port que
        // s'il est celui par défaut du schéma. Signer « host:minio » alors que le
        // serveur vérifie « host:minio:9000 » produit un condensé différent, et
        // MinIO répond 403 `SignatureDoesNotMatch`.
        //
        // Le défaut était INVISIBLE sur la cible de production : R2 et S3 sont
        // joints en HTTPS sur le port 443, donc `Host` et `Authority` rendent la
        // même chaîne. Il n'apparaît que face à un port non standard — c'est-à-dire
        // exactement MinIO en développement, le seul endroit où l'on pouvait
        // l'attraper avant la mise en ligne.
        //
        // `Authority` est la bonne primitive et pas seulement un correctif :
        // sa règle d'omission du port par défaut est précisément celle de
        // l'en-tête `Host`. Concaténer « host + ":" + port » à la main
        // réintroduirait l'écart dans l'autre sens, en signant « :443 » que
        // HttpClient n'envoie pas.
        // ═════════════════════════════════════════════════════════════════════
        var hote = requete.RequestUri!.Authority;

        // `AbsolutePath` EST DÉJÀ PERCENT-ENCODÉ, ce qu'exige la requête
        // canonique — et les barres obliques y restent littérales, comme SigV4 le
        // demande pour un chemin (contrairement à la chaîne de requête).
        var chemin = requete.RequestUri.AbsolutePath;

        requete.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        requete.Headers.TryAddWithoutValidation("x-amz-content-sha256", empreinteCorps);

        const string enTetesSignes = "host;x-amz-content-sha256;x-amz-date";

        var requeteCanonique = string.Join('\n',
            requete.Method.Method,
            chemin,
            string.Empty,
            $"host:{hote}",
            $"x-amz-content-sha256:{empreinteCorps}",
            $"x-amz-date:{amzDate}",
            string.Empty,
            enTetesSignes,
            empreinteCorps);

        var aSigner = string.Join('\n',
            Algorithme, amzDate, scope,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requeteCanonique))).ToLowerInvariant());

        var signature = Convert.ToHexString(HmacSha256(SigningKey(dateCourte), aSigner)).ToLowerInvariant();

        requete.Headers.TryAddWithoutValidation(
            "Authorization",
            $"{Algorithme} Credential={_options.AccessKeyId}/{scope}, SignedHeaders={enTetesSignes}, Signature={signature}");
    }

    private byte[] SigningKey(string dateCourte)
    {
        var cle = HmacSha256(Encoding.UTF8.GetBytes($"AWS4{_options.SecretAccessKey}"), dateCourte);
        cle = HmacSha256(cle, _options.Region);
        cle = HmacSha256(cle, Service);
        return HmacSha256(cle, "aws4_request");
    }

    private static byte[] HmacSha256(byte[] cle, string donnees)
    {
        using var hmac = new HMACSHA256(cle);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(donnees));
    }

    private static string Truncate(string? texte)
        => string.IsNullOrEmpty(texte) ? string.Empty : texte.Length <= 200 ? texte : texte[..200];
}
