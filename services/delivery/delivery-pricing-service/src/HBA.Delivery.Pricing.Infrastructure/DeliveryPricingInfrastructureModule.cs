using System.Globalization;
using HBA.Delivery.Pricing.Application.Abstractions;
using HBA.Delivery.Pricing.Domain.Policies;
using HBA.Delivery.Pricing.Infrastructure.Persistence;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HBA.Delivery.Pricing.Infrastructure;

public static class DeliveryPricingInfrastructureModule
{
    public static IServiceCollection AddDeliveryPricingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<DeliveryPricingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DeliveryPricingDbContext.SchemaName)));

        services.AddScoped<IPricingStore, EfDeliveryPricingStore>();

        // ═════════════════════════════════════════════════════════════════════
        // LES DEUX LEVIERS DE L'ESTIMATION D'ITINÉRAIRE.
        //
        // Une section absente donne les valeurs par défaut — 5,8 m/s et un
        // facteur de 1,0 — c'est-à-dire EXACTEMENT le comportement qui était codé
        // en dur. Aucun déploiement existant ne change de prix du fait de ce
        // câblage.
        //
        // LIAISON À LA MAIN, PAS `Get<T>()` NI `Configure<T>(section)`.
        //
        // Les deux vivent dans `Microsoft.Extensions.Configuration.Binder` et
        // `Microsoft.Extensions.Options.ConfigurationExtensions`, qu'aucun des
        // deux `PackageReference` de ce projet ne déclare. Ça compilerait
        // peut-être par transitivité — et casserait le jour où une dépendance
        // intermédiaire cesse de les traîner, sur un projet qui n'a jamais
        // demandé ces paquets.
        //
        // ET LA LIAISON MANUELLE FERME UN DÉFAUT QUE LE BINDER LAISSAIT OUVERT :
        // LA CULTURE. « 1.3 » lu par un convertisseur dépendant de la culture
        // vaut 13 sous une locale française. Un facteur multiplié par dix ne
        // lève aucune exception : il multiplie par dix le prix de toutes les
        // courses. `InvariantCulture` est donc imposée ici, explicitement.
        // ═════════════════════════════════════════════════════════════════════
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

        // POURQUOI `Valider()` ICI ET PAS À LA PREMIÈRE UTILISATION. Une vitesse
        // à zéro ou un facteur à 0,8 ne casse rien visiblement : ça produit des
        // devis faux, silencieusement, jusqu'à ce que quelqu'un compare une
        // facture à une course. Un service qui refuse de démarrer se remarque
        // dans la minute.
        estimation.Valider();

        services.AddSingleton(Options.Create(estimation));

        // ═════════════════════════════════════════════════════════════════════
        // LA FILE D'ÉVÉNEMENTS N'EST PLUS RÉENREGISTRÉE ICI.
        //
        // Ces deux lignes existaient parce que l'hôte n'appelait pas
        // `AddBuildingBlocksInfrastructure` — elles comblaient une partie du
        // socle absent, mais pas celle qui empêchait le démarrage. L'hôte prend
        // désormais le socle entier, qui pose `IntegrationEventQueue` et
        // `IIntegrationEventPublisher` exactement de la même façon.
        //
        // Les garder empilerait deux descripteurs identiques. `OutboxRegistration`
        // dit pourquoi on s'en abstient : « sans dommage fonctionnel — le dernier
        // gagne — mais c'est un mensonge dans le conteneur ».
        //
        // CONSÉQUENCE À CONNAÎTRE : ce module n'est plus autonome. Un hôte qui
        // l'appellerait sans poser le socle n'aurait ni file d'événements, ni
        // dispatcher de domaine, ni métriques d'outbox — et ne démarrerait pas.
        // ═════════════════════════════════════════════════════════════════════
        services.AddOutboxProcessor<DeliveryPricingDbContext>();
        return services;
    }

    /// <remarks>
    /// UNE VALEUR ILLISIBLE EST UNE ERREUR, PAS UN RETOUR AU DÉFAUT.
    ///
    /// `TryParse` suivi d'un repli silencieux ferait démarrer le service avec
    /// 1,0 alors que l'exploitant croit avoir posé 1,3 — une coquille dans une
    /// variable d'environnement resterait invisible jusqu'à ce qu'on compare des
    /// factures. Seule une valeur ABSENTE prend le défaut.
    /// </remarks>
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
