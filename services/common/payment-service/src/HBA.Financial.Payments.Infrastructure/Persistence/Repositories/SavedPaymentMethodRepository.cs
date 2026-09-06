using Microsoft.EntityFrameworkCore;
using HBA.Financial.Payments.Domain.PaymentMethods;

namespace HBA.Financial.Payments.Infrastructure.Persistence;

internal sealed class SavedPaymentMethodRepository : ISavedPaymentMethodRepository
{
    private readonly PaymentsDbContext _dbContext;

    public SavedPaymentMethodRepository(PaymentsDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(SavedPaymentMethod paymentMethod, CancellationToken cancellationToken = default)
        => await _dbContext.SavedPaymentMethods.AddAsync(paymentMethod, cancellationToken);

    public void Remove(SavedPaymentMethod paymentMethod)
        => _dbContext.SavedPaymentMethods.Remove(paymentMethod);

    public async Task<SavedPaymentMethod?> GetByIdAsync(SavedPaymentMethodId id, CancellationToken cancellationToken = default)
        => await _dbContext.SavedPaymentMethods.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.SavedPaymentMethods
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.IsDefault)
            .ThenByDescending(p => p.CreatedOnUtc)
            .ToListAsync(cancellationToken);
}
