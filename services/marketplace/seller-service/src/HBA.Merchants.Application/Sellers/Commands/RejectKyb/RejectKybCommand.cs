using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.RejectKyb;

/// <summary>Rejette le dossier KYB d'un vendeur (modération, Admin).</summary>
public sealed record RejectKybCommand(Guid SellerId, string? Reason = null) : ICommand;
