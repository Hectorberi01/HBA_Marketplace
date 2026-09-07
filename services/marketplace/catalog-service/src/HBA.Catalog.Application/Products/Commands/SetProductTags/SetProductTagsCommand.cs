using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Products.Commands.SetProductTags;

/// <summary>Remplace la liste des tags d'un produit.</summary>
public sealed record SetProductTagsCommand(Guid ProductId, IReadOnlyList<string> Tags) : ICommand;
