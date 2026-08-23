using HBA.DeliveryPricing.Contracts.Grpc;
using HBA.Commerce.Contracts.Grpc;
using HBA.Deliveries.Contracts.Grpc;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Food.Contracts.Grpc;
using HBA.Orders.Api.Integration;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Inventory.Contracts.Grpc;
using HBA.Products.Contracts.Grpc;
using HBA.Merchants.Contracts.Grpc;
using HBA.Orders.Api.Endpoints;
using HBA.Orders.Infrastructure;
using HBA.Orders.Infrastructure.Persistence;
using HBA.Ordering.Contracts.Grpc;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<OrderingDbContext>(new OrderingModuleInstaller());
builder.Services.AddInventoryGrpcClient(builder.Configuration);

// LE CATALOGUE, POUR REVÉRIFIER LE PRIX AU CHECKOUT (ISSUE-048).
//
// Le prix, le statut « publié » et l'achetabilité d'une offre n'étaient jamais
// relus entre l'ajout au panier et le paiement : tout venait du panier, qui fige
// son prix à l'ajout. `Services:Catalog` est déjà fourni à ce service.
builder.Services.AddProductsGrpcClient(builder.Configuration);

// LE PANIER VIT DANS commerce-service, ET LE CHECKOUT EN DÉPEND.
//
// `PlaceOrderCommandHandler` lit le panier valorisé pour figer ses prix. Sans
// ce client, `ICartModuleApi` n'a aucune implémentation et la validation du
// conteneur refuse de démarrer le service — ce qu'elle a fait.
builder.Services.AddCommerceGrpcClient(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// LA COURSE D'UNE COMMANDE MARKETPLACE — LE MAILLON QUI MANQUAIT.
//
// Shipping posait la référence dans le monolithe ; il n'a jamais été extrait.
// Depuis, la chaîne s'arrêtait au paiement : aucune course, donc jamais
// « livrée », donc aucun escrow libéré et AUCUN VENDEUR RÉGLÉ.
//
// Le gestionnaire vit dans le composition root : il connaît la commande, le
// lieu d'expédition et le transporteur — trois mondes que la couche Application
// n'a pas à connaître ensemble.
// ═════════════════════════════════════════════════════════════════════════
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

// CE CLIENT NE SERT QU'À UNE AUTORISATION.
//
// `GET /api/sellers/{sellerId}/orders` doit vérifier que l'appelant EST ce
// vendeur. Le jeton porte un identifiant d'utilisateur, la route un identifiant
// de vendeur ; seule merchant-service connaît la correspondance. Sans lui, la
// route rendait le carnet de commandes de n'importe quel vendeur à n'importe
// quel inscrit.
builder.Services.AddMerchantsGrpcClient(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// ORDER-SERVICE DOIT SAVOIR TRADUIRE « FOOD-… », ET SEULEMENT POUR CELA.
//
// Une course de repas annulée doit mettre la commande commerciale en arbitrage.
// Sa référence porte l'identifiant du TICKET DE CUISINE, inconnu de cette base ;
// `IFoodModuleApi.GetOrderAsync` fait la correspondance, et ne rend que des
// rattachements — ni lignes, ni prix, ni ticket.
//
// Le chemin habituel (food-service traduit, puis republie) ne convenait pas ici :
// le seul geste qu'il aurait sur son ticket, `CancelFoodOrderCommand`, publie
// `FoodOrderCancelled` — que ce service consomme en ANNULANT la commande. Le
// détour « propre » aurait donc produit exactement le remboursement automatique
// qu'on refuse. Voir `HoldOrderOnDeliveryCancelledHandler`.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddFoodGrpcClient(builder.Configuration);

// ENREGISTRÉ AUSSI SOUS SON TYPE CONCRET.
//
// La route de relance d'arbitrage (`POST /api/admin/orders/{id}/review/resume`)
// rejoue l'étape « créer la course d'une commande confirmée » en appelant
// `DemanderCourseAsync`. Sans cette ligne, seul le contrat d'événement serait
// résoluble et il faudrait fabriquer un faux `OrderConfirmed` pour y entrer —
// c'est-à-dire faire croire qu'une confirmation a eu lieu.
builder.Services.AddScoped<CreateDeliveryOnOrderConfirmedHandler>();

builder.Services.AddScoped<
    IIntegrationEventHandler<OrderConfirmedIntegrationEvent>>(
    sp => sp.GetRequiredService<CreateDeliveryOnOrderConfirmedHandler>());

// ═════════════════════════════════════════════════════════════════════════
// LES DEUX SENS DE LA COURSE, ENFIN BRANCHÉS.
//
// `DeliveryCancelled` N'AVAIT QU'UN CONSOMMATEUR, INTERNE À DELIVERY.
//
// Le webhook partenaire. Rien ne remontait ici : une course annulée laissait la
// commande `Confirmed` POUR TOUJOURS — payée, stock décrémenté, escrow gelé, et
// un acheteur qui attend un colis que personne n'apportera.
//
// ET LA RÉCIPROQUE MANQUAIT AUSSI : une commande annulée laissait sa course
// vivante, et un livreur partait chercher un colis que le vendeur ne remettrait
// pas.
//
// Les deux se répondent, d'où le garde-fou anti-boucle documenté dans
// `OrderDeliveryCancellation`.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddScoped<
    IIntegrationEventHandler<DeliveryCancelledIntegrationEvent>,
    HoldOrderOnDeliveryCancelledHandler>();

builder.Services.AddScoped<
    IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
    CancelDeliveryOnOrderCancelledHandler>();

builder.AddHbaGrpc();

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<OrderingGrpcService>();
app.MapOrderEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<OrderingDbContext>();

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
