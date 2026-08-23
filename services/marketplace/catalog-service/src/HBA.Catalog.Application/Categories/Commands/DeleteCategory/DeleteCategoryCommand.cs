using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Categories.Commands.DeleteCategory;

/// <summary>Supprime une catégorie. Les descendants et produits rattachés ne sont pas répercutés (référence souple).</summary>
public sealed record DeleteCategoryCommand(Guid CategoryId) : ICommand;
