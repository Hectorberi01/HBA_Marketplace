using HBA.Merchants.Contracts;

namespace HBA.Merchants.Application.Sellers.Queries.GetSeller;

/// <summary>LA FICHE VENDEUR COMPLÈTE — CELLE DU PROPRIÉTAIRE ET DE L'ADMINISTRATION.</summary>
public sealed record SellerDetail : SellerSummary
{
    public SellerDetail(
        SellerSummary seller,
        decimal rating,
        int salesCount,
        PayoutAccountSummary? payout,
        IReadOnlyList<KybDocumentSummary> kybDocuments,
        SellerCompanyInfoSummary? metadata,
        string? kybRejectionReason,
        IReadOnlyList<StoreSummary> stores)
        : base(seller)
    {
        Rating = rating;
        SalesCount = salesCount;
        Payout = payout;
        KybDocuments = kybDocuments;
        Metadata = metadata;
        KybRejectionReason = kybRejectionReason;
        Stores = stores;
    }

    /// <summary>Note moyenne, alimentée par le module Reviews.</summary>
    public decimal Rating { get; init; }

    public int SalesCount { get; init; }

    /// <summary>Le compte de reversement du vendeur.</summary>
    public PayoutAccountSummary? Payout { get; init; }

    /// <summary>Jamais nulle : un dossier vide rend une liste vide.</summary>
    public IReadOnlyList<KybDocumentSummary> KybDocuments { get; init; }

    /// <summary>Informations société déclarées (jsonb).</summary>
    public SellerCompanyInfoSummary? Metadata { get; init; }

    /// <summary>Pourquoi le dossier a été refusé.</summary>
    public string? KybRejectionReason { get; init; }

    /// <summary>Les boutiques du vendeur (§10.3).</summary>
    public IReadOnlyList<StoreSummary> Stores { get; init; }

    /// <summary>
    /// Projette la vitrine PUBLIQUE : uniquement ce qu'un visiteur anonyme a le
    /// droit de voir.
    /// </summary>
    public SellerPublicSummary ToPublic()
        => new(Id, ShopName, LogoUrl, Description, Rating, SalesCount);
}
