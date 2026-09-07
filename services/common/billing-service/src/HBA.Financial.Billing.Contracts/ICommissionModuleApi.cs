namespace HBA.Financial.Billing.Contracts;

/// <summary>API in-process publique du module Billing.</summary>
public interface ICommissionModuleApi
{
    Task<CommissionResult> ComputeCommissionAsync(
        Guid sellerId, Guid categoryId, decimal grossAmount, string currency, CancellationToken cancellationToken = default);
}
