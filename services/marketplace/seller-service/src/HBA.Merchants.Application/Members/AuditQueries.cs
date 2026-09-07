using HBA.Merchants.Domain.Members;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Application.Members;

/// <summary>Une ligne du journal, telle que l'écran d'équipe la montre.</summary>
/// <param name="ActorUserId">Le COMPTE, pas le membre.</param>
public sealed record AuditEntryView(
    long Id,
    string EntityType,
    string EntityId,
    string Operation,
    Guid? ActorUserId,
    string ActorType,
    string? CorrelationId,
    DateTime OccurredOnUtc);

/// <summary>LE JOURNAL D'ÉQUIPE — LA SEULE ROUTE QUE `AUDIT_VIEW` GARDE.</summary>
/// <param name="MemberUserId">
/// Filtre facultatif sur un membre précis — « qu'a fait Sophie ».
/// </param>
public sealed record ListAuditEntriesQuery(
    Guid SellerId,
    Guid ActorUserId,
    Guid? MemberUserId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page,
    int PageSize) : IQuery<PagedResult<AuditEntryView>>;

/// <summary>Lecture du journal d'audit du module.</summary>
public interface IAuditTrailReader
{
    Task<PagedResult<AuditEntryView>> ListAsync(
        IReadOnlyCollection<Guid> actorUserIds,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

internal sealed class AuditQueryHandler : IQueryHandler<ListAuditEntriesQuery, PagedResult<AuditEntryView>>
{
    private readonly ISellerMemberRepository _members;
    private readonly IAuditTrailReader _journal;
    private readonly MemberAccessResolver _acces;

    public AuditQueryHandler(
        ISellerMemberRepository members, IAuditTrailReader journal, MemberAccessResolver acces)
    {
        _members = members;
        _journal = journal;
        _acces = acces;
    }

    public async Task<Result<PagedResult<AuditEntryView>>> Handle(
        ListAuditEntriesQuery query, CancellationToken cancellationToken)
    {
        var acteur = await _acces.ResolveAsync(query.SellerId, query.ActorUserId, cancellationToken);
        if (acteur.IsFailure)
        {
            return Result.Failure<PagedResult<AuditEntryView>>(acteur.Error);
        }

        var habilitation = acteur.Value.Ensure(MerchantPermission.AuditView);
        if (habilitation.IsFailure)
        {
            return Result.Failure<PagedResult<AuditEntryView>>(habilitation.Error);
        }

        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);

        // TOUS LES MEMBRES, RÉVOQUÉS COMPRIS.
        var membres = await _members.ListBySellerAsync(query.SellerId, cancellationToken);

        var comptes = query.MemberUserId is { } cible

            // LE FILTRE PAR MEMBRE RESTE BORNÉ À L'ÉQUIPE.
            ? membres.Select(m => m.UserId).Where(id => id == cible).ToArray()
            : membres.Select(m => m.UserId).ToArray();

        if (comptes.Length == 0)
        {
            return PagedResult<AuditEntryView>.Empty(page, pageSize);
        }

        return await _journal.ListAsync(
            comptes, query.FromUtc, query.ToUtc, page, pageSize, cancellationToken);
    }
}
