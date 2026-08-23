using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.RequestSellerClosure;

/// <summary>Fermeture du compte demandée par le vendeur (suppression partielle).</summary>
public sealed record RequestSellerClosureCommand(Guid SellerId) : ICommand;
