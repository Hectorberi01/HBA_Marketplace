using HBA.Shared.Application.Abstractions;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Attributes;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.ChangeProductStatus;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ROUTE UN STATUT CIBLE VENDEUR VERS LA TRANSITION MÉTIER CORRESPONDANTE.
///
/// CE HANDLER NE PEUT PAS APPROUVER, REJETER, SUSPENDRE NI RESTAURER.
///
/// C'est la règle absolue du §4, posée une deuxième fois à l'endroit où elle
/// pourrait être contournée. Ces quatre transitions appartiennent à
/// l'administrateur (§16) et passent par leurs propres commandes, avec l'identité
/// du relecteur. Les accepter ici donnerait au vendeur le droit d'approuver sa
/// propre fiche par un appel d'apparence anodine — un simple changement de statut.
///
/// L'agrégat refuserait de toute façon la plupart de ces enchaînements. Mais s'y
/// fier supposerait que la liste blanche des transitions ne bouge jamais ; ce
/// refus-ci ne dépend d'aucune autre règle.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class ChangeProductStatusCommandHandler : ICommandHandler<ChangeProductStatusCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryAttributeRepository _attributsDeCategorie;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public ChangeProductStatusCommandHandler(
        IProductRepository productRepository,
        ICategoryAttributeRepository attributsDeCategorie,
        ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _attributsDeCategorie = attributsDeCategorie;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ChangeProductStatusCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        // « PENDING_REVIEW » et « PendingReview » désignent la même chose : le
        // cahier écrit les statuts en SCREAMING_SNAKE_CASE dans les JSON (§5) et en
        // PascalCase dans le C# (§7).
        var normalise = command.Status?.Replace("_", string.Empty).Trim();

        if (!Enum.TryParse<ProductStatus>(normalise, ignoreCase: true, out var cible)
            || !Enum.IsDefined(typeof(ProductStatus), cible))
        {
            return Result.Failure(Error.Validation(
                "catalog.product.status_invalid",
                $"Statut inconnu : « {command.Status} »."));
        }

        var maintenant = DateTimeOffset.UtcNow;

        // ═════════════════════════════════════════════════════════════════════
        // LES ATTRIBUTS REQUIS SE VÉRIFIENT ICI, PAS DANS L'AGRÉGAT (§23).
        //
        // `Product.SubmitForReview` contrôle ce qu'il POSSÈDE — boutique,
        // description, images. Les attributs requis, eux, dépendent du SCHÉMA de la
        // catégorie, qui vit dans une autre table : le domaine ne peut pas le
        // connaître sans devenir dépendant d'un dépôt.
        //
        // C'est donc le handler qui charge le schéma et le confie à
        // `ValidationDesAttributs`. La règle reste dans le domaine, seule la lecture
        // est ici — l'inverse aurait mis la règle dans un validateur qui, pour
        // travailler, devrait interroger la base.
        //
        // ET SEULEMENT À LA SOUMISSION.
        //
        // Pas à la création ni à la modification : le §23 exige les attributs
        // « avant soumission », et les exiger plus tôt empêcherait d'enregistrer une
        // ébauche à l'étape 1 du formulaire, qui en compte onze.
        // ═════════════════════════════════════════════════════════════════════
        if (cible is ProductStatus.PendingReview)
        {
            var revision = product.CurrentRevision;

            var schema = await _attributsDeCategorie.ListByCategoryAsync(
                revision.CategoryId, cancellationToken);

            var attributs = ValidationDesAttributs.Valider(schema, revision.Attributes);
            if (attributs.IsFailure)
            {
                return attributs;
            }
        }

        var transition = cible switch
        {
            ProductStatus.PendingReview => product.SubmitForReview(maintenant),
            ProductStatus.Published => product.Publish(maintenant),
            ProductStatus.Unpublished => product.Unpublish(),
            ProductStatus.Archived => product.Archive(),

            ProductStatus.Approved or ProductStatus.Rejected or ProductStatus.Suspended
                => Result.Failure(Error.Forbidden(
                    "catalog.product.admin_transition",
                    "Cette transition appartient à l'administration : approbation, rejet, suspension et restauration passent par l'API admin.")),

            // ON NE REVIENT PAS À DRAFT PAR CETTE ROUTE.
            //
            // Le retour en brouillon est la CONSÉQUENCE d'un rejet (REJECTED →
            // DRAFT, §5), pas une action que le vendeur déclenche. L'ouvrir ici lui
            // permettrait de sortir de PENDING_REVIEW pendant la relecture — voir
            // l'encadré correspondant dans ProductStatusTransitions.
            ProductStatus.Draft => Result.Failure(Error.Conflict(
                "catalog.product.invalid_status_transition",
                "Le retour en brouillon suit un rejet ; il ne se demande pas.")),

            _ => Result.Failure(Error.Validation("catalog.product.status_invalid", "Statut invalide."))
        };

        if (transition.IsFailure)
        {
            return transition;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
