using HBA.Food.Application.Abstractions;
using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Restaurants;
using HBA.Food.Domain.Staff;
using HBA.Food.Domain.Stations;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.Infrastructure.Outbox;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Food.Infrastructure.Persistence;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE SCHÉMA DU MODULE FOOD.
///
/// Établissements, cartes, articles, options. Le lieu physique est ailleurs
/// (Inventory), la course aussi (Delivery), le paiement également.
///
/// TROIS RACINES, ET UNE QUI SURPREND
///
/// <c>Restaurant</c> possède ses créneaux de service. <c>MenuItem</c> possède ses
/// groupes d'options et leurs choix — parce que valider le panier d'un client,
/// c'est charger UN article. <c>Menu</c>, lui, n'est qu'une section : les
/// articles la référencent sans lui appartenir, sinon vérifier un plat
/// obligerait à charger les quarante autres.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class FoodDbContext : ModuleDbContext, IFoodUnitOfWork
{
    public const string SchemaName = "food";

    public FoodDbContext(
        DbContextOptions<FoodDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Restaurant> Restaurants => Set<Restaurant>();

    /// <summary>Les CARTES : « Menu du midi », « Carte du soir ». Elles portent les créneaux (§5).</summary>
    public DbSet<Menu> Menus => Set<Menu>();

    /// <summary>
    /// Les SECTIONS : « Entrées », « Plats », « Boissons ».
    ///
    /// C'est ce que la table <c>menus</c> contenait avant la bascule à deux
    /// niveaux. Elles vivent désormais dans <c>menu_categories</c>, et
    /// <c>menus</c> a été vidée puis regarnie d'une carte par restaurant.
    /// </summary>
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();

    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    /// <summary>
    /// Le personnel (§8). Quatrième racine : un membre possède ses dérogations de
    /// permission, et rien d'autre. Son <c>UserId</c> n'est qu'une référence vers
    /// Identity — aucune donnée d'authentification ne vit ici.
    /// </summary>
    public DbSet<RestaurantStaff> Staff => Set<RestaurantStaff>();

    /// <summary>Les postes de préparation (§9) : GRILL, PIZZA, DRINKS.</summary>
    public DbSet<PreparationStation> PreparationStations => Set<PreparationStation>();

    /// <summary>
    /// La part OPÉRATIONNELLE des commandes (§10 à §13).
    ///
    /// Le module Ordering reste propriétaire de la commande commerciale : on ne
    /// trouvera ici ni paiement, ni facture, ni remboursement. Et le ticket de
    /// cuisine n'a pas de table à lui — il EST cette commande, vue de la cuisine.
    /// </summary>
    public DbSet<FoodOrder> FoodOrders => Set<FoodOrder>();

    /// <summary>
    /// Traces de consommation Kafka (§19.5).
    ///
    /// Elle n'appartient à aucune des racines ci-dessus, et c'est voulu :
    /// aucune règle métier ne la lit. Elle vit dans CE contexte parce qu'une
    /// trace écrite hors de la transaction du ticket ne protège rien — un
    /// incident entre les deux écritures rouvrirait le ticket au rejeu suivant.
    /// </summary>
    public DbSet<ConsumerInboxEntry> ConsumerInbox => Set<ConsumerInboxEntry>();

    protected override string Schema => SchemaName;

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE JOURNAL D'AUDIT EST ACTIF ICI (lot 7.1, ISSUE-042 / ISSUE-043).
    ///
    /// `KeepsAuditTrail` VALAIT `false` SUR VINGT ET UN CONTEXTES SUR VINGT-QUATRE.
    ///
    /// Ce qui n'y laissait AUCUNE trace : l'approbation, le refus, la suspension et
    /// la levée de suspension d'un ÉTABLISSEMENT.
    ///
    /// Suspendre un restaurant lui coupe tout son chiffre d'affaires du jour. Le
    /// dossier retenait qu'il était suspendu, pas par qui ni quand.
    ///
    /// Activé DANS LE MÊME COMMIT que la migration qui crée `food.audit_entries` —
    /// l'inverse produirait une surcharge qui promet une table absente, et le défaut
    /// ne se verrait qu'au premier `SaveChanges` en production.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    protected override bool KeepsAuditTrail => true;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FoodDbContext).Assembly);
        // Configuration du socle : autre assembly, le balayage ne la trouve pas.
        modelBuilder.ApplyConfiguration(new ConsumerInboxConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
