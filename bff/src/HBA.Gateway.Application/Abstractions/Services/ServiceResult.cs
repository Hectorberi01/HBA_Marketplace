using System.Text.Json;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>
/// Résultat d'un appel sortant vers un microservice.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA CHARGE UTILE EST UN `JsonElement`, ET CE N'EST PAS UN RACCOURCI.
///
/// Aucun des treize services n'est écrit à ce jour. Déclarer ici un
/// `ProductDto` ou un `RestaurantDto` reviendrait à INVENTER un contrat que les
/// équipes n'ont pas encore arrêté — et à figer ce contrat inventé dans la
/// passerelle, c'est-à-dire à l'endroit le plus coûteux à corriger ensuite.
///
/// Le typage viendra service par service, à mesure que les contrats existent :
/// chaque section d'agrégation pourra alors passer de `JsonElement` à un DTO
/// réel sans toucher au reste.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="IsSuccess">Vrai si le service a répondu avec un code 2xx.</param>
/// <param name="StatusCode">Code HTTP renvoyé, ou 0 si la requête n'a pas abouti.</param>
/// <param name="Payload">Corps JSON de la réponse, présent uniquement en cas de succès.</param>
/// <param name="FailureReason">
/// Motif technique court et NON destiné au client final : il peut nommer un
/// service interne. Il sert aux journaux, jamais au corps de réponse public.
/// </param>
public sealed record ServiceResult(
    bool IsSuccess,
    int StatusCode,
    JsonElement? Payload,
    string? FailureReason)
{
    public static ServiceResult Success(int statusCode, JsonElement payload)
        => new(true, statusCode, payload, null);

    public static ServiceResult Failure(int statusCode, string reason)
        => new(false, statusCode, null, reason);
}
