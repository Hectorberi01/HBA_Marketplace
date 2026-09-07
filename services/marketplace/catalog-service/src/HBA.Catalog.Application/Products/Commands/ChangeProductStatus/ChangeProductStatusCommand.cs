using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Products.Commands.ChangeProductStatus;

/// <summary>Change le statut d'un produit.</summary>
public sealed record ChangeProductStatusCommand(Guid ProductId, string Status) : ICommand;
