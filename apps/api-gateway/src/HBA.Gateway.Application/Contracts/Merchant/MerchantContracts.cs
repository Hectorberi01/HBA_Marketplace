namespace HBA.Gateway.Application.Contracts.Merchant;

/// <summary>
/// Vitrine d'une boutique.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUNE ROUTE PUBLIQUE N'EXPOSE CE CONTRAT AUJOURD'HUI.
///
/// `StorePublicSummary` existe bien dans `HBA.Merchants.Contracts`, avec la
/// projection `StoreSummary.ToPublic()` déjà écrite. Mais la seule route qui rend
/// une boutique est :
///
///     GET /api/merchants/{sellerId:guid}/stores/{storeId:guid}
///
/// — authentifiée, imbriquée sous le vendeur, et elle rend le `StoreSummary`
/// COMPLET, motif de suspension inclus.
///
/// La fiche produit d'un acheteur ne peut donc pas afficher la boutique. Je ne
/// contourne pas : appeler cette route avec le jeton d'un acheteur exposerait des
/// informations de gestion, et la contourner côté passerelle reviendrait à
/// déplacer une décision de divulgation hors du service qui en est propriétaire.
///
/// Manque à combler dans merchant-service :
///     GET /api/merchants/stores/{storeId}  → StorePublicSummary, anonyme
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record StoreShowcase(
    Guid Id,
    string Name,
    string? LogoUrl,
    string? Description,
    string ContactPhone,
    bool IsSelling);

/// <summary>Le dossier vendeur du compte connecté — miroir PARTIEL de <c>SellerSummary</c>.</summary>
/// <remarks>
/// NI `Metadata`, NI `KybDocuments`, NI `Payout`, NI `KybRejectionReason`.
///
/// Le contrat amont les porte parce qu'il sert AUSSI l'écran KYB et
/// l'administration. Le sélecteur d'activité n'en a besoin d'aucun : y faire
/// transiter des informations de société et un motif de rejet reviendrait à les
/// envoyer sur un téléphone à chaque ouverture de l'application.
/// </remarks>
public sealed record SellerAccount(
    Guid Id,
    Guid UserId,
    string ShopName,
    string? LogoUrl,
    string Status,
    string KybStatus,
    decimal CommissionRate,
    decimal Rating,
    int SalesCount);

/// <summary>Une boutique, vue par son vendeur — miroir de <c>StoreSummary</c>.</summary>
public sealed record MerchantStore(
    Guid Id,
    Guid SellerId,
    string Name,
    string? LogoUrl,
    string? Description,
    string ContactPhone,
    string Status,
    bool IsSelling,
    string? StatusReason);
