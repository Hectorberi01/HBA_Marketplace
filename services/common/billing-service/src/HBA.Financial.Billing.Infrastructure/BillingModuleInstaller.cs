using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Configuration;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Financial.Billing.Application.Abstractions;
using HBA.Financial.Billing.Application.Commissions;
using HBA.Financial.Billing.Contracts;
using HBA.Financial.Billing.Domain.Commissions;
using HBA.Financial.Billing.Domain.Invoices;
using HBA.Financial.Billing.Infrastructure.Persistence;
using HBA.Financial.Billing.Infrastructure.Public;

namespace HBA.Financial.Billing.Infrastructure;

/// <summary>Enregistre le module Billing : DbContext, repositories, API commission, validators, outbox.</summary>
public sealed class BillingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Billing";

    public Assembly ApplicationAssembly => typeof(CreateCommissionRuleCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // « Billing:DefaultCommissionRate » N'EXISTE PLUS.
        //
        // Ce taux servait de repli quand aucune règle de commission ne
        // s'applique — c'est-à-dire, en pratique, toujours. Il était lu ici,
        // sans validation, pendant que Products et Wallet en lisaient deux
        // autres ailleurs : trois définitions du même chiffre, dans deux unités.
        //
        // PlatformPricing est désormais la seule. La présence de l'ancienne clé
        // fait échouer le démarrage plutôt que d'être ignorée en silence.
        //
        // CE REPLI N'EST PLUS UN REPLI DE COIN : C'EST LE TAUX COURANT.
        //
        // `AccrueEarningsOnOrderConfirmedHandler` interroge désormais
        // `ICommissionModuleApi` pour chaque ligne de marchandise. Faute de règle
        // — le cas ordinaire —, c'est cette valeur qui est prélevée. Elle et
        // `PricingOptions.PlatformCommissionRate` sortent du MÊME barème, deux
        // lignes plus bas et dans WalletModuleInstaller : elles ne peuvent pas
        // diverger, et il n'y a plus qu'une clé à éditer pour les changer.
        var bareme = new PlatformPricing(configuration);

        var billingOptions = new BillingOptions
        {
            DefaultCommissionRate = bareme.CommissionRate
        };

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

        services.AddOutboxProcessor<BillingDbContext>();
    }
}
