using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.RejectKyb;

/// <summary>
/// Rejette le dossier KYB d'un vendeur (modération, Admin).
///
/// LE MOTIF EST CE QUI REND LE REFUS UTILE. Il est transmis au vendeur et
/// conservé sur sa fiche. Sans lui, il voit « Rejeté », ne sait pas quoi
/// corriger, et redépose la même pièce.
///
/// CE REFUS SUSPEND UN VENDEUR ACTIF, et retire donc son catalogue de la
/// vente. Ce n'est pas un effet de bord : c'était le trou — la garde existait à
/// l'activation, pas à la sortie.
/// </summary>
public sealed record RejectKybCommand(Guid SellerId, string? Reason = null) : ICommand;
