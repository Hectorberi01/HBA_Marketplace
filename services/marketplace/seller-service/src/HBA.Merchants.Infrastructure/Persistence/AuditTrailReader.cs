using HBA.Merchants.Application.Members;
using HBA.Shared.Application.Pagination;
using HBA.Merchants.Infrastructure.Auditing;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Merchants.Infrastructure.Persistence;

/// <summary>Lecture du journal d'audit du schéma <c>sellers</c>.</summary>
internal sealed class AuditTrailReader : IAuditTrailReader
{
    private readonly SellersDbContext _dbContext;

    public AuditTrailReader(SellersDbContext dbContext) => _dbContext = dbContext;

    public async Task<PagedResult<AuditEntryView>> ListAsync(
        IReadOnlyCollection<Guid> actorUserIds,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // COURT-CIRCUIT : une liste vide partirait vers un `IN ()` que PostgreSQL
        // refuse.
        if (actorUserIds.Count == 0)
        {
            return PagedResult<AuditEntryView>.Empty(page, pageSize);
        }

        var requete = _dbContext.Set<AuditEntry>()
            .AsNoTracking()
            .Where(e => e.ActorUserId != null && actorUserIds.Contains(e.ActorUserId.Value));

        if (fromUtc is { } debut)
        {
            requete = requete.Where(e => e.OccurredOnUtc >= debut);
        }

        // BORNE HAUTE INCLUSIVE, contrairement à l'usage habituel des intervalles.
        if (toUtc is { } fin)
        {
            requete = requete.Where(e => e.OccurredOnUtc <= fin);
        }

        var total = await requete.CountAsync(cancellationToken);

        // TRI SUR `Id` ET NON SUR `OccurredOnUtc`.
        var lignes = await requete
            .OrderByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AuditEntryView(
                e.Id,
                e.EntityType,
                e.EntityId,
                e.Operation.ToString(),
                e.ActorUserId,
                e.ActorType,
                e.CorrelationId,
                e.OccurredOnUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEntryView>(lignes, total, page, pageSize);
    }
}
