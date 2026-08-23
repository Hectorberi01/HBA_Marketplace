using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Products;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ÉTAT D'UNE RÉVISION — DEUXIÈME AXE, ET IL EN FAUT VRAIMENT DEUX.
///
/// POURQUOI CE N'EST PAS UN DOUBLON DE <see cref="ProductStatus"/>.
///
/// Le §6 demande qu'une fiche PUBLIÉE reste servie aux acheteurs pendant qu'une
/// nouvelle version passe en validation. Il existe donc, au même instant, deux
/// vérités : le produit est PUBLISHED, et sa révision courante est PENDING_REVIEW.
///
/// Une seule énumération ne peut pas porter les deux. Si l'on force le produit en
/// PENDING_REVIEW pendant la relecture, la fiche disparaît de la marketplace à
/// chaque correction de faute de frappe — et le vendeur perd ses ventes le temps
/// qu'un administrateur passe. Si l'on ne modélise pas l'état de la révision, on
/// ne sait plus laquelle a été approuvée, et la publication devient un pari.
///
/// Le prix de ce second axe est réel : deux machines à états à tenir cohérentes.
/// C'est <see cref="Product"/> qui les fait avancer ENSEMBLE — jamais l'appelant.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>Remplacée par une révision plus récente. Conservée pour l'historique.</summary>
    Superseded = 5
}

