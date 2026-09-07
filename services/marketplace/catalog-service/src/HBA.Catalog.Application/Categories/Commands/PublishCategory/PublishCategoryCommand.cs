using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Categories.Commands.PublishCategory;

/// <summary>
/// Publie une catégorie (Draft -&gt; Published), la rendant visible dans l'arbre.
/// </summary>
public sealed record PublishCategoryCommand(Guid CategoryId, bool IncludeDescendants = false) : ICommand<int>;
