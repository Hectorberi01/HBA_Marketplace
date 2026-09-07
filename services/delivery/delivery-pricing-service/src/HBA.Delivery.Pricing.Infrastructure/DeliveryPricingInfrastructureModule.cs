using System.Globalization;
using HBA.Delivery.Pricing.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Delivery.Pricing.Application.Abstractions;
using HBA.Delivery.Pricing.Domain.Policies;
using HBA.Delivery.Pricing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using HBA.Delivery.Pricing.Infrastructure.Caching.Redis;
using HBA.Delivery.Pricing.Infrastructure.Observability;
using HBA.Delivery.Pricing.Infrastructure.Persistence.Outbox;
namespace HBA.Delivery.Pricing.Infrastructure;

public static class DeliveryPricingInfrastructureModule
{
    public static IServiceCollection AddDeliveryPricingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // LE CACHE DE CE SERVICE (Caching/Redis/).
        services.AjouterCacheDeliveryPricing(configuration);

        // LES SONDES DE CE SERVICE (Observability/).
        services.AjouterObservabiliteDeliveryPricing(configuration);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        // L'outbox et les abonnements sont descendus dans `Messaging/Kafka/`, donc
        // hors de ce module d'infrastructure : ils sont enregistres par
        // `AjouterMessagerieDeliveryPricing()`, que le composition root peut
        // oublier.
        services.AddHostedService<GardeDeCablage>();

        services.AddDbContext<DeliveryPricingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DeliveryPricingDbContext.SchemaName)));

        services.AddScoped<IPricingStore, EfDeliveryPricingStore>();

        // LES DEUX LEVIERS DE L'ESTIMATION D'ITINÉRAIRE.
        var defauts = new EstimationItineraireOptions();
        var section = configuration.GetSection(EstimationItineraireOptions.SectionName);

        var estimation = new EstimationItineraireOptions
        {
            VitesseMoyenneMetresParSeconde = LireDouble(
                section["VitesseMoyenneMetresParSeconde"], defauts.VitesseMoyenneMetresParSeconde),
            FacteurCorrectionUrbaine = LireDecimal(
                section["FacteurCorrectionUrbaine"], defauts.FacteurCorrectionUrbaine),
            DureeMinimaleSecondes = LireEntier(
                section["DureeMinimaleSecondes"], defauts.DureeMinimaleSecondes)
        };

        // POURQUOI `Valider()` ICI ET PAS À LA PREMIÈRE UTILISATION. Une vitesse à
        // zéro ou un facteur à 0,8 ne casse rien visiblement : ça produit des devis
        // faux, silencieusement, jusqu'à ce que quelqu'un compare une facture à une
        // course.
        estimation.Valider();

        services.AddSingleton(Options.Create(estimation));

        // LA FILE D'ÉVÉNEMENTS N'EST PLUS RÉENREGISTRÉE ICI.
        return services;
    }

    private static decimal LireDecimal(string? brut, decimal defaut)
    {
        if (string.IsNullOrWhiteSpace(brut))
        {
            return defaut;
        }

        return decimal.TryParse(brut, NumberStyles.Number, CultureInfo.InvariantCulture, out var valeur)
            ? valeur
            : throw new InvalidOperationException(
                $"{EstimationItineraireOptions.SectionName} : « {brut} » n'est pas un nombre décimal lisible. "
                + "Le séparateur décimal attendu est le POINT, quelle que soit la locale de la machine.");
    }

    private static double LireDouble(string? brut, double defaut)
    {
        if (string.IsNullOrWhiteSpace(brut))
        {
            return defaut;
        }

        return double.TryParse(brut, NumberStyles.Float, CultureInfo.InvariantCulture, out var valeur)
            ? valeur
            : throw new InvalidOperationException(
                $"{EstimationItineraireOptions.SectionName} : « {brut} » n'est pas un nombre lisible. "
                + "Le séparateur décimal attendu est le POINT, quelle que soit la locale de la machine.");
    }

    private static int LireEntier(string? brut, int defaut)
    {
        if (string.IsNullOrWhiteSpace(brut))
        {
            return defaut;
        }

        return int.TryParse(brut, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valeur)
            ? valeur
            : throw new InvalidOperationException(
                $"{EstimationItineraireOptions.SectionName} : « {brut} » n'est pas un entier lisible.");
    }
}
