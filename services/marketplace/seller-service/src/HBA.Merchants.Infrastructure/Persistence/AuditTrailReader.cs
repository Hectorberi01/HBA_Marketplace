using HBA.Merchants.Application.Members;
using HBA.Shared.Application.Pagination;
using HBA.Shared.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;

namespace HBA.Merchants.Infrastructure.Persistence;

/// <summary>
/// Lecture du journal d'audit du schéma <c>sellers</c>.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// IL LIT `AuditEntry` DIRECTEMENT, SANS PASSER PAR UN AGRÉGAT.
///
/// C'est délibéré, et c'est la seule lecture du module dans ce cas. `AuditEntry`
/// n'est pas un objet métier : c'est une trace d'infrastructure, écrite par
/// `ModuleDbContext.SaveChangesAsync` à partir du `ChangeTracker`. Lui inventer un
/// agrégat, un dépôt et des événements de domaine ajouterait trois couches à une
/// table qu'on n'écrit jamais depuis le métier et qu'on ne modifie jamais.
///
/// `AsNoTracking` N'EST PAS UNE OPTIMISATION ICI, C'EST UNE PROTECTION.
///
/// Sans lui, chaque ligne lue entre dans le `ChangeTracker` du contexte — et le
/// prochain `SaveChangesAsync` de la même requête les inspecterait toutes pour
/// décider s'il faut les journaliser. Lire cent lignes d'audit ferait donc
/// travailler le journal sur son propre contenu, à chaque page.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
        // refuse. L'appelant garde déjà le cas, mais un second appelant ne le
        // saurait pas, et l'erreur serveur ne dirait rien de sa cause.
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
        //
        // Le paramètre vient d'un sélecteur de dates : l'utilisateur qui choisit
        // « jusqu'au 19 août » attend les gestes du 19 août. Une borne exclusive lui
        // rendrait une journée vide sans qu'il comprenne pourquoi — et il conclurait
        // que le journal ne retient rien.
        if (toUtc is { } fin)
        {
            requete = requete.Where(e => e.OccurredOnUtc <= fin);
        }

        var total = await requete.CountAsync(cancellationToken);

        // TRI SUR `Id` ET NON SUR `OccurredOnUtc`.
        //
        // Toutes les lignes d'une même transaction partagent l'horodatage — il est
        // calculé une fois par `SaveChangesAsync`, délibérément. Trier dessus
        // rendrait l'ordre INTERNE de ces lignes non déterministe, donc la
        // pagination instable : une même ligne pourrait apparaître sur deux pages,
        // ou sur aucune. `Id` est une séquence, il tranche.
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
