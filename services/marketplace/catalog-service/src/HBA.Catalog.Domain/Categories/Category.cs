using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Categories.Events;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Domain.Categories;

/// <summary>Cycle de publication d'une catégorie dans l'arbre visible.</summary>
public enum CategoryStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}

/// <summary>
/// Nœud de l'arbre des catégories. Porte le schéma d'attributs attendus pour ses
/// produits et peut avoir une image (cf. dossier, Category). Le chemin (Path)
/// est matérialisé pour des lectures d'arbre efficaces.
/// </summary>
public sealed class Category : AggregateRoot<CategoryId>
{
    private Category()
    {
    }

    private Category(
        CategoryId id,
        Guid? parentId,
        string name,
        Slug slug,
        string path,
        string? imageUrl,
        string attributeSchema)
        : base(id)
    {
        ParentId = parentId;
        Name = name;
        Slug = slug;
        Path = path;
        ImageUrl = imageUrl;
        AttributeSchema = attributeSchema;
        Status = CategoryStatus.Draft;

        Raise(new CategoryCreatedDomainEvent(id.Value, parentId, name, slug.Value, path));
    }

    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = default!;
    public Slug Slug { get; private set; } = default!;

    /// <summary>Chemin matérialisé, ex : « /electronique/telephones ».</summary>
    public string Path { get; private set; } = default!;

    public string? ImageUrl { get; private set; }

    /// <summary>Schéma d'attributs attendus + validation (jsonb).</summary>
    public string AttributeSchema { get; private set; } = "{}";

    public CategoryStatus Status { get; private set; }

    /// <summary>
    /// Construit le chemin matérialisé d'une catégorie à partir du chemin de son
    /// parent et de son slug.
    ///
    /// C'EST LE CHEMIN, ET NON LE SLUG, QUI IDENTIFIE UNE CATÉGORIE.
    ///
    /// Le slug ne porte que le nom : « alimentation » vaut pour les chiens comme pour
    /// les chats. L'imposer unique interdisait une taxonomie pourtant banale —
    /// impossible de créer « Animaux &gt; Chats &gt; Alimentation » dès lors que
    /// « Animaux &gt; Chiens &gt; Alimentation » existait.
    ///
    /// Le chemin, lui, porte la branche entière : « /animaux/chiens/alimentation »
    /// et « /animaux/chats/alimentation » diffèrent. Il reste unique là où il doit
    /// l'être — deux sœurs de même nom sous un même parent produisent le même chemin
    /// et sont donc toujours refusées.
    ///
    /// Exposé publiquement pour que l'Application vérifie la disponibilité d'un
    /// chemin AVANT de construire l'agrégat, sans réécrire cette règle de son côté.
    /// </summary>
    public static string BuildPath(string? parentPath, string slug)
    {
        var basePath = string.IsNullOrWhiteSpace(parentPath) ? string.Empty : parentPath.TrimEnd('/');
        return $"{basePath}/{slug}";
    }

    /// <summary>
    /// Crée une catégorie. <paramref name="parentPath"/> est le chemin du parent
    /// (résolu par l'Application en chargeant le parent), null si racine.
    /// </summary>
    public static Result<Category> Create(
        string name,
        Guid? parentId = null,
        string? parentPath = null,
        string? imageUrl = null,
        string? attributeSchema = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("catalog.category.name_required", "Le nom de la catégorie est obligatoire.");
        }

        var slugResult = Slug.Create(name);
        if (slugResult.IsFailure)
        {
            return Result.Failure<Category>(slugResult.Error);
        }

        var path = BuildPath(parentPath, slugResult.Value.Value);

