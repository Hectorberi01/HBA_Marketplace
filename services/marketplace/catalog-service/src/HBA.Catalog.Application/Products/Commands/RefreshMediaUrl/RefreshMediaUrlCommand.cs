using HBA.Shared.Application.Messaging;
using HBA.Catalog.Application.Abstractions;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Application.Products.Commands.RefreshMediaUrl;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// REMET À JOUR LA COPIE DE LECTURE D'UNE IMAGE.
///
/// C'EST CETTE COMMANDE QUI EMPÊCHE `ProductMedia.Url` D'ÊTRE UNE SECONDE
/// VÉRITÉ.
///
/// Garder une URL recopiée à côté d'un identifiant de média est un choix de
/// performance assumé : les listes de produits ne peuvent pas résoudre cinquante
/// médias par page. Le prix de ce choix est qu'il existe un cas — un seul — où la
/// copie devient fausse : un changement d'INFRASTRUCTURE. Domaine CDN modifié,
/// bucket renommé, `PublicBaseUrl` réécrite. Toutes les URL du catalogue pointent
/// alors vers une adresse morte, et aucun événement métier ne le signale.
///
/// CETTE COMMANDE EST UN OUTIL D'EXPLOITATION, PAS UN ABONNEMENT.
///
/// Rien ne l'appelle en réaction à un événement, et il ne faut pas en chercher :
/// retraiter un média régénère ses variantes, pas son original, donc l'URL de
/// celui-ci ne bouge pas. Elle est déclenchée par une route d'administration,
/// après une décision humaine — voir `POST /catalog/products/{id}/media/refresh`.
///
/// SI ELLE N'ÉTAIT APPELÉE DE NULLE PART, elle serait du code mort qui donne
/// l'illusion d'une garantie. C'est le motif pour lequel la route existe.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record RefreshMediaUrlCommand(Guid ProductId, Guid MediaId, string Url) : ICommand;

internal sealed class RefreshMediaUrlCommandHandler : ICommandHandler<RefreshMediaUrlCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public RefreshMediaUrlCommandHandler(IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RefreshMediaUrlCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);

        // UN PRODUIT DISPARU N'EST PAS UNE ERREUR ICI.
        //
        // Le message peut arriver après la suppression du produit, ou après le
        // détachement de l'image. Répondre en échec le ferait rejouer sans fin
        // dans l'outbox pour une image que plus personne n'affiche.
        if (product is null || !product.RefreshMediaUrl(command.MediaId, command.Url))
        {
            return Result.Success();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
