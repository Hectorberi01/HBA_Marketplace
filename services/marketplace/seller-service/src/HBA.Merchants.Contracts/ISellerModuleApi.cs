namespace HBA.Merchants.Contracts;

/// <summary>
/// API in-process publique du module Sellers. Permet par exemple à Catalog de
/// vérifier qu'un vendeur est actif avant d'accepter la création d'un produit.
/// </summary>
public interface ISellerModuleApi
{
    Task<SellerSummary?> GetSellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    Task<SellerSummary?> GetSellerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> IsActiveSellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Une boutique par son identifiant, ou null.
    ///
    /// NÉCESSAIRE AU MODULE PRODUCTS : une offre porte un StoreId, et rien ne
    /// permettait de vérifier que cette boutique existe, ni qu'elle appartient au
    /// vendeur qui pose l'offre. Le champ était accepté sur parole.
    /// </summary>
    Task<StoreSummary?> GetStoreAsync(Guid storeId, CancellationToken cancellationToken = default);

    /// <summary>Les boutiques d'un vendeur — le multi-boutiques, vu de l'extérieur.</summary>
    Task<IReadOnlyList<StoreSummary>> ListStoresBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE COMPTE DE REVERSEMENT — LE SEUL CHEMIN VALABLE POUR L'OBTENIR.
    ///
    /// NE LISEZ JAMAIS `SellerSummary.Payout` DEPUIS UN AUTRE SERVICE.
    ///
    /// Ce champ existe sur le record C#, mais le proto gRPC ne le transporte pas :
    /// à distance, il vaut `null` pour TOUS les vendeurs, y compris ceux qui ont
    /// un compte parfaitement configuré. wallet-service l'a lu, et plus aucun
    /// retrait vendeur n'était possible sur la plateforme — la demande était
    /// refusée, et la validation administrative d'une demande existante échouait
    /// AVEC remboursement, sur un motif faux.
    ///
    /// Cette méthode-ci a son propre RPC, qui transporte réellement le compte.
    ///
    /// ET ELLE N'EST PAS MISE EN CACHE, contrairement à `GetSellerAsync`.
    ///
    /// Les lectures de vendeur alimentent des écrans : un nom de boutique vieux
    /// de dix minutes n'a jamais fait de mal. Un NUMÉRO MOBILE MONEY vieux de dix
    /// minutes, si : c'est l'argent envoyé à l'ancien numéro d'un vendeur qui
    /// vient de corriger une faute de frappe.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<SellerPayout> GetSellerPayoutAsync(Guid sellerId, CancellationToken cancellationToken = default);
}
