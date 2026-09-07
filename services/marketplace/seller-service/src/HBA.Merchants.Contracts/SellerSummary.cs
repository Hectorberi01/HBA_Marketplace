namespace HBA.Merchants.Contracts;

/// <summary>UN VENDEUR, TEL QU'IL VOYAGE ENTRE SERVICES — ET RIEN DE PLUS.</summary>
public record SellerSummary(
    Guid Id,
    Guid UserId,
    string ShopName,
    string? LogoUrl,
    string? Description,
    string Status,
    string KybStatus,
    decimal CommissionRate);

/// <summary>Vitrine PUBLIQUE d'une boutique — ce qu'un visiteur anonyme peut voir.</summary>
public sealed record SellerPublicSummary(
    Guid Id,
    string ShopName,
    string? LogoUrl,
    string? Description,
    decimal Rating,
    int SalesCount);

/// <param name="MediaId">UN IDENTIFIANT DE MÉDIA, PLUS UNE URL.</param>
/// <param name="LegacyFileUrl">
/// <summary> TRANSITOIRE : l'URL d'avant la bascule, tant que les pièces ne sont
/// pas reversées.</summary>
/// </param>
public sealed record KybDocumentSummary(
    Guid Id,
    string Type,
    Guid MediaId,
    string? LegacyFileUrl,
    // Statut par pièce, dérivé de la vérification : Verified si la pièce est
    // vérifiée, Rejected si la boutique est refusée, sinon InReview.
    string Status,
    DateTime UploadedAtUtc,
    DateTime? VerifiedAtUtc)
{
    /// <summary>Cette pièce est-elle antérieure au service média ?</summary>
    public bool IsLegacyDocument => MediaId == Guid.Empty;
}

public sealed record PayoutAccountSummary(
    string Provider,
    string AccountNumber,
    string AccountName);

/// <summary>Informations société déclarées par le vendeur (raison sociale, RCCM, IFU…).</summary>
/// <param name="Commune">CODE d'une des 77 communes (« abomey-calavi »).</param>
/// <param name="CommuneName">
/// Libellé accentué (« Abomey-Calavi »), RÉSOLU PAR LE SERVEUR — jamais stocké.
/// </param>
public sealed record SellerCompanyInfoSummary(
    string? LegalName,
    string? Rccm,
    string? Ifu,
    string? Address,
    string? Commune,
    string CommuneName,
    string? Activity,
    string? ManagerName,
    string? Phone);
