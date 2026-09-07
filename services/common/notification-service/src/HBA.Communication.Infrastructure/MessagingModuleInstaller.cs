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
using HBA.Communication.Infrastructure.Caching.Redis;
using HBA.Communication.Infrastructure.Observability;
using HBA.Communication.Infrastructure.Persistence.Outbox;
namespace HBA.Communication.Infrastructure;

/// <summary>
/// Enregistre le module Messaging : DbContext, repository, handlers, validators,
/// outbox.
/// </summary>
public sealed class MessagingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Messaging";

    public Assembly ApplicationAssembly => typeof(StartConversationCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheCommunication(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
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
        services.AddHostedService<GardeDeCablage>();
    }
}
