using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Categories.Commands.UpdateCategory;

/// <summary>Met à jour le nom (slug/chemin recalculés), l'image et le schéma d'attributs d'une catégorie.</summary>
public sealed record UpdateCategoryCommand(
    Guid CategoryId,
    string Name,
    string? ImageUrl = null,
    string? AttributeSchema = null) : ICommand;
