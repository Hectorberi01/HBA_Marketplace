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
/// <summary>
/// La page des dossiers de retour, toutes boutiques confondues (administration).
/// </summary>
/// <remarks>
/// IL N'Y A PAS DE « FILE DES LITIGES » AU SENS STRICT, ET C'EST VOULU.
///
/// `ReturnStatus` compte SEIZE états. Décider ici lesquels forment « un litige »
/// figerait dans le serveur un jugement qui appartient à l'exploitation : selon
/// le jour, ce qui presse est `ManualReview`, ou `RefundPending` qui traîne, ou
/// `InspectionPending`. La requête rend donc TOUS les dossiers, filtrables par
/// statut, avec le compte de chaque statut — et c'est l'écran qui met en avant ce
/// qui doit l'être.
/// </remarks>
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

        // `items.Count` ÉTAIT PASSÉ EN GUISE DE TOTAL, ET C'ÉTAIT LA TAILLE DE
        //    LA PAGE.
        //
        // `PagedResult.TotalPages` en déduisait toujours UNE page : le client
        // n'affichait jamais de bouton « suivant », et un client qui avait plus de
        // vingt retours ne voyait que les vingt premiers — sans rien qui indique
        // qu'il en existait d'autres.
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
        //
        // Même choix que `ListUsersQuery` d'identity-service. Le refuser
        // obligerait chaque client à connaître les seize valeurs de l'énumération ;
        // l'ignorer rend la liste complète, ce qui se voit. Ce qu'il NE FAUT PAS
        // faire, en revanche, c'est laisser croire au filtre : le compte par statut
        // rendu avec la page permet à l'écran de vérifier qu'il a bien filtré.
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

    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE CORPS NE LISAIT RIEN (audit du 27 août, constat 1.5).
    ///
    /// Il valait exactement ceci :
    ///
    ///     return Task.FromResult&lt;Result&lt;OrderReturnSummaryDto&gt;&gt;(
    ///         new OrderReturnSummaryDto(query.OrderId, 0m, "XOF", 0));
    ///
    /// Le dépôt était injecté et jamais appelé. Toutes les commandes de la
    /// plateforme affichaient « 0 remboursé, 0 retour actif », y compris celles
    /// remboursées la veille — et un zéro se lit comme une réponse, pas comme une
    /// absence de réponse.
    ///
    /// AUCUN `NotFound` ICI, ET C'EST VOULU. Une commande sans aucun retour est le
    /// cas NORMAL, pas une erreur : elle rend un résumé à zéro, qui est alors la
    /// vérité. Ce service ne connaît d'ailleurs pas les commandes — il ne saurait
    /// pas distinguer « commande inexistante » de « commande sans retour », et
    /// prétendre le contraire demanderait un appel à order-service pour une
    /// information que l'écran a déjà.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Result<OrderReturnSummaryDto>> Handle(GetOrderReturnSummaryQuery query, CancellationToken cancellationToken)
    {
        var (montant, devise, actifs) = await _returns.GetOrderSummaryAsync(query.OrderId, cancellationToken);

        return new OrderReturnSummaryDto(query.OrderId, montant, devise, actifs);
    }
}
