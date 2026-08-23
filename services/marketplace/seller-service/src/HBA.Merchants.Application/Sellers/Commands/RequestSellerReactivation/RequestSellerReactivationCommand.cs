using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.RequestSellerReactivation;

/// <summary>Le vendeur (compte fermé) demande la réactivation de son compte.</summary>
public sealed record RequestSellerReactivationCommand(Guid SellerId) : ICommand;
