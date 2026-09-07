using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Infrastructure.Configuration;

namespace HBA.Gateway.Infrastructure.HttpClients;

/// <inheritdoc cref="IServiceClientRegistry" />
public sealed class ServiceClientRegistry : IServiceClientRegistry
{
    private readonly Dictionary<string, IServiceClient> _clients;

    /// <summary>Reçoit TOUS les clients enregistrés et les indexe par leur propre clé.</summary>
    public ServiceClientRegistry(IEnumerable<IServiceClient> clients)
        => _clients = clients.ToDictionary(
            client => client.ServiceKey, StringComparer.OrdinalIgnoreCase);

    public IServiceClient? Find(string serviceKey)
        => _clients.GetValueOrDefault(serviceKey);

    public IReadOnlyCollection<string> KnownKeys => ServiceKeys.All.ToArray();
}
