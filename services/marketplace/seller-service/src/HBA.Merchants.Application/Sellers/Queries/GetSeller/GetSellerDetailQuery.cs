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
        var stores = await _stores.ListBySellerAsync(query.SellerId, cancellationToken);

        return SellerMapper.ToDetail(
            seller, _pricing.CommissionRate, stores.Select(StoreMapper.ToSummary).ToList());
    }
}
