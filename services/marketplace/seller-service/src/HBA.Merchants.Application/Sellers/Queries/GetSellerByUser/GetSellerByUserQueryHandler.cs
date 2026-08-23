using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Application.Sellers.Queries.GetSeller;
using HBA.Merchants.Application.Stores;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Stores;

namespace HBA.Merchants.Application.Sellers.Queries.GetSellerByUser;

internal sealed class GetSellerByUserQueryHandler : IQueryHandler<GetSellerByUserQuery, SellerDetail>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly IStoreRepository _stores;
    private readonly IPlatformPricing _pricing;

    public GetSellerByUserQueryHandler(
        ISellerRepository sellerRepository, IStoreRepository stores, IPlatformPricing pricing)
    {
        _sellerRepository = sellerRepository;
        _stores = stores;
        _pricing = pricing;
    }

    public async Task<Result<SellerDetail>> Handle(
        GetSellerByUserQuery query, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByUserIdAsync(query.UserId, cancellationToken);
        if (seller is null)
        {
            return Error.NotFound("sellers.seller.not_found", "Aucune boutique pour ce compte.");
        }

        // LES BOUTIQUES AUSSI, COMME SUR `GET /merchants/{id}`.
        //
        // `/me` et la fiche par identifiant servent le même écran, atteint par deux
        // chemins. Les faire diverger obligerait l'application à savoir lequel des
        // deux porte `stores` — et le §10.3 les veut imbriquées dans les deux cas.
        var stores = await _stores.ListBySellerAsync(seller.Id.Value, cancellationToken);

        return SellerMapper.ToDetail(
            seller, _pricing.CommissionRate, stores.Select(StoreMapper.ToSummary).ToList());
    }
}
