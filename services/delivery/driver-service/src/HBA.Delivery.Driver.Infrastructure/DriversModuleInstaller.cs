using System.Reflection;
using HBA.Delivery.Driver.Domain.Events;
using HBA.Delivery.Driver.Domain.Repositories;
using HBA.Drivers.Application.Abstractions;
using HBA.Drivers.Application.Accounts.Commands;
using HBA.Drivers.Application.Accounts.Events;
using HBA.Drivers.Infrastructure.Persistence;
using HBA.Drivers.Infrastructure.Persistence.Repositories;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Drivers.Infrastructure;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'INSTALLEUR DU MODULE Drivers — IL REMPLACE `DriversInfrastructureModule`.
///
/// L'ancien enregistrait TROIS choses : un `DriverStore` en singleton, une file
/// d'événements et un publieur qui écrivait dedans. Il portait aussi un encadré
/// disant pourquoi ISSUE-007 ne pouvait pas y être corrigée — « il n'y a rien à
/// quoi brancher un processeur d'outbox » — et désignait le lot qui devrait le
/// faire. C'est celui-ci.
///
/// CE QUI CHANGE POUR LES APPELANTS : ce service passe de `AddHbaSecurity` à
/// `AddHbaService&lt;DriverDbContext&gt;`. Il gagne donc la sonde `/health/ready`
/// branchée sur la base, la validation MediatR, le pipeline d'observabilité et
/// la documentation OpenAPI de sa surface — tout ce qu'un service sans base ne
/// pouvait pas avoir.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DriversModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Drivers";

    public Assembly ApplicationAssembly => typeof(RegisterDriverCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<DriverDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DriverDbContext.SchemaName)));

        services.AddScoped<IDriverAccountRepository, DriverAccountRepository>();
        services.AddScoped<IDriverUnitOfWork, DriverUnitOfWork>();

        // NI `IntegrationEventQueue` NI `IIntegrationEventPublisher` NE SONT
        // ENREGISTRÉS ICI, ET C'EST VOLONTAIRE.
        //
        // `AddBuildingBlocksInfrastructure`, appelé par `AddHbaService`, les pose
        // déjà — le publieur EST la file, que `ModuleDbContext` draine vers
        // l'outbox. Les redéclarer donnerait deux inscriptions identiques dont la
        // dernière gagne : sans effet aujourd'hui, mais c'est exactement la forme
        // qui masque une divergence le jour où l'une des deux change.
        // (`DeliveryPricingInfrastructureModule` les déclare, lui, parce qu'il
        // passe par le socle PARTIEL `AddHbaSecurity`, qui ne les pose pas.)

        // ═════════════════════════════════════════════════════════════════════
        // SANS CES QUATRE LIGNES, LES ÉVÉNEMENTS DU MODULE NE SORTENT PAS.
        //
        // `DomainEventDispatcher` résout ses gestionnaires par le conteneur : un
        // gestionnaire non enregistré n'est pas une erreur de démarrage, c'est un
        // SILENCE. C'est exactement la nature du défaut qu'on referme ici — rien
        // ne signalait que les événements de ce service disparaissaient.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IDomainEventHandler<DriverAccountRegisteredDomainEvent>, DriverRegisteredDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DriverAccountVerifiedDomainEvent>, DriverVerifiedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DriverAccountSuspendedDomainEvent>, DriverSuspendedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DriverVehicleDeclaredDomainEvent>, DriverVehicleDeclaredDomainEventHandler>();

        // ═════════════════════════════════════════════════════════════════════
        // LA LIGNE QUE LE LOT 5.4 AVAIT ANNONCÉE ICI — ISSUE-007, CRITICAL.
        //
        // Elle ne pouvait pas être écrite tant que ce service n'avait pas de
        // `DbContext` : `AddOutboxProcessor<TContext>()` exige
        // `where TContext : DbContext, IOutboxDbContext`. Elle l'a maintenant, et
        // avec elle la table `drivers.outbox_messages` que la migration initiale
        // crée.
        //
        // CE QUE CETTE LIGNE RÉPARE EXACTEMENT. Avant, `PublishAsync` ajoutait
        // l'événement à une `List<>` scopée que PERSONNE ne drainait, et rendait
        // `Task.CompletedTask` : l'appelant voyait un succès, la requête se
        // terminait, la liste était collectée. La perte était TOTALE et
        // SYSTÉMATIQUE — aucun message n'est jamais parti, pas même une fois.
        //
        // CE QU'ELLE NE RÉPARE PAS : les quatre autres services de la livraison
        // (dispatch, route, tracking, preuve) restent des maquettes en mémoire,
        // sans base ni outbox. Ce lot ne les touche pas — décider de leur schéma
        // demande de décider de leur métier, ce qui n'est pas corriger un défaut.
        // ═════════════════════════════════════════════════════════════════════
        services.AddOutboxProcessor<DriverDbContext>();
    }
}
