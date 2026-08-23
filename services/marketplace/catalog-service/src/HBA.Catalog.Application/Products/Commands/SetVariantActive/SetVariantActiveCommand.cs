using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Application.Products.Commands.SetVariantActive;

/// <summary>Retire une déclinaison de la vente, ou l'y remet.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CETTE COMMANDE EXISTE (tâche #230).
///
/// IL N'Y AVAIT QUE `RemoveProductVariantCommand`, ET SUPPRIMER N'EST PAS
///    RETIRER DE LA VENTE.
///
/// Un vendeur dont la taille 42 est épuisée pour la saison n'a qu'une option : la
/// supprimer. Or une commande passée référence cette déclinaison par son
/// identifiant — l'effacer laisse un historique qui pointe vers rien, et le SKU
/// libéré peut être réattribué. Il perd aussi ses attributs, son code-barres, son
/// poids : tout à ressaisir en septembre.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// DÉSACTIVER ARCHIVE LES OFFRES. C'EST LE CŒUR DE LA COMMANDE.
///
/// Sans cela, la déclinaison serait « retirée de la vente » et resterait pourtant
/// achetable : les offres existantes ne consultent pas l'état de la variante à
/// chaque affichage — la Buy Box lit `ProductOffer.Status`. Le vendeur croirait
/// avoir fermé, et les commandes continueraient d'arriver.
///
/// C'est précisément ce qu'attendait `IProductOfferRepository.ListByVariantAsync`,
/// dont le commentaire annonce « l'appelant archive ces offres quand la variante
/// est désactivée » — et qui n'avait AUCUN appelant. Le voici.
///
/// L'ARCHIVAGE EST TERMINAL, DONC LA RÉACTIVATION NE REND RIEN.
///
/// `OfferStatus.Archived` ne se quitte pas. Réactiver la déclinaison ne remet donc
/// aucune offre en vitrine : le vendeur devra recréer sa mise en vente, au prix du
/// jour. Rétablir automatiquement afficherait un prix décidé six mois plus tôt —
/// et sur un marché où le sac de riz change de prix chaque mois, ce serait vendre à
/// perte sans s'en apercevoir.
///
/// La commande le dit dans son résultat : `ArchivedOffers` compte ce qui a été
/// fermé, pour que l'interface puisse avertir avant plutôt que surprendre après.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

    /// <returns>Le nombre d'offres archivées. Zéro à la réactivation.</returns>
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
            // emporte. C'est écrit dans le dépôt, et c'est pour CE cas.
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

        // UNE SEULE TRANSACTION pour la variante ET ses offres. Deux
        // enregistrements laisseraient, entre eux, une déclinaison retirée dont les
        // offres restent actives — précisément l'état qu'on cherche à rendre
        // impossible.
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(archivees);
    }
}
