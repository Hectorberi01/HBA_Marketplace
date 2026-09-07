using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Engagement.Wishlist.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>LES SUJETS QUE CE SERVICE ECOUTE.</summary>
public static class SujetsEngagementWishlist
{
    private static readonly string[] Sujets =
    [

    ];

    internal static IServiceCollection AjouterSujetsEngagementWishlist(this IServiceCollection services)
    {
        services.AddSingleton(new AbonnementsKafka(Sujets));
        return services;
    }
}
