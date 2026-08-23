using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Domain.PaymentMethods;

namespace HBA.Financial.Payments.Application.PaymentMethods;

/// <summary>Définit le moyen de paiement par défaut de l'utilisateur.</summary>
public sealed record SetDefaultPaymentMethodCommand(Guid UserId, Guid SavedPaymentMethodId) : ICommand;

internal sealed class SetDefaultPaymentMethodCommandHandler : ICommandHandler<SetDefaultPaymentMethodCommand>
{
    private readonly ISavedPaymentMethodRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public SetDefaultPaymentMethodCommandHandler(ISavedPaymentMethodRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetDefaultPaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var methods = await _repository.ListByUserAsync(command.UserId, cancellationToken);
        var target = methods.FirstOrDefault(m => m.Id.Value == command.SavedPaymentMethodId);
        if (target is null)
        {
            return Result.Failure(Error.NotFound("payments.payment_method.not_found", "Moyen de paiement introuvable."));
        }

        foreach (var method in methods.Where(m => m.IsDefault))
        {
            method.ClearDefault();
        }

        target.MarkDefault();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
