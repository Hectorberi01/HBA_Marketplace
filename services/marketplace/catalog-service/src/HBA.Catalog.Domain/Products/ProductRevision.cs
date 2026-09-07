using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Products;

/// <summary>ÉTAT D'UNE RÉVISION — DEUXIÈME AXE, ET IL EN FAUT VRAIMENT DEUX.</summary>
public enum RevisionStatus
{
    /// <summary>En cours d'écriture par le vendeur.</summary>
    Draft = 0,

    /// <summary>Soumise, verrouillée, en attente d'un administrateur.</summary>
    PendingReview = 1,

    /// <summary>Validée. Publiable, pas encore publiée.</summary>
    Approved = 2,

    /// <summary>Refusée avec motifs. Le vendeur corrige et resoumet.</summary>
    Rejected = 3,

    /// <summary>C'est elle que voit l'acheteur.</summary>
    Published = 4,

    /// <summary>Remplacée par une révision plus récente.</summary>
    Superseded = 5
}

/// <summary>Le contenu descriptif tel qu'il arrive du formulaire vendeur (§13).</summary>
public sealed record ContenuProduit(
    string Name,
    string Description,
    Guid CategoryId,
    ProductPricing Pricing,
    ProductCondition Condition,
    string? ShortDescription = null,
    ProductType Type = ProductType.Physical,
    Guid? BrandId = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IEnumerable<string>? Tags = null,
    Slug? Slug = null,
    IReadOnlyList<GroupeDeSpecifications>? Specifications = null);

/// <summary>UNE VERSION DESCRIPTIVE DU PRODUIT — TABLE <c>product_revisions</c> (§6, §8).</summary>
public sealed class ProductRevision : Entity<Guid>
{
    private readonly List<ProductSpecificationGroup> _specifications = new();

    private ProductRevision()
    {
    }

    private ProductRevision(Guid id, ProductId productId, int version, ContenuProduit contenu, Slug slug)
        : base(id)
    {
        ProductId = productId;
        Version = version;
        Status = RevisionStatus.Draft;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        Appliquer(contenu, slug);
    }

    /// <summary>TYPÉ `ProductId`, PAS `Guid`.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>1, 2, 3… Unique par produit (§21 : index unique sur ProductId+Version).</summary>
    public int Version { get; private set; }

    public RevisionStatus Status { get; private set; }

    public string Name { get; private set; } = default!;
    public Slug Slug { get; private set; } = default!;
    public string? ShortDescription { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public ProductType Type { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid? BrandId { get; private set; }

    public ProductPricing Pricing { get; private set; } = default!;
    public ProductCondition Condition { get; private set; } = default!;

    /// <summary>Les caractéristiques groupées de la fiche technique (§12).</summary>
    public IReadOnlyCollection<ProductSpecificationGroup> Specifications => _specifications.AsReadOnly();

    /// <summary>Attributs dynamiques pilotés par la catégorie (§10), en jsonb.</summary>
    public Dictionary<string, string> Attributes { get; private set; } = new();

    /// <summary>Mots-clés, en text[].</summary>
    public List<string> Tags { get; private set; } = new();

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }

    // Construction et contenu — `internal` : seul l'agrégat racine y touche.

    internal static Result<ProductRevision> Create(ProductId productId, int version, ContenuProduit contenu)
    {
        var validation = Valider(contenu);
        if (validation.IsFailure)
        {
            return Result.Failure<ProductRevision>(validation.Error);
        }

        var slug = ResoudreSlug(contenu);
        if (slug.IsFailure)
        {
            return Result.Failure<ProductRevision>(slug.Error);
        }

        var revision = new ProductRevision(Guid.NewGuid(), productId, version, contenu, slug.Value);

        // LES SPÉCIFICATIONS SE POSENT APRÈS LE CONSTRUCTEUR, PAS DEDANS.
        var specifications = revision.RemplacerSpecifications(contenu.Specifications);
        if (specifications.IsFailure)
        {
            return Result.Failure<ProductRevision>(specifications.Error);
        }

        return revision;
    }

    internal Result Remplacer(ContenuProduit contenu)
    {
        var validation = Valider(contenu);
        if (validation.IsFailure)
        {
            return validation;
        }

        var slug = ResoudreSlug(contenu);
        if (slug.IsFailure)
        {
            return Result.Failure(slug.Error);
        }

        // LES SPÉCIFICATIONS D'ABORD, LE RESTE ENSUITE.
        var specifications = RemplacerSpecifications(contenu.Specifications);
        if (specifications.IsFailure)
        {
            return specifications;
        }

        Appliquer(contenu, slug.Value);
        return Result.Success();
    }

    /// <summary>Reconstruit les groupes de caractéristiques.</summary>
    private Result RemplacerSpecifications(IReadOnlyList<GroupeDeSpecifications>? groupes)
    {
        var construits = new List<ProductSpecificationGroup>();
        var rang = 0;

        foreach (var saisie in groupes ?? Array.Empty<GroupeDeSpecifications>())
        {
            var groupe = ProductSpecificationGroup.Create(saisie, rang++);
            if (groupe.IsFailure)
            {
                return Result.Failure(groupe.Error);
            }

            groupe.Value.AttacherA(Id);
            construits.Add(groupe.Value);
        }

        _specifications.Clear();
        _specifications.AddRange(construits);
        return Result.Success();
    }

    private void Appliquer(ContenuProduit contenu, Slug slug)
    {
        Name = contenu.Name.Trim();
        Slug = slug;
        ShortDescription = string.IsNullOrWhiteSpace(contenu.ShortDescription) ? null : contenu.ShortDescription.Trim();
        Description = contenu.Description?.Trim() ?? string.Empty;
        Type = contenu.Type;
        CategoryId = contenu.CategoryId;
        BrandId = contenu.BrandId == Guid.Empty ? null : contenu.BrandId;
        Pricing = contenu.Pricing;

        Condition = contenu.Condition;
        // LE RATTACHEMENT SE FAIT ICI, ET NULLE PART AILLEURS.
        Condition.AttacherA(Id);
        Attributes = contenu.Attributes is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(contenu.Attributes);
        Tags = contenu.Tags?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct()
            .ToList() ?? new List<string>();
    }

    /// <summary>Remplace les mots-clés seuls, sans toucher au reste.</summary>
    internal void RemplacerTags(IReadOnlyList<string>? tags)
        => Tags = tags?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct()
            .ToList() ?? new List<string>();

    /// <summary>LE MÊME TRI DES DEUX CÔTÉS, SINON LA COMPARAISON MENT.</summary>
    private IReadOnlyList<string> EmpreinteDesSpecifications()
        => _specifications.Select(g => g.Empreinte()).OrderBy(e => e, StringComparer.Ordinal).ToList();

    /// <summary>L'empreinte d'une saisie, calculée SANS construire les groupes.</summary>
    private static IReadOnlyList<string> EmpreinteDe(IReadOnlyList<GroupeDeSpecifications>? groupes)
    {
        var rendu = new List<string>();
        var rang = 0;

        foreach (var groupe in groupes ?? Array.Empty<GroupeDeSpecifications>())
        {
            var ordre = groupe.DisplayOrder > 0 ? groupe.DisplayOrder : rang;
            var lignes = (groupe.Items ?? Array.Empty<SpecificationSaisie>())
                .Select(i => $"{i.Name?.Trim()}={i.Value?.Trim()}");

            rendu.Add($"{ordre}:{groupe.Name?.Trim()}:" + string.Join(";", lignes));
            rang++;
        }

        return rendu.OrderBy(e => e, StringComparer.Ordinal).ToList();
    }

    private static Result Valider(ContenuProduit contenu)
    {
        if (contenu is null)
        {
            return Result.Failure(Error.Validation("catalog.product.content_required", "Le contenu du produit est obligatoire."));
        }

        if (string.IsNullOrWhiteSpace(contenu.Name))
        {
            return Result.Failure(Error.Validation("catalog.product.name_required", "Le nom du produit est obligatoire."));
        }

        if (contenu.CategoryId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("catalog.product.category_required", "Un produit doit être rattaché à une catégorie."));
        }

        if (contenu.Pricing is null)
        {
            return Result.Failure(Error.Validation("catalog.pricing.required", "La tarification de référence est obligatoire."));
        }

        if (contenu.Condition is null)
        {
            return Result.Failure(Error.Validation("catalog.condition.required", "La condition commerciale est obligatoire."));
        }

        return Result.Success();
    }

