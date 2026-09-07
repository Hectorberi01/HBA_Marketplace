using HBA.Shared.Application.Abstractions;

namespace HBA.Analytics.Application.Abstractions;

/// <summary>
/// Unit of Work propre au module Analytics (évite la collision DI inter-modules).
/// </summary>
public interface IAnalyticsUnitOfWork : IUnitOfWork
{
}
