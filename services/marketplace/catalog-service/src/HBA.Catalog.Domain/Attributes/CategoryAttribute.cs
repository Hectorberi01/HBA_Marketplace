using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Attributes;

/// <summary>
/// LE RATTACHEMENT D'UN ATTRIBUT À UNE CATÉGORIE — TABLE <c>
/// category_attributes</c>.
/// </summary>
public sealed class CategoryAttribute : Entity<Guid>
{
    private CategoryAttribute()
    {
    }

    private CategoryAttribute(
        Guid id, Guid categoryId, Guid attributeDefinitionId,
        bool required, bool variant, int displayOrder)
        : base(id)
    {
        CategoryId = categoryId;
        AttributeDefinitionId = attributeDefinitionId;
        Required = required;
        Variant = variant;
        DisplayOrder = displayOrder;
    }

    public Guid CategoryId { get; private set; }
    public Guid AttributeDefinitionId { get; private set; }

    /// <summary>Le vendeur doit le renseigner avant de soumettre (§23).</summary>
    public bool Required { get; private set; }

    /// <summary>L'attribut distingue les variantes (§11 : couleur, stockage).</summary>
    public bool Variant { get; private set; }

    public int DisplayOrder { get; private set; }

    public static Result<CategoryAttribute> Create(
        Guid categoryId, Guid attributeDefinitionId,
        bool required = false, bool variant = false, int displayOrder = 0)
    {
        if (categoryId == Guid.Empty)
        {
            return Error.Validation("catalog.category_attribute.category_required", "La catégorie est obligatoire.");
        }

        if (attributeDefinitionId == Guid.Empty)
        {
            return Error.Validation("catalog.category_attribute.definition_required", "L'attribut est obligatoire.");
        }

        return new CategoryAttribute(
            Guid.NewGuid(), categoryId, attributeDefinitionId, required, variant, Math.Max(0, displayOrder));
    }

    public void Update(bool required, bool variant, int displayOrder)
    {
        Required = required;
        Variant = variant;
        DisplayOrder = Math.Max(0, displayOrder);
    }
}

/// <summary>Une définition et son rattachement, tels que le formulaire les consomme.</summary>
public sealed record AttributDeCategorie(
    AttributeDefinition Definition,
    CategoryAttribute Rattachement);
