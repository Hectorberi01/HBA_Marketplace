namespace HBA.Catalog.Domain.Categories;

public interface ICategoryRepository
{
    Task AddAsync(Category category, CancellationToken cancellationToken = default);

    void Remove(Category category);

    Task<Category?> GetByIdAsync(CategoryId id, CancellationToken cancellationToken = default);

    /// <summary>Liste toutes les catégories (sélecteur parent, gouvernance admin).</summary>
    Task<IReadOnlyList<Category>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ce CHEMIN est-il déjà pris ? Remplace l'ancienne vérification d'unicité du
    /// slug, qui interdisait deux sous-catégories homonymes sous des parents
    /// différents — « Alimentation » sous « Chiens » ET sous « Chats », par
    /// exemple.
    /// </summary>
    /// <param name="excludeId">
    /// Catégorie à ignorer — indispensable en modification, sans quoi une catégorie
    /// entrerait en conflit avec elle-même dès qu'on enregistre sans changer le
    /// nom.
    /// </param>
    Task<bool> PathExistsAsync(string path, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>Descendants d'une catégorie, à TOUTE profondeur, ordonnés par chemin.</summary>
    Task<IReadOnlyList<Category>> ListDescendantsAsync(string path, CancellationToken cancellationToken = default);
}
