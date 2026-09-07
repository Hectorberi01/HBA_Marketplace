using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>
/// Échéance des appels gRPC sortants : un défaut central, des surcharges par
/// service appelé.
/// </summary>
public sealed class EcheancesGrpcOptions
{
    public const string SectionName = "Grpc:Echeances";

    /// <summary>
    /// Appliquée à tout appel sortant qui ne porte pas déjà une échéance et dont le
    /// service appelé n'est pas dans <see cref="ParService"/> .
    /// </summary>
    public TimeSpan Defaut { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Surcharges, par nom COURT de service proto (« MerchantApi », « MediaApi »).
    /// </summary>
    public Dictionary<string, TimeSpan> ParService { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Échéance à appliquer pour un <c>context.Method.ServiceName</c> donné.</summary>
    public TimeSpan Pour(string nomCompletDuServiceProto)
    {
        if (string.IsNullOrWhiteSpace(nomCompletDuServiceProto))
        {
            return Defaut;
        }

        var dernierPoint = nomCompletDuServiceProto.LastIndexOf('.');

        var nomCourt = dernierPoint >= 0 && dernierPoint < nomCompletDuServiceProto.Length - 1
            ? nomCompletDuServiceProto[(dernierPoint + 1)..]
            : nomCompletDuServiceProto;

        // UNE SURCHARGE ABSURDE EST IGNORÉE PLUTÔT QU'APPLIQUÉE.
        return ParService.TryGetValue(nomCourt, out var surcharge) && surcharge > TimeSpan.Zero
            ? surcharge
            : Defaut;
    }
}

/// <summary>Câblage des échéances gRPC.</summary>
public static class EcheancesGrpcExtensions
{
    /// <summary>Lit la section « Grpc:Echeances ».</summary>
    public static IServiceCollection AjouterLesEcheancesGrpc(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EcheancesGrpcOptions>(
            configuration.GetSection(EcheancesGrpcOptions.SectionName));

        return services;
    }

    /// <summary>Surcharge, en code, l'échéance d'un service appelé.</summary>
    public static IServiceCollection SurchargerLEcheanceGrpc(
        this IServiceCollection services, string nomCourtDuServiceProto, TimeSpan echeance)
    {
        if (echeance <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(echeance),
                echeance,
                "Une échéance gRPC doit être strictement positive : zéro ferait expirer "
                + $"chaque appel à {nomCourtDuServiceProto} avant qu'il ne parte.");
        }

        services.PostConfigure<EcheancesGrpcOptions>(
            options => options.ParService[nomCourtDuServiceProto] = echeance);

        return services;
    }
}
