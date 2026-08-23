using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Products;

/// <summary>
/// État commercial déclaré (§9).
///
/// CETTE ÉNUMÉRATION N'EST NI LE STATUT DE PUBLICATION NI LE STOCK.
///
/// Le cahier ouvre le §9 par cette phrase, et elle mérite d'être répétée ici :
/// un produit RECONDITIONNÉ peut être PUBLISHED et en rupture. Trois axes
/// indépendants. Les mélanger — « occasion donc pas encore validé » — produit des
/// fiches invisibles que le vendeur croit en ligne.
/// </summary>
public enum ProductConditionType
{
    /// <summary>Neuf, jamais déballé.</summary>
    New = 0,

    /// <summary>Boîte ouverte, produit non utilisé.</summary>
    OpenBox = 1,

    LikeNew = 2,
    VeryGood = 3,
    Good = 4,
    Fair = 5,

    /// <summary>Remis à neuf par un professionnel, le fabricant ou le vendeur.</summary>
    Refurbished = 6
}

/// <summary>Ce que l'appareil fait encore (§9).</summary>
public enum ProductFunctionalStatus
{
    FullyFunctional = 0,
    PartiallyFunctional = 1,

    /// <summary>
    /// Vendu pour pièces. Ne fonctionne pas.
    ///
    /// INATTEIGNABLE : aucune route n'accepte cette valeur (lot 9.2). La
    /// chaîne « ForParts » n'apparaît nulle part dans le dépôt — ni dans un DTO,
    /// ni dans un proto, ni dans une charge de test. Un vendeur ne peut donc pas
    /// déclarer un article vendu pour pièces, alors que la grille d'état le
    /// prévoit et que l'acheteur le verrait à l'écran.
    /// </summary>
    ForParts = 2
}

public enum ProductDefectType
{
    /// <summary>Rayure, marque, décoloration : n'empêche rien.</summary>
    Cosmetic = 0,

    /// <summary>Une fonction dégradée ou absente.</summary>
    Functional = 1,

    /// <summary>Un accessoire ou un élément manquant.</summary>
    Missing = 2
}

public enum ProductDefectSeverity
{
    Minor = 0,
    Moderate = 1,
    Major = 2
}

public enum RefurbisherType
{
    Manufacturer = 0,
    Professional = 1,
    Seller = 2
}

/// <summary>Défaut déclaré par le vendeur, tel qu'il arrive du formulaire (§13, étape 3).</summary>
public readonly record struct DefautDeclare(
    ProductDefectType Type,
    string Location,
    string Description,
    ProductDefectSeverity Severity);

/// <summary>
/// Un défaut visible ou fonctionnel, déclaré fiche par fiche.
/// Table <c>product_condition_defects</c> (§20).
/// </summary>
public sealed class ProductDefect : Entity<Guid>
{
    private ProductDefect()
    {
    }

    internal ProductDefect(
        Guid id,
        ProductDefectType type,
        string location,
        string description,
        ProductDefectSeverity severity)
        : base(id)
    {
        Type = type;
        Location = location;
        Description = description;
        Severity = severity;
    }

    /// <summary>Clé étrangère explicite vers <see cref="ProductCondition"/>.</summary>
    public Guid ConditionId { get; private set; }

    public ProductDefectType Type { get; private set; }

    /// <summary>Où, en clair : « SCREEN », « dos », « angle inférieur droit ».</summary>
    public string Location { get; private set; } = string.Empty;

    internal void AttacherA(Guid conditionId) => ConditionId = conditionId;

    public string Description { get; private set; } = string.Empty;

