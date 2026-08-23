using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Attributes;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE RATTACHEMENT D'UN ATTRIBUT À UNE CATÉGORIE — TABLE <c>category_attributes</c>.
///
/// C'est ce qui pilote le formulaire vendeur (§13, étape 8 : « caractéristiques
/// dynamiques de catégorie ») et les filtres de vitrine (§17).
///
/// `Required` ET `Variant` VIVENT ICI, PAS SUR LA DÉFINITION.
///
/// La même « Couleur » est obligatoire et formante pour un téléphone, facultative
/// et décorative pour un meuble. Les porter sur la définition obligerait à créer
/// deux attributs « couleur » — et c'est exactement le doublon que la séparation
/// des deux tables évite.
/// ═════════════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// L'attribut distingue les variantes (§11 : couleur, stockage).
    ///
    /// CE DRAPEAU NE VALIDE RIEN AUJOURD'HUI, IL DÉCRIT.
    ///
    /// Il dit au formulaire quels champs proposer par déclinaison plutôt qu'une
    /// fois pour la fiche. Faire de lui une contrainte — « toute variante DOIT
    /// porter tous les attributs formants » — casserait les fiches existantes, dont
    /// les variantes ont été saisies avant que ces définitions n'existent.
    /// </summary>
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
