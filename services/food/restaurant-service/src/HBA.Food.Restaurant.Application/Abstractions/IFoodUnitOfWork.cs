using HBA.Shared.Application.Abstractions;

namespace HBA.Food.Application.Abstractions;

/// <summary>Unit of Work propre au module Food (évite la collision DI inter-modules).</summary>
public interface IFoodUnitOfWork : IUnitOfWork
{
}
