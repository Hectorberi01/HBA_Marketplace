using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;
using HBA.Orders.Domain.Orders;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Orders.Application.Orders;
using HBA.Orders.Application.Orders.EventHandlers;

namespace HBA.Orders.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Le remboursement d'un retour est parti → la commande en garde la trace.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Orders.Application.Orders.EventHandlers.RecordReturnSettlementOnRefundHandler")]
public sealed class RecordReturnSettlementOnRefundHandler
    : IIntegrationEventHandler<ReturnRefundedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<RecordReturnSettlementOnRefundHandler> _logger;

    public RecordReturnSettlementOnRefundHandler(
        ISender sender, ILogger<RecordReturnSettlementOnRefundHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        ReturnRefundedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        // ZÉRO SIGNIFIE « INCONNU », PAS « RIEN ».
        var total = e.ReturnTotalRefundedAmount > 0m ? e.ReturnTotalRefundedAmount : e.RefundAmount;

        var lignes = e.Lines
            .Select(l => new ReturnSettlementLineDraft(l.OrderItemId, l.Quantity))
            .ToList();

        if (lignes.Count == 0)
        {
            // Le montant sera imputé, les quantités non : le plafond de la commande
            // se referme, mais la ligne restera retournable.
            _logger.LogWarning(
                "Remboursement de retour {Retour} sur la commande {Commande} sans détail de lignes : "
                + "le montant est imputé, les quantités retournées ne le sont pas.",
                e.ReturnRequestId, e.OrderId);
        }

        var resultat = await _sender.Send(
            new RecordReturnSettlementCommand(e.OrderId, e.ReturnRequestId, total, lignes),
            cancellationToken);

        if (resultat.IsFailure)
        {
            // ON LÈVE, ET C'EST VOULU.
            throw new InvalidOperationException(
                $"Impossible d'inscrire le remboursement du retour {e.ReturnRequestId} "
                + $"sur la commande {e.OrderId} : {resultat.Error.Code} — {resultat.Error.Message}");
        }
    }
}
