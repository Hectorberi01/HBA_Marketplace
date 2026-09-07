using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Categories.Commands.UnpublishCategory;

/// <summary>
/// Dépublie une catégorie (Published -&gt; Draft), la retirant de l'arbre visible.
/// </summary>
public sealed record UnpublishCategoryCommand(Guid CategoryId, bool IncludeDescendants = false) : ICommand<int>;
