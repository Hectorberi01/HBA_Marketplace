using HBA.Food.Contracts.IntegrationEvents;
using HBA.Promotions.Infrastructure.Messaging.Kafka;
using HBA.Merchants.Contracts.Grpc;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Promotions.Api.Endpoints;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Promotions.Contracts.Grpc;
using HBA.Promotions.Infrastructure;
using HBA.Promotions.Infrastructure.Persistence;
using HBA.Shared.Hosting;
using HBA.Shared.IntegrationEvents;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<PromotionsDbContext>(new PromotionsModuleInstaller());

// LES TROIS RPC DU §10.16 SONT LE CHEMIN PRINCIPAL, PAS UN EXTRA.
//
// `EvaluatePromotion`, `ReserveCoupon` et `CommitCoupon` sont appelés par les
// services de commande pendant les checkouts du §11. La seule route REST du
// service — `validate` — sert un écran ; c'est ici que passe l'argent.
builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════════
// SANS CE CLIENT, LES TROIS ROUTES MARCHAND NE SAVENT PAS À QUI PARLE LE JETON.
//
// Elles étaient fermées à `RequireAdmin` PAR DÉFAUT DE PROPRIÉTAIRE — c'était
// écrit noir sur blanc dans `PromotionEndpoints`. D28 ajoute `OwnerSellerId` et
// ouvre les routes au vendeur PROPRIÉTAIRE ; encore faut-il pouvoir répondre à
// « ce compte, quel vendeur est-il ? ». Le jeton ne le dit pas, et un membre
// d'équipe n'a pas de dossier vendeur à son nom : seul seller-service sait relier
// les deux.
//
// `AddMerchantsGrpcClient` LÈVE à la construction de l'hôte si `Services:Merchant`
// est absent. C'est le bon sens de l'erreur : un service qui pilote des budgets
// promotionnels et démarre sans savoir vérifier l'appartenance vaut moins qu'un
// service qui ne démarre pas.
//
// CE SERVICE N'APPELAIT PERSONNE, ET SON COMPOSE LE DISAIT.
//
// Le bloc `promotion-service` de `docker-compose.dev.yml` portait un encadré
// « AUCUNE ADRESSE `SERVICES__*`, ET C'EST LE POINT FORT DE CE SERVICE ». Ce n'est
// plus vrai, et l'encadré a été corrigé au lieu d'être laissé à mentir : la
// dépendance est réelle, elle est d'AUTORISATION et non de calcul — promotion
// continue d'ignorer ce qu'est un produit, un plat ou un restaurant.
// ═════════════════════════════════════════════════════════════════════════════
builder.Services.AddMerchantsGrpcClient(builder.Configuration);

        // Les gestionnaires d'evenements sont enregistres par le module de
        // messagerie du service : `Messaging/Kafka/DependencyInjection.cs`.


// ═════════════════════════════════════════════════════════════════════════
// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
//
// Cet appel porte aussi l'outbox et l'inbox : l'oublier laisserait un service
// qui demarre et n'emet plus rien. `GardeDeCablage`, enregistree par
// l'installeur, refuse le demarrage dans ce cas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AjouterMessageriePromotions();

var app = builder.Build();

app.UseHbaService();

app.MapPromotionEndpoints();

// `MapInternalGrpcService` ET NON `MapGrpcService`.
//
// Le port gRPC n'est pas exposé par la passerelle : il ne parle qu'entre
// services, derrière la clé d'API interne. Le mapper en public offrirait
// `ReserveCoupon` — donc la consommation de budget — à qui atteint le port.
app.MapInternalGrpcService<PromotionGrpcService>();

// ═════════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<PromotionsDbContext>();

// Un Job de migration s'arrête ici : les schémas sont à jour, aucun port ne
// s'ouvre, et le conteneur se termine avec le code 0. Placé APRÈS le dernier
// `MigrateHbaDatabaseAsync` — plusieurs services portent plusieurs DbContext, et
// sortir après le premier laisserait les autres bases sans schéma.
if (app.SortirApresMigrations())
{
    return;
}

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
