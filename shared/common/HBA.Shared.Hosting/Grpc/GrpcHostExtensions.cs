using HBA.Shared.Hosting.Grpc;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace HBA.Shared.Hosting;

/// <summary>Ports et protocoles d'un service HBA.</summary>
public sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    /// <summary>REST/JSON, appelé par la passerelle.</summary>
    public int HttpPort { get; init; } = 8080;

    /// <summary>gRPC, appelé par les autres services.</summary>
    public int GrpcPort { get; init; } = 8081;
}

public static class GrpcHostExtensions
{
    /// <summary>Ouvre deux ports : REST sur l'un, gRPC sur l'autre.</summary>
    public static WebApplicationBuilder AddHbaGrpc(this WebApplicationBuilder builder)
    {
        // AVANT TOUT LE RESTE : UN HÔTE MAL CONFIGURÉ NE DOIT PAS SE CONSTRUIRE.
        IdentiteInterne.RefuserLeModeNonSigneHorsDeveloppement(
            builder.Configuration.GetValue<bool>(
                $"{InternalCallOptions.SectionName}:{nameof(InternalCallOptions.IdentitesNonSignees)}"),
            builder.Environment.IsDevelopment());

        // LA CLÉ PRIVÉE EST LUE ICI, ET NON AU PREMIER APPEL gRPC.
        IdentiteInterne.RefuserUneClePriveeIllisible(
            builder.Configuration[$"{InternalCallOptions.SectionName}:{nameof(InternalCallOptions.PrivateKey)}"]);

        var hosting = builder.Configuration
            .GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>() ?? new HostingOptions();

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(hosting.HttpPort, listen => listen.Protocols = HttpProtocols.Http1);
            kestrel.ListenAnyIP(hosting.GrpcPort, listen => listen.Protocols = HttpProtocols.Http2);
        });

        builder.Services.AddSingleton<TraductionDesErreursServerInterceptor>();
        builder.Services.AddSingleton<InternalCallServerInterceptor>();
        builder.Services.AddSingleton<InternalCallClientInterceptor>();

        // L'ÉCHÉANCE DES APPELS SORTANTS EST DÉSORMAIS RÉGLABLE.
        builder.Services.AjouterLesEcheancesGrpc(builder.Configuration);

        // SINGLETON, ET C'EST LA CONDITION POUR QU'IL SERVE À QUELQUE CHOSE.
        builder.Services.AddSingleton<DisjoncteurClientInterceptor>();

        builder.Services.AddGrpc(options =>
        {
            // LA TRADUCTION EST POSÉE EN PREMIER, DONC LA PLUS À L'EXTÉRIEUR.
            options.Interceptors.Add<TraductionDesErreursServerInterceptor>();
            options.Interceptors.Add<InternalCallServerInterceptor>();

            // LES DÉTAILS D'EXCEPTION NE SORTENT PAS, MÊME EN DÉVELOPPEMENT.
            options.EnableDetailedErrors = false;
        });

        return builder;
    }

    /// <summary>
    /// Publie un service gRPC interne, explicitement hors du pipeline
    /// d'autorisation HTTP.
    /// </summary>
    public static GrpcServiceEndpointConventionBuilder MapInternalGrpcService<TService>(
        this IEndpointRouteBuilder app)
        where TService : class
    {
        var route = app.MapGrpcService<TService>();
        route.AllowAnonymous();
        return route;
    }
}
