using HBA.Gateway.Application.Contracts.Merchant;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>merchant-service</c> — vendeurs, boutiques, KYB.</summary>
public interface IMerchantClient : IServiceClient
{
    /// <summary>
    /// Vitrine publique d'une boutique.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// AUCUNE ROUTE NE PERMET DE L'IMPLÉMENTER AUJOURD'HUI.
    ///
    /// La seule route rendant une boutique est
    /// <c>GET /api/merchants/{sellerId}/stores/{storeId}</c> : authentifiée,
    /// imbriquée sous le vendeur, et elle rend le résumé COMPLET — motif de
    /// suspension inclus. L'appeler avec le jeton d'un acheteur exposerait des
    /// informations de gestion.
    ///
    /// L'implémentation rend donc systématiquement <c>NotConfigured</c>. Elle
    /// existe pour que les agrégateurs soient écrits une fois, et non deux : le
    /// jour où merchant-service expose
    /// <c>GET /api/merchants/stores/{storeId}</c> en anonyme, seule
    /// l'implémentation change.
    ///
    /// C'est le choix du §51 : déclarer le manque, ne pas le contourner.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    Task<ServiceResult<StoreShowcase>> GetStoreShowcaseAsync(
        Guid storeId, CancellationToken cancellationToken);

    /// <summary><c>GET /api/merchants/me</c> — AUTHENTIFIÉ, résout depuis le jeton.</summary>
    Task<ServiceResult<SellerAccount>> GetMySellerAsync(CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /api/merchants/{sellerId}/stores/</c> — AUTHENTIFIÉ.
    /// </summary>
    /// <remarks>
    /// LE `sellerId` NE VIENT JAMAIS DU CLIENT HTTP.
    ///
    /// Il provient de <see cref="GetMySellerAsync"/>, donc du jeton. L'accepter
    /// depuis la requête entrante laisserait un vendeur lister les boutiques d'un
    /// autre — la route étant scopée par vendeur, c'est le paramètre qui porte
    /// toute la protection.
    /// </remarks>
    Task<ServiceResult<IReadOnlyList<MerchantStore>>> ListStoresAsync(
        Guid sellerId, CancellationToken cancellationToken);

    /// <summary><c>GET /api/merchants/{sellerId}/stores/{storeId}</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<MerchantStore>> GetStoreAsync(
        Guid sellerId, Guid storeId, CancellationToken cancellationToken);
}
