using System.Net;
using System.Text.Json;
using HBA.Gateway.Application.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients;

/// <summary>Volet TYPÉ de <see cref="ServiceHttpClient"/>.</summary>
public abstract partial class ServiceHttpClient
{
    /// <summary>Options de désérialisation partagées.</summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Exécute un GET et désérialise vers <typeparamref name="T"/>.</summary>
    protected async Task<ServiceResult<T>> GetAsync<T>(
        string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(relativePath, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Le CODE est conservé tel quel : c'est lui qui permet à l'appelant
                // de distinguer 404 (ressource absente), 401 (service authentifié
                // appelé sans session) et 5xx (panne).
                return ServiceResult<T>.Failure(
                    (int)response.StatusCode,
                    $"{ServiceKey} a répondu {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            var value = await DeserialiserCorpsAsync<T>(stream, cancellationToken);

            if (value is null)
            {
                // Corps `null` littéral sur une réponse 2xx.
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

    /// <summary>DÉSÉRIALISE LE CORPS, EN DÉBALLANT L'ENVELOPPE DU §25 SI ELLE EST LÀ.</summary>
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
        return element.TryGetProperty("data", out data);
    }
}
