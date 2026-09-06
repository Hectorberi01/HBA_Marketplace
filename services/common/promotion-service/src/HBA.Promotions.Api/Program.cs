using HBA.Food.Contracts.IntegrationEvents;
using HBA.Promotions.Infrastructure.Messaging.Kafka;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Promotions.Api.Endpoints;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Promotions.Infrastructure;
using HBA.Promotions.Infrastructure.Persistence;
using HBA.Shared.Hosting;
using HBA.Shared.IntegrationEvents;

using HBA.Promotions.Api.Grpc.Services;
using HBA.Promotions.Infrastructure.Grpc;
var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<PromotionsDbContext>(new PromotionsModuleInstaller());

// LES TROIS RPC DU §10.16 SONT LE CHEMIN PRINCIPAL, PAS UN EXTRA.
//
// `EvaluatePromotion`, `ReserveCoupon` et `CommitCoupon` sont appelés par les
// services de commande pendant les checkouts du §11. La seule route REST du
// service — `validate` — sert un écran ; c'est ici que passe l'argent.
builder.AddHbaGrpc();

// LES CLIENTS gRPC DE CE SERVICE SONT DANS SON MODULE (lot C).
//
// Ils etaient enregistres ici, un par un, chacun precede de la raison
// qui l'avait fait ajouter. Ces raisons ont voyage avec eux vers
// `Infrastructure/Grpc/DependencyInjection.cs` — les separer aurait
// produit deux mensonges : un commentaire sans code, du code sans raison.
builder.Services.AjouterClientsGrpcPromotions(builder.Configuration);

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
