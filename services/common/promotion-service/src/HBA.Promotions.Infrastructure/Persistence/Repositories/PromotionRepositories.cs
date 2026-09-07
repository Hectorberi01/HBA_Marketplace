using HBA.Promotions.Domain.Promotions;
using Microsoft.EntityFrameworkCore;

namespace HBA.Promotions.Infrastructure.Persistence;

/// <summary>CHAQUE LECTURE INCLUT SA COLLECTION. L'OUBLI NE LÈVE RIEN.</summary>
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
        if (ownerSellerId is { } proprietaire)
        {
            requete = requete.Where(p => p.OwnerSellerId == proprietaire);
        }

        if (scope is { } univers)
        {
            // « GLOBAL » REMONTE AUSSI QUAND ON FILTRE SUR UN UNIVERS.
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
        // existe.
        var normalise = (code ?? string.Empty).Trim().ToUpperInvariant();

        return _context.Coupons
            .Include(c => c.Reservations)
            .FirstOrDefaultAsync(c => c.Code == normalise, cancellationToken);
    }

    public Task<Coupon?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.Coupons
            .Include(c => c.Reservations)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    /// <summary>LE FILTRE EST DANS LA REQUÊTE **ET** DANS L'AGRÉGAT, DÉLIBÉRÉMENT.</summary>
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