    public ProductDefectSeverity Severity { get; private set; }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CONDITION COMMERCIALE — TABLE <c>product_conditions</c> (§9, §20).
///
/// CE QUE CETTE CLASSE APPORTE TIENT DANS SES INCOHÉRENCES REFUSÉES.
///
/// Le cahier donne la forme des données, pas les règles qui les lient. Or ce sont
/// ces liens qui protègent l'acheteur, et chacun correspond à une annonce qu'on
/// voit passer sur toutes les marketplaces :
///
///   • « NEUF » avec trois défauts déclarés. Le formulaire l'accepte, l'acheteur
///     lit « Neuf » en gros et les défauts en petit, puis ouvre un litige.
///   • « RECONDITIONNÉ » sans reconditionneur. Le mot vaut prime de prix ; sans
///     savoir QUI a remis à neuf, il ne vaut rien.
///   • « PARTIELLEMENT FONCTIONNEL » sans dire ce qui ne marche pas. C'est la
///     mention la plus inutile qui soit : elle inquiète sans informer.
///
/// Aucune de ces trois n'est une faute de code — ce sont des fiches parfaitement
/// valides au sens du schéma. Elles se refusent ici ou nulle part.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ProductCondition : Entity<Guid>
{
    private readonly List<ProductDefect> _defects = new();

    private ProductCondition()
    {
    }

    private ProductCondition(
        Guid id,
        ProductConditionType type,
        string? grade,
        string? description,
        bool isUsed,
        bool isRefurbished,
        bool hasOriginalPackaging,
        bool hasOriginalAccessories,
        ProductFunctionalStatus functionalStatus,
        RefurbisherType? refurbishedByType,
        Guid? refurbishedBySellerId,
        IReadOnlyList<string> refurbishmentOperations,
        int? batteryHealthPercentage,
        bool? batteryReplaced)
        : base(id)
    {
        Type = type;
        Grade = grade;
        Description = description;
        IsUsed = isUsed;
        IsRefurbished = isRefurbished;
        HasOriginalPackaging = hasOriginalPackaging;
        HasOriginalAccessories = hasOriginalAccessories;
        FunctionalStatus = functionalStatus;
        RefurbishedByType = refurbishedByType;
        RefurbishedBySellerId = refurbishedBySellerId;
        RefurbishmentOperations = refurbishmentOperations.ToList();
        BatteryHealthPercentage = batteryHealthPercentage;
        BatteryReplaced = batteryReplaced;
    }

    /// <summary>
    /// Révision porteuse. Renseignée quand la condition est attachée.
    ///
    /// CLÉ ÉTRANGÈRE EXPLICITE, PAS UNE PROPRIÉTÉ FANTÔME.
    ///
    /// EF sait poser une FK invisible, et c'est ce que font déjà Variants et Media
    /// via <c>HasForeignKey("ProductId")</c>. Ici on l'écrit, parce que la condition
    /// est CONSTRUITE avant sa révision — le formulaire vendeur la valide à
    /// l'étape 3 — et qu'une propriété fantôme ne peut pas être renseignée par le
    /// domaine au moment du rattachement.
    /// </summary>
    public Guid RevisionId { get; private set; }

    public ProductConditionType Type { get; private set; }

    /// <summary>A, B, C ou D. Nul si le vendeur n'a pas gradé.</summary>
    public string? Grade { get; private set; }

    public string? Description { get; private set; }

    public bool IsUsed { get; private set; }
    public bool IsRefurbished { get; private set; }
    public bool HasOriginalPackaging { get; private set; }
    public bool HasOriginalAccessories { get; private set; }
    public ProductFunctionalStatus FunctionalStatus { get; private set; }

    public RefurbisherType? RefurbishedByType { get; private set; }
    public Guid? RefurbishedBySellerId { get; private set; }
    public List<string> RefurbishmentOperations { get; private set; } = new();
    public int? BatteryHealthPercentage { get; private set; }
    public bool? BatteryReplaced { get; private set; }

    public IReadOnlyCollection<ProductDefect> Defects => _defects.AsReadOnly();

    private static readonly string[] GradesAdmis = { "A", "B", "C", "D" };

    public static Result<ProductCondition> Create(
        ProductConditionType type,
        string? grade = null,
        string? description = null,
        bool hasOriginalPackaging = false,
        bool hasOriginalAccessories = false,
        ProductFunctionalStatus functionalStatus = ProductFunctionalStatus.FullyFunctional,
        IEnumerable<DefautDeclare>? defects = null,
        RefurbisherType? refurbishedByType = null,
        Guid? refurbishedBySellerId = null,
        IEnumerable<string>? refurbishmentOperations = null,
        int? batteryHealthPercentage = null,
        bool? batteryReplaced = null)
    {
        var declares = (defects ?? Enumerable.Empty<DefautDeclare>()).ToList();

        if (grade is not null)
        {
            grade = grade.Trim().ToUpperInvariant();
            if (!GradesAdmis.Contains(grade))
            {
                return Error.Validation(
                    "catalog.condition.grade_invalid",
                    "Le grade doit être A, B, C ou D.");
            }
        }

        // « isUsed » ET « isRefurbished » SONT DÉDUITS, PAS SAISIS.
        //
        // Le §9 les montre dans le JSON, ce qui invite à les recevoir du client.
        // Les accepter tels quels autoriserait { "type": "NEW", "isUsed": true },
        // c'est-à-dire deux affirmations contradictoires dont on ne saurait plus
        // laquelle croire au moment de l'afficher. Le TYPE décide, seul.
        var isUsed = type is not (ProductConditionType.New or ProductConditionType.OpenBox);
        var isRefurbished = type is ProductConditionType.Refurbished;

        // Un reconditionné a forcément servi avant d'être remis à neuf.
        if (isRefurbished)
        {
            isUsed = true;
        }

        if (type is ProductConditionType.New)
        {
            if (declares.Count > 0)
            {
                return Error.Validation(
                    "catalog.condition.new_with_defects",
                    "Un produit déclaré neuf ne peut pas porter de défauts. Choisissez « boîte ouverte » ou un état d'occasion.");
            }

            if (functionalStatus is not ProductFunctionalStatus.FullyFunctional)
            {
                return Error.Validation(
                    "catalog.condition.new_not_fully_functional",
                    "Un produit déclaré neuf doit être pleinement fonctionnel.");
            }
        }

        if (isRefurbished && refurbishedByType is null)
        {
            return Error.Validation(
                "catalog.condition.refurbisher_required",
                "Un produit reconditionné doit indiquer qui l'a remis à neuf : le fabricant, un professionnel ou le vendeur.");
        }

        if (refurbishedByType is RefurbisherType.Seller
            && (refurbishedBySellerId is null || refurbishedBySellerId == Guid.Empty))
        {
            return Error.Validation(
                "catalog.condition.refurbisher_seller_required",
                "Un reconditionnement fait par le vendeur doit désigner le vendeur.");
        }

        // « PARTIELLEMENT FONCTIONNEL » SANS DÉFAUT N'INFORME PERSONNE.
        //
        // C'est la mention qui fait hésiter sans permettre de décider : l'acheteur
        // sait que quelque chose ne marche pas, et rien de plus. Exiger au moins un
        // défaut transforme un avertissement vague en information utilisable — et
        // c'est aussi ce qui rend un litige arbitrable.
        if (functionalStatus is not ProductFunctionalStatus.FullyFunctional && declares.Count == 0)
        {
            return Error.Validation(
                "catalog.condition.defect_required",
                "Un produit qui n'est pas pleinement fonctionnel doit déclarer au moins un défaut.");
        }

        if (batteryHealthPercentage is < 0 or > 100)
        {
            return Error.Validation(
                "catalog.condition.battery_health_invalid",
                "L'état de la batterie s'exprime en pourcents, entre 0 et 100.");
        }

        var condition = new ProductCondition(
            Guid.NewGuid(),
            type,
            grade,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            isUsed,
            isRefurbished,
            hasOriginalPackaging,
            hasOriginalAccessories,
            functionalStatus,
            refurbishedByType,
            refurbishedBySellerId == Guid.Empty ? null : refurbishedBySellerId,
            (refurbishmentOperations ?? Enumerable.Empty<string>())
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Select(o => o.Trim().ToUpperInvariant())
                .Distinct()
                .ToList(),
            batteryHealthPercentage,
            batteryReplaced);

        foreach (var declare in declares)
        {
            if (string.IsNullOrWhiteSpace(declare.Description))
            {
                return Error.Validation(
                    "catalog.condition.defect_description_required",
                    "Chaque défaut déclaré doit être décrit.");
            }

            condition._defects.Add(new ProductDefect(
                Guid.NewGuid(),
                declare.Type,
                declare.Location?.Trim() ?? string.Empty,
                declare.Description.Trim(),
                declare.Severity));
        }

        return condition;
    }

    /// <summary>
    /// Rattache la condition à sa révision, et ses défauts à elle-même.
    /// Appelé par <see cref="ProductRevision"/> seul.
    /// </summary>
    internal void AttacherA(Guid revisionId)
    {
        RevisionId = revisionId;
        foreach (var defect in _defects)
        {
            defect.AttacherA(Id);
        }
    }

    /// <summary>
    /// Le neuf par défaut, pour une fiche qui ne déclare rien.
    ///
    /// CE DÉFAUT EST UN CHOIX, PAS UNE ABSENCE.
    ///
    /// Il fallait trancher : sans condition, l'affichage n'a rien à dire. « Neuf »
    /// est ce que le vendeur voulait dire dans la quasi-totalité des cas, et c'est
    /// aussi l'état le plus contraint — donc celui qui se corrige le plus vite si
    /// l'on s'est trompé, puisqu'il refuse tout défaut.
    /// </summary>
    public static ProductCondition Neuf()
        => Create(ProductConditionType.New).Value;

    /// <summary>
    /// Vrai si le passage à l'autre condition est une modification critique (§6).
    /// La condition figure explicitement dans la liste du cahier.
    /// </summary>
    public bool DiffereCritiquementDe(ProductCondition? autre)
        => autre is null
           || Type != autre.Type
           || Grade != autre.Grade
           || FunctionalStatus != autre.FunctionalStatus
           || _defects.Count != autre._defects.Count;
}
