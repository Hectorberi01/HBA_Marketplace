namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>media-service</c> — médias et URL signées.</summary>
/// <remarks>
/// Interface distincte plutôt qu'un <see cref="IServiceClient"/> nommé : elle
/// permet d'attacher à CE service ses propres délais, son propre disjoncteur et
/// sa propre politique de réessai. Un catalogue lent ne doit pas imposer ses
/// délais au service de paiement.
/// </remarks>
public interface IMediaClient : IServiceClient
{
}
