using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Attributes;

/// <summary>Types d'attributs (§10).</summary>
public enum AttributeValueType
{
    Text = 0,
    TextArea = 1,
    Integer = 2,
    Decimal = 3,
    Boolean = 4,
    Select = 5,
    MultiSelect = 6,
    Color = 7,
    Date = 8
}

/// <summary>UNE DÉFINITION D'ATTRIBUT — TABLE <c>attribute_definitions</c> (§10, §20).</summary>
public sealed class AttributeDefinition : AggregateRoot<Guid>
{
    private AttributeDefinition()
    {
    }

    private AttributeDefinition(
        Guid id, string code, string name, AttributeValueType type,
        string? unit, IReadOnlyList<string> options)
        : base(id)
    {
        Code = code;
        Name = name;
        Type = type;
        Unit = unit;
        Options = options.ToList();
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>L'identifiant technique — « color », « storage », « screen_size ».</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public AttributeValueType Type { get; private set; }

    /// <summary>« INCH », « GB », « kg »… Nulle si l'attribut n'a pas d'unité.</summary>
    public string? Unit { get; private set; }

    /// <summary>Valeurs possibles pour SELECT et MULTI_SELECT. Vide sinon.</summary>
    public List<string> Options { get; private set; } = new();

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static readonly System.Text.RegularExpressions.Regex CodeValide =
        new("^[a-z][a-z0-9_]{1,49}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static Result<AttributeDefinition> Create(
        string code,
        string name,
        AttributeValueType type,
        string? unit = null,
        IEnumerable<string>? options = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("catalog.attribute.name_required", "Le libellé de l'attribut est obligatoire.");
        }

        var codeNormalise = (code ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');

        if (!CodeValide.IsMatch(codeNormalise))
        {
            return Error.Validation(
                "catalog.attribute.code_invalid",
                "Le code doit commencer par une lettre et ne contenir que des minuscules, des chiffres et des soulignés.");
        }

        var valeurs = (options ?? Enumerable.Empty<string>())
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // UN « SELECT » SANS OPTIONS EST UN CHAMP QUE PERSONNE NE PEUT REMPLIR.
        if (type is AttributeValueType.Select or AttributeValueType.MultiSelect && valeurs.Count == 0)
        {
            return Error.Validation(
                "catalog.attribute.options_required",
                "Un attribut à choix doit proposer au moins une valeur.");
        }

        if (type is not (AttributeValueType.Select or AttributeValueType.MultiSelect) && valeurs.Count > 0)
        {
            return Error.Validation(
                "catalog.attribute.options_unexpected",
                "Seuls les attributs à choix portent une liste de valeurs.");
        }

        return new AttributeDefinition(
            Guid.NewGuid(), codeNormalise, name.Trim(), type,
            string.IsNullOrWhiteSpace(unit) ? null : unit.Trim().ToUpperInvariant(),
            valeurs);
    }

    /// <summary>Met à jour le libellé, l'unité et les valeurs possibles.</summary>
    public Result Update(string name, string? unit, IEnumerable<string>? options)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("catalog.attribute.name_required", "Le libellé de l'attribut est obligatoire."));
        }

        var valeurs = (options ?? Enumerable.Empty<string>())
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Type is AttributeValueType.Select or AttributeValueType.MultiSelect && valeurs.Count == 0)
        {
            return Result.Failure(Error.Validation(
                "catalog.attribute.options_required",
                "Un attribut à choix doit proposer au moins une valeur."));
        }

        // ON N'INTERDIT PAS DE RETIRER UNE OPTION, MAIS IL FAUT LE SAVOIR.
        Name = name.Trim();
        Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim().ToUpperInvariant();
        Options = valeurs;
        return Result.Success();
    }
}
