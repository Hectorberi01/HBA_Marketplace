using HBA.Shared.Application.Abstractions;

namespace HBA.Communication.Application.Abstractions;

/// <summary>Unit of Work propre au module Messaging (évite la collision DI inter-modules).</summary>
public interface IMessagingUnitOfWork : IUnitOfWork
{
}
