using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Products.Commands.DeleteProduct;

/// <summary>Supprime définitivement un produit (et, par cascade, ses variantes et médias).</summary>
public sealed record DeleteProductCommand(Guid ProductId) : ICommand;
