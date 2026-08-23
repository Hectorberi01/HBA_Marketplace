using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Orders.Application.Abstractions;
using HBA.Orders.Domain.Orders;

namespace HBA.Orders.Application.Orders.Commands;

/// <summary>
/// Inscrit sur la commande ce qu'un dossier de retour lui a définitivement
/// retiré : l'argent rendu, et les exemplaires repris ligne à ligne.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST LA SEULE SOURCE D'ORDER-SERVICE SUR LES RETOURS (ISSUE-014).
///
/// Order-service ne possède pas les dossiers de retour et n'a aucun moyen de les
/// interroger sur le chemin d'une lecture. Tant que rien ne les lui apprenait,
/// `GetOrderReturnContextAsync` répondait `AlreadyReturnedQuantity: 0` et
/// `AlreadyRefundedAmount: 0m` en dur — et le même exemplaire se retournait,
/// puis se remboursait, autant de fois qu'on ouvrait de demandes.
///
/// LES MONTANTS ET QUANTITÉS SONT CUMULÉS POUR LE DOSSIER, PAS INCRÉMENTAUX.
///
/// L'agrégat les POSE au maximum vu au lieu de les additionner : un message
/// rejoué — Kafka en livre — n'impute rien de plus, et un message arrivé dans le
/// désordre ne fait pas reculer le compteur. Voir <see cref="Order.RecordReturnSettlement"/>.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record RecordReturnSettlementCommand(
    Guid OrderId,
    Guid ReturnRequestId,
    decimal TotalRefundedAmount,
    IReadOnlyCollection<ReturnSettlementLineDraft> Lines) : ICommand;

internal sealed class RecordReturnSettlementCommandHandler : ICommandHandler<RecordReturnSettlementCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public RecordReturnSettlementCommandHandler(IOrderRepository orderRepository, IOrderingUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RecordReturnSettlementCommand command, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("ordering.not_found", "Commande introuvable."));
        }

        var enregistre = order.RecordReturnSettlement(
            command.ReturnRequestId,
            command.TotalRefundedAmount,
            command.Lines,
            DateTime.UtcNow);

        if (enregistre.IsFailure)
        {
            return enregistre;
        }

        // TOUJOURS, MÊME QUAND RIEN N'A BOUGÉ.
        //
        // Un rejeu ne change aucune valeur de l'agrégat — mais la trace d'inbox,
        // posée par le dispatcher AVANT l'appel, n'est committée que par ce
        // `SaveChanges`. S'en dispenser quand « rien n'a changé » laisserait la
        // trace en attente et rendrait le message éternellement rejouable.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
