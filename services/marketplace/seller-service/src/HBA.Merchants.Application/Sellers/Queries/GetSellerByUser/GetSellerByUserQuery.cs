using HBA.Shared.Application.Messaging;
using HBA.Merchants.Application.Sellers.Queries.GetSeller;

namespace HBA.Merchants.Application.Sellers.Queries.GetSellerByUser;

/// <summary>
/// Récupère la boutique rattachée à un compte utilisateur — `GET /merchants/me`.
/// </summary>
/// <remarks>
/// ELLE REND `SellerDetail`, ET C'EST OBLIGATOIRE DEPUIS D24.
///
/// Elle rendait `SellerSummary`, qui portait alors quatorze champs. Ce contrat-là
/// a été ramené aux huit que le proto transporte : la laisser dessus aurait fait
/// cesser à `/merchants/me` d'émettre `rating`, `salesCount`, `payout`,
/// `kybDocuments`, `metadata` et `kybRejectionReason` — c'est-à-dire l'essentiel
/// de l'écran d'accueil de l'application vendeur.
///
/// Et la panne aurait été SILENCIEUSE : `SellerAccount`, côté passerelle, est un
/// record positionnel sans `[JsonPropertyName]`. Les champs manquants seraient
/// devenus `0` et `null` à la désérialisation, sans une erreur — et le double de
/// test de la passerelle construit ce record directement, sans passer par du JSON.
/// Rien n'aurait échoué.
/// </remarks>
public sealed record GetSellerByUserQuery(Guid UserId) : IQuery<SellerDetail>;