/// <summary>
/// Le contenu descriptif tel qu'il arrive du formulaire vendeur (§13).
///
/// Regroupé en un seul objet — plutôt que douze paramètres — parce qu'il voyage
/// ensemble partout : création, mise à jour, comparaison critique. Douze
/// paramètres positionnels sont aussi la meilleure façon d'inverser deux chaînes
/// sans que le compilateur bronche.
/// </summary>
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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE VERSION DESCRIPTIVE DU PRODUIT — TABLE <c>product_revisions</c> (§6, §8).
///
/// TOUT CE QUI DÉCRIT LE PRODUIT VIT ICI, PLUS DANS <see cref="Product"/>.
///
/// C'est le déplacement le plus lourd de ce lot : nom, slug, description,
/// catégorie, marque, prix, condition, attributs et mots-clés QUITTENT la table
/// products. La raison tient en une phrase : ce qui peut changer sans nouvelle
/// validation n'a pas la même durée de vie que ce qui l'exige.
///
/// La conséquence pratique est que `product.Name` n'existe plus. Il faut désormais
/// choisir — <c>CurrentRevision.Name</c> pour la vue vendeur, <c>PublishedRevision?.Name</c>
/// pour la vue acheteur — et ce choix est exactement celui qu'on faisait par
/// accident jusqu'ici. Le compilateur le pose maintenant à chaque appel.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// TYPÉ `ProductId`, PAS `Guid`. EF REFUSE LE SECOND.
    ///
    /// La clé primaire de <see cref="Product"/> est un
    /// `readonly record struct ProductId(Guid Value)` avec convertisseur de
    /// valeur. Une clé étrangère déclarée en `Guid` nu fait échouer la
    /// construction du modèle, dès `dotnet ef migrations add` :
    ///
    ///     The relationship from 'ProductRevision' to 'Product.Revisions' with
    ///     foreign key properties {'ProductId' : Guid} cannot target the primary
    ///     key {'Id' : ProductId} because it is not compatible.
    ///
    /// Le convertisseur ne franchit PAS cette frontière : EF exige le même type
    /// CLR des deux côtés de la relation, et applique la conversion ensuite.
    ///
    /// C'est pour cela que `Variants` et `Media` n'ont pas le problème — leur clé
    /// étrangère est une propriété FANTÔME, qu'EF type lui-même correctement. Ici
    /// la propriété est explicite (voir l'encadré de `ProductCondition.RevisionId`
    /// pour la raison), il faut donc la typer soi-même.
    ///
    /// En colonne, c'est un `uuid` dans les deux cas : le changement ne touche pas
    /// le schéma.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // Construction et contenu — `internal` : seul l'agrégat racine y touche.
    // ═════════════════════════════════════════════════════════════════════════

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
        //
        // Elles ont besoin de l'identifiant de la révision pour s'y rattacher — et
        // leur construction peut échouer (groupe vide, ligne sans valeur). Les
        // mettre dans `Appliquer`, qui ne rend rien, obligerait soit à lever, soit à
        // avaler l'erreur en silence : une fiche technique à moitié enregistrée,
        // sans que personne ne l'apprenne.
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
        //
        // Leur construction peut échouer. Appliquer le contenu avant laisserait une
        // révision à moitié réécrite — nouveau nom, nouveau prix, ancienne fiche
        // technique — et le Result rendu ferait croire que rien n'a bougé.
        var specifications = RemplacerSpecifications(contenu.Specifications);
        if (specifications.IsFailure)
        {
            return specifications;
        }

        Appliquer(contenu, slug.Value);
        return Result.Success();
    }

    /// <summary>
    /// Reconstruit les groupes de caractéristiques.
    ///
    /// ON REMPLACE TOUT, ON NE FUSIONNE PAS.
    ///
    /// Le formulaire (§12) envoie la fiche technique entière à chaque
    /// enregistrement — c'est un tableau que le vendeur édite en bloc. Fusionner
    /// ligne à ligne rendrait impossible de SUPPRIMER une caractéristique : elle
    /// resterait faute d'être mentionnée, et le vendeur ne comprendrait pas
    /// pourquoi la ligne qu'il vient d'effacer réapparaît.
    /// </summary>
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
        //
        // La condition arrive construite du formulaire (§13, étape 3), donc sans
        // savoir à quelle révision elle appartiendra. Oublier ce geste laisserait
        // un RevisionId à zéro : EF insérerait la ligne, la contrainte de clé
        // étrangère la refuserait, et le message parlerait d'une violation de
        // contrainte sans dire quel champ n'a pas été rempli.
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

    /// <summary>
    /// Remplace les mots-clés seuls, sans toucher au reste.
    ///
    /// Séparé de <see cref="Remplacer"/> parce que les mots-clés ne sont PAS une
    /// modification critique (§6) : passer par le chemin complet exigerait de
    /// fournir prix et condition pour poser une étiquette « featured ».
    /// </summary>
    internal void RemplacerTags(IReadOnlyList<string>? tags)
        => Tags = tags?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct()
            .ToList() ?? new List<string>();

    /// <summary>
    /// LE MÊME TRI DES DEUX CÔTÉS, SINON LA COMPARAISON MENT.
    ///
    /// `SequenceEqual` est sensible à l'ordre. Trier ici par `DisplayOrder` et
    /// là-bas par chaîne ferait passer pour critique une fiche technique
    /// inchangée — donc une révision de plus à chaque enregistrement, et la file de
    /// validation remplie de fiches identiques.
    /// </summary>
    private IReadOnlyList<string> EmpreinteDesSpecifications()
        => _specifications.Select(g => g.Empreinte()).OrderBy(e => e, StringComparer.Ordinal).ToList();

    /// <summary>
    /// L'empreinte d'une saisie, calculée SANS construire les groupes.
    ///
    /// Elle doit produire exactement la même chaîne que
    /// <see cref="ProductSpecificationGroup.Empreinte"/> — sans quoi toute
    /// modification, même à l'identique, passerait pour critique et ouvrirait une
    /// révision de plus à chaque enregistrement.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // Machine à états — pilotée par Product, jamais appelée depuis l'extérieur.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Retour en brouillon après correction d'un rejet.
    ///
    /// CE N'EST PAS UNE ANNULATION, C'EST LA SUITE DU §4.
    ///
    /// Le diagramme du cahier porte l'étiquette « correction » sur la flèche
    /// REJECTED → DRAFT : corriger EST la transition. Laisser la révision en
    /// « rejetée » après réécriture donnerait une fiche modifiée que rien ne
    /// distingue d'une fiche refusée et abandonnée — et la file de validation
    /// n'aurait aucun moyen de savoir laquelle attend un second regard.
    /// </summary>
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

    /// <summary>Remplacée par une révision plus récente. Elle n'est jamais supprimée.</summary>
    internal void MarquerRemplacee() => Status = RevisionStatus.Superseded;

    /// <summary>
    /// Le vendeur peut-il réécrire CETTE révision en place ?
    ///
    /// Une révision soumise ou déjà publiée ne se réécrit pas : la première parce
    /// qu'un administrateur la lit, la seconde parce que des acheteurs la voient.
    /// </summary>
    internal bool EstModifiableEnPlace
        => Status is RevisionStatus.Draft or RevisionStatus.Rejected;

    /// <summary>
    /// La modification proposée exige-t-elle une nouvelle validation (§6) ?
    ///
    /// LA LISTE DU §6 EST LIMITATIVE, ET SA FRONTIÈRE EST CE QUI COMPTE.
    ///
    /// Sont critiques : nom, catégorie, marque, description, condition, images
    /// principales, variantes, caractéristiques essentielles, type de produit,
    /// conformité — et le prix affiché. Tout le reste (mots-clés, description
    /// courte, coût d'achat) se corrige sans repasser devant un administrateur.
    ///
    /// Élargir cette liste « par prudence » aurait un coût direct : chaque
    /// correction de mot-clé mettrait la fiche en file d'attente, la file
    /// deviendrait ingérable, et les administrateurs finiraient par approuver en
    /// série sans lire — ce qui supprimerait la validation bien plus sûrement que
    /// de ne pas l'exiger.
    /// </summary>
    internal bool EstModificationCritique(ContenuProduit contenu)
        => !string.Equals(Name, contenu.Name?.Trim(), StringComparison.Ordinal)
           || !string.Equals(Description, contenu.Description?.Trim() ?? string.Empty, StringComparison.Ordinal)
           || CategoryId != contenu.CategoryId
           || BrandId != (contenu.BrandId == Guid.Empty ? null : contenu.BrandId)
           || Type != contenu.Type
           || Pricing.DiffereCritiquementDe(contenu.Pricing)
           || Condition.DiffereCritiquementDe(contenu.Condition)
           // LA FICHE TECHNIQUE EST CRITIQUE — c'est « caractéristiques
           // essentielles » dans la liste du §6. Modifier « Batterie : 4400 mAh »
           // en « 5000 mAh » sur une fiche en vente change ce que l'acheteur croit
           // acheter, et doit repasser devant un administrateur.
           //
           // La comparaison porte sur le CONTENU, pas sur les objets : ce sont des
           // entités, égales par identifiant, et deux groupes reconstruits à
           // l'identique en portent de nouveaux.
           || !EmpreinteDesSpecifications().SequenceEqual(EmpreinteDe(contenu.Specifications));
}
