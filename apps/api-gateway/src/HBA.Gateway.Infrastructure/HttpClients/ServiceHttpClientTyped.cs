using System.Net;
using System.Text.Json;
using HBA.Gateway.Application.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients;

/// <summary>
/// Volet TYPÉ de <see cref="ServiceHttpClient"/>.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// MÊME CLASSE, DEUX FICHIERS — `partial`, et non une classe de plus.
///
/// Le volet non typé rend du `JsonElement` pour le mécanisme configuré ; celui-ci
/// désérialise vers un contrat lu. Les deux partagent la MÊME instance
/// d'`HttpClient`, donc le même délai, le même disjoncteur et la même propagation
/// d'en-têtes. Une classe séparée aurait dupliqué cette pile de résilience — et
/// le premier réglage n'aurait été appliqué que d'un côté.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public abstract partial class ServiceHttpClient
{
    /// <summary>
    /// Options de désérialisation partagées.
    /// </summary>
    /// <remarks>
    /// `PropertyNameCaseInsensitive` EST INDISPENSABLE ICI.
    ///
    /// Les services sérialisent en camelCase (réglage ASP.NET Core par défaut),
    /// les records de la passerelle sont en PascalCase. Sans cette option, chaque
    /// propriété reviendrait à `null` ou `default` — une réponse 200 avec un
    /// objet entièrement vide, qui ne lève rien et ne se voit qu'à l'écran.
    ///
    /// Statique et en lecture seule : `JsonSerializerOptions` met en cache ses
    /// métadonnées à la première utilisation. En créer une instance par appel
    /// annule ce cache et coûte cher sous charge.
    /// </remarks>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Exécute un GET et désérialise vers <typeparamref name="T"/>.
    /// Ne lève aucune exception pour un échec attendu.
    /// </summary>
    protected async Task<ServiceResult<T>> GetAsync<T>(
        string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(relativePath, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Le CODE est conservé tel quel : c'est lui qui permet à
                // l'appelant de distinguer 404 (ressource absente), 401 (service
                // authentifié appelé sans session) et 5xx (panne). Les écraser en
                // un « échec » unique rendrait la dégradation impossible à régler.
                return ServiceResult<T>.Failure(
                    (int)response.StatusCode,
                    $"{ServiceKey} a répondu {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            var value = await DeserialiserCorpsAsync<T>(stream, cancellationToken);

            if (value is null)
            {
                // Corps `null` littéral sur une réponse 2xx. Le rendre comme un
                // succès obligerait chaque agrégateur à tester la nullité du
                // `Value` d'un résultat pourtant marqué réussi.
                return ServiceResult<T>.Failure(
                    (int)HttpStatusCode.BadGateway, $"{ServiceKey} : corps vide");
            }

            return ServiceResult<T>.Success((int)response.StatusCode, value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ServiceResult<T>.Failure(0, $"{ServiceKey} : appel annulé");
        }
        catch (JsonException exception)
        {
            // CE CAS EST LE PLUS UTILE DE TOUS EN INTÉGRATION.
            //
            // Il se déclenche quand le contrat amont a CHANGÉ : un champ requis
            // renommé, un type qui passe de nombre à chaîne. Le journal doit donc
            // nommer le type attendu, sans quoi le diagnostic repart de zéro.
            Log.LogWarning(
                exception,
                "Contrat rompu avec {Service} sur {Path} : réponse non conforme à {Type}",
                ServiceKey, relativePath, typeof(T).Name);

            return ServiceResult<T>.Failure(
                (int)HttpStatusCode.BadGateway, $"{ServiceKey} : réponse illisible");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            Log.LogWarning(
                exception, "Appel sortant vers {Service} en échec : {Path}", ServiceKey, relativePath);

            return ServiceResult<T>.Failure(
                (int)HttpStatusCode.BadGateway, $"{ServiceKey} injoignable");
        }
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DÉSÉRIALISE LE CORPS, EN DÉBALLANT L'ENVELOPPE DU §25 SI ELLE EST LÀ.
    ///
    /// SANS CE DÉBALLAGE, UNE MIGRATION DE SERVICE REND DES OBJETS VIDES —
    ///    SANS EXCEPTION, SANS JOURNAL, SANS 500.
    ///
    /// Un service migré vers l'enveloppe rend
    /// `{ "success": true, "data": { … }, "meta": { … } }` là où il rendait la
    /// ressource nue. `JsonSerializer.DeserializeAsync&lt;CatalogProduct&gt;` sur ce
    /// corps ne LÈVE PAS : il ne reconnaît aucune propriété, ignore les membres
    /// inconnus par défaut, et rend un `CatalogProduct` parfaitement construit dont
    /// TOUS les champs valent `null` ou `default`.
    ///
    /// Le résultat est marqué succès, l'agrégateur l'accepte, et l'application
    /// affiche une fiche produit sans nom, sans prix et sans image. Rien dans les
    /// journaux de la passerelle, rien dans ceux du service. C'est le pire mode de
    /// panne du dépôt : celui qui ressemble à un succès.
    ///
    /// (Une liste, elle, lève bien — objet contre tableau — et rend 502. Ce n'est
    /// pas une consolation : cela veut dire que la moitié des appels échouent
    /// bruyamment et l'autre moitié silencieusement, à la même seconde.)
    ///
    /// POURQUOI DÉTECTER PLUTÔT QUE CONFIGURER PAR SERVICE.
    ///
    /// Les quatorze services ne migrent pas le même jour — c'est la décision D15,
    /// et c'est ce qui rend la migration tenable. Un drapeau « ce client est
    /// enveloppé » par service devrait être basculé à la même seconde que le
    /// déploiement du service, depuis un autre dépôt de configuration. Il serait
    /// oublié une fois, et l'on chercherait la cause dans le service migré.
    ///
    /// LA DÉTECTION EST VOLONTAIREMENT ÉTROITE.
    ///
    /// Il faut un objet racine portant À LA FOIS `success` (booléen) ET `meta`
    /// (objet) ET `data`. Un DTO métier qui aurait un champ `data` ne suffit donc
    /// pas à se faire déballer par erreur — il lui faudrait aussi un `success`
    /// booléen et un `meta` objet, ce qui n'est plus une coïncidence mais
    /// l'enveloppe.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<T?> DeserialiserCorpsAsync<T>(
        Stream stream, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var racine = document.RootElement;

        var charge = EstEnveloppe(racine, out var data) ? data : racine;

        return charge.Deserialize<T>(SerializerOptions);
    }

    /// <summary>Vrai si l'élément est une enveloppe `{ success, data, meta }` du §25.</summary>
    private static bool EstEnveloppe(JsonElement element, out JsonElement data)
    {
        data = default;

        if (element.ValueKind is not JsonValueKind.Object
            || !element.TryGetProperty("success", out var success)
            || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !element.TryGetProperty("meta", out var meta)
            || meta.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        // UNE ENVELOPPE D'ERREUR N'A PAS DE `data`, ET ON NE DOIT PAS LA DÉBALLER.
        //
        // Elle n'arrive normalement pas ici — le code HTTP non-2xx est intercepté
        // plus haut. Mais un service qui rendrait 200 avec `success: false` (cela
        // s'est déjà vu ailleurs) donnerait sinon un `default` silencieux de plus.
        // Ne pas reconnaître l'enveloppe fait retomber sur la désérialisation
        // directe, qui échoue franchement.
        return element.TryGetProperty("data", out data);
    }
}
