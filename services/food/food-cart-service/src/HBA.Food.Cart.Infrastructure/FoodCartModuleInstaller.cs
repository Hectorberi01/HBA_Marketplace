using HBA.Shared.Infrastructure.Hosting;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Configuration;
using System.Reflection;
using FluentValidation;
using HBA.FoodCarts.Application.Abstractions;
using HBA.FoodCarts.Application.Carts.Commands;
using HBA.FoodCarts.Infrastructure.Messaging.Kafka.Consumers;
using HBA.FoodCarts.Contracts;
using HBA.FoodCarts.Domain.Carts;
using HBA.FoodCarts.Domain.Carts.Events;
using HBA.FoodCarts.Infrastructure.Persistence;
using HBA.FoodCarts.Infrastructure.Public;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Pricing.Contracts;
using HBA.Pricing.Promotion;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using HBA.FoodCarts.Infrastructure.Caching.Redis;
using HBA.FoodCarts.Infrastructure.Observability;
namespace HBA.FoodCarts.Infrastructure;

/// <summary>
/// Enregistre le module FoodCart : DbContext, repository, API publique,
/// gestionnaires, validateurs, outbox.
/// </summary>
public sealed class FoodCartModuleInstaller : IModuleInstaller
{
    public string ModuleName => "FoodCart";

    public Assembly ApplicationAssembly => typeof(AddItemToFoodCartCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/). Il etait branche par le
        // socle pour les vingt-six services a la fois ; il l'est desormais ici.
        services.AjouterCacheFoodCart(configuration);

        // LES SONDES DE CE SERVICE (Observability/). Jusqu'ici seule la base
        // etait verifiee : un service dont le consommateur Kafka etait mort
        // repondait « ready », et le deploiement individuel le croyait sain.
        services.AjouterObservabiliteFoodCart(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et l'inbox sont descendues dans `Messaging/Kafka/`, donc
        // hors de cet installeur : elles sont desormais enregistrees par
        // `AjouterMessagerieFoodCart()`, que le composition root peut oublier.
        // Un oubli ne casserait rien de visible — le service demarre et n'emet
        // plus rien. Cette garde, elle, est enregistree ici : elle doit exister
        // quand ce qu'elle verifie est absent.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<FoodCartDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", FoodCartDbContext.SchemaName)));

        services.AddScoped<IFoodCartUnitOfWork>(sp => sp.GetRequiredService<FoodCartDbContext>());

        services.AddScoped<IFoodCartRepository, FoodCartRepository>();

        // Sans cette ligne, le dispatcher tourne SANS garde d'idempotence et se
        // contente de le journaliser : le service resterait rejouable en silence.
        services.AddScoped<IFoodCartModuleApi, FoodCartModuleApi>();
        // LE SEUL BOUCHON DE TARIFICATION QUI SUBSISTE (ISSUE-033). Il refuse
        // désormais de démarrer si l'adresse de promotion-service manque — voir
        // `AddPromotionGrpcClient` dans le composition root.
        // ═══════════════════════════════════════════════════════════════════
        // LE PANIER DE REPAS EST BRANCHÉ SUR LES PROMOTIONS DEPUIS LE 29 AOÛT.
        //
        // Il employait `NeutralPricingModuleApi` : aucune remise jamais
        // appliquée, TOUT code promo refusé — et en silence. Le garde-fou
        // `GuardNeutralPricing` refusait donc de démarrer en production, ce qui
        // était le bon choix tant que le branchement n'existait pas.
        //
        // `PromotionPricingModuleApi` est LA MÊME implémentation que
        // `cart-service`, pas une copie : elle vit dans
        // `shared/contracts/HBA.Pricing.Promotion` depuis ce jour, parce qu'elle
        // n'a jamais dépendu que de deux contrats. Deux tarifications recopiées
        // divergent au premier correctif — et une divergence de tarification ne
        // se voit pas, elle se facture.
        //
        // CE QUE CE BRANCHEMENT EXIGE DE L'HÔTE : `AddPromotionGrpcClient` dans
        // le composition root, et `SERVICES__PROMOTION` au déploiement.
        // L'enregistrement du client LÈVE si l'adresse manque — le service ne
        // démarre pas plutôt que de refuser les coupons sans le dire.
        // ═══════════════════════════════════════════════════════════════════
        services.AddScoped<IPricingModuleApi, PromotionPricingModuleApi>();

        services.AddScoped<
            IDomainEventHandler<FoodCartCheckedOutDomainEvent>, FoodCartCheckedOutDomainEventHandler>();

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

    }

    // ═══════════════════════════════════════════════════════════════════════════
    // `GuardNeutralPricing` A ÉTÉ RETIRÉ LE 29 AOÛT 2026, ET C'EST LE BON GESTE.
    //
    // Il refusait de démarrer en production parce que le panier de repas
    // n'était branché sur aucun service de promotion. Ce n'est plus le cas :
    // `PromotionPricingModuleApi` est enregistré plus haut, et
    // `AddPromotionGrpcClient` lève si son adresse manque.
    //
    // LE GARDER AURAIT ÉTÉ PIRE QUE DE NE JAMAIS L'ÉCRIRE. Un garde-fou qui
    // décrit un défaut corrigé bloque la production pour rien, et le premier
    // qui le lit cherche un problème qui n'existe plus. C'est le défaut inverse
    // de celui qu'il corrigeait, et c'est un défaut quand même — son propre
    // commentaire le disait de la liste de `return-refund-service`.
    // ═══════════════════════════════════════════════════════════════════════════


    /// <summary>
    /// Sommes-nous en production ?
    /// </summary>
    /// <remarks>
    /// L'installeur ne reçoit qu'un <see cref="IConfiguration"/> — les modules
    /// s'installent avant que l'hôte ne soit construit, donc pas
    /// d'<c>IHostEnvironment</c>. La règle elle-même vit dans
    /// <c>EnvironnementDeploiement</c>, en un seul exemplaire.
    ///
    /// CE PARAGRAPHE DÉCRIVAIT AUPARAVANT UN FAIL-OPEN ASSUMÉ : « l'inconnu est
    /// traité comme pas la production, sinon un nom mal orthographié empêcherait
    /// de travailler ». Ce n'est plus vrai, et ce n'était pas défendable : une
    /// variable ABSENTE tombait du même côté qu'une faute de frappe, alors
    /// qu'ASP.NET Core considère une variable absente comme la production.
    /// Désormais l'inconnu et l'absent sont la production ; seuls les noms
    /// explicitement listés en dispensent.
    /// </remarks>
    private static bool IsProduction(IConfiguration configuration)
    {
        // DÉLÉGUÉ À `EnvironnementDeploiement`, ET C'EST LA CORRECTION.
        //
        // Ce corps était une copie parmi six d'une règle FAIL-OPEN : tout ce qui
        // n'était pas littéralement « Production » — variable absente, chaîne
        // vide, faute de frappe — était traité comme du développement, alors
        // qu'ASP.NET Core, lui, considère une variable absente comme la
        // production. Voir l'encadré de `EnvironnementDeploiement`.
        return EnvironnementDeploiement.EstProduction(configuration);
    }
}
