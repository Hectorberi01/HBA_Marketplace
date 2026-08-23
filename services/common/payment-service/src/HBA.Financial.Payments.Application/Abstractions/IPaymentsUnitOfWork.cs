using HBA.Shared.Application.Abstractions;

namespace HBA.Financial.Payments.Application.Abstractions;

/// <summary>Unit of Work propre au module Payments (évite la collision DI inter-modules).</summary>
public interface IPaymentsUnitOfWork : IUnitOfWork
{
}
