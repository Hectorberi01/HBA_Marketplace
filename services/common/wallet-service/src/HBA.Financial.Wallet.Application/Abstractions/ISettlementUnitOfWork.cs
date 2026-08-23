using HBA.Shared.Application.Abstractions;

namespace HBA.Financial.Wallet.Application.Abstractions;

/// <summary>Unit of Work propre au module Settlement (évite la collision DI inter-modules).</summary>
public interface IWalletUnitOfWork : IUnitOfWork
{
}
