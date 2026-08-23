using System.Reflection;
using HBA.Promotions.Application.Promotions;
using HBA.Promotions.Infrastructure.BackgroundJobs;
using HBA.Promotions.Contracts;
using HBA.Promotions.Domain.Promotions;
using HBA.Promotions.Domain.Promotions.Events;
using HBA.Promotions.Infrastructure.Persistence;
using HBA.Promotions.Infrastructure.Public;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Modularity;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Promotions.Infrastructure;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ENREGISTREMENT DU SERVICE PROMOTION.
///
/// Campagnes, règles d'éligibilité, coupons, budgets. Ce module ne connaît AUCUN
/// autre service : il reçoit un contexte de panier — univers, sous-total, frais de
/// livraison, devise, utilisateur — et rend une remise. Il ignore ce qu'est un
/// produit, un plat ou un restaurant, et c'est ce qui lui permet de servir les
/// deux checkouts du §11 sans en connaître aucun.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PromotionsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Promotions";

    public Assembly ApplicationAssembly => typeof(ValidateCouponQuery).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Chaîne de connexion « Default » absente.");

        services.AddDbContext<PromotionsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PromotionsDbContext.SchemaName)));

        services.AddScoped<IPromotionsUnitOfWork>(sp => sp.GetRequiredService<PromotionsDbContext>());
        services.AddScoped<IPromotionRepository, PromotionRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<IPromotionModuleApi, PromotionModuleApi>();

        // Inbox de consommation (§19.5) et idempotence HTTP (§5), dans le schéma
        // du service — voir l'encadré de `PromotionsDbContext`.
        services.AddScoped<IConsumerInbox, EfConsumerInbox<PromotionsDbContext>>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore<PromotionsDbContext>>();

        // ═════════════════════════════════════════════════════════════════════
        // TROIS LIGNES SANS LESQUELLES LES ÉVÉNEMENTS NE SORTENT PAS.
        //
        // Le domaine lève `promotion.created`, `promotion.exhausted` et
        // `coupon.used` ; les gestionnaires qui les traduisent existent dans la
        // couche Application. Rien ne les relie sinon ces trois inscriptions, et
        // rien dans le compilateur ne les réclame.
        //
        // C'est exactement la panne trouvée dans media-service : trois événements
        // levés depuis l'origine, un commentaire affirmant qu'ils partaient par
        // l'outbox, et aucun gestionnaire inscrit. Le service compilait, les tests
        // passaient, et rien ne quittait le processus pendant un an.
        // ═════════════════════════════════════════════════════════════════════
        services.AddScoped<IDomainEventHandler<PromotionCreatedDomainEvent>, PromotionCreatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PromotionExhaustedDomainEvent>, PromotionExhaustedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<CouponUsedDomainEvent>, CouponUsedDomainEventHandler>();

        services.AddOutboxProcessor<PromotionsDbContext>();

        // ═════════════════════════════════════════════════════════════════════
        // LE BALAYEUR DE BUDGET (ISSUE-053). SANS CETTE LIGNE, RIEN NE REND
        // L'ENVELOPPE D'UN PANIER ABANDONNÉ.
        //
        // `Coupon.HoldLifetime` vaut trente minutes ; `ExpiresAtUtc` était écrite
        // depuis la migration initiale et relue par personne. Une campagne passait
        // `Exhausted` sur des paniers que personne n'avait jamais payés, et
        // `promotion.exhausted` partait vers le marketing avec un budget intact.
        //
        // Période PAR DÉFAUT : 5 minutes. Une expiration n'a aucune urgence — la
        // retenue est hors délai depuis un quart d'heure quand on la voit — et
        // balayer plus souvent relirait la table pour rien. Elle reste réglable :
        //
        //     Promotions:HoldSweep:IntervalSeconds
        //     Promotions:HoldSweep:BatchSize
        //
        // `configuration[...]` + `TryParse`, PAS `GetValue<T>` : ce projet ne
        // référence que `Microsoft.Extensions.Configuration.Abstractions`, et
        // `GetValue<T>` vit dans le paquet `.Binder`. C'est la manière de faire du
        // dépôt (voir `PaymentsModuleInstaller` et `InventoryModuleInstaller`).
        //
        // Les valeurs absurdes sont ignorées au profit du défaut : une période de
        // zéro seconde ferait tourner le balayeur en boucle serrée sur la base, et
        // un lot négatif ne balaierait plus rien — sans que rien ne le dise.
        // ═════════════════════════════════════════════════════════════════════
        var periode = TimeSpan.FromMinutes(5);
        if (int.TryParse(configuration["Promotions:HoldSweep:IntervalSeconds"], out var secondes)
            && secondes > 0)
        {
            periode = TimeSpan.FromSeconds(secondes);
        }

        var taillePar = 100;
        if (int.TryParse(configuration["Promotions:HoldSweep:BatchSize"], out var lot) && lot > 0)
        {
            taillePar = lot;
        }

        services.AddSingleton(new CouponHoldSweepOptions(periode, taillePar));
        services.AddHostedService<ExpireCouponHoldsWorker>();
    }
}
