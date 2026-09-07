using HBA.Merchants.Domain.Members;
using Microsoft.EntityFrameworkCore;

namespace HBA.Merchants.Infrastructure.Persistence;

/// <summary>LES MEMBRES — TOUTES LES LECTURES CHARGENT LES AFFECTATIONS.</summary>
internal sealed class SellerMemberRepository : ISellerMemberRepository
{
    private readonly SellersDbContext _dbContext;

    public SellerMemberRepository(SellersDbContext dbContext) => _dbContext = dbContext;

    public async Task<SellerMember?> GetByIdAsync(
        SellerMemberId id, CancellationToken cancellationToken = default)
        => await Complets().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<SellerMember?> GetMembershipAsync(
        Guid sellerId, Guid userId, CancellationToken cancellationToken = default)
        => await Complets()
            .FirstOrDefaultAsync(m => m.SellerId == sellerId && m.UserId == userId, cancellationToken);

    public async Task<SellerMember?> GetActiveMembershipByUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await Complets()
            .Where(m => m.UserId == userId && m.Status == MemberStatus.Active)
            // ORDRE STABLE, MÊME SI L'INDEX UNIQUE REND LE CAS THÉORIQUE.
            .OrderBy(m => m.CreatedOnUtc)
            .ThenBy(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SellerMember>> ListBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
        => await Complets()
            .Where(m => m.SellerId == sellerId)
            .OrderBy(m => m.CreatedOnUtc)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

    /// <summary>LE DÉCOMPTE SE FAIT EN MÉMOIRE, ET C'EST UN CHOIX ASSUMÉ.</summary>
    public async Task<int> CountActiveOwnersAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
    {
        var actifs = await _dbContext.SellerMembers
            .Where(m => m.SellerId == sellerId && m.Status == MemberStatus.Active)
            .ToListAsync(cancellationToken);

        return actifs.Count(m => m.IsOwner);
    }

    /// <summary>
    /// Combien de membres de CE vendeur portent ce rôle — au niveau vendeur ou sur
    /// une boutique.
    /// </summary>
    public async Task<int> CountByRoleAsync(
        Guid sellerId, SellerRoleId roleId, CancellationToken cancellationToken = default)
    {
        var membres = await Complets()
            .Where(m => m.SellerId == sellerId)
            .ToListAsync(cancellationToken);

        // SEULS LES MEMBRES QUI PEUVENT AGIR COMPTENT.
        return membres.Count(m =>
            m.Status is not (MemberStatus.Revoked or MemberStatus.Left)
            && m.ReferencedRoleIds.Contains(roleId));
    }

    /// <summary>EN SQL, ET SANS CHARGER LES AFFECTATIONS.</summary>
    public async Task<int> CountActiveMembershipsAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.SellerMembers
            .CountAsync(m => m.UserId == userId && m.Status == MemberStatus.Active, cancellationToken);

    public async Task AddAsync(SellerMember member, CancellationToken cancellationToken = default)
        => await _dbContext.SellerMembers.AddAsync(member, cancellationToken);

    /// <summary>`AsSplitQuery` PARCE QU'IL Y A DEUX COLLECTIONS.</summary>
    private IQueryable<SellerMember> Complets()
        => _dbContext.SellerMembers
            .Include("_storeMemberships")
            .AsSplitQuery();
}

/// <summary>Les rôles : ceux du système et ceux du vendeur, jamais ceux d'un autre.</summary>
internal sealed class SellerRoleRepository : ISellerRoleRepository
{
    private readonly SellersDbContext _dbContext;

    public SellerRoleRepository(SellersDbContext dbContext) => _dbContext = dbContext;

    public async Task<SellerRole?> GetByIdAsync(
        SellerRoleId id, CancellationToken cancellationToken = default)
        => await _dbContext.SellerRoles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    /// <summary>`SellerId == null || SellerId == sellerId` — ET PAS SEULEMENT LE SECOND.</summary>
    public async Task<IReadOnlyList<SellerRole>> ListAvailableAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.SellerRoles
            .Where(r => r.SellerId == null || r.SellerId == sellerId)
            .OrderBy(r => r.SellerId == null ? 0 : 1)
            .ThenBy(r => r.CreatedOnUtc)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SellerRole>> ListByIdsAsync(
        IReadOnlyCollection<SellerRoleId> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // UNE LISTE, ET SURTOUT PAS UN TABLEAU. LA RAISON N'EST PAS UN GOÛT.
        var cles = ids.Distinct().ToList();

        return await _dbContext.SellerRoles
            .Where(r => cles.Contains(r.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> NameExistsAsync(
        Guid sellerId, string name, CancellationToken cancellationToken = default)
        => await _dbContext.SellerRoles
            .AnyAsync(r => r.SellerId == sellerId && r.Name == name, cancellationToken);

    public async Task AddAsync(SellerRole role, CancellationToken cancellationToken = default)
        => await _dbContext.SellerRoles.AddAsync(role, cancellationToken);

    public void Remove(SellerRole role) => _dbContext.SellerRoles.Remove(role);
}

/// <summary>Les invitations.</summary>
internal sealed class SellerInvitationRepository : ISellerInvitationRepository
{
    private readonly SellersDbContext _dbContext;

    public SellerInvitationRepository(SellersDbContext dbContext) => _dbContext = dbContext;

    public async Task<SellerInvitation?> GetByIdAsync(
        SellerInvitationId id, CancellationToken cancellationToken = default)
        => await _dbContext.SellerInvitations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<SellerInvitation?> GetByTokenHashAsync(
        string tokenHash, CancellationToken cancellationToken = default)
        => await _dbContext.SellerInvitations
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<SellerInvitation>> ListBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.SellerInvitations
            .Where(i => i.SellerId == sellerId)
            .OrderByDescending(i => i.CreatedOnUtc)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

    public async Task<SellerInvitation?> GetPendingAsync(
        Guid sellerId, string email, CancellationToken cancellationToken = default)
        => await _dbContext.SellerInvitations
            .FirstOrDefaultAsync(
                i => i.SellerId == sellerId
                    && i.Email == email
                    && i.Status == InvitationStatus.Pending,
                cancellationToken);

    public async Task AddAsync(SellerInvitation invitation, CancellationToken cancellationToken = default)
        => await _dbContext.SellerInvitations.AddAsync(invitation, cancellationToken);
}
