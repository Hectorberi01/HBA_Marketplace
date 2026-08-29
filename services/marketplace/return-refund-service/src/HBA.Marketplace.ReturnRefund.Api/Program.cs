using HBA.Marketplace.ReturnRefund.Api.Endpoints;
using HBA.Marketplace.ReturnRefund.Infrastructure;
using HBA.Marketplace.ReturnRefund.Infrastructure.Persistence;
using HBA.Merchants.Contracts.Grpc;
using HBA.Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddHbaService<ReturnRefundDbContext>(new ReturnRefundModuleInstaller());
builder.AddHbaGrpc();

// ═════════════════════════════════════════════════════════════════════════
// SANS CE CLIENT, LES ROUTES VENDEUR NE SAVENT PAS À QUI PARLE LE JETON.
//
// Les six routes de `/api/v1/seller/returns` exigeaient le rôle `Seller` et
// rien d'autre : tout vendeur inscrit arbitrait — et CHIFFRAIT le
// remboursement — du dossier d'un concurrent. Le vendeur d'un retour n'est
// pas dans le jeton, il est dans la ressource ; seul seller-service sait
// relier les deux.
//
// `AddMerchantsGrpcClient` LÈVE à la construction de l'hôte si
// `Services:Merchant` est absent. C'est voulu, et c'est le bon sens de
// l'erreur : un service d'arbitrage de remboursements qui démarre sans savoir
// vérifier l'appartenance vaut moins qu'un service qui ne démarre pas.
// ═════════════════════════════════════════════════════════════════════════
builder.Services.AddMerchantsGrpcClient(builder.Configuration);

var app = builder.Build();

app.UseHbaService("return-refund-service", "MARKETPLACE_RETURN_REFUND");

app.MapCustomerReturnsEndpoints();
app.MapSellerReturnsEndpoints();
app.MapAdminReturnsEndpoints();

// ═════════════════════════════════════════════════════════════════════════════
// `MapReturnPolicyEndpoints()` A ÉTÉ RETIRÉ — IL RÉPONDAIT ET N'ÉCRIVAIT RIEN.
//
// Les deux routes de `/api/v1/admin/return-policies` étaient deux lambdas sans
// la moindre dépendance injectée : ni `ISender`, ni dépôt, ni `DbContext`. Le
// `GET` rendait une politique écrite en dur dans la lambda ; le `POST` renvoyait
// la requête reçue, enrobée d'un 201.
//
// La route était relayée par la passerelle. Un écran d'administration construit
// dessus aurait affiché « politique enregistrée » à chaque envoi, et la fenêtre
// de retour de la plateforme serait restée la même, indéfiniment. C'est pire
// qu'une absence : une absence se voit.
//
// CE RETRAIT NE REND PAS LA POLITIQUE DE RETOUR CONFIGURABLE POUR AUTANT.
//
// `ReturnPolicyRepository.GetApplicableSnapshotAsync` — celui que
// `CreateReturnCommand` appelle réellement — rend lui aussi une constante, sans
// toucher la base et sans lire ses deux paramètres. Toute la plateforme applique
// donc la même politique : fenêtre de 14 jours, preuve et inspection exigées,
// 0 % de frais de remise en stock, retour à la charge du client pour
// `ChangedMind`, approbation automatique pour `WrongItem` et
// `DamagedOnArrival`.
//
// Rendre cette politique variable est un lot à part entière — agrégat, table,
// migration, résolution par portée — et il devra remonter des routes
// d'administration à ce moment-là. `ReturnPolicyDto` et `UpsertReturnPolicyDto`
// sont conservés pour cela.
// ═════════════════════════════════════════════════════════════════════════════

await app.MigrateHbaDatabaseAsync<ReturnRefundDbContext>();

// Un Job de migration s'arrête ici : les schémas sont à jour, aucun port ne
// s'ouvre, et le conteneur se termine avec le code 0. Placé APRÈS le dernier
// `MigrateHbaDatabaseAsync` — plusieurs services portent plusieurs DbContext, et
// sortir après le premier laisserait les autres bases sans schéma.
if (app.SortirApresMigrations())
{
    return;
}

app.Run();

public partial class Program
{
}
