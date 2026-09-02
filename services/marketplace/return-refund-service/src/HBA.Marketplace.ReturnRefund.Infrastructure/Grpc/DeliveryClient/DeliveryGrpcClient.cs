using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.DeliveryClient;

/// <summary>
/// BOUCHON : aucune course de retour n'est jamais créée.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CETTE CLASSE N'A PAS DE CLIENT gRPC. ELLE NE CONTACTE PERSONNE.
///
/// Pas de champ injecté, pas de constructeur : rien qu'une expression-corps qui
/// fabrique sa réponse sur place. Elle satisfait l'interface, le conteneur la
/// résout, l'appelant reçoit un succès — et AUCUNE COURSE D'ENLÈVEMENT N'EXISTE.
/// La chaîne `RET-DELIVERY-{returnId:N}` est fabriquée ici, inscrite sur le retour
/// par `RegisterReturnShipmentCommandHandler`, puis rendue au client comme numéro
/// d'enlèvement. Le client attend un livreur que personne n'a commandé, et son
/// numéro de suivi n'est connu d'aucun système.
///
/// C'est pire qu'une `NotImplementedException` : celle-là se verrait au premier
/// appel. Ici, tout se déroule normalement et rien ne se passe.
///
/// POURQUOI ELLE EST ENCORE LÀ.
///
/// L'implémentation réelle demande le `.proto` de delivery-service, un serveur en face, et le choix du
/// modèle de course de retour (qui paie, qui enlève, sous quel délai) — des décisions de contrat qui
/// dépassent le lot en cours. Ce qui a été fait à la place :
/// `ReturnRefundModuleInstaller.GuardSimulatedGrpcAdapters` REFUSE LE DÉMARRAGE
/// EN PRODUCTION tant que cette classe est enregistrée, et l'annonce bruyamment
/// partout ailleurs. Même règle que `SimulatedPayoutGateway` côté paiements.
///
/// CE QUI L'AVAIT LAISSÉE PASSER.
///
/// le contrôle `grpc-stubs` balayait `<dépôt>/src`, chemin hérité du
/// monolithe et inexistant dans ce monorepo. Il rendait « 0 bouchon » depuis
/// toujours. Réparé, il désigne cette classe.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
internal sealed class DeliveryGrpcClient : IDeliveryGrpcClient
{
    public Task<Result<string>> CreateReturnDeliveryAsync(Guid returnId, Guid orderId, Guid sellerId, Guid customerId, CancellationToken cancellationToken)
        => Task.FromResult<Result<string>>($"RET-DELIVERY-{returnId:N}");
}
