using HBA.Shared.Application.Messaging;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.UploadProductImage;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// RATTACHE UNE IMAGE DÉJÀ DÉPOSÉE À UN PRODUIT.
///
/// CETTE COMMANDE NE TÉLÉVERSE PLUS RIEN, ET SON NOM LE DIT MAL.
///
/// Elle recevait les octets et appelait le stockage elle-même. Deux
/// conséquences : Catalog portait sa propre implémentation S3 — la troisième du
/// dépôt — et un échec réseau au milieu d'un `Handle` laissait un fichier
/// téléversé sans ligne pour le désigner.
///
/// Le dépôt appartient désormais au service média, et se fait AVANT : la route
/// téléverse, obtient un identifiant, puis envoie cette commande. Si le
/// rattachement échoue, le média existe sans produit — visible dans le service
/// média, donc récupérable, ce que l'orphelin précédent n'était pas.
///
/// L'URL EST FOURNIE, PAS CHOISIE. C'est la copie de lecture décrite sur
/// `ProductMedia` : l'appelant la tient du service média, il ne l'invente pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record UploadProductImageCommand(
    Guid ProductId,
    Guid MediaId,
    string Url,
    string? AltText = null,
    bool IsPrimary = false) : ICommand<AttachedProductImage>;

/// <summary>
/// Ce qu'un rattachement rend à son appelant.
///
/// LA COMMANDE NE RENDAIT QUE L'URL, ET C'ÉTAIT INSUFFISANT.
///
/// L'appelant a besoin de <see cref="ProductMediaId"/> — l'identifiant de la
/// LIGNE — pour proposer ensuite « supprimer cette image » ou « en faire l'image
/// principale ». Le lui faire deviner, ou lui faire relire le produit entier,
/// c'est le pousser à passer l'identifiant du FICHIER à ces routes, qui répondent
/// alors « média introuvable » sans expliquer pourquoi.
/// </summary>
public sealed record AttachedProductImage(Guid ProductMediaId, string Url);

internal sealed class UploadProductImageCommandHandler : ICommandHandler<UploadProductImageCommand, AttachedProductImage>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public UploadProductImageCommandHandler(
        IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AttachedProductImage>> Handle(UploadProductImageCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable.");
        }

        var added = product.AddMedia(
            command.MediaId, command.Url, ProductMediaType.Image, command.AltText, command.IsPrimary);
        if (added.IsFailure)
        {
            return Result.Failure<AttachedProductImage>(added.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new AttachedProductImage(added.Value.Id, added.Value.Url));
    }
}
