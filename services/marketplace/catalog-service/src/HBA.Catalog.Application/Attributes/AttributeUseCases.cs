using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Contracts;
using HBA.Catalog.Domain.Attributes;
using HBA.Catalog.Domain.Categories;

namespace HBA.Catalog.Application.Attributes;

// ═════════════════════════════════════════════════════════════════════════════
// LE RÉFÉRENTIEL D'ATTRIBUTS (§10) ET LE SCHÉMA DE CATÉGORIE.
//
// Ce qui rend ce lot utile n'est pas d'avoir deux tables de plus : c'est que
// `SubmitForReview` refuse désormais une fiche dont les attributs requis manquent
// (§23), et que le formulaire vendeur (§13, étape 8) sait quoi afficher.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Crée une définition d'attribut réutilisable (§10).</summary>
public sealed record CreateAttributeDefinitionCommand(
    string Code,
    string Name,
    string Type,
    string? Unit = null,
    IReadOnlyList<string>? Options = null) : ICommand<Guid>;

/// <summary>Rattache un attribut à une catégorie (§10).</summary>
public sealed record AssignAttributeToCategoryCommand(
    Guid CategoryId,
    Guid AttributeDefinitionId,
    bool Required = false,
    bool Variant = false,
    int DisplayOrder = 0) : ICommand;

/// <summary>Retire un attribut d'une catégorie.</summary>
public sealed record RemoveAttributeFromCategoryCommand(
    Guid CategoryId,
    Guid AttributeDefinitionId) : ICommand;

/// <summary>Le schéma d'une catégorie, tel que le formulaire vendeur le consomme.</summary>
public sealed record GetCategoryAttributesQuery(Guid CategoryId) : IQuery<IReadOnlyList<CategoryAttributeSummary>>;

/// <summary>Toutes les définitions connues (console d'administration).</summary>
public sealed record ListAttributeDefinitionsQuery : IQuery<IReadOnlyList<AttributeDefinitionSummary>>;

