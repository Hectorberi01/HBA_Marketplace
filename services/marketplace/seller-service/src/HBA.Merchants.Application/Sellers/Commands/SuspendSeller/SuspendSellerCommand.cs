using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.SuspendSeller;

/// <summary>
/// Suspend un vendeur (Admin) : son catalogue quitte la vente immédiatement.
///
/// Le motif est facultatif mais fortement souhaitable : il voyage jusqu'au
/// catalogue et reste lisible sur chaque fiche retirée. Sans lui, le vendeur
/// rétabli des semaines plus tard — et le support qui l'accompagne — ne saura
/// pas dire ce qui s'était passé.
/// </summary>
public sealed record SuspendSellerCommand(Guid SellerId, string? Reason = null) : ICommand;

/// <summary>
/// Lève une suspension (Admin) : le catalogue retiré POUR CE MOTIF revient en vente.
///
/// NE PAS CONFONDRE avec ApproveSellerReactivationCommand, qui accueille un
/// vendeur ayant lui-même fermé son compte. Ici on lève une sanction.
/// </summary>
public sealed record LiftSellerSuspensionCommand(Guid SellerId) : ICommand;
