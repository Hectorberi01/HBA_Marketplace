using System.Net;
using System.Text.Json;
using HBA.Gateway.Application.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients;

/// <summary>
/// Implémentation commune des clients sortants : exécute la requête, convertit tout
/// échec en <see cref="ServiceResult"/> et ne laisse remonter aucune exception
/// attendue.
/// </summary>
public abstract partial class ServiceHttpClient : IServiceClient
{
    /// <summary>
    /// Client HTTP configuré : adresse de base, résilience et propagation
    /// d'en-têtes sont déjà posées par le conteneur.
    /// </summary>
    protected HttpClient Http { get; }

    protected ILogger Log { get; }

    protected ServiceHttpClient(HttpClient http, ILogger logger)
    {
        Http = http;
        Log = logger;
    }

    public abstract string ServiceKey { get; }

    public async Task<ServiceResult> GetJsonAsync(
        string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            // `HttpCompletionOption` par défaut : le corps est lu entièrement avant
            // de rendre la main.
            using var response = await Http.GetAsync(relativePath, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ServiceResult.Failure(
                    (int)response.StatusCode,
                    $"{ServiceKey} a répondu {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            // `Clone()` EST OBLIGATOIRE. NE PAS SUPPRIMER.
            return ServiceResult.Success((int)response.StatusCode, document.RootElement.Clone());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Le client est parti ou le budget d'agrégation a expiré : ce n'est pas
            // une panne du service appelé, et cela ne doit pas être compté comme
            // telle.
            return ServiceResult.Failure(0, $"{ServiceKey} : appel annulé");
        }
        // `JsonException` AVANT le filtre générique, et non l'inverse.
        catch (JsonException exception)
        {
            // Réponse 2xx dont le corps n'est pas du JSON : page d'erreur d'un
            // intermédiaire, portail captif, service qui rend du HTML. Traiter ce
            // cas comme un succès ferait remonter n'importe quoi jusqu'au client.
            Log.LogWarning(
                exception, "Réponse non JSON de {Service} : {Path}", ServiceKey, relativePath);

            return ServiceResult.Failure((int)HttpStatusCode.BadGateway, $"{ServiceKey} : réponse illisible");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            Log.LogWarning(
                exception, "Appel sortant vers {Service} en échec : {Path}", ServiceKey, relativePath);

            return ServiceResult.Failure((int)HttpStatusCode.BadGateway, $"{ServiceKey} injoignable");
        }
    }
}
