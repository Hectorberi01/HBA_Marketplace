using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Media;

/// <inheritdoc cref="IMediaClient" />
public sealed class MediaClient : ServiceHttpClient, IMediaClient
{
    public MediaClient(HttpClient http, ILogger<MediaClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Media;
}
