namespace HBA.Financial.Payments.Domain.PaymentMethods;

/// <summary>Accès au stockage des moyens de paiement enregistrés.</summary>
public interface ISavedPaymentMethodRepository
{
    Task AddAsync(SavedPaymentMethod paymentMethod, CancellationToken cancellationToken = default);

    void Remove(SavedPaymentMethod paymentMethod);

    Task<SavedPaymentMethod?> GetByIdAsync(SavedPaymentMethodId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
