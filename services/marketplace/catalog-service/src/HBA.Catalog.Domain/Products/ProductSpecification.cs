using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Products;

/// <summary>Une ligne de spécification, telle qu'elle arrive du formulaire (§12).</summary>
public readonly record struct SpecificationSaisie(string Name, string Value);

/// <summary>Un groupe de spécifications saisi par le vendeur (§12).</summary>
public sealed record GroupeDeSpecifications(
    string Name,
    IReadOnlyList<SpecificationSaisie> Items,
    int DisplayOrder = 0);

/// <summary>
/// Une caractéristique — « Type : Super Retina XDR OLED ».
/// Table <c>product_specifications</c> (§12, §20).
/// </summary>
public sealed class ProductSpecification : Entity<Guid>
{
    private ProductSpecification()
    {
    }

    internal ProductSpecification(Guid id, Guid groupId, string name, string value, int displayOrder)
        : base(id)
    {
        GroupId = groupId;
        Name = name;
        Value = value;
        DisplayOrder = displayOrder;
    }

    public Guid GroupId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }

    internal void AttacherA(Guid groupId) => GroupId = groupId;
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN GROUPE DE CARACTÉRISTIQUES — TABLE <c>product_specification_groups</c> (§12).
///
/// « Écran », « Processeur », « Connectivité » : le §12 les montre groupés, et ce
/// groupement est la seule chose qui rend une liste de trente caractéristiques
/// lisible sur un téléphone.
///
/// POURQUOI DEUX TABLES PLUTÔT QU'UN jsonb.
///
/// Les attributs de catégorie sont dans un jsonb, et l'on pourrait faire pareil
/// ici. La différence est l'ORDRE : ces lignes s'affichent dans un ordre choisi par
/// le vendeur, groupe par groupe. Un objet JSON ne garantit pas l'ordre de ses
/// clés — la sérialisation .NET le préserve aujourd'hui, PostgreSQL le réordonne
/// en `jsonb`. La fiche s'afficherait donc dans un ordre différent de celui saisi,
/// et changerait après chaque écriture, sans que rien ne le signale.
///
/// ELLES SONT PORTÉES PAR LA RÉVISION, PAS PAR LE PRODUIT.
///
/// Le §6 range les « caractéristiques essentielles » parmi les modifications
/// critiques : les changer exige une nouvelle validation. Les mettre sur le produit
/// permettrait de réécrire, sur une fiche en vente, la fiche technique qu'un
/// administrateur avait relue.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ProductSpecificationGroup : Entity<Guid>
{
    private readonly List<ProductSpecification> _items = new();

    private ProductSpecificationGroup()
    {
    }

    private ProductSpecificationGroup(Guid id, Guid revisionId, string name, int displayOrder)
        : base(id)
    {
        RevisionId = revisionId;
        Name = name;
        DisplayOrder = displayOrder;
    }

    public Guid RevisionId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }

    public IReadOnlyCollection<ProductSpecification> Items => _items.AsReadOnly();

    internal static Result<ProductSpecificationGroup> Create(GroupeDeSpecifications saisie, int rang)
    {
        if (saisie is null || string.IsNullOrWhiteSpace(saisie.Name))
        {
            return Error.Validation(
                "catalog.specification.group_name_required",
                "Chaque groupe de caractéristiques doit porter un nom.");
        }

        var lignes = (saisie.Items ?? Array.Empty<SpecificationSaisie>()).ToList();

        // UN GROUPE VIDE EST UN TITRE SANS CONTENU.
        //
        // Il s'affiche comme un intertitre suivi de rien. Le refuser au moment de la
        // saisie évite une fiche produit qui a l'air tronquée sans l'être.
        if (lignes.Count == 0)
        {
            return Error.Validation(
                "catalog.specification.group_empty",
                $"Le groupe « {saisie.Name} » ne contient aucune caractéristique.");
        }

        var groupe = new ProductSpecificationGroup(
            Guid.NewGuid(),
            revisionId: Guid.Empty,
            saisie.Name.Trim(),
            saisie.DisplayOrder > 0 ? saisie.DisplayOrder : rang);

        var position = 0;
        foreach (var ligne in lignes)
        {
            if (string.IsNullOrWhiteSpace(ligne.Name) || string.IsNullOrWhiteSpace(ligne.Value))
            {
                return Error.Validation(
                    "catalog.specification.item_incomplete",
                    $"Une caractéristique du groupe « {saisie.Name} » n'a pas de nom ou pas de valeur.");
            }

            groupe._items.Add(new ProductSpecification(
                Guid.NewGuid(), groupe.Id, ligne.Name.Trim(), ligne.Value.Trim(), position++));
        }

        return groupe;
    }

    /// <summary>
    /// Rattache le groupe à sa révision, et ses lignes à lui-même.
    ///
    /// MÊME MÉCANIQUE QUE `ProductCondition.AttacherA`, ET MÊME PIÈGE.
    ///
    /// Les groupes arrivent construits du formulaire, donc sans savoir à quelle
    /// révision ils appartiendront. Oublier ce geste laisse un `RevisionId` à zéro :
    /// EF insère, la contrainte de clé étrangère refuse, et le message parle d'une
    /// violation sans dire quel champ n'a pas été rempli.
    /// </summary>
    internal void AttacherA(Guid revisionId)
    {
        RevisionId = revisionId;
        foreach (var item in _items)
        {
            item.AttacherA(Id);
        }
    }

    /// <summary>
    /// Une empreinte du contenu, pour décider si la modification est critique (§6).
    ///
    /// Comparer les objets ne suffirait pas : ce sont des entités, égales par leur
    /// identifiant, et deux groupes reconstruits à l'identique portent des
    /// identifiants différents. C'est le CONTENU qui doit être comparé.
    /// </summary>
    internal string Empreinte()
        => $"{DisplayOrder}:{Name}:" + string.Join(
            ";", _items.OrderBy(i => i.DisplayOrder).Select(i => $"{i.Name}={i.Value}"));
}
