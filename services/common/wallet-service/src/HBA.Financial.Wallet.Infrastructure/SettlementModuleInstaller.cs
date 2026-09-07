using System.Reflection;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Configuration;
using HBA.Shared.IntegrationEvents;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Application.Batches;
using HBA.Financial.Wallet.Application.Batches.EventHandlers;
using HBA.Financial.Wallet.Application.Earnings;
using HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Financial.Wallet.Application.Pricing;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Financial.Wallet.Domain.Batches;
using HBA.Financial.Wallet.Domain.Batches.Events;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Financial.Wallet.Domain.Wallets;
using HBA.Financial.Wallet.Infrastructure.Persistence;

using HBA.Financial.Wallet.Infrastructure.Caching.Redis;
using HBA.Financial.Wallet.Infrastructure.Observability;
namespace HBA.Financial.Wallet.Infrastructure;

/// <summary>
/// Enregistre le module Settlement : DbContext, repositories, accrual consumer,
/// validators, outbox.
/// </summary>
public sealed class WalletModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Settlement";

    public Assembly ApplicationAssembly => typeof(RunSettlementCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheFinancialWallet(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteFinancialWallet(configuration);

        // LE BARÈME VIENT DE LA SOURCE UNIQUE, PLUS D'UNE LECTURE LOCALE.
        var bareme = new PlatformPricing(configuration);

        var pricingOptions = new PricingOptions
        {
            PlatformCommissionRate = bareme.CommissionRate,
            ProviderFeeRate = bareme.ProviderFeeRate,
            FoodCommissionRate = bareme.FoodCommissionRate
        };

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc hors de
        // cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieFinancialWallet()`, que le composition root peut
        // oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddSingleton(pricingOptions);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<WalletDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", WalletDbContext.SchemaName)));

        services.AddScoped<IWalletUnitOfWork>(sp => sp.GetRequiredService<WalletDbContext>());

        // SANS CETTE LIGNE, UN REJEU KAFKA CRÉDITE DEUX FOIS.

        services.AddScoped<ISellerEarningRepository, SellerEarningRepository>();
        services.AddScoped<ISettlementBatchRepository, SettlementBatchRepository>();

        // Portefeuilles (wallet) : repositories + service de mutation partagé.
        services.AddScoped<ISellerWalletRepository, SellerWalletRepository>();
        services.AddScoped<IDriverWalletRepository, DriverWalletRepository>();
        services.AddScoped<IPlatformWalletRepository, PlatformWalletRepository>();
        services.AddScoped<IWithdrawalRepository, WithdrawalRepository>();
        services.AddScoped<ICustomerRefundRepository, CustomerRefundRepository>();
        services.AddScoped<IWalletTransactionRepository, WalletTransactionRepository>();

        // SANS CES DEUX LIGNES, LE CLIENT N'EST JAMAIS REMBOURSÉ (D33).
        services.AddScoped<ICustomerWalletRepository, CustomerWalletRepository>();
        services.AddScoped<ICustomerWithdrawalRepository, CustomerWithdrawalRepository>();

        services.AddScoped<WalletMutations>();

        // SANS CETTE LIGNE, PAYMENT-SERVICE NE SAIT PAS OÙ RENDRE L'ARGENT.
        services.AddScoped<HBA.Financial.Wallet.Contracts.ICustomerWalletApi, Public.CustomerWalletApi>();

        // SANS CE SERVICE, UN GAIN RETIRÉ EST RE-VERSÉ PAR LE PROCHAIN LOT.
        services.AddScoped<SellerEarningImputation>();

        // Règle de clôture d'un retrait (sent → payé, failed → remboursé), partagée
        // par le webhook et la réconciliation : une seule et même logique, donc pas
        // de risque qu'un double verdict produise un double remboursement.
        services.AddScoped<WithdrawalSettlement>();

        services.AddScoped<IDomainEventHandler<PayoutPaidDomainEvent>, PayoutPaidDomainEventHandler>();

        // Réconciliation des retraits « en cours » avec le statut réel du PSP. SANS
        // ce service, un versement échoué chez FedaPay laisserait le retrait marqué
        // « payé » et le vendeur débité, sans jamais avoir reçu son argent.
        services.AddHostedService<Reconciliation.WithdrawalReconciliationService>();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }
}
