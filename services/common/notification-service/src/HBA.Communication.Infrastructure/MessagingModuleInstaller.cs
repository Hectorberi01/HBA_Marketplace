using System.Reflection;
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

namespace HBA.Communication.Infrastructure;

/// <summary>Enregistre le module Messaging : DbContext, repository, handlers, validators, outbox.</summary>
public sealed class MessagingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Messaging";

    public Assembly ApplicationAssembly => typeof(StartConversationCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
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

        // CE MODULE N'A PAS ETE PORTE SUR `Messaging/Kafka/`, ET C'EST UNE
        //     EXCEPTION ASSUMEE.
        //
        // `HBA.Communication.Api` compose DEUX modules : Notifications, qui a des
        // consommateurs et un module Kafka a lui, et celui-ci — la messagerie
        // interne — qui ne consomme RIEN. Il ne publie qu'un evenement, par son
        // outbox, et n'a donc ni sujet a declarer ni gestionnaire a enregistrer.
        //
        // Lui donner un module complet aurait produit un `Sujets` vide, un
        // `Consumers/` vide et un second `AjouterMessagerie…()` dans le meme
        // Program.cs — trois dossiers pour une seule ligne utile, celle-ci.
        //
        // CE QUE CETTE EXCEPTION COUTE. La regle « l'outbox est cablee par le
        // module Kafka du service » souffre ici une entorse : quelqu'un qui
        // cherche le videur d'outbox de la messagerie interne ne le trouvera pas
        // dans `Messaging/Kafka/Outbox/`, parce qu'il est reste ici.
        services.AddOutboxProcessor<MessagingDbContext>();
    }
}
