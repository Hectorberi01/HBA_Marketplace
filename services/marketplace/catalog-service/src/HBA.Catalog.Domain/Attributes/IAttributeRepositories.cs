namespace HBA.Catalog.Domain.Attributes;

public interface IAttributeDefinitionRepository
{
    Task AddAsync(AttributeDefinition definition, CancellationToken cancellationToken = default);

    Task<AttributeDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Par son code — c'est lui qui doit rester unique (§10).</summary>
    Task<AttributeDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttributeDefinition>> ListAsync(CancellationToken cancellationToken = default);
}

public interface ICategoryAttributeRepository
{
    Task AddAsync(CategoryAttribute attribute, CancellationToken cancellationToken = default);

    void Remove(CategoryAttribute attribute);

    Task<CategoryAttribute?> GetAsync(
        Guid categoryId, Guid attributeDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Le SCHÉMA d'une catégorie : ses attributs, définitions comprises, dans
    /// l'ordre d'affichage du formulaire (§13, étape 8).
    /// </summary>
    Task<IReadOnlyList<AttributDeCategorie>> ListByCategoryAsync(
        Guid categoryId, CancellationToken cancellationToken = default);
}
