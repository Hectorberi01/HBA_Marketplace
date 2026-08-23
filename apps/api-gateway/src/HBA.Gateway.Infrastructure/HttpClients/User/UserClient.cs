using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.User;

/// <inheritdoc cref="IUserClient" />
public sealed class UserClient : ServiceHttpClient, IUserClient
{
    public UserClient(HttpClient http, ILogger<UserClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.User;
}
