using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.ApproveSellerReactivation;

/// <summary>L'admin approuve la demande de réactivation d'un vendeur.</summary>
public sealed record ApproveSellerReactivationCommand(Guid SellerId) : ICommand;
