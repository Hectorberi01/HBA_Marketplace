using Microsoft.Extensions.DependencyInjection;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>Les interceptions que TOUT client gRPC interne doit porter.</summary>
public static class ClientGrpcInterneExtensions
{
    /// <summary>
    /// Pose, dans le bon ordre, le disjoncteur puis la clé interne, la corrélation
    /// et l'échéance.
    /// </summary>
    public static IHttpClientBuilder AjouterLesInterceptionsInternes(this IHttpClientBuilder builder)
        => builder
            .AddInterceptor<DisjoncteurClientInterceptor>()
            .AddInterceptor<InternalCallClientInterceptor>();
}
