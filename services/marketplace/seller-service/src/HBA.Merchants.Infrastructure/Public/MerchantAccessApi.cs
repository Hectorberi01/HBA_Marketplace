using HBA.Merchants.Application;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Members;
using HBA.Shared.Application.Abstractions;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Infrastructure.Public;

/// <summary>LA RÉSOLUTION D'AUTORISATION VENDEUR — UNE SEULE, POUR TOUTE LA PLATEFORME.</summary>
internal sealed class MerchantAccessApi : IMerchantAccessApi
{
    private readonly ISellerMemberRepository _members;
    private readonly ISellerRoleRepository _roles;
    private readonly ISellerRepository _sellers;
    private readonly ICacheService _cache;

    public MerchantAccessApi(
        ISellerMemberRepository members,
        ISellerRoleRepository roles,
        ISellerRepository sellers,
        ICacheService cache)
    {
        _members = members;
        _roles = roles;
        _sellers = sellers;
        _cache = cache;
    }

    /// <summary>LE STATUT DU VENDEUR ENTRE ENFIN DANS LA DÉCISION.</summary>
    public async Task<MerchantAccess?> GetAccessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var acces = await ResoudreEnCacheAsync(userId, cancellationToken);

        if (acces is null)
        {
            return null;
        }

        return await PeutOpererAsync(acces.SellerId, cancellationToken) ? acces : null;
    }

    /// <summary>Le vendeur est-il en état de vendre ?</summary>
    private async Task<bool> PeutOpererAsync(Guid sellerId, CancellationToken cancellationToken)
    {
        // UN ENREGISTREMENT PLUTÔT QU'UN `bool`, PARCE QUE LE CACHE EXIGE UNE
        // RÉFÉRENCE (`where T : class`) — et parce qu'un booléen ne saurait pas
        // distinguer « connu comme interdit » de « absent du cache ».
        var etat = await _cache.GetOrCreateAsync<EtatOperationnelVendeur>(
            SellersCacheKeys.SellerCanOperate(sellerId),
            async ct =>
            {
                var vendeur = await _sellers.GetByIdAsync(new SellerId(sellerId), ct);

                return vendeur is null ? null : new EtatOperationnelVendeur(vendeur.Status == SellerStatus.Active);
            },
            SellersCacheKeys.SellerTtl,
            SellersCacheKeys.MissTtl,
            cancellationToken);

        // Un vendeur introuvable ne vend pas.
        return etat?.PeutOperer ?? false;
    }

    private Task<MerchantAccess?> ResoudreEnCacheAsync(Guid userId, CancellationToken cancellationToken)
        => _cache.GetOrCreateAsync(
            SellersCacheKeys.MemberAccess(userId),
            ct => ResoudreAsync(userId, ct),
            SellersCacheKeys.MemberAccessTtl,

            // LA MÉMORISATION DES ABSENCES COMPTE AUTANT QUE CELLE DES PRÉSENCES.
            SellersCacheKeys.MissTtl,
            cancellationToken);

    public async Task<bool> HasCapabilityAsync(
        Guid userId,
        Guid sellerId,
        Guid? storeId,
        string permission,
        CancellationToken cancellationToken = default)
    {
        var acces = await GetAccessAsync(userId, cancellationToken);

        // LE `sellerId` DE L'APPELANT EST VÉRIFIÉ, JAMAIS ACCEPTÉ.
        if (acces is null || acces.SellerId != sellerId)
        {
            return false;
        }

        // `storeId` EST HONORÉ DEPUIS LE LOT F.
        return acces.CanInStore(storeId, permission);
    }

    private async Task<MerchantAccess?> ResoudreAsync(Guid userId, CancellationToken cancellationToken)
    {
        var membre = await _members.GetActiveMembershipByUserAsync(userId, cancellationToken);

        if (membre is null)
        {
            return null;
        }

        var roles = await _roles.ListByIdsAsync([.. membre.ReferencedRoleIds], cancellationToken);
        var acteur = MemberAccess.For(membre, roles);

        return new MerchantAccess(
            membre.SellerId,
            membre.Id.Value,
            membre.UserId,
            membre.IsOwner,

            // TRIÉES, ET CE N'EST PAS COSMÉTIQUE : la réponse est sérialisée en
            // JSON dans Redis, et un ordre instable produirait une entrée
            // différente à chaque calcul — invisible, mais suffisant pour rendre
            // incomparables deux traces d'un même incident.
            [.. acteur.Permissions.Select(p => p.ToCode()).Order()],

            [.. membre.StoreMemberships
                .Where(s => s.Status == StoreMembershipStatus.Active)
                .Select(s => s.StoreId)],

            [.. acteur.SellerLevelPermissions.Select(p => p.ToCode()).Order()],

            acteur.PermissionsByStore.ToDictionary(
                e => e.Key,
                e => (IReadOnlyList<string>)[.. e.Value.Select(p => p.ToCode()).Order()]));
    }
}

/// <summary>Ce que l'on met en cache sous `sellers:can-operate:{sellerId}`.</summary>
internal sealed record EtatOperationnelVendeur(bool PeutOperer);
