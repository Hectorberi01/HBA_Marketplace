using HBA.Shared.Application.Abstractions;

namespace HBA.Commerce.Application.Abstractions;

/// <summary>Unit of Work propre au module Cart (évite la collision DI inter-modules).</summary>
public interface ICartUnitOfWork : IUnitOfWork
{
}
