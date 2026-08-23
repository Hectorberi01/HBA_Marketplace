using HBA.Shared.Application.Abstractions;

namespace HBA.Orders.Application.Abstractions;

/// <summary>Unit of Work propre au module Ordering (évite la collision DI inter-modules).</summary>
public interface IOrderingUnitOfWork : IUnitOfWork
{
}
