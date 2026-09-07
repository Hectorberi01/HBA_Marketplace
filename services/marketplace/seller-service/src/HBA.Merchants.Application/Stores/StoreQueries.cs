using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Stores;

namespace HBA.Merchants.Application.Stores;

/// <summary>Les boutiques d'un vendeur — l'écran « mes boutiques ».</summary>
public sealed record ListSellerStoresQuery(Guid SellerId) : IQuery<IReadOnlyList<StoreSummary>>;

/// <summary>Une boutique par son identifiant.</summary>
public sealed record GetStoreQuery(Guid StoreId) : IQuery<StoreSummary>;

internal sealed class StoreQueryHandler
    : IQueryHandler<ListSellerStoresQuery, IReadOnlyList<StoreSummary>>,
      IQueryHandler<GetStoreQuery, StoreSummary>
{
    private readonly IStoreRepository _stores;

    public StoreQueryHandler(IStoreRepository stores) => _stores = stores;

    public async Task<Result<IReadOnlyList<StoreSummary>>> Handle(
        ListSellerStoresQuery query, CancellationToken cancellationToken)
    {
        var stores = await _stores.ListBySellerAsync(query.SellerId, cancellationToken);

        IReadOnlyList<StoreSummary> result = stores.Select(StoreMapper.ToSummary).ToList();
        return Result.Success(result);
    }

    public async Task<Result<StoreSummary>> Handle(GetStoreQuery query, CancellationToken cancellationToken)
    {
        var store = await _stores.GetByIdAsync(new StoreId(query.StoreId), cancellationToken);

        return store is null
            ? Result.Failure<StoreSummary>(Error.NotFound("sellers.store.not_found", "Boutique introuvable."))
            : Result.Success(StoreMapper.ToSummary(store));
    }
}

internal static class StoreMapper
{
    public static StoreSummary ToSummary(Store store)
        => new(
            store.Id.Value,
            store.SellerId,
            store.Name,
            store.LogoUrl,
            store.Description,
            store.Contact.Phone,
            store.Contact.Email,
            store.Status.ToString(),
            store.IsSelling,
            store.FulfillmentLocationId,
            store.StatusReason,
            store.OpeningHours
                // LUNDI EN TÊTE, PAS DIMANCHE.
                .OrderBy(h => ((int)h.Day + 6) % 7)
                .ThenBy(h => h.OpensAt)
                .Select(h => new StoreOpeningHourSummary(
                    h.Day.ToString(),
                    h.OpensAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture),
                    h.ClosesAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture)))
                .ToList(),
            store.CreatedOnUtc);
}
