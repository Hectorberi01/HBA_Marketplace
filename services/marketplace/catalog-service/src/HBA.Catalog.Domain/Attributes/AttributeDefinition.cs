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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE DÉFINITION D'ATTRIBUT — TABLE <c>attribute_definitions</c> (§10, §20).
///
/// ELLE EST DÉFINIE UNE FOIS, ET RÉUTILISÉE PAR PLUSIEURS CATÉGORIES.
///
/// « Couleur » est le même attribut pour les téléphones, les chaussures et les
/// meubles : même code, mêmes valeurs possibles, même rendu. Le §10 le montre
/// rattaché à une catégorie, ce qui invite à le recréer à chaque fois — et l'on se
/// retrouve alors avec `color`, `couleur` et `Colour` selon qui a rempli le
/// formulaire, trois filtres de vitrine au lieu d'un, et une recherche par couleur
/// qui ne trouve qu'un tiers du catalogue.
///
/// La définition vit donc à part ; c'est <see cref="CategoryAttribute"/> qui
/// l'attache à une catégorie, avec ce qui, LUI, dépend de la catégorie : obligatoire
/// ou non, formant variante ou non, position dans le formulaire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// L'identifiant technique — « color », « storage », « screen_size ».
    ///
    /// C'EST LUI QUI VOYAGE, PAS LE NOM.
    ///
    /// Le nom est affiché et traduit ; le code est la clé sous laquelle la valeur
    /// est rangée dans `product_revisions.attributes` et sur laquelle la vitrine
    /// filtre. Le renommer casserait toutes les fiches déjà saisies — d'où son
    /// unicité et son immuabilité.
    /// </summary>
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
        //
        // Le formulaire vendeur (§13, étape 8) construit une liste déroulante à
        // partir de ces valeurs. Vide, elle s'affiche sans choix — et si
        // l'attribut est requis, la fiche devient impossible à soumettre. Le
        // vendeur ne voit qu'un champ obligatoire et vide ; rien ne lui dit que
        // c'est la définition qui est incomplète.
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

    /// <summary>
    /// Met à jour le libellé, l'unité et les valeurs possibles.
    ///
    /// NI LE CODE NI LE TYPE NE CHANGENT — VOIR L'ENCADRÉ DE <see cref="Code"/>.
    ///
    /// Changer le type d'un attribut déjà utilisé rendrait invalides toutes les
    /// valeurs saisies : un `screen_size` passé de DECIMAL à SELECT ferait échouer
    /// la soumission de chaque fiche qui le porte, sans qu'aucune n'ait changé.
    /// </summary>
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
        //
        // Les fiches qui portaient la valeur retirée gardent leur donnée : elle est
        // dans `product_revisions.attributes`, pas ici. Elles resteront affichées
        // telles quelles, et ne repasseront la validation qu'à leur prochaine
        // soumission — où l'erreur sera claire. Bloquer la suppression figerait le
        // référentiel pour toujours.
        Name = name.Trim();
        Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim().ToUpperInvariant();
        Options = valeurs;
        return Result.Success();
    }
}
