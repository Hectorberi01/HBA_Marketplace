using HBA.Shared.Application.Messaging;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.RemoveProductMedia;

/// <summary>
/// Retire un média d'un produit.
///
/// LA GARANTIE D'EFFACEMENT A CHANGÉ DE NATURE.
///
/// Ce gestionnaire supprimait le fichier AVANT d'enregistrer : si l'appel
/// distant échouait, rien n'était écrit et les deux systèmes restaient d'accord.
/// C'était simple et lisible — mais cela imposait un appel réseau synchrone au
/// milieu d'une transaction, et rendait le détachement impossible dès que le
/// service de stockage était indisponible.
///
/// L'agrégat NOMME désormais le fichier (`ProductMediaRemovedDomainEvent`), et
/// l'outbox porte l'effacement. La transaction ne dépend plus d'un tiers, et une
/// panne du service média retarde l'effacement au lieu de bloquer le vendeur.
/// Le prix est une fenêtre de quelques secondes pendant laquelle le fichier
/// existe encore — acceptable pour une image publique, et documenté comme tel
/// sur l'événement.
/// </summary>
public sealed record RemoveProductMediaCommand(Guid ProductId, Guid MediaId) : ICommand;

internal sealed class RemoveProductMediaCommandHandler : ICommandHandler<RemoveProductMediaCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public RemoveProductMediaCommandHandler(
        IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveProductMediaCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        var removed = product.RemoveMedia(command.MediaId);
        if (removed.IsFailure)
        {
            return Result.Failure(removed.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
