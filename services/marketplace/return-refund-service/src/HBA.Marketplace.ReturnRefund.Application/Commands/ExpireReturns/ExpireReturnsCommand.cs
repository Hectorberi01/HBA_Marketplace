using HBA.Marketplace.ReturnRefund.Application.Abstractions;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Application.Commands.ExpireReturns;

/// <summary>Ferme les dossiers dont le délai de retour est dépassé.</summary>
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
            // depuis la sélection.
            if (dossier.Expire(maintenant).IsSuccess)
            {
                expires++;
            }
        }

        // UN SEUL `SaveChanges` POUR TOUT LE LOT, et aucun si rien n'a changé : le
        // contexte porte un journal d'audit (`KeepsAuditTrail`), donc chaque
        // sauvegarde à vide coûterait un aller-retour pour zéro ligne.
        if (expires > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return expires;
    }
}
