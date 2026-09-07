namespace HBA.Shared.Domain.Results;

/// <summary>Erreur métier typée, transportée par <see cref="Result"/> sans exception.</summary>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    // DEUX TYPES AJOUTÉS POUR LE §5 DU CAHIER DES CHARGES.

    /// <summary>
    /// 422 — la requête est bien formée, l'état métier ne permet pas l'opération.
    /// </summary>
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);

    /// <summary>503 — une dépendance gRPC, Kafka ou provider externe n'a pas répondu.</summary>
    public static Error DependencyUnavailable(string code, string message) => new(code, message, ErrorType.DependencyUnavailable);
}

public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
    BusinessRule = 6,
    DependencyUnavailable = 7
}
