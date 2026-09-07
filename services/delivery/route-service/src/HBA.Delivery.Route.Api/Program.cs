using HBA.Routes.Api.Endpoints;
using HBA.Routes.Infrastructure.Messaging.Kafka;
using HBA.Routes.Infrastructure;
using HBA.Shared.Hosting;

using HBA.Routes.Api.Grpc.Services;
var builder = WebApplication.CreateBuilder(args);

// CE SERVICE N'AVAIT AUCUNE AUTHENTIFICATION.
builder.AddHbaSecurity();

builder.Services.AddRoutesInfrastructure(builder.Configuration);
builder.AddHbaGrpc();

// TOUT CE QUE CE SERVICE ECOUTE ET PUBLIE EST DECLARE DANS SON PROPRE MODULE.
builder.Services.AjouterMessagerieDeliveryRoute();

var app = builder.Build();

app.UseHbaSecurity();

app.MapGet("/health/live", () => Results.Ok(new { status = "live", service = "HBA.Routes" })).AllowAnonymous();
// SONDE CONSTANTE : ELLE NE PEUT PAS ÉCHOUER (lot 9.5).
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", service = "HBA.Routes" })).AllowAnonymous();

app.MapRouteEndpoints();
app.MapInternalGrpcService<RoutesGrpcService>();

app.Run();

public partial class Program
{
}
