using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Drivers;

/// <inheritdoc cref="IDriversClient" />
public sealed class DriversClient : ServiceHttpClient, IDriversClient
{
    public DriversClient(HttpClient http, ILogger<DriversClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Drivers;
}
