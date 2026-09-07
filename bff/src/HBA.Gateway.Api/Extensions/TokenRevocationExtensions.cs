using HBA.Gateway.Api.Middlewares;
using HBA.Gateway.Api.Options;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;

using HBA.Gateway.Infrastructure.Grpc;
namespace HBA.Gateway.Api.Extensions;

/// <summary>Câblage du contrôle de révocation (ISSUE-022, décision D27).</summary>
public static class TokenRevocationExtensions
{
    /// <param name="estDeveloppement">TRANSMIS PLUTÔT QUE DEVINÉ.</param>
    public static IServiceCollection AddGatewayTokenRevocation(
        this IServiceCollection services, IConfiguration configuration, bool estDeveloppement)
    {
        IdentiteInterne.RefuserLeModeNonSigneHorsDeveloppement(
            configuration.GetValue<bool>(
                $"{InternalCallOptions.SectionName}:{nameof(InternalCallOptions.IdentitesNonSignees)}"),
            estDeveloppement);

        // LA CLÉ PRIVÉE EST LUE ICI, ET NON AU PREMIER APPEL gRPC.
        IdentiteInterne.RefuserUneClePriveeIllisible(
            configuration[$"{InternalCallOptions.SectionName}:{nameof(InternalCallOptions.PrivateKey)}"]);

        services.Configure<TokenRevocationOptions>(
            configuration.GetSection(TokenRevocationOptions.SectionName));

        // ENREGISTRÉ MÊME QUAND LE CONTRÔLE EST DÉSACTIVÉ, ET C'EST VOLONTAIRE.
        services.AddMemoryCache();

        // LA PASSERELLE N'APPELLE PAS `AddHbaService` : CE QUE LE SOCLE POSE
        // AILLEURS, IL FAUT LE POSER ICI.
        services.Configure<InternalCallOptions>(
            configuration.GetSection(InternalCallOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddSingleton<InternalCallClientInterceptor>();

        // MANQUANT DEPUIS LE LOT 8.8, ET L'ERREUR N'ARRIVAIT QU'À L'EXÉCUTION.
        services.AddSingleton<DisjoncteurClientInterceptor>();

        // MÊME MOTIF QUE LES DEUX INTERCEPTEURS CI-DESSUS : `AddHbaGrpc` le fait
        // pour les vingt-trois services, la passerelle ne l'appelle pas.
        services.AjouterLesEcheancesGrpc(configuration);

        // LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
        services.AjouterClientsGrpcGateway(configuration);

        return services;
    }

    /// <summary>À PLACER APRÈS `UseAuthentication` ET APRÈS `UseRateLimiter`.</summary>
    public static WebApplication UseGatewayTokenRevocation(this WebApplication app)
    {
        app.UseMiddleware<TokenRevocationMiddleware>();

        return app;
    }
}
