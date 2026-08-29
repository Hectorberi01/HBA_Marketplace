using HBA.Shared.Infrastructure.Hosting;
using System.Reflection;
using FluentValidation;
using HBA.FoodCarts.Application.Abstractions;
using HBA.FoodCarts.Application.Carts.Commands;
using HBA.FoodCarts.Application.Carts.EventHandlers;
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
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<FoodCartDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", FoodCartDbContext.SchemaName)));

        services.AddScoped<IFoodCartUnitOfWork>(sp => sp.GetRequiredService<FoodCartDbContext>());

        services.AddScoped<IFoodCartRepository, FoodCartRepository>();

        // Sans cette ligne, le dispatcher tourne SANS garde d'idempotence et se
        // contente de le journaliser : le service resterait rejouable en silence.
        services.AddScoped<IConsumerInbox, EfConsumerInbox<FoodCartDbContext>>();
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

        // Chorégraphie : le panier se clôt quand la commande de repas est partie.
        services.AddScoped<
            IIntegrationEventHandler<MealOrderPlacedIntegrationEvent>, CloseFoodCartOnMealOrderPlacedHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<FoodCartDbContext>();
    }

    /// <summary>
    /// Refuse le démarrage en production tant que la tarification du panier de
    /// repas est neutre ; l'annonce bruyamment partout ailleurs.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// MÊME RÈGLE QUE `PaymentsModuleInstaller` ET `ReturnRefundModuleInstaller`,
    /// MÊME RAISON.
    ///
    /// La tarification neutre d'avant le 29/08/2026 ne MENTAIT pas — elle rendait
    /// le prix de base et refusait
    /// franchement les codes, ce qui est fail-closed. Ce qu'il fait, c'est
    /// PROMETTRE une fonctionnalité qui n'existe pas : le §11 décrit un checkout de
    /// restauration avec code promo, l'application affiche le champ,
    /// `PromotionScope.Food` existe dans le domaine de promotion — et rien ne
    /// l'appelle. Un exploitant croit donc livrer des campagnes food, et le seul
    /// symptôme est commercial : « pourquoi nos codes ne marchent-ils jamais ? ».
    ///
    /// C'est exactement ce qu'ISSUE-033 décrit, et exactement ce qui a permis à ce
    /// bouchon de vivre : un panier sans remise ressemble à un panier sans coupon.
    ///
    /// CE QUE CE REFUS COÛTE, ET POURQUOI IL EST ACCEPTABLE AUJOURD'HUI.
    ///
    /// Il empêche food-cart-service de démarrer en production. Ce n'est pas
    /// théorique : c'est une décision de déploiement. Elle est prise en connaissance
    /// de cause parce que ce service n'est PAS dans `k8s/base/services/` — il n'a
    /// aujourd'hui aucun chemin vers la production, et le refus force donc à
    /// trancher le branchement AVANT le premier déploiement plutôt qu'après.
    ///
    /// Si la restauration doit partir en production sans promotions, ce n'est pas ce
    /// garde-fou qu'il faut assouplir : c'est la décision « pas de codes promo sur
    /// les repas » qu'il faut écrire, et retirer le champ de l'application.
    ///
    /// PAS DE DRAPEAU DE CONFIGURATION POUR PASSER OUTRE.
    ///
    /// C'est exactement la variable qu'on recopie d'un fichier d'environnement de
    /// recette vers celui de production. Et il n'y aurait rien à assumer : brancher
    /// promotion sur ce panier est une demi-journée de travail, pas un arbitrage.
    /// ═════════════════════════════════════════════════════════════════════════

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
