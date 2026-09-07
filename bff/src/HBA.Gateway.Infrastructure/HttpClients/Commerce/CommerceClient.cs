using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Commerce;

/// <inheritdoc cref="ICommerceClient" />
public sealed class CommerceClient : ServiceHttpClient, ICommerceClient
{
    public CommerceClient(HttpClient http, ILogger<CommerceClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Commerce;
}
