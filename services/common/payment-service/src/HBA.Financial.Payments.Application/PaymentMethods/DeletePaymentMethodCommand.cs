using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Financial.Payments.Application.Abstractions;
using HBA.Financial.Payments.Domain.PaymentMethods;

namespace HBA.Financial.Payments.Application.PaymentMethods;

/// <summary>Supprime un moyen de paiement de l'utilisateur.</summary>
public sealed record DeletePaymentMethodCommand(Guid UserId, Guid SavedPaymentMethodId) : ICommand;

internal sealed class DeletePaymentMethodCommandHandler : ICommandHandler<DeletePaymentMethodCommand>
{
    private readonly ISavedPaymentMethodRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public DeletePaymentMethodCommandHandler(ISavedPaymentMethodRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeletePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var method = await _repository.GetByIdAsync(new SavedPaymentMethodId(command.SavedPaymentMethodId), cancellationToken);
        if (method is null || method.UserId != command.UserId)
        {
            return Result.Failure(Error.NotFound("payments.payment_method.not_found", "Moyen de paiement introuvable."));
        }

        _repository.Remove(method);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
