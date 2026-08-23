using HBA.Shared.Application.Abstractions;

namespace HBA.FoodOrders.Application.Abstractions;

/// <summary>Unit of Work propre au module FoodOrders (évite la collision DI inter-modules).</summary>
public interface IMealOrderUnitOfWork : IUnitOfWork
{
}
