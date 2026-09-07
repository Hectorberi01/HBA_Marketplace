using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Infrastructure.Configuration;

namespace HBA.Gateway.Infrastructure.HttpClients;

/// <inheritdoc cref="IServiceClientRegistry" />
public sealed class ServiceClientRegistry : IServiceClientRegistry
{
    private readonly Dictionary<string, IServiceClient> _clients;

    /// <summary>
    /// Reçoit TOUS les clients enregistrés et les indexe par leur propre clé.
    /// </summary>
    /// <remarks>
    /// L'INDEX VIENT DES CLIENTS, PAS D'UNE SECONDE LISTE ÉCRITE À LA MAIN.
    ///
    /// Recopier ici les quatorze associations aurait créé un endroit de plus à
    /// mettre à jour lors de l'ajout d'un quinzième service — et l'oubli
    /// n'aurait produit aucune erreur de compilation, seulement une section BFF
    /// silencieusement indisponible.
    /// </remarks>
    public ServiceClientRegistry(IEnumerable<IServiceClient> clients)
        => _clients = clients.ToDictionary(
            client => client.ServiceKey, StringComparer.OrdinalIgnoreCase);

    public IServiceClient? Find(string serviceKey)
        => _clients.GetValueOrDefault(serviceKey);

    public IReadOnlyCollection<string> KnownKeys => ServiceKeys.All.ToArray();
}
