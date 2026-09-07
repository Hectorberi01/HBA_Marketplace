namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Résultat TYPÉ d'un appel sortant.</summary>
public sealed record ServiceResult<T>(
    bool IsSuccess,
    int StatusCode,
    T? Value,
    string? FailureReason)
{
    /// <summary>Le service a répondu 404 : la ressource n'existe pas.</summary>
    public bool IsNotFound => StatusCode == 404;

    public static ServiceResult<T> Success(int statusCode, T value)
        => new(true, statusCode, value, null);

    public static ServiceResult<T> Failure(int statusCode, string reason)
        => new(false, statusCode, default, reason);
}