internal sealed class AttributeUseCases
    : ICommandHandler<CreateAttributeDefinitionCommand, Guid>,
      ICommandHandler<AssignAttributeToCategoryCommand>,
      ICommandHandler<RemoveAttributeFromCategoryCommand>,
      IQueryHandler<GetCategoryAttributesQuery, IReadOnlyList<CategoryAttributeSummary>>,
      IQueryHandler<ListAttributeDefinitionsQuery, IReadOnlyList<AttributeDefinitionSummary>>
{
    private readonly IAttributeDefinitionRepository _definitions;
    private readonly ICategoryAttributeRepository _rattachements;
    private readonly ICategoryRepository _categories;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public AttributeUseCases(
        IAttributeDefinitionRepository definitions,
        ICategoryAttributeRepository rattachements,
        ICategoryRepository categories,
        ICatalogUnitOfWork unitOfWork)
    {
        _definitions = definitions;
        _rattachements = rattachements;
        _categories = categories;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(
        CreateAttributeDefinitionCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<AttributeValueType>(
                (command.Type ?? string.Empty).Replace("_", string.Empty), ignoreCase: true, out var type)
            || !Enum.IsDefined(typeof(AttributeValueType), type))
        {
            return Result.Failure<Guid>(Error.Validation(
                "catalog.attribute.type_invalid",
                $"Type d'attribut inconnu : « {command.Type} ». "
                + "Attendu : TEXT, TEXTAREA, INTEGER, DECIMAL, BOOLEAN, SELECT, MULTI_SELECT, COLOR ou DATE."));
        }

        // CONTRÔLE D'UNICITÉ AVANT L'INDEX, POUR LE MESSAGE.
        //
        // L'index unique sur `Code` refuserait de toute façon le doublon — mais par
        // une violation de contrainte PostgreSQL, qui remonte en 500. L'administrateur
        // doit savoir qu'un attribut porte déjà ce code, et lequel.
        var existante = await _definitions.GetByCodeAsync(command.Code, cancellationToken);
        if (existante is not null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "catalog.attribute.code_taken",
                $"Le code « {existante.Code} » est déjà utilisé par l'attribut « {existante.Name} »."));
        }

        var definition = AttributeDefinition.Create(
            command.Code, command.Name, type, command.Unit, command.Options);

        if (definition.IsFailure)
        {
            return Result.Failure<Guid>(definition.Error);
        }

        await _definitions.AddAsync(definition.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return definition.Value.Id;
    }

    public async Task<Result> Handle(
        AssignAttributeToCategoryCommand command, CancellationToken cancellationToken)
    {
        // LES DEUX EXISTENCES SONT VÉRIFIÉES, ET AUCUNE CLÉ ÉTRANGÈRE NE LE FAIT.
        //
        // `category_attributes` ne porte pas de contrainte vers `categories` ni vers
        // `attribute_definitions` — ce sont des agrégats distincts. Sans ces deux
        // contrôles, un identifiant erroné produit une ligne orpheline : le
        // formulaire vendeur affiche un champ vide, et la validation exige un
        // attribut dont plus personne ne connaît la définition.
        var categorie = await _categories.GetByIdAsync(new CategoryId(command.CategoryId), cancellationToken);
        if (categorie is null)
        {
            return Result.Failure(Error.NotFound(
                "catalog.category.not_found", $"Catégorie {command.CategoryId} introuvable."));
        }

        var definition = await _definitions.GetByIdAsync(command.AttributeDefinitionId, cancellationToken);
        if (definition is null)
        {
            return Result.Failure(Error.NotFound(
                "catalog.attribute.not_found", $"Attribut {command.AttributeDefinitionId} introuvable."));
        }

        var existant = await _rattachements.GetAsync(
            command.CategoryId, command.AttributeDefinitionId, cancellationToken);

        if (existant is not null)
        {
            // Idempotent : réassigner met à jour les trois réglages plutôt que de
            // rendre un conflit. Une console d'administration renvoie l'état complet
            // du formulaire à chaque enregistrement.
            existant.Update(command.Required, command.Variant, command.DisplayOrder);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var rattachement = CategoryAttribute.Create(
            command.CategoryId, command.AttributeDefinitionId,
            command.Required, command.Variant, command.DisplayOrder);

        if (rattachement.IsFailure)
        {
            return Result.Failure(rattachement.Error);
        }

        await _rattachements.AddAsync(rattachement.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(
        RemoveAttributeFromCategoryCommand command, CancellationToken cancellationToken)
    {
        var rattachement = await _rattachements.GetAsync(
            command.CategoryId, command.AttributeDefinitionId, cancellationToken);

        if (rattachement is null)
        {
            // Idempotent : retirer ce qui n'est pas là est un succès. L'inverse
            // ferait échouer un second clic sur « supprimer ».
            return Result.Success();
        }

        // LES FICHES EXISTANTES GARDENT LEURS VALEURS.
        //
        // Elles vivent dans `product_revisions.attributes`, pas ici. Retirer un
        // attribut du schéma cesse de l'EXIGER et de l'afficher ; il ne l'efface
        // nulle part. C'est ce qui rend l'opération réversible.
        _rattachements.Remove(rattachement);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<CategoryAttributeSummary>>> Handle(
        GetCategoryAttributesQuery query, CancellationToken cancellationToken)
    {
        var schema = await _rattachements.ListByCategoryAsync(query.CategoryId, cancellationToken);

        IReadOnlyList<CategoryAttributeSummary> resume = schema
            .Select(a => new CategoryAttributeSummary(
                a.Definition.Id,
                a.Definition.Code,
                a.Definition.Name,
                a.Definition.Type.ToString(),
                a.Definition.Unit,
                a.Definition.Options,
                a.Rattachement.Required,
                a.Rattachement.Variant,
                a.Rattachement.DisplayOrder))
            .ToList();

        return Result.Success(resume);
    }

    public async Task<Result<IReadOnlyList<AttributeDefinitionSummary>>> Handle(
        ListAttributeDefinitionsQuery query, CancellationToken cancellationToken)
    {
        var definitions = await _definitions.ListAsync(cancellationToken);

        IReadOnlyList<AttributeDefinitionSummary> resume = definitions
            .Select(d => new AttributeDefinitionSummary(
                d.Id, d.Code, d.Name, d.Type.ToString(), d.Unit, d.Options))
            .ToList();

        return Result.Success(resume);
    }
}
