using System.Net;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Merchant;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Merchant;

/// <inheritdoc cref="IMerchantClient" />
public sealed class MerchantClient : ServiceHttpClient, IMerchantClient
{
    /// <summary>
    /// Code rendu tant qu'aucune route publique de boutique n'existe.
    /// </summary>
    /// <remarks>
    /// 501, ET NON 503.
    ///
    /// 503 signifie « indisponible, réessayez » — un client réessaierait
    /// indéfiniment une route qui n'existe pas. 501 dit « non implémenté » : la
    /// couche d'agrégation le traduit en <c>NOT_CONFIGURED</c>, que le client
    /// utilise pour masquer le bloc DÉFINITIVEMENT plutôt que d'afficher un
    /// bouton « réessayer » qui ne mènera nulle part.
    /// </remarks>
    private const int NotImplemented = (int)HttpStatusCode.NotImplemented;

    public MerchantClient(HttpClient http, ILogger<MerchantClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Merchant;

    // ═════════════════════════════════════════════════════════════════════════
    // CE CLIENT NE PASSE PAS PAR YARP, DONC PAS PAR LA COQUILLE DE
    //    DÉPRÉCIATION.
    //
    // La bascule de merchant-service vers `/api/v1/merchants` s'accompagne d'une
    // coquille à la passerelle qui réécrit l'ancien chemin. Elle protège les
    // clients EXTERNES — applications mobiles, web. Elle ne protège PAS ce
    // fichier : `HttpClient` tape l'adresse du service en direct, le proxy n'est
    // pas sur le chemin.
    //
    // Ces trois lignes seraient donc restées en 404 alors même que la surface
    // vendeur fonctionnait — et le symptôme se serait affiché sur le tableau de
    // bord marchand, c'est-à-dire loin d'ici. Le même oubli avait été fait sur
    // `CatalogClient`, et rattrapé de justesse.
    //
    // La règle pour la prochaine migration : chercher le préfixe du service dans
    // `HttpClients/`, PAS seulement dans les routes YARP.
    // ═════════════════════════════════════════════════════════════════════════

    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// IMPLÉMENTATION INUTILISABLE — ET C'EST DÉLIBÉRÉ (§51).
    ///
    /// Aucun appel n'est émis. La seule route rendant une boutique est
    /// <c>GET /api/v1/merchants/{sellerId}/stores/{storeId}</c> : authentifiée,
    /// imbriquée sous le vendeur, et elle rend le résumé COMPLET — motif de
    /// suspension inclus.
    ///
    /// Trois raisons de ne pas l'appeler quand même :
    ///   • elle exige le `sellerId`, que la fiche produit connaît mais qui ne
    ///     rend pas la route publique pour autant ;
    ///   • le jeton d'un acheteur n'a rien à y faire, et un visiteur anonyme n'en
    ///     a aucun ;
    ///   • filtrer le résumé complet côté passerelle déplacerait une décision de
    ///     divulgation hors du service qui en est propriétaire — merchant-service
    ///     a déjà écrit `ToPublic()`, c'est à lui de l'exposer.
    ///
    /// À REMPLACER PAR, le jour où la route existe :
    ///     GetAsync&lt;StoreShowcase&gt;($"/api/v1/merchants/stores/{storeId}", ct)
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public Task<ServiceResult<StoreShowcase>> GetStoreShowcaseAsync(
        Guid storeId, CancellationToken cancellationToken)
        => Task.FromResult(ServiceResult<StoreShowcase>.Failure(
            NotImplemented,
            "merchant-service n'expose aucune vitrine publique de boutique"));

    // AUCUN IDENTIFIANT DANS L'URL : le service résout le vendeur depuis le
    // jeton, que la propagation d'en-têtes transmet.
    public Task<ServiceResult<SellerAccount>> GetMySellerAsync(CancellationToken cancellationToken)
        => GetAsync<SellerAccount>("/api/v1/merchants/me", cancellationToken);

    public Task<ServiceResult<IReadOnlyList<MerchantStore>>> ListStoresAsync(
        Guid sellerId, CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<MerchantStore>>(
            $"/api/v1/merchants/{sellerId}/stores/", cancellationToken);

    public Task<ServiceResult<MerchantStore>> GetStoreAsync(
        Guid sellerId, Guid storeId, CancellationToken cancellationToken)
        => GetAsync<MerchantStore>(
            $"/api/v1/merchants/{sellerId}/stores/{storeId}", cancellationToken);
}
