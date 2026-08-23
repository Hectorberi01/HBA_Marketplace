using HBA.DeliveryPricing.Contracts.Grpc;
using HBA.Deliveries.Contracts.Grpc;
using HBA.Food.Contracts.Grpc;
using HBA.FoodCarts.Contracts.Grpc;
using HBA.FoodOrders.Api.Endpoints;
using HBA.FoodOrders.Contracts.Grpc;
using HBA.FoodOrders.Infrastructure;
using HBA.FoodOrders.Infrastructure.Persistence;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<MealOrderingDbContext>(new MealOrderingModuleInstaller());

// LE PANIER VIT DANS food-cart-service, ET LE PASSAGE EN COMMANDE EN DÉPEND.
//
// Sans ce client, `IFoodCartModuleApi` n'a aucune implémentation et la
// validation du conteneur refuse de démarrer le service. C'est exactement ce qui
// est arrivé à order-service le jour où le panier a été extrait.
builder.Services.AddFoodCartsGrpcClient(builder.Configuration);

// La carte et l'appartenance au personnel : le restaurant prend-il encore des
// commandes, et ce compte y travaille-t-il ?
builder.Services.AddFoodGrpcClient(builder.Configuration);

// LE DEVIS DE COURSE, RELU ET JAMAIS REDEMANDÉ.
//
// `RequestQuoteAsync` ÉCRIT et rendrait un SECOND prix, calculé sur la grille de
// l'instant : on facturerait un montant que le client n'a jamais accepté. Seule
// `LookupQuoteAsync` satisfait les deux exigences — le serveur impose le prix, ET
// c'est le prix affiché.
builder.Services.AddDeliveryGrpcClient(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// LE DEVIS DE COURSE SE RELIT CHEZ delivery-pricing.
//
// `DeliveryApi.LookupQuote` n'a JAMAIS eu de corps de serveur : le checkout
// rendait `UNIMPLEMENTED` sur toute commande portant un devis. Et
// delivery-service n'a plus de domaine de tarification — l'implémenter chez lui
// aurait interrogé une table vide. Ce client-ci apporte `IDeliveryQuoteLookup`.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddDeliveryPricingGrpcClient(builder.Configuration);

builder.AddHbaGrpc();

var app = builder.Build();

app.UseHbaService();

// SANS CETTE LIGNE, food-cart-service NE PEUT PAS SAVOIR SI L'ACHETEUR EN EST
// À SA PREMIÈRE COMMANDE.
//
// Le client existe de l'autre côté, la configuration pointe la bonne adresse, et
// l'appel rend « UNIMPLEMENTED ». Le symptôme apparaît à la première lecture de
// panier, pas au démarrage.
app.MapInternalGrpcService<FoodOrderGrpcService>();

app.MapMealOrderEndpoints();

// ═════════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<MealOrderingDbContext>();

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
