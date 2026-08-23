namespace HBA.Merchants.Contracts;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QU'UN COMPTE PEUT FAIRE, ET POUR QUEL VENDEUR — EN UN SEUL ALLER-RETOUR.
///
/// IL PORTE LE `SellerId`, ET C'EST LA MOITIÉ DE SON UTILITÉ.
///
/// Les cinq services appelants ne font pas seulement des GARDES : ils font du
/// CADRAGE. `CreateOfferCommand`, `CreateProductCommand`, `CreateLocationCommand`
/// et la liste « mes produits » ont tous besoin d'écrire ou de filtrer sur un
/// `sellerId`, qu'ils résolvaient jusqu'ici depuis le jeton. Un contrat qui ne
/// répondrait que « autorisé / refusé » les laisserait sans réponse, et ils
/// garderaient leur ancien chemin de résolution — celui, précisément, qui ignore
/// les membres.
///
/// UN APPEL PAR REQUÊTE, PAS UN PAR PERMISSION.
///
/// Le cahier propose `HasPermission(user, seller, store, permission)`. Une route
/// qui vérifie deux permissions ferait alors deux allers-retours, et un écran qui
/// veut griser ses boutons en ferait dix. Ici l'ensemble effectif voyage une fois
/// et la vérification est locale.
///
/// LES PERMISSIONS VOYAGENT EN CHAÎNES, PAS EN ÉNUMÉRATION.
///
/// L'énumération `MerchantPermission` vit dans le DOMAINE de seller-service, qu'un
/// service appelant n'a aucune raison de référencer — ce serait ouvrir la porte à
/// `HBA.Merchants.Domain` dans catalog, et la frontière ne tiendrait pas six mois.
/// Les codes publics (`ORDER_CONFIRM`…) sont le contrat ; `MerchantPermissions`
/// les produit, et c'est le même texte qui figure dans
/// `error.details.requiredPermission`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
    /// <remarks>
    /// C'EST LA QUESTION LARGE, ET ELLE RESTE LA BONNE POUR LA PLUPART DES
    /// ROUTES.
    ///
    /// Le dossier vendeur, les finances, l'équipe, le KYB : rien de tout cela
    /// n'appartient à une boutique. Employer <see cref="CanInStore"/> là-bas
    /// exigerait de nommer une boutique qui n'existe pas dans la question.
    ///
    /// Elle est en revanche TROP LARGE dès qu'une ressource situe elle-même sa
    /// boutique — une offre, une fiche produit. Voir <see cref="CanInStore"/>.
    /// </remarks>
    public bool Can(string permission) => Permissions.Contains(permission);

    /// <summary>
    /// Le compte détient-il cette permission DANS cette boutique ?
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// STRICTEMENT PLUS ÉTROIT QUE <see cref="Can"/>.
    ///
    /// `Can` répond sur l'union : un responsable de la boutique A y voit donc les
    /// offres de la boutique B. Celle-ci ne retient que le socle vendeur et les
    /// rôles de LA boutique visée.
    ///
    /// UNE BOUTIQUE INCONNUE RETOMBE SUR LE SOCLE, PAS SUR `Permissions`.
    ///
    /// La tentation serait de retomber sur l'union — « il n'y est pas affecté,
    /// appliquons ses droits généraux ». Ce serait exactement le trou qu'on ferme.
    ///
    /// `storeId` NUL VAUT `Can`, ET CE N'EST PAS UN CONTOURNEMENT.
    ///
    /// Une ressource sans boutique — un avis, qui ne porte qu'un `ProductId` —
    /// n'en a pas à opposer. Refuser dans ce cas fermerait la route à tout le
    /// monde ; retomber sur l'union rend le comportement d'avant le cadrage, ce
    /// qui est exactement ce qu'on veut tant que la ressource ne sait pas se
    /// situer. Le `null` est ÉCRIT par l'appelant, jamais deviné : il se voit en
    /// revue là où un défaut se devine.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RÉSOLUTION D'AUTORISATION VENDEUR — LE CONTRAT QUE LE LOT D1 CONSOMME.
///
/// IL NE REMPLACE PAS `ISellerModuleApi.GetSellerByUserIdAsync`. IL LUI SUCCÈDE.
///
/// L'ancienne méthode ne résout QUE le propriétaire, et elle continuera de le
/// faire jusqu'à ce que son dernier appelant ait migré. C'est ce qui rend la
/// migration sûre service par service : un service qui n'applique pas encore les
/// capacités ne voit tout simplement pas les membres. Élargir
/// `GetSellerByUserIdAsync` aurait ouvert les cinq services d'un coup — y compris
/// `PUT /{sellerId}/payout-account`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IMerchantAccessApi
{
    /// <summary>
    /// Le contexte d'accès d'un compte, ou <c>null</c> s'il n'appartient à aucune
    /// équipe vendeur active.
    /// </summary>
    /// <remarks>
    /// `null` NE VEUT PAS DIRE « INTERDIT » : il veut dire « ce compte n'a
    /// aucun dossier vendeur ». L'appelant en tire un 404 ou un 403 selon ce que
    /// la route doit révéler — le contrat ne tranche pas à sa place.
    /// </remarks>
    Task<MerchantAccess?> GetAccessAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vérification explicite, quand le vendeur est désigné par la RESSOURCE et
    /// non par le jeton — un avis, une commande, un retrait.
    /// </summary>
    /// <param name="storeId">
    /// HONORÉ DEPUIS LE LOT F. Un <c>null</c> vaut « cette ressource ne connaît
    /// pas sa boutique » et retombe sur l'union — voir
    /// <see cref="MerchantAccess.CanInStore"/>.
    /// <para>
    /// Il reste <c>null</c> là où la ressource ne sait pas se situer :
    /// <c>OrderLine</c> et <c>InventoryItem</c> ne portent aucune boutique. Ce
    /// n'est plus une limite du contrat, c'est une limite de LEUR schéma, et c'est
    /// là qu'il faudra la lever.
    /// </para>
    /// </param>
    Task<bool> HasCapabilityAsync(
        Guid userId,
        Guid sellerId,
        Guid? storeId,
        string permission,
        CancellationToken cancellationToken = default);
}
