using HBA.Shared.Infrastructure.Hosting;
using HBA.Marketplace.ReturnRefund.Infrastructure.Messaging.Kafka.Configuration;
using System.Reflection;
using FluentValidation;
using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Application.Commands.CreateReturn;
using HBA.Marketplace.ReturnRefund.Application.Events;
using HBA.Marketplace.ReturnRefund.Domain.Events;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using HBA.Marketplace.ReturnRefund.Infrastructure.BackgroundJobs;
using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.DeliveryClient;
using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.InventoryClient;
using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.MediaClient;
using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.OrderClient;
using HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.PaymentClient;
using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence;
using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence.Repositories;
using HBA.Marketplace.ReturnRefund.Infrastructure.Redis;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Modularity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.Marketplace.ReturnRefund.Infrastructure.Caching.Redis;
using HBA.Marketplace.ReturnRefund.Infrastructure.Observability;
using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence.Outbox;
namespace HBA.Marketplace.ReturnRefund.Infrastructure;

public sealed class ReturnRefundModuleInstaller : IModuleInstaller
{
    public string ModuleName => "ReturnRefund";

    public Assembly ApplicationAssembly => typeof(CreateReturnCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheMarketplaceReturnRefund(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteMarketplaceReturnRefund(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaine de connexion Default absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieMarketplaceReturnRefund()`, que le composition root
        // peut oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<ReturnRefundDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ReturnRefundDbContext.SchemaName)));

        services.AddScoped<IReturnRefundUnitOfWork>(sp => sp.GetRequiredService<ReturnRefundDbContext>());
        services.AddScoped<IReturnRequestRepository, ReturnRequestRepository>();
        services.AddScoped<IReturnPolicyRepository, ReturnPolicyRepository>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ReturnPolicyCache>();

        services.AddScoped<IOrderGrpcClient, OrderGrpcClient>();
        services.AddScoped<IPaymentGrpcClient, PaymentGrpcClient>();
        services.AddScoped<IInventoryGrpcClient, InventoryGrpcClient>();
        services.AddScoped<IDeliveryGrpcClient, DeliveryGrpcClient>();
        services.AddScoped<IMediaGrpcClient, MediaValidationClient>();

        // TROIS DE CES CINQ ADAPTATEURS NE PARLENT À PERSONNE.
        GuardSimulatedGrpcAdapters(configuration);

        // LES DEUX TRAVAILLEURS SONT CE QUI FAIT AVANCER LE MODULE.
        services.AddHostedService<ExpireReturnsWorker>();
        services.AddHostedService<RefundRetryWorker>();

        // `OutboxPublisherWorker` A ÉTÉ SUPPRIMÉ, PAS IMPLÉMENTÉ.

        // SANS CES DEUX LIGNES, LES ÉVÉNEMENTS DU MODULE NE SORTENT PAS.
        services.AddScoped<IDomainEventHandler<RefundRequestedDomainEvent>, RefundRequestedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<RefundSucceededDomainEvent>, RefundSucceededDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);
    }

    /// <summary>
    /// Refuse le démarrage en production tant qu'un adaptateur gRPC de ce module
    /// simule sa réponse ; l'annonce bruyamment partout ailleurs.
    /// </summary>
    private static void GuardSimulatedGrpcAdapters(IConfiguration configuration)
    {
        // (adaptateur, conséquence métier) — la conséquence, pas le symptôme
        // technique : c'est elle qui permet à qui lit le message de décider.
        var simulated = new (string Adapter, string Consequence)[]
        {
            ("InventoryGrpcClient.ProcessReturnedStockAsync",
             "la marchandise retournée n'est JAMAIS remise en stock, alors que le retour est clos « remettable en rayon »"),
            ("DeliveryGrpcClient.CreateReturnDeliveryAsync",
             "AUCUNE course d'enlèvement n'est créée ; le numéro rendu au client ne correspond à rien"),
            // MediaGrpcClient.ValidateMediaAsync A ÉTÉ ÉCRIT LE 29 AOÛT 2026.
        };

        var details = string.Join(
            Environment.NewLine,
            simulated.Select(adapter => $"  • {adapter.Adapter} — {adapter.Consequence}."));

        if (IsProduction(configuration))
        {
            throw new InvalidOperationException(
                "PRODUCTION AVEC DES ADAPTATEURS gRPC SIMULÉS — DÉMARRAGE REFUSÉ." + Environment.NewLine
                + Environment.NewLine
                + "Les adaptateurs suivants du module ReturnRefund ne contactent AUCUN serveur : ils"
                + " fabriquent leur réponse et rendent un succès." + Environment.NewLine
                + details + Environment.NewLine
                + Environment.NewLine
                + "Le refus est DÉLIBÉRÉ. Sans lui, le service démarrerait normalement, traiterait des"
                + " retours, et produirait des effets métier qui n'existent pas — sans une seule erreur,"
                + " sans une seule ligne de journal, et sans aucun moyen de reconstituer après coup ce"
                + " qui aurait dû se passer." + Environment.NewLine
                + Environment.NewLine
                + "Pour lever ce refus : implémenter réellement ces appels (clients gRPC générés à"
                + " partir des .proto d'inventory-service, delivery-service et media-service), puis"
                + " retirer leur ligne de GuardSimulatedGrpcAdapters.");
        }

        // Bruyant, et volontairement.
        Console.WriteLine(
            "[ReturnRefund]  ADAPTATEURS gRPC SIMULÉS ACTIFS :" + Environment.NewLine
            + details + Environment.NewLine
            + "Le parcours de retour se déroule intégralement en développement, mais ces trois effets"
            + " n'ont AUCUNE contrepartie réelle. Le démarrage est refusé en production.");
    }

    /// <summary>Sommes-nous en production ?</summary>
    private static bool IsProduction(IConfiguration configuration)
    {
        // DÉLÉGUÉ À `EnvironnementDeploiement`, ET C'EST LA CORRECTION.
        return EnvironnementDeploiement.EstProduction(configuration);
    }
}

internal sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
