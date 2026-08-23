using HBA.Deliveries.Contracts.Grpc;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Api.Endpoints;
using HBA.Food.Api.Integration;
using HBA.Food.Contracts.Grpc;
using HBA.FoodOrders.Contracts.Grpc;
using HBA.Food.Contracts.IntegrationEvents;
using HBA.Food.Infrastructure;
using HBA.Food.Infrastructure.Persistence;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Inventory.Contracts.Grpc;
using HBA.Merchants.Contracts.Grpc;
using HBA.Ordering.Contracts.Grpc;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.Hosting;
using HBA.Shared.IntegrationEvents;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<FoodDbContext>(new FoodModuleInstaller());
builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// LE PARCOURS FOOD, DE LA COMMANDE PAYÉE AU CLIENT SERVI.
//
// CES QUATRE PONTS N'EXISTAIENT PAS, ET LE PARCOURS S'ARRÊTAIT EN CHEMIN.
//
// Sans le premier, un client peut commander un repas, être débité, et aucune
// cuisine n'est servie — le ticket n'est jamais ouvert. Sans le second, un
// repas déclaré prêt refroidit sans qu'aucun livreur ne soit cherché.
//
// Les deux vivaient dans la composition root du monolithe. Le module Food est
// parti dans son service ; les fichiers qui le reliaient au reste sont restés.
//
// LES DEUX DERNIERS FERMENT LA CHAÎNE — SANS EUX, PERSONNE N'EST PAYÉ.
//
// L'aller était branché, le RETOUR ne l'était pas : aucun service du dépôt ne
// consommait la fin d'une course « FOOD- ». Le livreur remettait le repas au
// client, le ticket restait « prêt » à vie, la commande restait « confirmée »,
// et le gain du restaurateur restait bloqué en « à venir ».
//
// L'origine du défaut est une asymétrie : « ORDER- » et « FOOD- » ont été créés
// dans le même geste, et seul « ORDER- » a été relu, chez order-service. Voir
// `FoodDeliveryReturnHandlers` — le prochain préfixe posera le même piège.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddOrderingGrpcClient(builder.Configuration);
builder.Services.AddInventoryGrpcClient(builder.Configuration);
builder.Services.AddDeliveryGrpcClient(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// LE CINQUIÈME CLIENT — CELUI SANS LEQUEL AUCUN REPAS N'AVAIT DE LIVREUR.
//
// La création de course lisait l'adresse de remise chez order-service, sans se
// demander d'où venait le ticket. Un ticket né d'une `MealOrder` porte un
// identifiant que order-service ne connaît pas : le gestionnaire levait, les
// reprises Kafka s'épuisaient, et le sac restait sur le passe. Muet côté
// client — la commande reste « confirmée » indéfiniment.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddFoodOrdersGrpcClient(builder.Configuration);

// Le choix de l'univers, en un seul endroit. Voir `LecteurDeCommandeALivrer`.
builder.Services.AddScoped<LecteurDeCommandeALivrer>();

// ═════════════════════════════════════════════════════════════════════════
// LE QUATRIÈME CLIENT — CELUI QUI REND LE RESTAURATEUR PAYABLE.
//
// `PUT /api/food/partner/restaurants/{id}/payout-seller` doit prouver que le
// dossier vendeur rattaché appartient bien au porteur du jeton et qu'il est
// actif. Le module Food ne peut pas le faire : sa frontière lui interdit de
// connaître Sellers. La composition root, elle, le peut — c'est exactement ce
// que dit `Restaurant.PayoutSellerId` : « vérifiés par la couche qui voit les
// deux ».
//
// `AddMerchantsGrpcClient` LIT `Services:Merchant` ET LÈVE À LA CONSTRUCTION
// DE L'HÔTE si l'adresse manque. Le conteneur sort avant la première requête,
// et aucune sonde ne le dit. La ligne `Services__Merchant` de compose est donc
// arrivée dans le même geste que cette ligne-ci.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddMerchantsGrpcClient(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// DEUX AMONTS POUR UNE SEULE PORTE D'ENTRÉE — ET C'EST DÉLIBÉRÉ.
//
// `MealOrderConfirmed` vient de food-order-service, `OrderConfirmed` de la
// marketplace. Personne ne consommait le premier : une commande passée par le
// parcours food traversait le paiement sans qu'aucun ticket ne s'ouvre — client
// débité, aucune cuisine servie, et rien pour le dire puisqu'un événement sans
// consommateur se consomme en silence.
//
// L'ancien reste enregistré LE TEMPS DE LA BASCULE. Tant que le chemin
// marketplace→food peut porter une commande de repas, le retirer rouvrirait la
// panne symétrique. Les deux ouvrent le ticket par la MÊME commande applicative,
// idempotente sur `OrderId` : aucun doublon n'en sort.
//
// Il s'enlèvera quand le contrat de confirmation commun sera DÉPLACÉ chez son
// propriétaire unique — le lot suivant, décrit dans `MealOrderIntegrationEvents`.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddScoped<
    IIntegrationEventHandler<MealOrderConfirmedIntegrationEvent>,
    ReceiveFoodOrderOnMealOrderConfirmedHandler>();

builder.Services.AddScoped<
    IIntegrationEventHandler<OrderConfirmedIntegrationEvent>,
    ReceiveFoodOrderOnOrderConfirmedHandler>();

builder.Services.AddScoped<
    IIntegrationEventHandler<FoodOrderReadyForPickupIntegrationEvent>,
    CreateDeliveryOnFoodOrderReadyHandler>();

// CES DEUX-LÀ VONT ENSEMBLE. La remise exige l'état « enlevée » (§20) :
// n'enregistrer que la seconde produirait un conflit sur chaque commande, et la
// chaîne resterait rompue au même endroit.
builder.Services.AddScoped<
    IIntegrationEventHandler<DeliveryPickedUpIntegrationEvent>,
    MarkFoodOrderPickedUpOnDeliveryPickedUpHandler>();

builder.Services.AddScoped<
    IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>,
    MarkFoodOrderDeliveredOnDeliveryCompletedHandler>();

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<FoodGrpcService>();
app.MapFoodEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<FoodDbContext>();

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
