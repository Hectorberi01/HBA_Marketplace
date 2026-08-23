using HBA.Shared.Application.Abstractions;

namespace HBA.Identity.Application.Abstractions;

/// <summary>Unit of Work propre au module Identity (évite la collision DI inter-modules).</summary>
public interface IIdentityUnitOfWork : IUnitOfWork
{
}