        return new Category(
            CategoryId.New(),
            parentId == Guid.Empty ? null : parentId,
            name.Trim(),
            slugResult.Value,
            path,
            string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim(),
            string.IsNullOrWhiteSpace(attributeSchema) ? "{}" : attributeSchema.Trim());
    }

    /// <summary>Publie la catégorie (Draft -> Published), la rendant visible dans l'arbre.</summary>
    public Result Publish()
    {
        if (Status == CategoryStatus.Archived)
        {
            return Result.Failure(Error.Conflict("catalog.category.archived", "Une catégorie archivée ne peut pas être publiée."));
        }

        Status = CategoryStatus.Published;
        return Result.Success();
    }

    /// <summary>Dépublie la catégorie (Published -> Draft) ; elle pourra être republiée.</summary>
    public Result Unpublish()
    {
        if (Status == CategoryStatus.Archived)
        {
            return Result.Failure(Error.Conflict("catalog.category.archived", "Une catégorie archivée ne peut pas être dépubliée."));
        }

        Status = CategoryStatus.Draft;
        return Result.Success();
    }

    /// <summary>Archive la catégorie (la retire de l'arbre visible).</summary>
    public Result Archive()
    {
        Status = CategoryStatus.Archived;
        return Result.Success();
    }

    /// <summary>
    /// Met à jour le nom (slug et chemin recalculés à partir du
    /// <paramref name="parentPath"/> fourni par l'Application), l'image et le
    /// schéma d'attributs. Le rattachement parent n'est pas modifié ici.
    /// </summary>
    /// <remarks>
    /// LES DESCENDANTS SONT RÉPERCUTÉS PAR L'APPLICATION, VIA
    /// <see cref="RebasePath"/> — CE N'EST PLUS UNE « ÉVOLUTION FUTURE ».
    ///
    /// Cette phrase figurait ici et décrivait un vrai défaut (audit 2.4) :
    /// renommer « Animaux » (`/animaux`) en « Animaux domestiques » donnait
    /// `/animaux-domestiques` à la catégorie et laissait ses enfants en
    /// `/animaux/chiens`. `ListDescendantsAsync` cherche par PRÉFIXE : la branche
    /// entière devenait introuvable, donc publication et dépublication en cascade
    /// ne l'atteignaient plus, et les filtres par catégorie la perdaient —
    /// silencieusement.
    ///
    /// LA CASCADE N'EST PAS FAITE ICI, ET C'EST DÉLIBÉRÉ. L'agrégat ne connaît que
    /// lui-même ; charger ses descendants depuis le domaine lui donnerait un accès
    /// au dépôt. C'est `UpdateCategoryCommandHandler` qui les relit et appelle
    /// <see cref="RebasePath"/> sur chacun.
    /// </remarks>
    public Result Update(string name, string? parentPath, string? imageUrl, string? attributeSchema)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("catalog.category.name_required", "Le nom de la catégorie est obligatoire."));
        }

        var slugResult = Slug.Create(name);
        if (slugResult.IsFailure)
        {
            return Result.Failure(slugResult.Error);
        }

        Name = name.Trim();
        Slug = slugResult.Value;
        Path = BuildPath(parentPath, slugResult.Value.Value);
        ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        AttributeSchema = string.IsNullOrWhiteSpace(attributeSchema) ? "{}" : attributeSchema.Trim();

        return Result.Success();
    }

    /// <summary>
    /// Déplace le chemin matérialisé de cette catégorie d'un préfixe à un autre,
    /// quand un ANCÊTRE a été renommé. Ni le nom, ni le slug, ni le rattachement
    /// parent ne changent : seule la position dans l'arbre est réécrite.
    /// </summary>
    /// <remarks>
    /// ON RÉÉCRIT PAR SUBSTITUTION DE PRÉFIXE, ON NE RECONSTRUIT PAS.
    ///
    /// Reconstruire le chemin de chaque descendant par <see cref="BuildPath"/>
    /// supposerait de connaître le chemin de SON parent immédiat, donc de traiter
    /// la branche dans l'ordre de profondeur, en tenant à jour une carte des
    /// chemins déjà réécrits. La substitution de préfixe donne le même résultat
    /// sans dépendre de l'ordre : chaque descendant porte déjà sa position
    /// relative, et seule la racine bouge.
    ///
    /// AUCUN CONTRÔLE D'UNICITÉ N'EST REFAIT SUR LES DESCENDANTS, ET C'EST JUSTE.
    /// La structure relative de la branche est conservée à l'identique ; si la
    /// nouvelle racine est libre — ce que l'appelant vérifie — alors aucun
    /// descendant ne peut entrer en collision, sauf à avoir déjà été en collision
    /// avant le renommage.
    /// </remarks>
    public Result RebasePath(string ancienPrefixe, string nouveauPrefixe)
    {
        if (string.IsNullOrWhiteSpace(ancienPrefixe) || string.IsNullOrWhiteSpace(nouveauPrefixe))
        {
            return Result.Failure(Error.Validation(
                "catalog.category.rebase_invalid", "Les deux préfixes sont obligatoires."));
        }

        // ON REFUSE PLUTÔT QUE DE NE RIEN FAIRE. Appeler cette méthode sur une
        // catégorie qui n'est pas sous l'ancien préfixe est un défaut d'appelant —
        // il a mal constitué sa liste de descendants. Ignorer en silence
        // laisserait une branche à moitié déplacée, ce qui est exactement le
        // défaut qu'on corrige.
        if (!Path.StartsWith(ancienPrefixe, StringComparison.Ordinal))
        {
            return Result.Failure(Error.Validation(
                "catalog.category.rebase_not_descendant",
                $"« {Path} » n'est pas sous « {ancienPrefixe} »."));
        }

        Path = nouveauPrefixe + Path[ancienPrefixe.Length..];
        return Result.Success();
    }
}
