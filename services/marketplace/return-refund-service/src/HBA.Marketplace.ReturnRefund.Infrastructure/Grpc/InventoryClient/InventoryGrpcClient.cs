using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.InventoryClient;

/// <summary>
/// BOUCHON : la marchandise retournée n'est jamais remise en stock.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CETTE CLASSE N'A PAS DE CLIENT gRPC. ELLE NE CONTACTE PERSONNE.
///
/// Pas de champ injecté, pas de constructeur : rien qu'une expression-corps qui
/// fabrique sa réponse sur place. Elle satisfait l'interface, le conteneur la
/// résout, l'appelant reçoit un succès — et AUCUNE UNITÉ N'ENTRE À L'INVENTAIRE.
/// `InspectReturnCommandHandler` clôt l'inspection en « remettable en rayon », la
/// marchandise est physiquement à l'entrepôt, et le catalogue l'ignore. Elle ne
/// sera jamais revendue, et rien n'indiquera pourquoi le stock ne remonte pas.
///
/// C'est pire qu'une `NotImplementedException` : celle-là se verrait au premier
/// appel. Ici, tout se déroule normalement et rien ne se passe.
///
/// POURQUOI ELLE EST ENCORE LÀ.
///
/// L'implémentation réelle demande le `.proto` d'inventory-service et un serveur en face — des décisions de contrat qui
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
internal sealed class InventoryGrpcClient : IInventoryGrpcClient
{
    public Task<Result> ProcessReturnedStockAsync(Guid returnId, Guid orderItemId, StockDisposition disposition, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success());
}
