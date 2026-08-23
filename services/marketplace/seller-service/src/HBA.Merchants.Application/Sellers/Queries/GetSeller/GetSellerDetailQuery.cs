using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Application.Stores;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Stores;

namespace HBA.Merchants.Application.Sellers.Queries.GetSeller;

/// <summary>
/// La fiche vendeur COMPLÈTE du §10.3 : le dossier et ses boutiques, en un appel.
/// </summary>
/// <remarks>
/// ELLE REMPLACE `GetSellerQuery`, QUI EST SUPPRIMÉE — ET C'EST DÉLIBÉRÉ.
///
/// Une fois la route HTTP bascule ici, plus personne n'envoyait `GetSellerQuery` :
/// les appels INTER-SERVICES passent par `ISellerModuleApi` et le service gRPC,
/// jamais par MediatR. La garder « au cas où » aurait laissé dans la couche
/// Application une requête que rien n'exerce — donc que rien ne protège d'une
/// dérive silencieuse le jour où `SellerMapper` change.
///
/// La reprendre est trivial si un chemin a un jour besoin du dossier SANS ses
/// boutiques : c'est ce handler moins une lecture.
/// </remarks>
public sealed record GetSellerDetailQuery(Guid SellerId) : IQuery<SellerDetail>;

internal sealed class GetSellerDetailQueryHandler : IQueryHandler<GetSellerDetailQuery, SellerDetail>
{
    private readonly ISellerRepository _sellers;
    private readonly IStoreRepository _stores;
    private readonly IPlatformPricing _pricing;

    public GetSellerDetailQueryHandler(
        ISellerRepository sellers, IStoreRepository stores, IPlatformPricing pricing)
    {
        _sellers = sellers;
        _stores = stores;
        _pricing = pricing;
    }

    public async Task<Result<SellerDetail>> Handle(
        GetSellerDetailQuery query, CancellationToken cancellationToken)
    {
        var seller = await _sellers.GetByIdAsync(new SellerId(query.SellerId), cancellationToken);

        if (seller is null)
        {
            return Error.NotFound("sellers.seller.not_found", $"Vendeur {query.SellerId} introuvable.");
        }

        // DEUX AGRÉGATS, DONC DEUX LECTURES — ET C'EST VOULU.
        //
        // `Store` n'est pas une entité de `Seller` : il a son propre cycle de vie,
        // son propre dépôt et ses propres événements. L'attacher à l'agrégat vendeur
        // pour économiser une requête ferait charger toutes les boutiques à chaque
        // fois qu'on touche au dossier — y compris sur les écritures.
        var stores = await _stores.ListBySellerAsync(query.SellerId, cancellationToken);

        return SellerMapper.ToDetail(
            seller, _pricing.CommissionRate, stores.Select(StoreMapper.ToSummary).ToList());
    }
}
