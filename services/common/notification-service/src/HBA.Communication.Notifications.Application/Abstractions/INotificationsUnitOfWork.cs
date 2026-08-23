using HBA.Shared.Application.Abstractions;

namespace HBA.Communication.Notifications.Application.Abstractions;

/// <summary>Unit of Work propre au module Notifications (évite la collision DI inter-modules).</summary>
public interface INotificationsUnitOfWork : IUnitOfWork
{
}
