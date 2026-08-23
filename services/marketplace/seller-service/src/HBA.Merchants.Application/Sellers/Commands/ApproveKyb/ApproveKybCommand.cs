using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.ApproveKyb;

/// <summary>Valide le KYB d'un vendeur (modération, Admin).</summary>
public sealed record ApproveKybCommand(Guid SellerId) : ICommand;
