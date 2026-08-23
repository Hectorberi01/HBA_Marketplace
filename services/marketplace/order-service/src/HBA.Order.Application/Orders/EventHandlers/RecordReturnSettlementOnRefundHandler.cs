using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Application.Orders.Commands;
using HBA.Orders.Domain.Orders;

namespace HBA.Orders.Application.Orders.EventHandlers;

/// <summary>
/// Le remboursement d'un retour est parti → la commande en garde la trace.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CE GESTIONNAIRE, LE MÊME ARTICLE SE REMBOURSE INDÉFINIMENT (ISSUE-014).
///
/// `OrderingModuleApi.GetOrderReturnContextAsync` est la lecture sur laquelle
/// return-refund fonde CHAQUE ouverture de dossier et CHAQUE plafond de
/// remboursement. Elle répondait `AlreadyReturnedQuantity: 0` et
/// `AlreadyRefundedAmount: 0m` en dur, faute de la moindre source : order-service
/// ne possède pas les retours. Chaque nouvelle demande repartait donc de zéro,
/// et les deux garde-fous de return-refund — quantité encore retournable, plafond
/// de la commande — s'exécutaient sur des valeurs fausses.
///
/// Ce gestionnaire est le fil qui manquait. Il est branché sur l'événement du
/// versement ABOUTI, pas sur celui de la décision : `ReturnRefundApproved`
/// annonce une intention, que rien ne garantit d'aboutir. Imputer la marchandise
/// dès la décision fermerait le plafond d'un client dont le remboursement finit
/// par échouer chez l'opérateur.
///
/// CE QUE CELA NE FERME PAS, ET OÙ C'EST FERMÉ.
///
/// Entre la décision et le versement, order-service ne voit rien : deux dossiers
/// ouverts en parallèle sur la même ligne passeraient tous deux ce contrôle-ci.
/// Cette fenêtre appartient à return-refund, qui possède ses propres dossiers en
/// cours et les compte — voir `CreateReturnCommandHandler`.
///
/// L'IDEMPOTENCE N'EST PAS ICI, ET ELLE EST DOUBLE.
///
/// La trace d'inbox est posée par `IntegrationEventDispatcher` avant l'appel et
/// committée par le `SaveChanges` de la commande. Et l'agrégat, lui, POSE des
/// valeurs cumulées au lieu de les additionner : même sans inbox, un rejeu
/// n'impute rien de plus.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
        //
        // `ReturnTotalRefundedAmount` est un champ AJOUTÉ (décision D32, additive) :
        // un producteur antérieur à la correction ne le remplit pas. Retomber sur
        // `RefundAmount` — le montant de CE versement — vaut alors mieux que de
        // n'imputer aucun montant : c'est exact tant que le dossier n'a versé
        // qu'une fois, ce qui est le cas de tous les dossiers d'aujourd'hui
        // (`MarkRefundSucceeded` clôt le dossier en `Refunded`).
        var total = e.ReturnTotalRefundedAmount > 0m ? e.ReturnTotalRefundedAmount : e.RefundAmount;

        var lignes = e.Lines
            .Select(l => new ReturnSettlementLineDraft(l.OrderItemId, l.Quantity))
            .ToList();

        if (lignes.Count == 0)
        {
            // Le montant sera imputé, les quantités non : le plafond de la commande
            // se referme, mais la ligne restera retournable. C'est le seul cas où
            // ISSUE-014 demeure partiellement ouvert, et il se voit.
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
            //
            // L'exception traverse le dispatcher : ni l'effet ni la trace d'inbox
            // ne sont committés, et le message est rejoué. Avaler l'échec ici
            // laisserait la commande croire que rien n'est jamais revenu — soit
            // exactement le défaut que ce gestionnaire existe pour fermer.
            //
            // Le seul échec attendu est « commande introuvable », qui ne peut
            // venir que d'une base incohérente : un retour existe forcément sur
            // une commande livrée.
            throw new InvalidOperationException(
                $"Impossible d'inscrire le remboursement du retour {e.ReturnRequestId} "
                + $"sur la commande {e.OrderId} : {resultat.Error.Code} — {resultat.Error.Message}");
        }
    }
}
