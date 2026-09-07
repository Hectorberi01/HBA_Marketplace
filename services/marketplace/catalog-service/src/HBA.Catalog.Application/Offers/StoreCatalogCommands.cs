using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using Microsoft.Extensions.Logging;

namespace HBA.Catalog.Application.Offers;

/// <summary>LES OFFRES D'UNE BOUTIQUE QUI FERME.</summary>
public sealed record SuspendStoreCatalogCommand(Guid StoreId, string? Reason) : ICommand;

/// <summary>
/// Remet en vente les offres retirées PAR LA FERMETURE DE CETTE BOUTIQUE — et rien
/// d'autre.
/// </summary>
public sealed record ReinstateStoreCatalogCommand(Guid StoreId) : ICommand<IReadOnlyList<ReinstatedOffer>>;

internal sealed class StoreCatalogCommandHandler
    : ICommandHandler<SuspendStoreCatalogCommand>,
      ICommandHandler<ReinstateStoreCatalogCommand, IReadOnlyList<ReinstatedOffer>>
{
    private readonly IProductRepository _products;
    private readonly IProductOfferRepository _offers;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<StoreCatalogCommandHandler> _logger;

    public StoreCatalogCommandHandler(
        IProductRepository products,
        IProductOfferRepository offers,
        ICatalogUnitOfWork unitOfWork,
        ILogger<StoreCatalogCommandHandler> logger)
    {
        _products = products;
        _offers = offers;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(SuspendStoreCatalogCommand command, CancellationToken cancellationToken)
    {
        var offres = await _offers.ListAllByStoreForUpdateAsync(command.StoreId, cancellationToken);

        // ON NE TOUCHE PAS AUX FICHES PRODUIT, SEULEMENT AUX OFFRES.
        var retirees = 0;
        foreach (var offre in offres.Where(o =>
                     o.Status is OfferStatus.Active or OfferStatus.OutOfStock or OfferStatus.Paused))
        {
            // LE MOTIF EST COMPOSÉ AVANT L'APPEL, PAS DEDANS.
            var motif = StoreCatalogClosure.ComposeReason(command.Reason, offre.Status);

            if (offre.Suspend(motif).IsSuccess)
            {
                retirees++;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Boutique {StoreId} fermée : {Offres} offre(s) retirée(s) de la vente.",
            command.StoreId, retirees);

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<ReinstatedOffer>>> Handle(
        ReinstateStoreCatalogCommand command, CancellationToken cancellationToken)
    {
        var offres = await _offers.ListAllByStoreForUpdateAsync(command.StoreId, cancellationToken);

        // Le filtre sur le motif : voir `StoreCatalogClosure`.
        var candidates = offres
            .Where(o => o.Status == OfferStatus.Suspended && StoreCatalogClosure.IsStoreClosure(o.StatusReason))
            .ToList();

        var reactivees = new List<ReinstatedOffer>();

        if (candidates.Count > 0)
        {
            // Les SKU de TOUTES les fiches concernées : une offre de cette boutique
            // peut porter sur le produit d'un autre vendeur.
            var skusParVariante = await _products.GetSkusByVariantIdsAsync(
                candidates.Select(o => o.VariantId).Distinct().ToList(), cancellationToken);

            foreach (var offre in candidates)
            {
                // LU AVANT `Activate()`, QUI EFFACE LE MOTIF.
                var avant = StoreCatalogClosure.ReadPreviousStatus(offre.StatusReason);

                // La liste blanche n'autorise que `Suspended -> Active` et
                // `Suspended -> Archived`. On repasse donc par `Active` puis on
                // redescend : chaque saut est une transition légale.
                if (offre.Activate().IsFailure)
                {
                    continue;
                }

                switch (avant)
                {
                    case OfferStatus.OutOfStock:
                        offre.MarkOutOfStock();
                        break;

                    case OfferStatus.Paused:
                        offre.Pause();
                        break;
                }

                reactivees.Add(new ReinstatedOffer(
                    offre.Id.Value,
                    skusParVariante.GetValueOrDefault(offre.VariantId)));
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Ces offres redeviennent « Active » sans que rien ici ne connaisse le
        // stock.
        _logger.LogInformation(
            "Boutique {StoreId} rouverte : {Offres} offre(s) remise(s) en vente.",
            command.StoreId, reactivees.Count);

        return Result.Success<IReadOnlyList<ReinstatedOffer>>(reactivees);
    }
}
