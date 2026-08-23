using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Application.Commands.ExpireReturns;

/// <summary>
/// Ferme les dossiers dont le délai de retour est dépassé. Rend le nombre de
/// dossiers expirés.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// `ExpiresAtUtc` ÉTAIT CALCULÉE, ÉCRITE… ET JAMAIS RELUE.
///
/// `ReturnRequest` fixe `ExpiresAtUtc` à la création, la colonne existe depuis la
/// migration initiale, et `ReturnStateMachine` déclare deux chemins vers
/// `Expired`. Aucun code ne les empruntait : `ExpireReturnsWorker` journalisait
/// « active » et rendait la main.
///
/// Conséquence : un dossier ouvert puis abandonné restait `AwaitingApproval`
/// indéfiniment. Il pesait dans la file de travail du vendeur, il continuait
/// d'apparaître au client comme « en cours », et surtout il restait ÉLIGIBLE à un
/// remboursement des mois après la fin de la fenêtre de retour.
///
/// DEUX ÉTATS SEULEMENT, ET C'EST LA MACHINE QUI LE DIT.
///
/// `AwaitingApproval` (on attend le vendeur) et `AwaitingReturn` (on attend le
/// colis du client) sont les seuls états depuis lesquels `Expired` est atteignable.
/// Un dossier reçu, inspecté ou remboursé ne s'expire pas — il se conclut. Le
/// filtre est dans la requête ET la garde dans l'agrégat : un dossier peut avoir
/// avancé entre la sélection et l'écriture.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ExpireReturnsCommand(int BatchSize = 50) : ICommand<int>;

internal sealed class ExpireReturnsCommandHandler : ICommandHandler<ExpireReturnsCommand, int>
{
    private readonly IReturnRequestRepository _returns;
    private readonly IReturnRefundUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ExpireReturnsCommandHandler(
        IReturnRequestRepository returns,
        IReturnRefundUnitOfWork unitOfWork,
        IClock clock)
    {
        _returns = returns;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<int>> Handle(ExpireReturnsCommand command, CancellationToken cancellationToken)
    {
        var maintenant = _clock.UtcNow;
        var dossiers = await _returns.ListExpirableAsync(maintenant, command.BatchSize, cancellationToken);

        var expires = 0;
        foreach (var dossier in dossiers)
        {
            // Un refus n'est pas un incident : il signifie que le dossier a bougé
            // depuis la sélection. On passe au suivant sans rien annuler du lot.
            if (dossier.Expire(maintenant).IsSuccess)
            {
                expires++;
            }
        }

        // UN SEUL `SaveChanges` POUR TOUT LE LOT, et aucun si rien n'a changé :
        // le contexte porte un journal d'audit (`KeepsAuditTrail`), donc chaque
        // sauvegarde à vide coûterait un aller-retour pour zéro ligne.
        if (expires > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return expires;
    }
}
