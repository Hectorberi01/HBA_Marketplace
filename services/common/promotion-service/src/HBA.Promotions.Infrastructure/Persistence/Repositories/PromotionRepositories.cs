using HBA.Promotions.Domain.Promotions;
using Microsoft.EntityFrameworkCore;

namespace HBA.Promotions.Infrastructure.Persistence;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CHAQUE LECTURE INCLUT SA COLLECTION. L'OUBLI NE LÈVE RIEN.
///
/// Le chargement paresseux est désactivé dans ce dépôt. Une campagne chargée sans
/// `Rules` présente une liste VIDE — pas une erreur, pas une exception : une liste
/// vide. `EnsureApplicable` boucle dessus, ne trouve rien à refuser, et accorde la
/// remise à des paniers que la campagne excluait.
///
/// Même chose pour un coupon sans ses `Reservations` : ses plafonds se comptent
/// sur zéro usage, donc ne s'appliquent jamais.
///
/// C'est la classe de panne la plus désagréable de cette couche : le code
/// compile, les tests de domaine passent — ils construisent les agrégats en
/// mémoire, collections comprises — et seule la production diverge.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class PromotionRepository : IPromotionRepository
{
    private readonly PromotionsDbContext _context;

    public PromotionRepository(PromotionsDbContext context) => _context = context;

    public Task<Promotion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Promotions
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Promotion>> ListAsync(
        PromotionScope? scope, int take, Guid? ownerSellerId = null,
        CancellationToken cancellationToken = default)
    {
        var requete = _context.Promotions.Include(p => p.Rules).AsQueryable();

        // LE FILTRE D'APPARTENANCE PASSE AVANT LE `Take`, ET C'EST TOUT CE QUI
        // COMPTE ICI.
        //
        // Filtrer la liste APRÈS l'avoir tronquée à `take` rendrait au vendeur les
        // quelques campagnes qui lui appartiennent PARMI les cinquante dernières de
        // la plateforme — c'est-à-dire, la plupart du temps, une liste vide, et
        // toujours une requête qui a lu les campagnes des autres.
        if (ownerSellerId is { } proprietaire)
        {
            requete = requete.Where(p => p.OwnerSellerId == proprietaire);
        }

        if (scope is { } univers)
        {
            // « GLOBAL » REMONTE AUSSI QUAND ON FILTRE SUR UN UNIVERS.
            //
            // Une campagne globale s'applique au marketplace comme au food : la
            // masquer d'une liste filtrée sur « FOOD » ferait croire à un
            // gestionnaire de restaurant qu'aucune remise ne court sur ses plats.
            requete = requete.Where(p => p.Scope == univers || p.Scope == PromotionScope.Global);
        }

        return await requete
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Promotion promotion, CancellationToken cancellationToken = default)
        => await _context.Promotions.AddAsync(promotion, cancellationToken);
}

internal sealed class CouponRepository : ICouponRepository
{
    private readonly PromotionsDbContext _context;

    public CouponRepository(PromotionsDbContext context) => _context = context;

    public Task<Coupon?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        // Le code est normalisé en majuscules à la création : la recherche doit
        // l'être aussi, sinon « welcome10 » ne trouve rien alors que le coupon
        // existe. Normaliser ICI plutôt que d'exiger une comparaison insensible à
        // la casse garde l'index `ux_coupons_code` utilisable.
        var normalise = (code ?? string.Empty).Trim().ToUpperInvariant();

        return _context.Coupons
            .Include(c => c.Reservations)
            .FirstOrDefaultAsync(c => c.Code == normalise, cancellationToken);
    }

    public Task<Coupon?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Coupons
            .Include(c => c.Reservations)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <summary>
    /// LE FILTRE EST DANS LA REQUÊTE **ET** DANS L'AGRÉGAT, DÉLIBÉRÉMENT.
    ///
    /// `Coupon.ExpireHolds` refait le tri sur la collection chargée. Ce n'est pas
    /// une redondance décorative : entre le SELECT et le SaveChanges, une retenue
    /// a pu être engagée par un checkout concurrent. La requête choisit un LOT ;
    /// c'est l'agrégat qui décide, sur l'état qu'il tient, ce qu'il libère.
    ///
    /// UN ORDRE EXPLICITE, PARCE QU'UN LOT SANS ORDRE PEUT NE JAMAIS FINIR.
    ///
    /// Sans `OrderBy`, PostgreSQL est libre de rendre le même sous-ensemble à
    /// chaque tour : cent coupons pris au hasard dans dix mille laisseraient les
    /// autres indéfiniment de côté. L'ordre retenu est la date de création du
    /// COUPON — et non le minimum des échéances de ses retenues, qui serait plus
    /// juste mais se traduit en sous-requête agrégée corrélée. Le gain serait nul :
    /// un coupon balayé quitte le filtre (ses retenues ne sont plus `Held`), donc
    /// n'importe quel ordre DÉTERMINISTE draine le retard en quelques tours.
    /// </summary>
    public async Task<IReadOnlyList<Coupon>> ListWithExpiredHoldsAsync(
        DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default)
        => await _context.Coupons
            .Include(c => c.Reservations)
            .Where(c => c.Reservations.Any(
                r => r.Status == CouponReservationStatus.Held && r.ExpiresAtUtc < nowUtc))
            .OrderBy(c => c.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public Task<Coupon?> GetByReservationAsync(
        Guid reservationId, CancellationToken cancellationToken = default)
        => _context.Coupons
            .Include(c => c.Reservations)
            .FirstOrDefaultAsync(c => c.Reservations.Any(r => r.Id == reservationId), cancellationToken);

    public async Task<IReadOnlyList<Coupon>> ListByOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
        => await _context.Coupons
            .Include(c => c.Reservations)
            .Where(c => c.Reservations.Any(r => r.OrderId == orderId))
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Coupon coupon, CancellationToken cancellationToken = default)
        => await _context.Coupons.AddAsync(coupon, cancellationToken);
}
