namespace HBA.Merchants.Contracts;

/// <summary>CE QU'UN COMPTE PEUT FAIRE, ET POUR QUEL VENDEUR — EN UN SEUL ALLER-RETOUR.</summary>
public sealed record MerchantAccess(
    Guid SellerId,
    Guid MemberId,
    Guid UserId,
    bool IsOwner,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid> StoreIds,
    IReadOnlyList<string> SellerLevelPermissions,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>> PermissionsByStore)
{
    /// <summary>
    /// Le compte détient-il cette permission sur ce vendeur, TOUTES BOUTIQUES
    /// CONFONDUES ?
    /// </summary>
    public bool Can(string permission) => Permissions.Contains(permission);

    /// <summary>Le compte détient-il cette permission DANS cette boutique ?</summary>
    public bool CanInStore(Guid? storeId, string permission)
    {
        if (storeId is not { } boutique)
        {
            return Can(permission);
        }

        return PermissionsByStore.TryGetValue(boutique, out var dansLaBoutique)
            ? dansLaBoutique.Contains(permission)
            : SellerLevelPermissions.Contains(permission);
    }
}

/// <summary>LA RÉSOLUTION D'AUTORISATION VENDEUR — LE CONTRAT QUE LE LOT D1 CONSOMME.</summary>
public interface IMerchantAccessApi
{
    /// <summary>
    /// Le contexte d'accès d'un compte, ou <c> null</c> s'il n'appartient à aucune
    /// équipe vendeur active.
    /// </summary>
    Task<MerchantAccess?> GetAccessAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vérification explicite, quand le vendeur est désigné par la RESSOURCE et non
    /// par le jeton — un avis, une commande, un retrait.
    /// </summary>
    /// <param name="storeId">
    /// HONORÉ DEPUIS LE LOT F. Un <c> null</c> vaut « cette ressource ne connaît
    /// pas sa boutique » et retombe sur l'union — voir
    /// <see cref="MerchantAccess.CanInStore"/> .
    /// </param>
    Task<bool> HasCapabilityAsync(
        Guid userId,
        Guid sellerId,
        Guid? storeId,
        string permission,
        CancellationToken cancellationToken = default);
}
