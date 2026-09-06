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
using HBA.Shipping.Contracts.IntegrationEvents;
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

/// <summary>Enregistre le module Settlement : DbContext, repositories, accrual consumer, validators, outbox.</summary>
public sealed class WalletModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Settlement";

    public Assembly ApplicationAssembly => typeof(RunSettlementCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/). Il etait branche par le
        // socle pour les vingt-six services a la fois ; il l'est desormais ici.
        services.AjouterCacheFinancialWallet(configuration);

        // LES SONDES DE CE SERVICE (Observability/). Jusqu'ici seule la base
        // etait verifiee : un service dont le consommateur Kafka etait mort
        // repondait « ready », et le deploiement individuel le croyait sain.
        services.AjouterObservabiliteFinancialWallet(configuration);

        // ═══════════════════════════════════════════════════════════════════
        // LE BARÈME VIENT DE LA SOURCE UNIQUE, PLUS D'UNE LECTURE LOCALE.
        //
        // CETTE LECTURE N'AVAIT AUCUNE VALIDATION. Un TryParse silencieux :
        // une valeur illisible retombait sur 10 % sans un mot, et une valeur
        // absurde — « 10 » au lieu de « 0.1 » — était acceptée telle quelle et
        // aurait multiplié par cent la commission prélevée sur chaque vente.
        //
        // PlatformPricing refuse les deux, et lit les MÊMES clés que Products :
        // les deux calculs qui doivent s'inverser l'un l'autre ne peuvent plus
        // diverger.
        // ═══════════════════════════════════════════════════════════════════
        var bareme = new PlatformPricing(configuration);

        var pricingOptions = new PricingOptions
        {
            PlatformCommissionRate = bareme.CommissionRate,
            ProviderFeeRate = bareme.ProviderFeeRate,
            FoodCommissionRate = bareme.FoodCommissionRate
        };

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc
        // hors de cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieFinancialWallet()`, que le composition root peut oublier.
        // Un oubli ne casserait rien de visible — le service demarre et n'emet
        // plus rien. Cette garde, elle, est enregistree ici : elle doit exister
        // quand ce qu'elle verifie est absent.
        services.AddHostedService<GardeDeCablage>();

        services.AddSingleton(pricingOptions);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<WalletDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", WalletDbContext.SchemaName)));

        services.AddScoped<IWalletUnitOfWork>(sp => sp.GetRequiredService<WalletDbContext>());

        // ═══════════════════════════════════════════════════════════════════
        // SANS CETTE LIGNE, UN REJEU KAFKA CRÉDITE DEUX FOIS.
        //
        // `IntegrationEventDispatcher` résout l'inbox en OPTIONNEL : ce module
        // tournait donc sans aucune garde d'idempotence, avec un simple
        // avertissement au journal. Kafka livre AU MOINS UNE FOIS — un
        // rééquilibrage de partitions suffisait à re-comptabiliser un gain, à
        // recréditer le portefeuille d'un livreur, ou à contre-passer deux fois
        // un retour.
        //
        // Le DbContext lié est celui de CE module : la trace part avec le
        // `SaveChangesAsync` du handler, donc dans la même transaction que
        // l'écriture d'argent qu'elle protège.
        //
        // ELLE COHABITE AVEC CELLE DE PAYMENTS, ELLE NE LA REMPLACE PAS.
        //
        // `HBA.Financial.Api` compose payments, wallet et billing dans un seul
        // processus. Tant que `IntegrationEventDispatcher` résolvait l'inbox par
        // `GetService<T>()`, cette ligne aurait DÉPROTÉGÉ payments : `GetService`
        // rend le dernier enregistrement, et Settlement s'installe après Payments.
        // Les deux gestionnaires de payments auraient vu leur trace ajoutée à un
        // contexte qu'ils ne sauvegardent pas — jamais committée, donc rejouables,
        // et sans avertissement puisqu'une inbox existait bel et bien.
        //
        // Le dispatcher marque désormais dans TOUTES les inbox enregistrées
        // (`GetServices`). Chaque gestionnaire est donc protégé par celle de SON
        // module, et l'ordre des installeurs dans `Program.cs` cesse de décider qui
        // l'est. Voir son encadré.
        // ═══════════════════════════════════════════════════════════════════

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
        //
        // FedaPay n'expose aucune API de remboursement : l'argent revient au client
        // par SON portefeuille, et il en demande le virement Mobile Money ensuite.
        // Ces deux repositories sont ce canal. Sans eux, `WalletMutations` ne peut
        // plus être construit du tout — le module refuserait de démarrer, ce qui est
        // le bon échec : bruyant, immédiat, et pas au premier remboursement.
        services.AddScoped<ICustomerWalletRepository, CustomerWalletRepository>();
        services.AddScoped<ICustomerWithdrawalRepository, CustomerWithdrawalRepository>();

        services.AddScoped<WalletMutations>();

        // ═══════════════════════════════════════════════════════════════════
        // SANS CETTE LIGNE, PAYMENT-SERVICE NE SAIT PAS OÙ RENDRE L'ARGENT.
        //
        // `ICustomerWalletApi` est le point d'entrée que `RefundPaymentCommand`
        // appelle quand `IPaymentGateway.SupportsRefund` est faux. Elle est
        // résolue IN-PROCESS : `HBA.Financial.Api` héberge payments, wallet et
        // billing dans le même conteneur (voir son Program.cs). Le jour où ces
        // modules seraient séparés, c'est cette ligne qui devrait devenir un
        // client de transport — et c'est ici que la dépendance se voit.
        //
        // ELLE DOIT ÊTRE ENREGISTRÉE PAR SETTLEMENT, PAS PAR PAYMENTS.
        //
        // C'est ce module qui possède le portefeuille. L'enregistrer côté payments
        // reviendrait à faire dépendre le propriétaire de la donnée de son
        // appelant, et à installer wallet SANS son API si quelqu'un composait un
        // hôte différent — l'échec serait alors au premier remboursement, en
        // production, pas au démarrage.
        // ═══════════════════════════════════════════════════════════════════
        services.AddScoped<HBA.Financial.Wallet.Contracts.ICustomerWalletApi, Public.CustomerWalletApi>();

        // SANS CE SERVICE, UN GAIN RETIRÉ EST RE-VERSÉ PAR LE PROCHAIN LOT.
        //
        // Le retrait à la demande et le lot de reversement paient le même argent
        // par deux chemins qui ne se voyaient pas. C'est ici que le retrait impute
        // les gains qu'il consomme, et les rend s'il échoue.
        services.AddScoped<SellerEarningImputation>();

        // Règle de clôture d'un retrait (sent → payé, failed → remboursé), partagée par
        // le webhook et la réconciliation : une seule et même logique, donc pas de risque
        // qu'un double verdict produise un double remboursement.
        services.AddScoped<WithdrawalSettlement>();

        services.AddScoped<IDomainEventHandler<PayoutPaidDomainEvent>, PayoutPaidDomainEventHandler>();

        // Réconciliation des retraits « en cours » avec le statut réel du PSP.
        // SANS ce service, un versement échoué chez FedaPay laisserait le retrait
        // marqué « payé » et le vendeur débité, sans jamais avoir reçu son argent.
        services.AddHostedService<Reconciliation.WithdrawalReconciliationService>();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }
}
