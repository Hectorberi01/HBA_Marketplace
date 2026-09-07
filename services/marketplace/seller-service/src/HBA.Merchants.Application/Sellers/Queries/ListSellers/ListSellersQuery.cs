using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers.Queries.ListSellers;

/// <summary>UNE LIGNE DE LA FILE D'ADMINISTRATION — ET RIEN DE PLUS.</summary>
/// <param name="KybDocumentCount">
/// Combien de pièces attendent d'être ouvertes — le compte, pas les références.
/// </param>
/// <param name="KybRejectionReason">
/// Le motif du dernier refus, pour que le modérateur relise ce qu'il a écrit.
/// </param>
/// <param name="CreatedOnUtc">C'est l'ancienneté qui ordonne une file d'attente.</param>
public sealed record SellerListItem(
    Guid Id,
    Guid UserId,
    string ShopName,
    string? LogoUrl,
    string Status,
    string KybStatus,
    int KybDocumentCount,
    string? KybRejectionReason,
    DateTime CreatedOnUtc);

/// <summary>La file d'administration des vendeurs, paginée et filtrable.</summary>
/// <param name="Search">Sur le nom de boutique. Insensible à la casse, sous-chaîne.</param>
/// <param name="KybStatus">`NotStarted`, `InReview`, `Verified`, `Rejected`.</param>
/// <param name="Status">`Pending`, `Active`, `Suspended`, `Closed`, `PendingReactivation`.</param>
public sealed record ListSellersQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize,
    string? Search = null,
    string? KybStatus = null,
    string? Status = null) : IQuery<PagedResult<SellerListItem>>;

internal sealed class ListSellersQueryHandler
    : IQueryHandler<ListSellersQuery, PagedResult<SellerListItem>>
{
    private readonly ISellerRepository _sellerRepository;

    public ListSellersQueryHandler(ISellerRepository sellerRepository)
        => _sellerRepository = sellerRepository;

    public async Task<Result<PagedResult<SellerListItem>>> Handle(
        ListSellersQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);

        // UN FILTRE ILLISIBLE EST IGNORÉ, PAS REFUSÉ.
        KybStatus? kyb = Enum.TryParse<KybStatus>(query.KybStatus, ignoreCase: true, out var k) ? k : null;
        SellerStatus? statut = Enum.TryParse<SellerStatus>(query.Status, ignoreCase: true, out var s) ? s : null;

        var (sellers, total, facettes) = await _sellerRepository.ListPagedAsync(
            page, pageSize, query.Search, kyb, statut, cancellationToken);

        var lignes = sellers
            .Select(v => new SellerListItem(
                v.Id.Value,
                v.UserId,
                v.ShopName,
                v.LogoUrl,
                v.Status.ToString(),
                v.KybStatus.ToString(),
                v.KybDocuments.Count,
                v.KybRejectionReason,
                v.CreatedOnUtc))
            .ToList();

        return Result.Success(new PagedResult<SellerListItem>(lignes, total, page, pageSize, facettes));
    }
}
