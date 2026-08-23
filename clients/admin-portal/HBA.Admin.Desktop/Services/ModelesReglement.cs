using System.Text.Json.Serialization;

namespace HBA.Admin.Desktop.Services;

// ═════════════════════════════════════════════════════════════════════════════
// LES LOTS DE REVERSEMENT (settlement).
//
// C'est l'argent qui sort vers les vendeurs. La console le LIT ; elle ne le
// déclenche pas — voir `ReglementViewModel` pour ce que la passerelle relaie et
// ce qu'elle retient délibérément.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Un lot de reversement, tel que `SettlementBatchSummary` le rend.</summary>
/// <remarks>
/// LE LOT PORTE SES VERSEMENTS : UN SEUL APPEL SUFFIT.
///
/// `SettlementMapper.ToSummary` projette `b.Payouts` dans le résumé, et
/// `ListSettlementBatchesQuery` rend la liste complète des lots ainsi projetés.
/// L'écran n'a donc pas besoin d'un second appel par lot — ce qui, sur une
/// plateforme active, éviterait autant d'allers-retours qu'il y a de lots.
/// </remarks>
public sealed record LotReglement(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("periodStartUtc")] DateTime PeriodStartUtc,
    [property: JsonPropertyName("periodEndUtc")] DateTime PeriodEndUtc,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("totalNet")] decimal TotalNet,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc,
    [property: JsonPropertyName("payouts")] IReadOnlyList<VersementReglement>? Payouts);

/// <summary>Un versement à un vendeur dans un lot, `PayoutSummary`.</summary>
/// <param name="ProviderRef">
/// La référence rendue par l'opérateur de paiement. Nulle tant que le versement
/// n'a pas été marqué payé — et c'est la SEULE preuve qu'un franc est parti.
/// </param>
public sealed record VersementReglement(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("grossAmount")] decimal GrossAmount,
    [property: JsonPropertyName("commissionAmount")] decimal CommissionAmount,
    [property: JsonPropertyName("netAmount")] decimal NetAmount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("providerRef")] string? ProviderRef,
    [property: JsonPropertyName("paidAtUtc")] DateTime? PaidAtUtc);

/// <summary>Le relevé d'un vendeur sur une période, `SellerStatementSummary`.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE RELEVÉ N'EST PAS COMPARABLE TERME À TERME AVEC UN VERSEMENT.
///
/// Les deux portent sur la même période et ne comptent PAS la même chose :
///
///   • le relevé filtre les gains sur `CreatedAtUtc` — quand le gain est né ;
///   • le lot filtre sur `ReleasedAtUtc` et sur le statut `Released` — quand le
///     gain est devenu payable.
///
/// Le commentaire du dépôt le dit : « un gain confirmé avant la période mais
/// livré pendant doit être réglé ; un gain confirmé pendant mais pas encore
/// livré ne doit pas ». Deux axes de temps différents, donc deux totaux
/// différents, et **un écart n'est pas une anomalie**.
///
/// L'écran affiche les deux côte à côte en le disant. Il ne calcule PAS de
/// « différence à justifier » : ce chiffre-là n'aurait pas de sens, et il
/// enverrait chercher une erreur là où il n'y en a pas.
///
/// `GrossSales - Commissions` ne donne pas non plus `NetPayout` : il faut aussi
/// retirer `ProviderFees`, ajouté au contrat précisément parce que le résumé ne
/// s'équilibrait pas sans lui.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record ReleveVendeur(
    [property: JsonPropertyName("sellerId")] Guid SellerId,
    [property: JsonPropertyName("grossSales")] decimal GrossSales,
    [property: JsonPropertyName("commissions")] decimal Commissions,
    [property: JsonPropertyName("providerFees")] decimal ProviderFees,
    [property: JsonPropertyName("netPayout")] decimal NetPayout,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("lineCount")] int LineCount);
