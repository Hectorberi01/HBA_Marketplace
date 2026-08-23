using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using Microsoft.Extensions.Logging;

namespace HBA.Catalog.Application.Offers;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES OFFRES D'UNE BOUTIQUE QUI FERME.
///
/// SANS CECI, `StoreStatus` SERAIT DÉCORATIF.
///
/// Une boutique fermée dont les offres restent achetables, c'est le défaut S1
/// transposé d'un cran : le vendeur ferme, voit « Fermée » sur son écran, et les
/// commandes continuent d'arriver. Le statut ne vaut que par cet effet.
///
/// LA BOUCLE VIT ICI, PAS AU COMPOSITION ROOT.
///
/// Le raccordement pourrait lire la liste puis envoyer une commande par offre.
/// Ce serait une transaction par offre : une panne à mi-parcours laisserait une
/// boutique à moitié fermée, sans que rien ne dise où l'on s'était arrêté. Ici,
/// tout bascule ou rien.
///
/// CES DEUX COMMANDES ONT ATTENDU LONGTEMPS LEUR APPELANT (ISSUE-041).
///
/// Elles étaient écrites, complètes, et rien ne reliait l'événement de fermeture
/// de merchant-service à ce module : fermer une boutique laissait ses offres en
/// vente. Le raccordement vit désormais dans
/// `Catalog.Infrastructure.Integration.StoreLifecycleCatalogHandlers`, qui garde
/// l'idempotence et délègue ici.
///
/// LE `SaveChangesAsync` CI-DESSOUS VALIDE AUSSI LA MARQUE D'INBOX du handler
/// appelant : il l'a inscrite dans le MÊME `DbContext` avant d'envoyer la
/// commande. Committer plus tôt, ou dans une autre portée, rouvrirait la fenêtre
/// que l'inbox ferme.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record SuspendStoreCatalogCommand(Guid StoreId, string? Reason) : ICommand;

/// <summary>
/// Remet en vente les offres retirées PAR LA FERMETURE DE CETTE BOUTIQUE — et
/// rien d'autre. Rend les offres réactivées pour que l'appelant revérifie leur
/// stock.
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
        //
        // Une fiche est portée par le VENDEUR, pas par la boutique : plusieurs de
        // ses boutiques peuvent proposer le même article, et d'autres vendeurs
        // aussi. Dépublier la fiche parce qu'une boutique ferme retirerait de la
        // vente les offres de tout le monde.
        var retirees = 0;
        foreach (var offre in offres.Where(o =>
                     o.Status is OfferStatus.Active or OfferStatus.OutOfStock or OfferStatus.Paused))
        {
            // LE MOTIF EST COMPOSÉ AVANT L'APPEL, PAS DEDANS.
            //
            // `Suspend` écrase `Status`. Lire `offre.Status` en argument d'un appel
            // qui le modifie fonctionne — les arguments sont évalués d'abord — mais
            // c'est le genre de dépendance à l'ordre d'évaluation qu'une refonte
            // casse sans bruit. Et le motif est composé PAR OFFRE, désormais :
            // c'est lui qui portera l'état à restaurer à la réouverture.
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

        // Le filtre sur le motif : voir `StoreCatalogClosure`. Une offre suspendue
        // parce que son VENDEUR l'est, ou par un modérateur, reste retirée.
        var candidates = offres
            .Where(o => o.Status == OfferStatus.Suspended && StoreCatalogClosure.IsStoreClosure(o.StatusReason))
            .ToList();

        var reactivees = new List<ReinstatedOffer>();

        if (candidates.Count > 0)
        {
            // Les SKU de TOUTES les fiches concernées : une offre de cette
            // boutique peut porter sur le produit d'un autre vendeur.
            var skusParVariante = await _products.GetSkusByVariantIdsAsync(
                candidates.Select(o => o.VariantId).Distinct().ToList(), cancellationToken);

            foreach (var offre in candidates)
            {
                // LU AVANT `Activate()`, QUI EFFACE LE MOTIF.
                //
                // `ChangeStatus(Active, reason: null)` remet `StatusReason` à null.
                // Lire l'état d'avant après l'appel rendrait toujours `Active` —
                // donc remettrait en vente, à chaque réouverture, des offres qui
                // étaient en rupture.
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
        // stock. C'est l'appelant qui interroge Inventory et remet en rupture ce
        // qui doit l'être — d'où la liste rendue.
        _logger.LogInformation(
            "Boutique {StoreId} rouverte : {Offres} offre(s) remise(s) en vente.",
            command.StoreId, reactivees.Count);

        return Result.Success<IReadOnlyList<ReinstatedOffer>>(reactivees);
    }
}
