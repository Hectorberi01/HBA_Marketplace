using Google.Protobuf.WellKnownTypes;
using HBA.Commerce.Infrastructure.Grpc.Mappers;
using HBA.Promotion.Grpc.V1;

using HBA.Promotions.Contracts;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.Promotions.Contracts.Grpc` (lot D — dissolution des assemblages de contrats).
//
// `shared/` ne contient plus que les `.proto`. Ce service compile lui-meme le
// contrat dont il a besoin, et porte donc sa propre traduction.
//
// LES TYPES GENERES SONT `internal` A CET ASSEMBLAGE. Deux services qui
// compilent le meme proto obtiennent deux types CLR distincts ; les rendre
// publics ferait, dans un hote compose, deux types publics du meme nom complet —
// CS0433, a l'usage, loin de la cause. Les adaptateurs et mappings sont donc
// `internal` eux aussi : un type public dont la signature expose un type interne
// ne compile pas.
//
// CE QUE ÇA COUTE : cette traduction existe en 3 exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Commerce.Infrastructure.Grpc.Clients;

/// <summary>
/// Côté CLIENT : implémente <see cref="IPromotionModuleApi"/> par gRPC.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE CODE APPELANT NE CHANGE PAS.
///
/// cart-service, order-service et food-order-service écrivent
/// `_promotions.ReserveAsync(...)` sans savoir si l'implémentation est en
/// processus ou au bout d'un socket. C'est ce qui rend l'extraction réversible :
/// rebrancher l'implémentation locale se fait par une ligne d'enregistrement DI.
///
/// AUCUNE `RpcException` N'EST AVALÉE ICI.
///
/// La tentation est de l'attraper et de rendre « coupon invalide ». L'appelant ne
/// distinguerait alors plus « ce code ne s'applique pas » de « promotion-service
/// est à terre » — et le checkout retirerait au client une remise à laquelle il a
/// droit, sans laisser de trace. Un refus métier arrive déjà par une RÉPONSE
/// (`valid: false`) ; ce qui remonte en exception est une vraie panne, et doit
/// remonter.
///
/// La politique de résilience — délai, reprise, disjoncteur — se pose à
/// l'enregistrement du client, pas ici.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class PromotionGrpcClient : IPromotionModuleApi
{
    private readonly PromotionApi.PromotionApiClient _client;

    public PromotionGrpcClient(PromotionApi.PromotionApiClient client) => _client = client;

    public async Task<PromotionEvaluationResult> EvaluateAsync(
        string code, PromotionEvaluationContext context, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.EvaluatePromotionAsync(
            new EvaluatePromotionRequest { Code = code ?? string.Empty, Context = context.ToProto() },
            cancellationToken: cancellationToken);

        return reponse.ToContract();
    }

    public async Task<CouponReservationResult?> ReserveAsync(
        string code, Guid userId, Guid cartId, PromotionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        var reponse = await _client.ReserveCouponAsync(
            new ReserveCouponRequest
            {
                Code = code ?? string.Empty,
                UserId = userId.ToString(),
                CartId = cartId.ToString(),
                Context = context.ToProto()
            },
            cancellationToken: cancellationToken);

        return reponse.ToContract();
    }

    public async Task<bool> CommitAsync(
        Guid reservationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.CommitCouponAsync(
            new CommitCouponRequest
            {
                ReservationId = reservationId.ToString(),
                OrderId = orderId.ToString()
            },
            cancellationToken: cancellationToken);

        return reponse.Committed;
    }

    public async Task<bool> ReleaseAsync(
        Guid reservationId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.ReleaseCouponAsync(
            new ReleaseCouponRequest { ReservationId = reservationId.ToString() },
            cancellationToken: cancellationToken);

        return reponse.Released;
    }
}

internal static class PromotionGrpcRegistration
{
    /// <summary>
    /// Branche <see cref="IPromotionModuleApi"/> sur promotion-service, en gRPC.
    /// </summary>
    /// <remarks>
    /// L'ADRESSE VIENT DE `Services:Promotion`, COMME POUR LA PASSERELLE.
    ///
    /// Une seconde clé de configuration pour la même destination finirait par
    /// diverger : le proxy et les services de commande taperaient sur deux
    /// instances différentes, avec des budgets distincts selon le chemin emprunté.
    /// Seul le PORT change — gRPC écoute ailleurs que REST, faute de TLS pour
    /// négocier le protocole.
    ///
    /// CETTE MÉTHODE LÈVE SI L'ADRESSE MANQUE, ET C'EST VOULU.
    ///
    /// Un service de commande démarré sans savoir joindre promotion refuserait
    /// silencieusement tous les coupons — les clients paieraient le plein tarif
    /// sans que rien ne le signale. Mieux vaut ne pas démarrer.
    /// </remarks>
    public static IServiceCollection AddPromotionGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Promotion"]
            ?? throw new InvalidOperationException(
                "Services:Promotion est absent — impossible de joindre promotion-service.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services
            .AddGrpcClient<PromotionApi.PromotionApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<IPromotionModuleApi, PromotionGrpcClient>();

        return services;
    }
}
