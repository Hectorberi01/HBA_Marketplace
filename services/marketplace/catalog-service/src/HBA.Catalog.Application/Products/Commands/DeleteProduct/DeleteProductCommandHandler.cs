using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;
using HBA.Catalog.Contracts.IntegrationEvents;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.DeleteProduct;

/// <summary>Charge le produit puis le supprime ; renvoie NotFound s'il n'existe pas.</summary>
internal sealed class DeleteProductCommandHandler : ICommandHandler<DeleteProductCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly IIntegrationEventPublisher _publisher;

    public DeleteProductCommandHandler(
        IProductRepository productRepository,
        ICatalogUnitOfWork unitOfWork,
        IIntegrationEventPublisher publisher)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<Result> Handle(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        // NOMMER LES FICHIERS AVANT DE RETIRER L'AGRÉGAT : après, ses médias
        // ne sont plus lisibles, et rien ne désignerait plus les octets.
        //
        // La version précédente effaçait ici même, en « fail-soft » : un échec
        // distant était ignoré et le fichier restait, sans trace ni reprise. Passer
        // par l'outbox transforme cet abandon silencieux en message qui réessaie.
        product.PrepareForDeletion();

        _productRepository.Remove(product);
        // Propage la suppression (via l'outbox, même transaction) : Search retire
        // le produit de l'index, sinon des entrées orphelines persistent.
        await _publisher.PublishAsync(new ProductDeletedIntegrationEvent { ProductId = command.ProductId }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
