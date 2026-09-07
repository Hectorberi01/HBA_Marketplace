using HBA.Engagement.Wishlist.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

using HBA.Engagement.Wishlist.Infrastructure.Persistence.Outbox;
using HBA.Engagement.Wishlist.Infrastructure.Messaging.Kafka.Retry;
using HBA.Engagement.Wishlist.Infrastructure.Messaging.Kafka.Processors;
namespace HBA.Engagement.Wishlist.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>L'OUTBOX DE CE SERVICE — LE CABLAGE ICI, LE TYPE ET LA TABLE AILLEURS.</summary>
public static class OutboxEngagementWishlist
{
    internal static IServiceCollection AjouterOutboxEngagementWishlist(this IServiceCollection services)
    {
        services.AjouterLOutboxLocale();
        return services;
    }
}
