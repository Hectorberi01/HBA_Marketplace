using HBA.Merchants.Contracts;

namespace HBA.Merchants.Application.Sellers.Queries.GetSeller;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA FICHE VENDEUR COMPLÈTE — CELLE DU PROPRIÉTAIRE ET DE L'ADMINISTRATION.
///
/// CE RECORD NE DOIT JAMAIS ÊTRE SÉRIALISÉ VERS UN ENDPOINT ANONYME.
///
/// Il porte le NUMÉRO MOBILE MONEY du vendeur (`Payout.AccountNumber`), le TAUX DE
/// COMMISSION négocié, ses INFORMATIONS LÉGALES (RCCM, IFU, téléphone du gérant) et
/// les RÉFÉRENCES de ses PIÈCES D'IDENTITÉ (`KybDocuments.MediaId`).
///
/// L'avertissement n'est pas décoratif : ce contenu était autrefois rendu tel quel
/// par `GET /mobile/shop/{sellerId}`, endpoint ANONYME. Un attaquant récoltait les
/// sellerId sur les fiches produit — publiques — puis moissonnait, sans compte, le
/// RIB et les papiers d'identité de tous les vendeurs. Pour une vitrine, il y a
/// <see cref="SellerPublicSummary"/> et <see cref="ToPublic"/>.
///
/// POURQUOI CES SIX CHAMPS SONT ICI ET NON SUR `SellerSummary`.
///
/// `SellerSummary` porte exactement ce que le proto `merchant.v1` transporte. Les
/// six qui suivent — `Rating`, `SalesCount`, `Payout`, `KybDocuments`, `Metadata`,
/// `KybRejectionReason` — ne voyagent pas. Tant qu'ils vivaient sur le contrat
/// inter-services, le mappeur du client gRPC leur donnait une valeur neutre que
/// personne ne pouvait distinguer d'une vraie : `Payout: null` a ainsi rendu
/// impossible TOUT retrait vendeur de la plateforme (D21).
///
/// La règle qui en découle tient en une ligne : **un champ n'existe que là où il
/// est réellement rempli.** C'est le compilateur qui la tient maintenant.
///
/// ET ELLE HÉRITE, ELLE NE RECOPIE PAS.
///
/// `base(seller)` passe par le constructeur de COPIE du record : les huit champs
/// transportés sont repris sans en nommer un seul. Une recopie à la main aurait
/// compilé aujourd'hui et divergé au premier champ ajouté — la fiche HTTP aurait
/// cessé de porter une information sans qu'aucun test ne le voie.
///
/// L'héritage donne aussi la forme JSON du §10.3 — champs du vendeur À PLAT, le
/// reste à côté — là où un record enveloppant (`{ seller: {…}, … }`) aurait cassé,
/// en silence, tout client lisant `data.shopName`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// Le compte de reversement du vendeur.
    ///
    /// UN AUTRE SERVICE NE LE LIT PAS ICI — il appelle
    /// `ISellerModuleApi.GetSellerPayoutAsync`, qui a son propre RPC et n'est pas
    /// mise en cache. Voir D21 : ce champ, lu à travers le transport, valait `null`
    /// pour tout le monde et a bloqué tous les retraits de la plateforme.
    /// </summary>
    public PayoutAccountSummary? Payout { get; init; }

    /// <summary>Jamais nulle : un dossier vide rend une liste vide.</summary>
    public IReadOnlyList<KybDocumentSummary> KybDocuments { get; init; }

    /// <summary>
    /// Informations société déclarées (jsonb). Réservé au propriétaire et à
    /// l'administration — <see cref="ToPublic"/> ne les reprend pas.
    /// </summary>
    public SellerCompanyInfoSummary? Metadata { get; init; }

    /// <summary>
    /// Pourquoi le dossier a été refusé.
    ///
    /// SANS CE CHAMP, LE MOTIF NE SERAIT LISIBLE QUE DANS LA NOTIFICATION —
    /// c'est-à-dire une fois, le jour du refus. Le vendeur qui revient une semaine
    /// plus tard sur son espace verrait « Rejeté » sans savoir quoi corriger, et
    /// redéposerait la même pièce.
    /// </summary>
    public string? KybRejectionReason { get; init; }

    /// <summary>
    /// Les boutiques du vendeur (§10.3). Jamais nulle : un vendeur sans boutique
    /// rend une liste vide, et le client n'a pas à distinguer « aucune » de « pas
    /// chargées ».
    /// </summary>
    public IReadOnlyList<StoreSummary> Stores { get; init; }

    /// <summary>
    /// Projette la vitrine PUBLIQUE : uniquement ce qu'un visiteur anonyme a le
    /// droit de voir. Ni RIB, ni commission, ni documents KYB, ni informations
    /// légales.
    /// </summary>
    /// <remarks>
    /// ELLE VIVAIT SUR `SellerSummary` ET A SUIVI SES DEUX SEULS INGRÉDIENTS.
    ///
    /// `Rating` et `SalesCount` sont descendus ici ; la projection les suit, sans
    /// quoi elle aurait dû les lire ailleurs ou disparaître. Elle n'a aucun
    /// appelant aujourd'hui — la route publique reste à écrire, et
    /// `MerchantClient` l'attend — mais la supprimer ferait réécrire une décision
    /// de divulgation déjà prise.
    /// </remarks>
    public SellerPublicSummary ToPublic()
        => new(Id, ShopName, LogoUrl, Description, Rating, SalesCount);
}
