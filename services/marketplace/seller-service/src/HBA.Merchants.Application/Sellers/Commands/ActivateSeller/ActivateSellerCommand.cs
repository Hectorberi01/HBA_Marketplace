using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.ActivateSeller;

/// <summary>Active un vendeur (KYB validé + payout requis).</summary>
public sealed record ActivateSellerCommand(Guid SellerId) : ICommand;
