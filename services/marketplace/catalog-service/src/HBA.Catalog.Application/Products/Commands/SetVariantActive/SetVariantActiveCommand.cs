using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Application.Products.Commands.SetVariantActive;

/// <summary>Retire une déclinaison de la vente, ou l'y remet.</summary>
/// <param name="Active">
/// `false` retire de la vente et archive les offres ; `true` remet la déclinaison
/// proposable, sans rien rétablir.
/// </param>
public sealed record SetVariantActiveCommand(Guid ProductId, Guid VariantId, bool Active)
    : ICommand<int>;

internal sealed class SetVariantActiveCommandHandler : ICommandHandler<SetVariantActiveCommand, int>
{
    private readonly IProductRepository _products;
    private readonly IProductOfferRepository _offers;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public SetVariantActiveCommandHandler(
        IProductRepository products,
        IProductOfferRepository offers,
        ICatalogUnitOfWork unitOfWork)
    {
        _products = products;
        _offers = offers;
        _unitOfWork = unitOfWork;
    }

    /// <returns>Le nombre d'offres archivées.</returns>
    public async Task<Result<int>> Handle(SetVariantActiveCommand command, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(new ProductId(command.ProductId), ct);
        if (product is null)
        {
            return Result.Failure<int>(Error.NotFound(
                "catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        var variante = product.Variants.FirstOrDefault(v => v.Id == command.VariantId);
        if (variante is null)
        {
            return Result.Failure<int>(Error.NotFound(
                "catalog.variant.not_found",
                $"Variante {command.VariantId} introuvable sur ce produit."));
        }

        // IDEMPOTENT, ET SANS EFFET DE BORD. Une seconde désactivation ne doit pas
        // rearchiver — il n'y a plus rien à archiver — mais surtout ne doit pas
        // échouer : l'application peut rejouer le geste après un réseau coupé.
        if (variante.IsActive == command.Active)
        {
            return Result.Success(0);
        }

        var archivees = 0;

        if (command.Active)
        {
            variante.Reactivate();
        }
        else
        {
            variante.Deactivate();

            // LES OFFRES SONT SUIVIES PAR EF (`ListByVariantAsync` n'est pas
            // `AsNoTracking`) : les muter suffit, le `SaveChanges` ci-dessous les
            // emporte.
            var offres = await _offers.ListByVariantAsync(command.VariantId, ct);
            foreach (var offre in offres)
            {
                // Un échec d'archivage n'interrompt PAS la boucle : une offre déjà
                // dans un état terminal refuserait la transition, et laisser les
                // suivantes ouvertes serait le pire des deux mondes — la
                // déclinaison fermée, une partie de ses offres encore vendable.
                if (offre.Archive().IsSuccess)
                {
                    archivees++;
                }
            }
        }

        // UNE SEULE TRANSACTION pour la variante ET ses offres.
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(archivees);
    }
}
