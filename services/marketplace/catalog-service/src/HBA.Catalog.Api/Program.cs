using HBA.Catalog.Api.Endpoints;
using HBA.Catalog.Infrastructure;
using HBA.Catalog.Infrastructure.Persistence;
using HBA.Media.Contracts.Grpc;
using HBA.Merchants.Contracts.Grpc;
using HBA.Products.Contracts.Grpc;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<CatalogDbContext>(new CatalogModuleInstaller());

// ═════════════════════════════════════════════════════════════════════════
// UNE SEULE LIGNE MANQUAIT POUR FERMER UN IDOR OUVERT DEPUIS L'ORIGINE.
//
// `CatalogEndpoints` affirmait que la propriété était invérifiable parce que
// « catalog-service ne référence pas HBA.Merchants.Contracts ». C'était faux :
// `HBA.Catalog.Application` le référence depuis toujours (pour les événements
// d'intégration du cycle de vie vendeur), le `COPY` est dans le Dockerfile et
// `SERVICES__MERCHANT` est dans le compose.
//
// Il ne manquait que l'ENREGISTREMENT du client. Le commentaire décrivait une
// impossibilité là où il y avait un oubli — et c'est ce qui l'a laissé vivre.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddMerchantsGrpcClient(builder.Configuration);

// ═════════════════════════════════════════════════════════════════════════
// LE MÊME OUBLI SE REJOUAIT AVEC LE MÉDIA.
//
// `AddProductMediaCommandHandler` dépend maintenant de `IMediaModuleApi` pour
// vérifier qu'une image appartient bien au produit avant de l'afficher. Cette
// dépendance ne se voit qu'à l'exécution : sans cette ligne, le conteneur
// démarre, la vitrine fonctionne, et SEUL le rattachement d'image casse — avec
// une 500 que rien ne relie à une configuration manquante.
//
// `SERVICES__MEDIA` est déjà dans `infra/docker/env/catalog.env` et dans le
// compose ; là encore, il ne manquait que l'enregistrement.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddMediaGrpcClient(builder.Configuration);

builder.AddHbaGrpc();

var app = builder.Build();

app.UseHbaService();

app.MapInternalGrpcService<CatalogGrpcService>();
app.MapCatalogEndpoints();

// ═════════════════════════════════════════════════════════════════════════
// SCHÉMA À JOUR AVANT D'OUVRIR LE PORT.
//
// Actif par défaut en Development seulement (Database:MigrateOnStartup).
// ═════════════════════════════════════════════════════════════════════════
await app.MigrateHbaDatabaseAsync<CatalogDbContext>();

app.Run();

/// <summary>Rendu visible pour <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program
{
}