    private static Result<Slug> ResoudreSlug(ContenuProduit contenu)
        => contenu.Slug is not null ? contenu.Slug : Slug.Create(contenu.Name);

    // Machine à états — pilotée par Product, jamais appelée depuis l'extérieur.

    /// <summary>Retour en brouillon après correction d'un rejet.</summary>
    internal void MarquerCorrigee()
    {
        Status = RevisionStatus.Draft;
        ReviewedAtUtc = null;
    }

    internal void MarquerSoumise(DateTimeOffset nowUtc)
    {
        Status = RevisionStatus.PendingReview;
        SubmittedAtUtc = nowUtc;
    }

    internal void MarquerApprouvee(DateTimeOffset nowUtc)
    {
        Status = RevisionStatus.Approved;
        ReviewedAtUtc = nowUtc;
    }

    internal void MarquerRejetee(DateTimeOffset nowUtc)
    {
        Status = RevisionStatus.Rejected;
        ReviewedAtUtc = nowUtc;
    }

    internal void MarquerPubliee(DateTimeOffset nowUtc)
    {
        Status = RevisionStatus.Published;
        PublishedAtUtc = nowUtc;
    }

    /// <summary>Remplacée par une révision plus récente.</summary>
    internal void MarquerRemplacee() => Status = RevisionStatus.Superseded;

    /// <summary>Le vendeur peut-il réécrire CETTE révision en place ?</summary>
    internal bool EstModifiableEnPlace
        => Status is RevisionStatus.Draft or RevisionStatus.Rejected;

    /// <summary>La modification proposée exige-t-elle une nouvelle validation (§6) ?</summary>
    internal bool EstModificationCritique(ContenuProduit contenu)
        => !string.Equals(Name, contenu.Name?.Trim(), StringComparison.Ordinal)
           || !string.Equals(Description, contenu.Description?.Trim() ?? string.Empty, StringComparison.Ordinal)
           || CategoryId != contenu.CategoryId
           || BrandId != (contenu.BrandId == Guid.Empty ? null : contenu.BrandId)
           || Type != contenu.Type
           || Pricing.DiffereCritiquementDe(contenu.Pricing)
           || Condition.DiffereCritiquementDe(contenu.Condition)
           // LA FICHE TECHNIQUE EST CRITIQUE — c'est « caractéristiques
           // essentielles » dans la liste du §6.
           || !EmpreinteDesSpecifications().SequenceEqual(EmpreinteDe(contenu.Specifications));
}
