using HBA.Shared.Infrastructure.Persistence;
using HBA.Deliveries.Infrastructure.Persistence;
using HBA.Deliveries.Infrastructure.Persistence.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using HBA.Shared.Infrastructure.Events;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Retry;
using HBA.Deliveries.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Outbox`.

namespace HBA.Deliveries.Infrastructure.Persistence.Outbox;

/// <summary>Déclare qu'un DbContext porte une table d'outbox.</summary>
public sealed record OutboxContextRegistration(Type DbContextType, string ModuleName);

/// <summary>Enregistrement conditionnel du processeur d'outbox.</summary>
public static class OutboxRegistration
{
    // LE DRAPEAU EST LU AU SOCLE, PAS ICI.
    public static bool Enabled => DrainageDOutbox.Actif;
    public static IServiceCollection AjouterLOutboxLocale(this IServiceCollection services)
    {
        // Politique de réessai PARTAGÉE par les 25 modules : un seul endroit où
        // régler le plafond de tentatives et le backoff, plutôt que vingt-cinq.
        services.TryAddSingleton<OutboxRetryPolicy>();

        // Le registre est peuplé sur TOUS les hôtes, indépendamment d'Enabled : le
        // BFF Admin ne draine pas l'outbox, mais doit pouvoir en lister — et
        // rejouer — les lettres mortes.
        var moduleName = typeof(DeliveriesDbContext).Name.Replace("DbContext", string.Empty);
        services.AddSingleton(new OutboxContextRegistration(typeof(DeliveriesDbContext), moduleName));

        if (Enabled)
        {
            services.AddHostedService<OutboxProcessor>();

            // La purge suit le MÊME interrupteur que le processeur, et pour la même
            // raison : elle n'a rien à faire sur les hôtes qui ne drainent pas.
            services.AddHostedService<OutboxPurger>();
        }

        // LA PURGE DE L'INBOX, QUI N'EXISTAIT NULLE PART.
        services.AddHostedService<InboxCleanupService>();

        return services;
    }
}
