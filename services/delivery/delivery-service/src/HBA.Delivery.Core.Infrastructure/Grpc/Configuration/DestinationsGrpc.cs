using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Deliveries.Infrastructure.Grpc.Configuration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CE SERVICE APPELLE, ET AVEC QUELLE ÉCHÉANCE.
///
/// Services appelés :
    ///   • DeliveryPricing
///
/// L'ÉCHÉANCE PAR DÉFAUT EST DE CINQ SECONDES, POSÉE CENTRALEMENT par
/// `InternalCallClientInterceptor` sur tout appel qui n'en porte pas. Elle reste
/// centrale parce que c'est elle qui rend l'oubli impossible : un canal gRPC
/// n'a AUCUN délai par défaut, contrairement à `HttpClient`.
///
/// CE FICHIER EST LE POINT DE SURCHARGE, ET IL EST VIDE — VOLONTAIREMENT.
///
/// Cinq secondes ne veulent pas dire la même chose pour un devis demandé pendant
/// qu'un acheteur attend sa page de paiement et pour un import de catalogue. Mais
/// choisir 800 ms plutôt que 5 s demande des MESURES que personne n'a prises :
/// poser des valeurs inventées ici ferait échouer des appels sains et la panne
/// serait imputée au service appelé.
///
/// Quand la mesure existera, la surcharge s'écrit ainsi, et nulle part ailleurs :
///
///     services.SurchargerLEcheanceGrpc("MerchantApi", TimeSpan.FromMilliseconds(800));
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DestinationsGrpc
{
    public static IServiceCollection AjouterLesDestinationsGrpc(this IServiceCollection services)
        => services;
}
