using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.DeleteSeller;

/// <summary>Suppression DÉFINITIVE d'un vendeur (admin).</summary>
public sealed record DeleteSellerCommand(Guid SellerId) : ICommand;
