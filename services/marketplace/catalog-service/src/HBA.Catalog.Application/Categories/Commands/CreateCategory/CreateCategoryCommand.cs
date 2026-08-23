using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Categories.Commands.CreateCategory;

public sealed record CreateCategoryCommand(
    string Name,
    Guid? ParentId = null,
    string? ImageUrl = null,
    string? AttributeSchema = null) : ICommand<Guid>;
