using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// LES SUJETS QUE CE SERVICE ECOUTE.
///
/// Ils sont deduits des 3 evenement(s) declares dans `Consumers/` et du service
/// qui les publie — le sujet porte le domaine du PRODUCTEUR, jamais celui du
/// consommateur (voir `HbaTopics`).
///
///   OrderConfirmed    order-service    -> service.order.v1
///   SellerRegistered  seller-service   -> service.merchant.v1   (« merchant », pas « seller »)
///   UserRegistered    identity-service -> service.identity.v1
///
/// LA DEUXIEME LIGNE EST CELLE QU'ON SE TROMPE. Le dossier s'appelle
/// `seller-service`, l'espace de noms `HBA.Merchants.*`, et le sujet porte le
/// DOMAINE : `merchant`. S'abonner a `service.seller.v1` ne produirait aucune
/// erreur — seulement une courbe d'inscriptions vendeur plate a zero.
///
/// SANS CETTE LISTE, `SubscribeTopics` reste vide et le consommateur partage se
/// rabat sur les vingt sujets de la plateforme : le service desserialise tout et
/// jette presque tout.
///
/// UN GESTIONNAIRE DANS `Consumers/` DONT LE SUJET MANQUE ICI NE SERA JAMAIS
/// APPELE, en silence. Aucun compilateur ne relie les deux.
/// </summary>
public static class SujetsAnalytics
{
    private static readonly string[] Sujets =
    [
        "service.order.v1",
        "service.merchant.v1",
        "service.identity.v1"
    ];

    internal static IServiceCollection AjouterSujetsAnalytics(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
