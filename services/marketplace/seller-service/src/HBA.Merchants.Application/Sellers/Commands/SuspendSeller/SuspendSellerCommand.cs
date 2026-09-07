using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.SuspendSeller;

/// <summary>Suspend un vendeur (Admin) : son catalogue quitte la vente immédiatement.</summary>
public sealed record SuspendSellerCommand(Guid SellerId, string? Reason = null) : ICommand;

/// <summary>
/// Lève une suspension (Admin) : le catalogue retiré POUR CE MOTIF revient en
/// vente.
/// </summary>
public sealed record LiftSellerSuspensionCommand(Guid SellerId) : ICommand;
