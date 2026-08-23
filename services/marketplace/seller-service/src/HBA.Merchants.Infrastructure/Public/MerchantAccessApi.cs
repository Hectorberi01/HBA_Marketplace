using HBA.Merchants.Application;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Members;
using HBA.Shared.Application.Abstractions;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Infrastructure.Public;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RÉSOLUTION D'AUTORISATION VENDEUR — UNE SEULE, POUR TOUTE LA PLATEFORME.
///
/// ELLE NE PASSE PAS PAR `MemberAccessResolver`, ET CE N'EST PAS UN DOUBLON.
///
/// Le résolveur de la couche Application répond « cet acteur peut-il agir SUR CE
/// VENDEUR », question posée quand le vendeur est déjà connu — les routes
/// d'équipe portent son identifiant dans l'URL. Celle-ci répond « à quel vendeur
/// ce compte appartient-il, et avec quels droits », qui est la question des cinq
/// services appelants : eux n'ont qu'un jeton.
///
/// Les deux partagent l'essentiel — `MemberAccess.For` — et c'est là que vit la
/// règle. Ce qui diffère est le point d'entrée, pas le calcul.
///
/// LE CACHE EST ICI, ET NULLE PART AILLEURS.
///
/// Il est évincé dans le même `SaveChangesAsync` que la mutation qui le périme,
/// donc globalement depuis que Redis est branché. Un cache client, chez chacun
/// des cinq appelants, aurait exigé un événement Kafka par invalidation — et dans
/// un groupe de consommateurs, une seule réplique l'aurait reçu.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE STATUT DU VENDEUR ENTRE ENFIN DANS LA DÉCISION.
    ///
    /// Cette méthode ne regardait QUE l'appartenance : un compte rattaché à une
    /// équipe active recevait ses permissions, quel que soit l'état du vendeur.
    /// Un vendeur SUSPENDU — y compris rejeté au KYB — et toute son équipe
    /// continuaient donc de publier des produits, d'ajuster le stock et de
    /// demander des retraits. La suspension était décorative, alors que c'est la
    /// principale mesure de police de la plateforme.
    ///
    /// ON REND `null`, PAS UN ACCÈS VIDE.
    ///
    /// Un accès aux permissions vides laisserait passer toute route qui se
    /// contente de « l'appelant a-t-il un vendeur ». `null` ferme tout, et c'est
    /// ce que veut une suspension. Le contrat le prévoit : « `null` ne veut pas
    /// dire interdit, il veut dire ce compte n'a aucun dossier vendeur » —
    /// l'appelant en tire un 403 ou un 404 selon ce qu'il doit révéler.
    ///
    /// CELA NE ROMPT PAS L'ACCÈS DU VENDEUR À SON PROPRE DOSSIER.
    ///
    /// Les routes d'équipe de seller-service passent par `MemberAccessResolver`,
    /// pas par ici. Un vendeur suspendu voit donc toujours son dossier, son KYB et
    /// peut demander une réactivation — il ne peut simplement plus VENDRE.
    ///
    /// SEUL `Active` OPÈRE, ET C'EST PLUS STRICT QUE LE DÉFAUT SIGNALÉ.
    ///
    /// L'audit ne parlait que de `Suspended`. `Pending` (jamais validé),
    /// `Closed` et `PendingReactivation` sont ajoutés parce qu'aucun d'eux ne
    /// décrit un vendeur autorisé à mettre en vente. Si l'intégration exige qu'un
    /// vendeur `Pending` publie avant validation, c'est une ligne à changer dans
    /// `PeutOpererAsync` — et une décision à écrire.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<MerchantAccess?> GetAccessAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var acces = await ResoudreEnCacheAsync(userId, cancellationToken);

        if (acces is null)
        {
            return null;
        }

        return await PeutOpererAsync(acces.SellerId, cancellationToken) ? acces : null;
    }

    /// <summary>
    /// Le vendeur est-il en état de vendre ?
    ///
    /// MIS EN CACHE SOUS SA PROPRE CLÉ, PORTÉE PAR LE VENDEUR.
    ///
    /// Rangé dans le contexte d'accès — qui est mis en cache par UTILISATEUR — le
    /// statut n'aurait pas été évincé au bon moment : suspendre un vendeur ne
    /// touche aucun `SellerMember`, donc aucune clé de membre. Porté par le
    /// vendeur, il tombe avec lui (voir `SellersDbContext.CollectCacheKeysToEvict`).
    /// </summary>
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

        // Un vendeur introuvable ne vend pas. Le cas ne devrait pas se produire —
        // l'appartenance vient d'être résolue — mais rendre « autorisé » sur une
        // absence serait la mauvaise moitié du doute.
        return etat?.PeutOperer ?? false;
    }

    private Task<MerchantAccess?> ResoudreEnCacheAsync(Guid userId, CancellationToken cancellationToken)
        => _cache.GetOrCreateAsync(
            SellersCacheKeys.MemberAccess(userId),
            ct => ResoudreAsync(userId, ct),
            SellersCacheKeys.MemberAccessTtl,

            // LA MÉMORISATION DES ABSENCES COMPTE AUTANT QUE CELLE DES PRÉSENCES.
            //
            // La très grande majorité des comptes de la plateforme sont des
            // ACHETEURS : ils n'appartiennent à aucune équipe. Sans cache négatif,
            // chaque appel autorisé d'un acheteur produirait une requête SQL pour
            // apprendre, à nouveau, qu'il n'est pas vendeur.
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
        //
        // C'est la règle du §36 : « sellerId provenant du client ne constitue
        // jamais une preuve d'autorisation ». Il désigne le vendeur visé ; c'est
        // l'appartenance résolue depuis le JETON qui décide.
        if (acces is null || acces.SellerId != sellerId)
        {
            return false;
        }

        // `storeId` EST HONORÉ DEPUIS LE LOT F.
        //
        // `CanInStore(null, …)` vaut `Can(…)` : une ressource qui ne connaît pas sa
        // boutique — un avis ne porte qu'un `ProductId` — n'en a aucune à opposer,
        // et refuser dans ce cas fermerait la route à tout le monde. Le cadrage
        // mord là où la ressource sait se situer, et nulle part ailleurs.
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

/// <summary>
/// Ce que l'on met en cache sous `sellers:can-operate:{sellerId}`.
///
/// AU NIVEAU DE L'ESPACE DE NOMS, PAS IMBRIQUÉ EN PRIVÉ. Un type privé
/// imbriqué se sérialise mal — et pas du tout avec un contexte de sérialisation
/// généré à la compilation. Le jour où l'on activerait la génération de source,
/// le cache se mettrait à rendre `null` en silence, c'est-à-dire « ce vendeur ne
/// peut pas opérer » pour tout le monde.
/// </summary>
internal sealed record EtatOperationnelVendeur(bool PeutOperer);
