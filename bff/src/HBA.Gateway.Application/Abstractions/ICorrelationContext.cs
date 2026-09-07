namespace HBA.Gateway.Application.Abstractions;

/// <summary>
/// Expose l'identifiant de corrélation de la requête en cours aux couches qui n'ont
/// pas — et ne doivent pas avoir — accès à <c> HttpContext</c>.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>Identifiant de corrélation, jamais nul pendant une requête.</summary>
    string CorrelationId { get; }
}
