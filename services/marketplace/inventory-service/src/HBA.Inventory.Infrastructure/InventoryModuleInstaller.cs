using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Domain.Events;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Inventory.Application.Abstractions;
using HBA.Inventory.Application.Stock.Commands;
using HBA.Inventory.Application.Stock.EventHandlers;
using HBA.Inventory.Contracts;
using HBA.Inventory.Domain.Locations;
using HBA.Inventory.Domain.Stock;
using HBA.Inventory.Domain.Stock.Events;
using HBA.Inventory.Infrastructure.BackgroundJobs;
using HBA.Inventory.Infrastructure.Persistence;
using HBA.Inventory.Infrastructure.Public;

namespace HBA.Inventory.Infrastructure;

/// <summary>Enregistre le module Inventory : DbContext, repositories, API publique, handlers, validators, outbox.</summary>
public sealed class InventoryModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Inventory";

    public Assembly ApplicationAssembly => typeof(ReserveStockCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<InventoryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", InventoryDbContext.SchemaName)));

        services.AddScoped<IInventoryUnitOfWork>(sp => sp.GetRequiredService<InventoryDbContext>());

        services.AddScoped<IInventoryItemRepository, InventoryItemRepository>();

        // Le journal des mouvements (lot 7.3, ISSUE-044). Écrit dans la MÊME unité
        // de travail que la mutation : un journal tenu à part laisserait, au premier
        // incident, un stock modifié sans ligne qui l'explique.
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IFulfillmentLocationRepository, FulfillmentLocationRepository>();
        services.AddScoped<IInventoryModuleApi, InventoryModuleApi>();

        services.AddScoped<IDomainEventHandler<StockReservedDomainEvent>, StockReservedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<StockDepletedDomainEvent>, StockDepletedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<StockReplenishedDomainEvent>, StockReplenishedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<InventoryDbContext>();

        // ═════════════════════════════════════════════════════════════════════
        // LE BALAYAGE D'EXPIRATION DES RÉSERVATIONS (ISSUE-031).
        //
        // SANS CET ENREGISTREMENT, LA CORRECTION N'EXISTE PAS.
        //
        // `ExpiresAtUtc` était écrite depuis la migration initiale et relue par
        // personne : la colonne, la commande et l'agrégat peuvent tous être justes,
        // si rien ne les appelle le stock continue de s'éroder exactement comme
        // avant. C'est la panne qu'a connue return-refund, dont les trois
        // travailleurs étaient enregistrés — et vides.
        //
        // CE MODULE N'EST COMPOSÉ QUE PAR inventory-service (voir son
        // `Program.cs`). Contrairement à l'outbox, il n'y a donc pas de second hôte
        // qui lancerait un deuxième balayeur. Si un BFF venait un jour à composer
        // ce module, il faudrait un interrupteur du même genre que
        // `OUTBOX_ENABLED` — deux balayeurs liraient le même lot.
        //
        // Période PAR DÉFAUT : 5 minutes. Une expiration n'a aucune urgence — la
        // réservation est hors délai depuis un quart d'heure quand on la voit — et
        // balayer plus souvent relirait la table pour rien. Elle reste réglable :
        //
        //     Inventory:ReservationSweep:IntervalSeconds
        //     Inventory:ReservationSweep:BatchSize
        //
        // `configuration[...]` + `TryParse`, PAS `GetValue<T>` : ce projet ne
        // référence que `Microsoft.Extensions.Configuration.Abstractions`, et
        // `GetValue<T>` vit dans le paquet `.Binder`. C'est la manière de faire du
        // dépôt (voir `PaymentsModuleInstaller`).
        //
        // Les valeurs absurdes sont ignorées au profit du défaut : une période de
        // zéro seconde ferait tourner le balayeur en boucle serrée sur la base, et
        // un lot négatif ne balaierait plus rien — sans que rien ne le dise.
        // ═════════════════════════════════════════════════════════════════════
        var periode = TimeSpan.FromMinutes(5);
        if (int.TryParse(configuration["Inventory:ReservationSweep:IntervalSeconds"], out var secondes)
            && secondes > 0)
        {
            periode = TimeSpan.FromSeconds(secondes);
        }

        var taillePar = 100;
        if (int.TryParse(configuration["Inventory:ReservationSweep:BatchSize"], out var lot) && lot > 0)
        {
            taillePar = lot;
        }

        services.AddSingleton(new StockReservationSweepOptions(periode, taillePar));
        services.AddHostedService<ExpireStockReservationsWorker>();

        // ═════════════════════════════════════════════════════════════════════
        // LA PURGE DES RÉSERVATIONS TERMINÉES (manque connu depuis le lot 3.5).
        //
        // TROIS RÉGLAGES, ET LE PLUS DÉLICAT EST LA RÉTENTION.
        //
        // `ConfirmReservation` teste une ligne `Confirmed` pour être idempotent :
        // effacer cette ligne avant qu'un rejeu Kafka ne puisse encore arriver
        // ferait décrémenter le stock une seconde fois. Quatre-vingt-dix jours par
        // défaut, très au-delà de la rétention d'un topic et des reprises
        // d'outbox. La raccourcir sous une semaine est une décision à prendre en
        // connaissance de cette phrase.
        //
        // UNE FOIS PAR JOUR, ET NON TOUTES LES CINQ MINUTES comme l'expiration.
        // Celle-ci rend du stock à la vente et doit courir ; la purge n'a aucun
        // effet métier — plus souvent ne ferait que relire une table pour n'y rien
        // trouver.
        //
        // LOT VOLONTAIREMENT BAS. Le PREMIER passage sur une base en service
        // depuis longtemps a des mois d'historique à reprendre : mieux vaut
        // plusieurs tours courts qu'une transaction qui verrouille la table.
        // ═════════════════════════════════════════════════════════════════════
        var periodePurge = TimeSpan.FromHours(24);
        if (int.TryParse(configuration["Inventory:ReservationPurge:IntervalHours"], out var heures)
            && heures > 0)
        {
            periodePurge = TimeSpan.FromHours(heures);
        }

        var retention = TimeSpan.FromDays(90);
        if (int.TryParse(configuration["Inventory:ReservationPurge:RetentionDays"], out var jours)
            && jours > 0)
        {
            retention = TimeSpan.FromDays(jours);
        }

        var lotPurge = 500;
        if (int.TryParse(configuration["Inventory:ReservationPurge:BatchSize"], out var taille)
            && taille > 0)
        {
            lotPurge = taille;
        }

        services.AddSingleton(new StockReservationPurgeOptions(periodePurge, retention, lotPurge));
        services.AddHostedService<PurgeStockReservationsWorker>();
    }
}
