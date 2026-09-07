using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Delivery.Driver.Domain.Repositories;
using HBA.Drivers.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Drivers.Application.Accounts.Commands;

// LES TROIS DÉCISIONS DE L'EXPLOITATION.

/// <summary>L'exploitation valide un dossier : le livreur peut travailler.</summary>
public sealed record VerifyDriverCommand(Guid DriverId) : ICommand;

/// <summary>L'exploitation refuse un dossier.</summary>
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
