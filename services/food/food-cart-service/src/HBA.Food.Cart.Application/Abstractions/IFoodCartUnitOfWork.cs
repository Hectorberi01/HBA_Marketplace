using HBA.Shared.Application.Abstractions;

namespace HBA.FoodCarts.Application.Abstractions;

/// <summary>Unit of Work propre au module FoodCart (évite la collision DI inter-modules).</summary>
public interface IFoodCartUnitOfWork : IUnitOfWork
{
}
