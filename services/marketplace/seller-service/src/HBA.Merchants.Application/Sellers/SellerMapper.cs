using HBA.Shared.Domain.Geography;
using HBA.Merchants.Application.Sellers.Queries.GetSeller;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers;

/// <summary>
/// Projections de l'agrégat <see cref="Seller"/> — deux, et la distinction porte.
/// </summary>
internal static class SellerMapper
{
    /// <summary>LE TAUX EST PASSÉ, PAS LU SUR LE VENDEUR.</summary>
    public static SellerSummary ToSummary(Seller seller, decimal effectiveCommissionRate) => new(
        seller.Id.Value,
        seller.UserId,
        seller.ShopName,
        seller.LogoUrl,
        seller.Description,
        seller.Status.ToString(),
        seller.KybStatus.ToString(),
        effectiveCommissionRate);

    /// <summary>La fiche complète : le résumé transporté, plus tout ce qui reste ici.</summary>
    public static SellerDetail ToDetail(
        Seller seller,
        decimal effectiveCommissionRate,
        IReadOnlyList<StoreSummary> stores)
        => new(
            ToSummary(seller, effectiveCommissionRate),
            seller.Rating,
            seller.SalesCount,
            ToPayout(seller.PayoutAccount),
            seller.KybDocuments
                .Select(d => new KybDocumentSummary(
                    d.Id,
                    d.Type.ToString(),
                    d.MediaId,
                    d.LegacyFileUrl,
                    ResolveDocStatus(d, seller.KybStatus),
                    d.UploadedOnUtc,
                    d.VerifiedAtUtc))
                .ToList(),
            MapMetadata(seller.Metadata),
            seller.KybRejectionReason,
            stores);

    /// <summary>Partagé avec l'API interne, qui sert le même compte par son RPC dédié.</summary>
    public static PayoutAccountSummary? ToPayout(PayoutAccount? compte)
        => compte is null
            ? null
            : new PayoutAccountSummary(
                compte.Provider.ToString(), compte.AccountNumber, compte.AccountName);

    private static SellerCompanyInfoSummary? MapMetadata(SellerCompanyInfo? m) =>
        m is null
            ? null
            : new SellerCompanyInfoSummary(
                m.LegalName, m.Rccm, m.Ifu, m.Address,
                m.Commune,
                // Résolu ICI, une fois, plutôt que par chaque client : un écran de
                // lecture seule n'a aucune raison d'avoir chargé les 77 communes.
                BeninGeography.CommuneName(m.Commune),
                m.Activity, m.ManagerName, m.Phone);

    /// <summary>
    /// Statut affichable d'une pièce : vérifiée, refusée (boutique refusée) ou en
    /// revue.
    /// </summary>
    private static string ResolveDocStatus(KybDocument doc, KybStatus sellerStatus) =>
        doc.VerifiedAtUtc.HasValue ? "Verified"
        : sellerStatus == KybStatus.Rejected ? "Rejected"
        : "InReview";
}
