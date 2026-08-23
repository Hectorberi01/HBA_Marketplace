using HBA.Shared.Domain.Geography;
using HBA.Merchants.Application.Sellers.Queries.GetSeller;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Application.Sellers;

/// <summary>
/// Projections de l'agrégat <see cref="Seller"/> — deux, et la distinction porte.
///
/// <see cref="ToSummary"/> rend ce qui VOYAGE entre services : les huit champs du
/// proto. <see cref="ToDetail"/> rend la fiche complète, qui ne sort jamais de la
/// surface HTTP du service. Voir l'encadré de `SellerSummary` : c'est cette
/// séparation qui empêche un champ non transporté de mentir à un appelant distant.
/// </summary>
internal static class SellerMapper
{
    /// <summary>
    /// LE TAUX EST PASSÉ, PAS LU SUR LE VENDEUR.
    ///
    /// Ce mapper servait `seller.CommissionRate` — une colonne écrite à
    /// l'inscription et consultée par aucun calcul. Le marchand lisait donc un
    /// taux qui n'était pas celui qu'on lui appliquait, et rien ne le signalait.
    /// </summary>
    public static SellerSummary ToSummary(Seller seller, decimal effectiveCommissionRate) => new(
        seller.Id.Value,
        seller.UserId,
        seller.ShopName,
        seller.LogoUrl,
        seller.Description,
        seller.Status.ToString(),
        seller.KybStatus.ToString(),
        effectiveCommissionRate);

    /// <summary>
    /// La fiche complète : le résumé transporté, plus tout ce qui reste ici.
    /// </summary>
    /// <remarks>
    /// LES BOUTIQUES SONT PASSÉES, PAS CHARGÉES ICI. `Store` est un agrégat
    /// distinct, avec son propre dépôt ; c'est à l'appelant de décider s'il paie
    /// cette seconde lecture.
    /// </remarks>
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

    /// <summary>Statut affichable d'une pièce : vérifiée, refusée (boutique refusée) ou en revue.</summary>
    private static string ResolveDocStatus(KybDocument doc, KybStatus sellerStatus) =>
        doc.VerifiedAtUtc.HasValue ? "Verified"
        : sellerStatus == KybStatus.Rejected ? "Rejected"
        : "InReview";
}
