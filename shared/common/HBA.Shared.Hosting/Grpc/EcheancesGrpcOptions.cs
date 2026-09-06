using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>
/// Échéance des appels gRPC sortants : un défaut central, des surcharges par
/// service appelé.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI EXISTAIT DÉJÀ, ET QU'IL NE FAUT PAS DÉFAIRE.
///
/// `InternalCallClientInterceptor` posait une échéance de CINQ SECONDES sur tout
/// appel qui n'en portait pas. Elle est posée là plutôt qu'aux 540 sites d'appel
/// pour une raison qui reste vraie : un seul oubli suffirait à rouvrir le trou,
/// et un appel gRPC sans échéance attend INDÉFINIMENT — un canal gRPC n'a pas de
/// délai par défaut, contrairement à `HttpClient`.
///
/// LE DÉFAUT RESTE DONC CENTRAL. Ce fichier ne le déplace pas ; il lui donne un
/// point de réglage.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CE QUI MANQUAIT.
///
/// Le commentaire d'origine le disait lui-même : « 5 secondes correspond au
/// TotalTimeout déjà retenu pour les clients HTTP de la passerelle. VALEUR DE
/// DÉPART, À AJUSTER SUR DES MESURES. » Il n'existait aucun endroit où l'ajuster.
///
/// Or cinq secondes ne veulent pas dire la même chose partout :
///
///   • un devis de livraison demandé pendant que l'acheteur attend la page de
///     paiement — cinq secondes, c'est une commande perdue ; il vaut mieux
///     échouer vite et proposer un repli ;
///   • un appel à media-service pendant l'import d'un catalogue — cinq secondes
///     peuvent être trop courtes, et l'échec coûte plus cher que l'attente.
///
/// C'est un arbitrage du service APPELANT, sur l'appel qu'il fait. Il descend donc
/// dans le module gRPC de ce service (`Grpc/Configuration/`), pendant que le
/// défaut, lui, reste ici.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// LA CLÉ EST LE NOM COURT DU SERVICE PROTO, ET C'EST DÉLIBÉRÉ.
///
/// `context.Method.ServiceName` rend le nom complet — « hba.merchant.v1.MerchantApi ».
/// La clé retenue est le dernier segment, « MerchantApi », pour une raison
/// pratique : une clé contenant des points ne peut pas s'écrire en variable
/// d'environnement sous bash (`Grpc__Echeances__ParService__hba.merchant.v1...`
/// n'est pas un nom de variable assignable). Le nom court est unique dans ce
/// dépôt — vérifié sur les 22 protos.
///
/// CE QUE ÇA NE COUVRE PAS. Deux protos qui nommeraient un jour leur service de
/// la même façon partageraient la même surcharge, en silence. Le jour où cela
/// arrive, la clé doit passer au nom complet et ce commentaire devient faux :
/// c'est le genre de bascule qui mérite un contrôle, pas une relecture.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class EcheancesGrpcOptions
{
    public const string SectionName = "Grpc:Echeances";

    /// <summary>
    /// Appliquée à tout appel sortant qui ne porte pas déjà une échéance et dont
    /// le service appelé n'est pas dans <see cref="ParService"/>.
    /// </summary>
    public TimeSpan Defaut { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Surcharges, par nom COURT de service proto (« MerchantApi », « MediaApi »).
    /// </summary>
    public Dictionary<string, TimeSpan> ParService { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Échéance à appliquer pour un <c>context.Method.ServiceName</c> donné.
    /// </summary>
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
        //
        // Une valeur nulle ou négative — « 00:00:00 » posée par erreur dans un
        // env — ferait expirer CHAQUE appel avant même de partir, et la panne
        // ressemblerait à un service injoignable. On retombe sur le défaut, qui
        // est sûr, plutôt que de faire confiance à une valeur qui ne peut pas
        // avoir été voulue.
        return ParService.TryGetValue(nomCourt, out var surcharge) && surcharge > TimeSpan.Zero
            ? surcharge
            : Defaut;
    }
}

/// <summary>
/// Câblage des échéances gRPC.
/// </summary>
public static class EcheancesGrpcExtensions
{
    /// <summary>
    /// Lit la section « Grpc:Echeances ». Appelé par <c>AddHbaGrpc</c>.
    /// </summary>
    public static IServiceCollection AjouterLesEcheancesGrpc(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EcheancesGrpcOptions>(
            configuration.GetSection(EcheancesGrpcOptions.SectionName));

        return services;
    }

    /// <summary>
    /// Surcharge, en code, l'échéance d'un service appelé. C'est le point d'entrée
    /// du module gRPC de chaque service : <c>Grpc/Configuration/</c>.
    /// </summary>
    /// <remarks>
    /// `PostConfigure` ET NON `Configure` : la surcharge écrite dans le module
    /// doit s'appliquer APRÈS la lecture de la configuration, sans quoi une
    /// section « Grpc:Echeances » vide — le cas courant — remplacerait le
    /// dictionnaire par un dictionnaire neuf et effacerait ce que le module vient
    /// de poser.
    ///
    /// CE QUE ÇA NE COUVRE PAS : la configuration ne peut plus reprendre la main
    /// sur une surcharge écrite en code. C'est le compromis assumé — une valeur
    /// choisie pour une raison métier ne doit pas se laisser défaire par un env
    /// posé « le temps d'un test ». Le jour où l'inverse est voulu, il faudra un
    /// second niveau, pas un renversement de celui-ci.
    /// </remarks>
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
