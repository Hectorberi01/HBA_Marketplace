using System.Text.Json;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Résultat d'un appel sortant vers un microservice.</summary>
/// <param name="IsSuccess">Vrai si le service a répondu avec un code 2xx.</param>
/// <param name="StatusCode">Code HTTP renvoyé, ou 0 si la requête n'a pas abouti.</param>
/// <param name="Payload">Corps JSON de la réponse, présent uniquement en cas de succès.</param>
/// <param name="FailureReason">
/// Motif technique court et NON destiné au client final : il peut nommer un service
/// interne.
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
