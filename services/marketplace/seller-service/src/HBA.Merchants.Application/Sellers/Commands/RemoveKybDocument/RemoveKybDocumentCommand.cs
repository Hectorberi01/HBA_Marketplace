using HBA.Shared.Application.Messaging;

namespace HBA.Merchants.Application.Sellers.Commands.RemoveKybDocument;

/// <summary>Supprime une pièce KYB de la boutique du vendeur.</summary>
public sealed record RemoveKybDocumentCommand(Guid SellerId, Guid DocumentId) : ICommand;
