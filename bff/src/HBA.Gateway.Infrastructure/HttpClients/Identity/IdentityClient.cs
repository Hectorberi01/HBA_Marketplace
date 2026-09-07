using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Identity;

/// <inheritdoc cref="IIdentityClient" />
public sealed class IdentityClient : ServiceHttpClient, IIdentityClient
{
    public IdentityClient(HttpClient http, ILogger<IdentityClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Identity;
}
