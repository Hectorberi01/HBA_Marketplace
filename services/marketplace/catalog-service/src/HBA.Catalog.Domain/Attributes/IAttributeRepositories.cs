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
    ///
    /// IL NE REMONTE PAS LA HIÉRARCHIE, ET C'EST UNE LIMITE CONNUE.
    ///
    /// Le §10 montre des catégories hiérarchiques — Électronique → Téléphones →
    /// Smartphones — et l'on attendrait qu'un attribut posé sur « Téléphones »
    /// s'applique aux « Smartphones ». Ce n'est pas le cas ici : seul le
    /// rattachement direct compte.
    ///
    /// L'héritage demande de décider ce qui se passe quand un enfant redéfinit un
    /// attribut du parent avec un autre caractère obligatoire — et de le décider
    /// AVANT que des fiches ne dépendent du résultat. Poser le mécanisme simple
    /// d'abord permet de le trancher sur des cas réels.
    /// </summary>
    Task<IReadOnlyList<AttributDeCategorie>> ListByCategoryAsync(
        Guid categoryId, CancellationToken cancellationToken = default);
}
