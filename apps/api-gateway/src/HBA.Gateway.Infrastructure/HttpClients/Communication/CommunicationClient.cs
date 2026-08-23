using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Communication;

/// <inheritdoc cref="ICommunicationClient" />
public sealed class CommunicationClient : ServiceHttpClient, ICommunicationClient
{
    public CommunicationClient(HttpClient http, ILogger<CommunicationClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Communication;
}
