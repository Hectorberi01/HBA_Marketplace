namespace HBA.Gateway.Application.Abstractions;

/// <summary>
/// Expose l'identifiant de corrélation de la requête en cours aux couches qui
/// n'ont pas — et ne doivent pas avoir — accès à <c>HttpContext</c>.
/// </summary>
/// <remarks>
/// POURQUOI CETTE INTERFACE PLUTÔT QUE `IHttpContextAccessor`.
///
/// Un agrégateur BFF a besoin de l'identifiant pour le renvoyer au client et le
/// joindre à ses journaux. Lui donner `IHttpContextAccessor` lui donnerait aussi
/// les en-têtes bruts, les cookies et le jeton — c'est-à-dire tout ce que la
/// séparation Application/Api sert justement à ne pas laisser fuiter.
/// </remarks>
public interface ICorrelationContext
{
    /// <summary>Identifiant de corrélation, jamais nul pendant une requête.</summary>
    string CorrelationId { get; }
}
