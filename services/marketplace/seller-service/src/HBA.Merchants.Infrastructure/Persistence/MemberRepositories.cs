using HBA.Merchants.Domain.Members;
using Microsoft.EntityFrameworkCore;

namespace HBA.Merchants.Infrastructure.Persistence;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES MEMBRES — TOUTES LES LECTURES CHARGENT LES AFFECTATIONS.
///
/// SANS `Include`, `EffectivePermissions` REND UN ENSEMBLE VIDE SANS RIEN DIRE.
///
/// Les rôles de niveau vendeur sont un type possédé : EF les charge toujours avec
/// leur propriétaire. Les affectations boutique, elles, sont une relation :
/// oubliées, la collection est simplement vide, et un membre parfaitement habilité
/// se voit tout refuser — sans exception, sans journal, avec un message
/// d'autorisation parfaitement plausible. C'est la panne la plus difficile à
/// diagnostiquer que ce module puisse produire ; elle est fermée ici, une fois,
/// pour toutes les lectures.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
            //
            // Le §55 vise plusieurs organisations par compte ; le jour où ce sera
            // vrai, cette méthode devra rendre une LISTE. En attendant elle rend la
            // plus ancienne, et non « une au hasard selon l'ordre de la table ».
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

    /// <summary>
    /// LE DÉCOMPTE SE FAIT EN MÉMOIRE, ET C'EST UN CHOIX ASSUMÉ.
    ///
    /// Les rôles d'un membre sont un type POSSÉDÉ : EF interdit d'interroger une
    /// entité possédée par elle-même, donc « compte les lignes de
    /// `seller_member_roles` qui portent OWNER » ne s'écrit pas en LINQ. La requête
    /// ci-dessous ne ramène que les membres ACTIFS D'UN SEUL VENDEUR, avec leurs
    /// rôles (chargés d'office, sans jointure supplémentaire) et SANS leurs
    /// affectations boutique. C'est une équipe, pas une table : quelques lignes.
    ///
    /// Le jour où une équipe atteindrait une taille où cela compte, la réponse
    /// n'est pas un `Include` de plus mais une vue SQL — pas une optimisation
    /// prématurée d'un code correct.
    /// </summary>
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
    /// <remarks>
    /// BORNÉ AU VENDEUR, ET PAS PAR PRUDENCE : PAR CORRECTION.
    ///
    /// Un rôle personnalisé appartient à un vendeur ; un rôle système est partagé
    /// mais ne se supprime pas. La question « qui porte ce rôle » n'a donc de sens
    /// que dans le périmètre d'un vendeur, et la poser globalement chargerait la
    /// table entière pour une réponse dont personne n'a l'usage.
    /// </remarks>
    public async Task<int> CountByRoleAsync(
        Guid sellerId, SellerRoleId roleId, CancellationToken cancellationToken = default)
    {
        var membres = await Complets()
            .Where(m => m.SellerId == sellerId)
            .ToListAsync(cancellationToken);

        // SEULS LES MEMBRES QUI PEUVENT AGIR COMPTENT.
        //
        // Sans ce filtre, un rôle porté uniquement par des comptes RÉVOQUÉS ou
        // PARTIS restait indéboulonnable : `EnsureDeletable` refusait la
        // suppression en annonçant « porté par 3 membre(s) » quand l'écran d'équipe
        // n'en montrait aucun. Le vendeur cherchait des porteurs qui n'existaient
        // plus, et le rôle s'accumulait dans sa liste sans qu'il puisse l'effacer.
        //
        // Un membre SUSPENDU, lui, compte encore : il est destiné à revenir, et
        // supprimer son rôle pendant sa suspension le lui retirerait en silence —
        // exactement la révocation muette que ce décompte existe pour empêcher.
        // `Status` ET NON `CanAct` — LA NUANCE DÉCIDE DU COMPORTEMENT.
        //
        // `CanAct` vaut `Status == Active`, donc il exclurait aussi les SUSPENDUS.
        // Or un membre suspendu est destiné à revenir : supprimer son rôle pendant
        // sa suspension le lui retirerait en silence, ce qui est exactement la
        // révocation muette que ce décompte existe pour empêcher.
        //
        // Les RÉVOQUÉS et les PARTIS, eux, ne reviennent pas. Les compter rendait le
        // rôle indéboulonnable : `EnsureDeletable` annonçait « porté par 3 membre(s) »
        // quand l'écran d'équipe n'en montrait aucun, et le vendeur cherchait des
        // porteurs qui n'existaient plus.
        return membres.Count(m =>
            m.Status is not (MemberStatus.Revoked or MemberStatus.Left)
            && m.ReferencedRoleIds.Contains(roleId));
    }

    /// <summary>
    /// EN SQL, ET SANS CHARGER LES AFFECTATIONS.
    ///
    /// Contrairement aux deux décomptes ci-dessus, celui-ci ne porte pas sur une
    /// équipe mais sur un COMPTE, tous vendeurs confondus. Il ne lit aucune
    /// collection : `Status` suffit, et la requête tient sur l'index
    /// `IX_seller_members_UserId`.
    /// </summary>
    public async Task<int> CountActiveMembershipsAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await _dbContext.SellerMembers
            .CountAsync(m => m.UserId == userId && m.Status == MemberStatus.Active, cancellationToken);

    public async Task AddAsync(SellerMember member, CancellationToken cancellationToken = default)
        => await _dbContext.SellerMembers.AddAsync(member, cancellationToken);

    /// <summary>
    /// `AsSplitQuery` PARCE QU'IL Y A DEUX COLLECTIONS.
    ///
    /// Les rôles possédés et les affectations boutique : une jointure unique
    /// multiplierait les lignes l'une par l'autre. C'est la même règle que sur la
    /// file de modération, et pour la même raison.
    /// </summary>
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

    /// <summary>
    /// `SellerId == null || SellerId == sellerId` — ET PAS SEULEMENT LE SECOND.
    ///
    /// Les rôles système ont un `SellerId` nul : les omettre rendrait une liste
    /// d'attribution VIDE pour tout vendeur n'ayant pas encore créé de rôle
    /// personnalisé — c'est-à-dire pour tous, le premier jour.
    /// </summary>
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

        var cles = ids.Distinct().ToArray();

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

/// <summary>
/// Les invitations.
/// </summary>
/// <remarks>
/// AUCUNE MÉTHODE NE PREND UN JETON EN CLAIR.
///
/// La recherche se fait sur l'empreinte, calculée par l'appelant. La valeur en
/// clair ne descend donc jamais jusqu'au pilote de base — donc ni dans un journal
/// de requêtes lentes, ni dans un plan d'exécution conservé, ni dans une trace
/// d'instrumentation qui capture le texte des commandes.
/// </remarks>
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
