namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>commerce-service</c> — panier, liste d'envies, promotions.</summary>
/// <remarks>
/// Interface distincte plutôt qu'un <see cref="IServiceClient"/> nommé : elle
/// permet d'attacher à CE service ses propres délais, son propre disjoncteur et
/// sa propre politique de réessai. Un catalogue lent ne doit pas imposer ses
/// délais au service de paiement.
/// </remarks>
public interface ICommerceClient : IServiceClient
{
}
