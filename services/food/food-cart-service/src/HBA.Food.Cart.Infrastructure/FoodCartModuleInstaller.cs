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
        // désormais de démarrer en production — voir `GuardNeutralPricing`.
        services.AddScoped<IPricingModuleApi, NeutralPricingModuleApi>();
        GuardNeutralPricing(configuration);

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
    /// `NeutralPricingModuleApi` ne MENT pas — il rend le prix de base et refuse
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
    /// </remarks>
    private static void GuardNeutralPricing(IConfiguration configuration)
    {
        const string details =
            "  \u2022 NeutralPricingModuleApi.CalculatePriceAsync \u2014 aucune remise n'est jamais appliquée "
            + "à un panier de repas, quelle que soit la campagne en cours.\n"
            + "  \u2022 NeutralPricingModuleApi.ValidateCouponAsync \u2014 TOUT code promo est refusé, "
            + "y compris un code valide et actif.";

        if (IsProduction(configuration))
        {
            throw new InvalidOperationException(
                "PRODUCTION AVEC UNE TARIFICATION NEUTRE \u2014 DÉMARRAGE REFUSÉ.\n\n"
                + "Le panier de repas n'est branché sur AUCUN service de promotion :\n"
                + details + "\n\n"
                + "Le refus est DÉLIBÉRÉ. Sans lui, le service démarrerait normalement et aucune "
                + "campagne food ne fonctionnerait \u2014 sans une erreur, sans une ligne de journal, et "
                + "sans aucun moyen de distinguer « pas de coupon » de « coupon jamais appliqué ». "
                + "C'est le défaut ISSUE-033, corrigé côté marketplace et non côté restauration.\n\n"
                + "Pour lever ce refus : enregistrer un fournisseur branché sur promotion-service "
                + "(voir `PromotionPricingModuleApi` dans cart-service), ajouter "
                + "`AddPromotionGrpcClient` au composition root et `SERVICES__PROMOTION` au "
                + "déploiement, puis retirer cet appel.");
        }

        // Bruyant, et volontairement. En production ce cas est impossible (voir
        // ci-dessus) ; ailleurs, il faut qu'un développeur qui saisit un code promo
        // sur un panier de repas sache qu'aucun service ne le regarde — sans quoi il
        // conclura que le code est mauvais.
        Console.WriteLine(
            "[FoodCart] \u26a0\ufe0f  TARIFICATION NEUTRE ACTIVE :\n" + details + "\n"
            + "Le parcours de commande se déroule intégralement, mais aucune promotion n'existe "
            + "sur les repas. Le démarrage est refusé en production.");
    }

    /// <summary>
    /// Sommes-nous en production ?
    /// </summary>
    /// <remarks>
    /// Copie assumée de `PaymentsModuleInstaller.IsProduction` : l'installeur ne
    /// reçoit qu'un <see cref="IConfiguration"/> — les modules s'installent avant
    /// que l'hôte ne soit construit, donc pas d'IHostEnvironment.
    ///
    /// FAIL-SAFE À L'ENVERS DE CE QU'ON VOUDRAIT, ET DÉLIBÉRÉMENT. Un
    /// environnement inconnu est traité comme « pas la production », sinon un nom
    /// mal orthographié empêcherait de travailler. Le risque assumé est donc qu'une
    /// VRAIE prod dont ASPNETCORE_ENVIRONMENT serait mal renseigné passe au travers
    /// du refus — c'est pourquoi l'avertissement ci-dessus est aussi bruyant.
    /// </remarks>
    private static bool IsProduction(IConfiguration configuration)
    {
        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? string.Empty;

        return string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);
    }
}
