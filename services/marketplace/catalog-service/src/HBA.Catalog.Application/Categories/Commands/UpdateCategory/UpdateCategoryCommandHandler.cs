using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Categories;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Categories.Commands.UpdateCategory;

/// <summary>
/// Recharge le chemin du parent pour recalculer le chemin matérialisé, vérifie
/// l'unicité du slug si le nom change, applique la mise à jour puis persiste.
/// </summary>
internal sealed class UpdateCategoryCommandHandler : ICommandHandler<UpdateCategoryCommand>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public UpdateCategoryCommandHandler(ICategoryRepository categoryRepository, ICatalogUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(new CategoryId(command.CategoryId), cancellationToken);
        if (category is null)
        {
            return Result.Failure(Error.NotFound("catalog.category.not_found", $"Catégorie {command.CategoryId} introuvable."));
        }

        var slugResult = Slug.Create(command.Name);
        if (slugResult.IsFailure)
        {
            return Result.Failure(slugResult.Error);
        }

        // Le chemin du parent doit être connu AVANT le contrôle d'unicité : c'est lui
        // qui distingue « /animaux/chiens/alimentation » de « /animaux/chats/alimentation ».
        string? parentPath = null;
        if (category.ParentId is { } parentId)
        {
            var parent = await _categoryRepository.GetByIdAsync(new CategoryId(parentId), cancellationToken);
            parentPath = parent?.Path;
        }

        // `excludeId` : sans lui, enregistrer une catégorie sans changer son nom la
        // ferait entrer en conflit avec elle-même.
        var newPath = Category.BuildPath(parentPath, slugResult.Value.Value);
        if (await _categoryRepository.PathExistsAsync(newPath, category.Id.Value, cancellationToken))
        {
            return Result.Failure(Error.Conflict(
                "catalog.category.path_taken",
                $"Une catégorie « {command.Name} » existe déjà à cet emplacement."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // LES DESCENDANTS SONT RELUS AVANT LA MUTATION, PAS APRÈS (audit 2.4).
        //
        // CE QUI ÉTAIT CASSÉ. `Update()` recalculait le chemin de la catégorie
        // modifiée et d'AUCUN descendant. Renommer « Animaux » (`/animaux`) en
        // « Animaux domestiques » donnait `/animaux-domestiques` à la racine, et
        // laissait ses enfants en `/animaux/chiens`, `/animaux/chats`.
        //
        // `ListDescendantsAsync` cherche par PRÉFIXE : la branche entière devenait
        // introuvable. Publication et dépublication en cascade ne l'atteignaient
        // plus, et les filtres par catégorie la perdaient — sans une erreur, sans
        // une ligne de journal. La méthode nécessaire existait pourtant, et
        // `PublishCategoryCommandHandler` s'en servait déjà : seul le renommage
        // l'oubliait.
        //
        // L'ORDRE EST LE POINT DÉLICAT. `category.Path` est la CLÉ DE RECHERCHE
        // des descendants. Muter d'abord et chercher ensuite ne ramènerait rien —
        // on chercherait sous le nouveau chemin, où personne n'habite encore. On
        // capture donc l'ancien chemin et on charge la branche AVANT `Update()`.
        //
        // Les entités rendues sont SUIVIES par EF (`ListDescendantsAsync` n'appelle
        // pas `AsNoTracking`), donc la réécriture part au `SaveChangesAsync` final,
        // dans la MÊME transaction que la racine. Une branche à moitié déplacée
        // serait pire que pas de cascade du tout.
        // ═════════════════════════════════════════════════════════════════════
        var ancienChemin = category.Path;

        // Chemin inchangé — l'appelant n'a touché qu'à l'image ou au schéma : on
        // n'ouvre pas une lecture de branche pour rien.
        IReadOnlyList<Category> descendants = Array.Empty<Category>();

        if (!string.Equals(ancienChemin, newPath, StringComparison.Ordinal))
        {
            descendants = await _categoryRepository.ListDescendantsAsync(ancienChemin, cancellationToken);
        }

        var updateResult = category.Update(command.Name, parentPath, command.ImageUrl, command.AttributeSchema);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        // LE PRÉFIXE PORTE LE SÉPARATEUR, comme dans `ListDescendantsAsync`. Sans
        // lui, `/animaux` → `/animaux-domestiques` transformerait aussi le chemin
        // d'un `/animaux-sauvages` qui n'est pas un descendant — et qui n'est
        // d'ailleurs pas dans la liste, d'où le refus explicite de `RebasePath`.
        var ancienPrefixe = ancienChemin.TrimEnd('/') + "/";
        var nouveauPrefixe = category.Path.TrimEnd('/') + "/";

        foreach (var descendant in descendants)
        {
            var rebase = descendant.RebasePath(ancienPrefixe, nouveauPrefixe);

            if (rebase.IsFailure)
            {
                // ON ÉCHOUE LA COMMANDE ENTIÈRE, ON NE SAUTE PAS.
                //
                // Un descendant qui refuse la réécriture signale que la liste ne
                // correspond pas à l'arbre — donc que le résultat serait une
                // branche incohérente. Rien n'ayant encore été persisté, échouer
                // ici laisse la catégorie telle qu'elle était.
                return rebase;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
