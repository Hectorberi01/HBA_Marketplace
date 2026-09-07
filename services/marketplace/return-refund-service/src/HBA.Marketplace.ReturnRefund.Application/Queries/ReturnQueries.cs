using HBA.Marketplace.ReturnRefund.Application.DTOs;
using HBA.Marketplace.ReturnRefund.Application.Mappings;
using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;

namespace HBA.Marketplace.ReturnRefund.Application.Queries;

public sealed record GetReturnQuery(Guid ReturnId) : IQuery<ReturnRequestDto>;
public sealed record GetCustomerReturnsQuery(Guid CustomerId, int Page, int PageSize) : IQuery<PagedResult<ReturnRequestDto>>;
public sealed record GetSellerReturnsQuery(Guid SellerId, int Page, int PageSize) : IQuery<PagedResult<ReturnRequestDto>>;
/// <summary>La page des dossiers de retour, toutes boutiques confondues (administration).</summary>
public sealed record ListAdminReturnsQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize,
    string? Status = null) : IQuery<PagedResult<ReturnRequestDto>>;

public sealed record GetReturnTimelineQuery(Guid ReturnId) : IQuery<IReadOnlyList<ReturnTimelineEntryDto>>;
public sealed record GetOrderReturnSummaryQuery(Guid OrderId) : IQuery<OrderReturnSummaryDto>;

internal sealed class GetReturnQueryHandler : IQueryHandler<GetReturnQuery, ReturnRequestDto>
{
    private readonly IReturnRequestRepository _returns;

    public GetReturnQueryHandler(IReturnRequestRepository returns) => _returns = returns;

    public async Task<Result<ReturnRequestDto>> Handle(GetReturnQuery query, CancellationToken cancellationToken)
    {
        var request = await _returns.GetAsync(query.ReturnId, cancellationToken);
        return request is null
            ? Error.NotFound("return.not_found", "Retour introuvable.")
            : request.ToDto();
    }
}

internal sealed class GetCustomerReturnsQueryHandler : IQueryHandler<GetCustomerReturnsQuery, PagedResult<ReturnRequestDto>>
{
    private readonly IReturnRequestRepository _returns;

    public GetCustomerReturnsQueryHandler(IReturnRequestRepository returns) => _returns = returns;

    public async Task<Result<PagedResult<ReturnRequestDto>>> Handle(GetCustomerReturnsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);
        var items = await _returns.ListCustomerAsync(query.CustomerId, page, pageSize, cancellationToken);

        // `items.Count` ÉTAIT PASSÉ EN GUISE DE TOTAL, ET C'ÉTAIT LA TAILLE DE LA
        // PAGE.
        var total = await _returns.CountCustomerAsync(query.CustomerId, cancellationToken);

        return new PagedResult<ReturnRequestDto>(items.Select(r => r.ToDto()).ToList(), total, page, pageSize);
    }
}

internal sealed class GetSellerReturnsQueryHandler : IQueryHandler<GetSellerReturnsQuery, PagedResult<ReturnRequestDto>>
{
    private readonly IReturnRequestRepository _returns;

    public GetSellerReturnsQueryHandler(IReturnRequestRepository returns) => _returns = returns;

    public async Task<Result<PagedResult<ReturnRequestDto>>> Handle(GetSellerReturnsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);
        var items = await _returns.ListSellerAsync(query.SellerId, page, pageSize, cancellationToken);

        // Même correction que côté client : le total était la taille de la page.
        var total = await _returns.CountSellerAsync(query.SellerId, cancellationToken);

        return new PagedResult<ReturnRequestDto>(items.Select(r => r.ToDto()).ToList(), total, page, pageSize);
    }
}

internal sealed class ListAdminReturnsQueryHandler : IQueryHandler<ListAdminReturnsQuery, PagedResult<ReturnRequestDto>>
{
    private readonly IReturnRequestRepository _returns;

    public ListAdminReturnsQueryHandler(IReturnRequestRepository returns) => _returns = returns;

    public async Task<Result<PagedResult<ReturnRequestDto>>> Handle(
        ListAdminReturnsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);

        // UN STATUT ILLISIBLE EST IGNORÉ, IL NE FAIT PAS ÉCHOUER LA REQUÊTE.
        ReturnStatus? statut = Enum.TryParse<ReturnStatus>(query.Status, ignoreCase: true, out var lu)
            ? lu
            : null;

        var (items, total, comptes) = await _returns.ListForAdminAsync(page, pageSize, statut, cancellationToken);

        return new PagedResult<ReturnRequestDto>(
            items.Select(r => r.ToDto()).ToList(), total, page, pageSize, comptes);
    }
}

internal sealed class GetReturnTimelineQueryHandler : IQueryHandler<GetReturnTimelineQuery, IReadOnlyList<ReturnTimelineEntryDto>>
{
    private readonly IReturnRequestRepository _returns;

    public GetReturnTimelineQueryHandler(IReturnRequestRepository returns) => _returns = returns;

    public async Task<Result<IReadOnlyList<ReturnTimelineEntryDto>>> Handle(GetReturnTimelineQuery query, CancellationToken cancellationToken)
    {
        var request = await _returns.GetAsync(query.ReturnId, cancellationToken);
        return request is null
            ? Error.NotFound("return.not_found", "Retour introuvable.")
            : request.History.OrderBy(h => h.OccurredAtUtc).Select(h => h.ToDto()).ToList();
    }
}

internal sealed class GetOrderReturnSummaryQueryHandler : IQueryHandler<GetOrderReturnSummaryQuery, OrderReturnSummaryDto>
{
    private readonly IReturnRequestRepository _returns;

    public GetOrderReturnSummaryQueryHandler(IReturnRequestRepository returns) => _returns = returns;

    public async Task<Result<OrderReturnSummaryDto>> Handle(GetOrderReturnSummaryQuery query, CancellationToken cancellationToken)
    {
        var (montant, devise, actifs) = await _returns.GetOrderSummaryAsync(query.OrderId, cancellationToken);

        return new OrderReturnSummaryDto(query.OrderId, montant, devise, actifs);
    }
}
