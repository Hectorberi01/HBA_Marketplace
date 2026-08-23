using HBA.Shared.Application.Abstractions;

namespace HBA.Financial.Billing.Application.Abstractions;

/// <summary>Unit of Work propre au module Billing (évite la collision DI inter-modules).</summary>
public interface IBillingUnitOfWork : IUnitOfWork
{
}
