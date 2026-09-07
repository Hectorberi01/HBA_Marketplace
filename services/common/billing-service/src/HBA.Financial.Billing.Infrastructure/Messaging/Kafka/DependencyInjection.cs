using HBA.Financial.Billing.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Financial.Billing.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Financial.Billing.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE CE SERVICE — UN SEUL POINT D'ENTREE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieFinancialBilling(this IServiceCollection services)
    {
        services.AjouterSujetsFinancialBilling();
        services.AjouterOutboxFinancialBilling();

        return services;
    }
}
