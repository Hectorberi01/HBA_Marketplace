using System.Reflection;
using HBA.Communication.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Communication.Application.Abstractions;
using HBA.Communication.Application.Conversations;
using HBA.Communication.Application.Conversations.EventHandlers;
using HBA.Communication.Domain.Conversations;
using HBA.Communication.Domain.Conversations.Events;
using HBA.Communication.Contracts;
using HBA.Communication.Infrastructure.Public;
using HBA.Communication.Infrastructure.Persistence;
using HBA.Shared.Infrastructure.Outbox;

using HBA.Communication.Infrastructure.Caching.Redis;
using HBA.Communication.Infrastructure.Observability;
namespace HBA.Communication.Infrastructure;

/// <summary>Enregistre le module Messaging : DbContext, repository, handlers, validators, outbox.</summary>
public sealed class MessagingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Messaging";

    public Assembly ApplicationAssembly => typeof(StartConversationCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/). Il etait branche par le
        // socle pour les vingt-six services a la fois ; il l'est desormais ici.
        services.AjouterCacheCommunication(configuration);

        // LES SONDES DE CE SERVICE (Observability/). Jusqu'ici seule la base
        // etait verifiee : un service dont le consommateur Kafka etait mort
        // repondait « ready », et le deploiement individuel le croyait sain.
        services.AjouterObservabiliteCommunication(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<MessagingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MessagingDbContext.SchemaName)));

        services.AddScoped<IMessagingUnitOfWork>(sp => sp.GetRequiredService<MessagingDbContext>());

        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessagingModuleApi, MessagingModuleApi>();

        services.AddScoped<IDomainEventHandler<MessageSentDomainEvent>, MessageSentDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        // L'EXCEPTION EST FERMEE : CE MODULE A DESORMAIS LE SIEN.
        //
        // `AddOutboxProcessor` est descendu dans `Messaging/Kafka/Outbox/`, comme
        // dans les vingt-quatre autres services. Il n'est donc plus enregistre par
        // cet installeur — que le composition root appelle toujours — mais par
        // `AjouterMessagerieCommunication()`, qu'il peut oublier. Un oubli ne
        // casserait rien de visible : les messages s'ecriraient dans l'outbox et
        // n'en sortiraient jamais. La garde ci-dessous, elle, est enregistree ici :
        // elle doit exister quand ce qu'elle verifie est absent.
        services.AddHostedService<GardeDeCablage>();
    }
}
