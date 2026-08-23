using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Delivery.Driver.Domain.Repositories;
using HBA.Drivers.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Drivers.Application.Accounts.Commands;

// ═════════════════════════════════════════════════════════════════════════════
// LES TROIS DÉCISIONS DE L'EXPLOITATION.
//
// ELLES PRENNENT UN `DriverId`, PAS UN `UserId`, ET CE N'EST PAS UNE ENTORSE
// À LA RÈGLE DU JETON.
//
// L'appelant n'est PAS le titulaire du dossier : c'est un administrateur qui
// arbitre celui de quelqu'un d'autre. Il n'y a donc rien à déduire de son jeton,
// et l'identifiant doit venir de l'URL. La protection ici est le RÔLE — ces
// commandes ne sont exposées que sous `MapAdminGroup` — pas l'appartenance.
//
// C'est la même distinction que `FinancialEndpoints` fait entre `/wallets/me` et
// les routes d'administration : le `/me` interdit l'identifiant, l'administration
// l'exige.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>L'exploitation valide un dossier : le livreur peut travailler.</summary>
public sealed record VerifyDriverCommand(Guid DriverId) : ICommand;

/// <summary>L'exploitation refuse un dossier. Le livreur peut redéposer ses pièces.</summary>
public sealed record RejectDriverCommand(Guid DriverId, string? Reason) : ICommand;

/// <summary>L'exploitation écarte un livreur déjà vérifié.</summary>
public sealed record SuspendDriverCommand(Guid DriverId, string? Reason) : ICommand;

internal sealed class DriverVerificationCommandHandler
    : ICommandHandler<VerifyDriverCommand>,
      ICommandHandler<RejectDriverCommand>,
      ICommandHandler<SuspendDriverCommand>
{
    private readonly IDriverAccountRepository _accounts;
    private readonly IDriverUnitOfWork _unitOfWork;

    public DriverVerificationCommandHandler(IDriverAccountRepository accounts, IDriverUnitOfWork unitOfWork)
    {
        _accounts = accounts;
        _unitOfWork = unitOfWork;
    }

    public Task<Result> Handle(VerifyDriverCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.DriverId, account => account.Verify(), cancellationToken);

    public Task<Result> Handle(RejectDriverCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.DriverId, account => account.Reject(command.Reason), cancellationToken);

    public Task<Result> Handle(SuspendDriverCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.DriverId, account => account.Suspend(command.Reason), cancellationToken);

    private async Task<Result> MutateAsync(
        Guid driverId,
        Func<DriverAccount, Result> mutate,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.GetByIdAsync(driverId, cancellationToken);
        if (account is null)
        {
            return Result.Failure(Error.NotFound("driver.not_found", "Dossier livreur introuvable."));
        }

        var result = mutate(account);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
