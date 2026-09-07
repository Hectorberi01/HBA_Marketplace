using System.Reflection;
using HBA.Financial.Billing.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Configuration;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Financial.Billing.Application.Abstractions;
using HBA.Financial.Billing.Application.Commissions;
using HBA.Financial.Billing.Contracts;
using HBA.Financial.Billing.Domain.Commissions;
using HBA.Financial.Billing.Domain.Invoices;
using HBA.Financial.Billing.Infrastructure.Persistence;
using HBA.Financial.Billing.Infrastructure.Public;

using HBA.Financial.Billing.Infrastructure.Caching.Redis;
using HBA.Financial.Billing.Infrastructure.Observability;
namespace HBA.Financial.Billing.Infrastructure;

/// <summary>
/// Enregistre le module Billing : DbContext, repositories, API commission,
/// validators, outbox.
/// </summary>
public sealed class BillingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Billing";

    public Assembly ApplicationAssembly => typeof(CreateCommissionRuleCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheFinancialBilling(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteFinancialBilling(configuration);

        // « Billing:DefaultCommissionRate » N'EXISTE PLUS.
        var bareme = new PlatformPricing(configuration);

        var billingOptions = new BillingOptions
        {
            DefaultCommissionRate = bareme.CommissionRate
        };

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieFinancialBilling()`, que le composition root peut
        // oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddSingleton(billingOptions);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<BillingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BillingDbContext.SchemaName)));

        services.AddScoped<IBillingUnitOfWork>(sp => sp.GetRequiredService<BillingDbContext>());

        services.AddScoped<ICommissionRuleRepository, CommissionRuleRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<ICommissionModuleApi, CommissionModuleApi>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }
}
