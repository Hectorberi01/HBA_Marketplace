namespace HBA.Gateway.Application.Bff.Shared;

/// <summary>
/// Une dépendance <see cref="DependencyCriticality.Critical"/> a échoué :
/// l'écran ne peut pas être rendu.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE EXCEPTION ICI, ALORS QUE TOUT LE RESTE REND DES RÉSULTATS. POURQUOI.
///
/// Les échecs ATTENDUS — un service optionnel muet, un 404 — sont des valeurs :
/// l'agrégateur doit continuer, et un `try/catch` par dépendance serait à la fois
/// illisible et fragile (la moindre omission ferait tomber l'écran entier).
///
/// L'échec d'une dépendance critique est l'inverse : il n'y a plus rien à
/// construire, et TOUT le code qui suit dans le handler est sans objet. Le rendre
/// comme une valeur obligerait chaque handler à tester puis à sortir — un `if`
/// oublié laisserait passer un DTO à moitié rempli, avec un nom de produit nul.
///
/// L'exception garantit qu'on ne peut pas oublier. Elle est traduite en 503
/// `application/problem+json` par `ExceptionMiddleware`, sans jamais nommer le
/// service interne en cause.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class CriticalDependencyException : Exception
{
    public CriticalDependencyException(string source, int statusCode, string? reason)
        : base($"Dépendance critique indisponible : {source}")
    {
        Source = source;
        StatusCode = statusCode;
        Reason = reason;
    }

    /// <summary>Nom logique de la dépendance. Ne part PAS dans la réponse.</summary>
    public new string Source { get; }

    public int StatusCode { get; }

    public string? Reason { get; }
}

/// <summary>La ressource demandée n'existe pas — traduit en 404.</summary>
/// <remarks>
/// Distinct de <see cref="CriticalDependencyException"/> : un catalogue à terre
/// rend 503, un produit supprimé rend 404. Les confondre ferait croire à des
/// milliers de clients que leurs produits ont disparu pendant une panne.
/// </remarks>
public sealed class BffResourceNotFoundException : Exception
{
    public BffResourceNotFoundException(string resource, object id)
        : base($"{resource} introuvable : {id}")
    {
        Resource = resource;
    }

    public string Resource { get; }
}
