using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.RemoveKybDocument;

/// <summary>
/// Supprime une pièce KYB de la boutique du vendeur. Le <see cref="SellerId"/> est
/// résolu côté serveur (jamais fourni par le client), garantissant que la pièce
/// retirée appartient bien à la boutique du jeton (anti-IDOR).
/// </summary>
public sealed record RemoveKybDocumentCommand(Guid SellerId, Guid DocumentId) : ICommand;
