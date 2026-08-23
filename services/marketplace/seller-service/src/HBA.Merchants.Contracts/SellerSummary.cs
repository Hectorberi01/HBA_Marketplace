namespace HBA.Merchants.Contracts;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN VENDEUR, TEL QU'IL VOYAGE ENTRE SERVICES — ET RIEN DE PLUS.
///
/// CE RECORD PORTE EXACTEMENT LES HUIT CHAMPS DU PROTO `merchant.v1`. C'EST SA
///    DÉFINITION, PAS UNE COÏNCIDENCE À MAINTENIR À LA MAIN.
///
/// Il en portait quatorze. Les six autres — `Rating`, `SalesCount`, `Payout`,
/// `KybDocuments`, `Metadata`, `KybRejectionReason` — ne traversaient pas le
/// transport, et le mappeur du client gRPC leur donnait donc une valeur neutre :
/// `0`, `null`, liste vide. Aucun appelant distant ne pouvait distinguer cette
/// valeur d'une vraie.
///
/// UNE INTERFACE, DEUX SÉMANTIQUES. Dans seller-service, `ISellerModuleApi`
/// rendait le vendeur ; ailleurs, il rendait un objet EN FORME de vendeur, dont
/// l'argent et les pièces d'identité avaient été remplacés par du plausible. Rien
/// — ni le type, ni la DI — ne distinguait les deux.
///
/// CE N'ÉTAIT PAS THÉORIQUE. `Payout: null` a rendu IMPOSSIBLE tout retrait
/// vendeur de la plateforme : wallet-service lisait ce champ, obtenait `null` quel
/// que soit le vendeur, et refusait chaque demande — pendant que la validation
/// administrative d'une demande existante ÉCHOUAIT ET REMBOURSAIT, sur un motif
/// faux (D21). C'est ce que coûte un champ qui ment.
///
/// Désormais le compilateur interdit ce qu'un commentaire se contentait
/// d'avertir : ce qui n'est pas ici ne voyage pas, et ce qui voyage est ici. La
/// vue riche — celle du propriétaire et de l'administration — vit dans
/// `SellerDetail`, côté Application, qui hérite de ce record et n'en sort jamais.
///
/// Le compte de reversement a son propre RPC : `ISellerModuleApi.GetSellerPayoutAsync`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <remarks>
/// NON SCELLÉ, ET UNE SEULE DÉRIVATION EXISTE — `SellerDetail`.
///
/// Elle hérite par le constructeur de COPIE du record, ce qui évite de recopier
/// les huit champs à la main : une recopie aurait compilé aujourd'hui et divergé
/// au premier champ ajouté. L'héritage donne aussi la forme JSON attendue par les
/// clients — champs du vendeur à plat, le reste à côté.
///
/// AJOUTER UN CHAMP ICI, C'EST L'AJOUTER AU PROTO DANS LE MÊME GESTE. Sans
/// quoi on rouvre exactement le trou que cette séparation vient de fermer.
/// </remarks>
public record SellerSummary(
    Guid Id,
    Guid UserId,
    string ShopName,
    string? LogoUrl,
    string? Description,
    string Status,
    string KybStatus,
    decimal CommissionRate);

/// <summary>
/// Vitrine PUBLIQUE d'une boutique — ce qu'un visiteur anonyme peut voir.
///
/// Contient STRICTEMENT ce qui doit être affiché sur une page boutique. Tout ajout à ce
/// record est une décision de divulgation : réfléchir à deux fois avant d'y mettre quoi
/// que ce soit qui touche à l'identité, l'argent ou les documents du vendeur.
///
/// AUCUN APPELANT AUJOURD'HUI, ET IL EST CONSERVÉ EXPRÈS. La passerelle attend
/// une route publique qui n'existe pas encore — `MerchantClient` le dit :
/// « merchant-service a déjà écrit `ToPublic()`, c'est à lui de l'exposer ». La
/// projection vit désormais sur `SellerDetail`, seul type à porter `Rating` et
/// `SalesCount`.
/// </summary>
public sealed record SellerPublicSummary(
    Guid Id,
    string ShopName,
    string? LogoUrl,
    string? Description,
    decimal Rating,
    int SalesCount);

public sealed record KybDocumentSummary(
    Guid Id,
    string Type,
    /// <summary>
    /// UN IDENTIFIANT DE MÉDIA, PLUS UNE URL.
    ///
    /// Une URL de pièce d'identité qui circule dans un contrat, c'est une URL qui
    /// finit dans un journal applicatif, une capture d'écran de support, ou le
    /// cache d'un navigateur. Le service média ne sert ces fichiers que par URL
    /// signée de cinq minutes, demandée nommément — et seulement après que
    /// l'appelant a vérifié le droit métier.
    /// </summary>
    Guid MediaId,

    /// <summary>TRANSITOIRE : l'URL d'avant la bascule, tant que les pièces ne sont pas reversées.</summary>
    string? LegacyFileUrl,
    // Statut par pièce, dérivé de la vérification : Verified si la pièce est
    // vérifiée, Rejected si la boutique est refusée, sinon InReview.
    string Status,
    DateTime UploadedAtUtc,
    DateTime? VerifiedAtUtc)
{
    /// <summary>
    /// Cette pièce est-elle antérieure au service média ?
    ///
    /// CE PRÉDICAT EXISTE POUR QUE LES APPELANTS NE L'ÉCRIVENT PAS EUX-MÊMES.
    ///
    /// Les pièces déposées avant la bascule n'ont pas de média : leur `MediaId`
    /// vaut `Guid.Empty` et leur adresse dort dans `LegacyFileUrl`. Trois routes
    /// doivent en tenir compte, et une comparaison à `Guid.Empty` recopiée trois
    /// fois est une comparaison qu'on oublie la quatrième — auquel cas on demande
    /// au service média un identifiant nul, on reçoit « introuvable », et le
    /// vendeur lit que sa pièce a disparu alors qu'elle est bien là.
    /// </summary>
    public bool IsLegacyDocument => MediaId == Guid.Empty;
}

public sealed record PayoutAccountSummary(
    string Provider,
    string AccountNumber,
    string AccountName);

/// <summary>
/// Informations société déclarées par le vendeur (raison sociale, RCCM, IFU…).
/// Miroir contractuel du VO domaine SellerCompanyInfo. Tous les champs optionnels.
/// </summary>
/// <param name="Commune">
/// CODE d'une des 77 communes (« abomey-calavi »). C'est la valeur STOCKÉE, et celle
/// qu'un client renvoie à l'écriture.
/// </param>
/// <param name="CommuneName">
/// Libellé accentué (« Abomey-Calavi »), RÉSOLU PAR LE SERVEUR — jamais stocké.
///
/// Il existe parce que sans lui les écrans en lecture seule affichaient le code brut :
/// la fiche vendeur de la console d'administration et l'écran boutique de l'application
/// vendeur montraient « abomey-calavi » là où le formulaire, juste à côté, affichait
/// « Abomey-Calavi ». Faire résoudre le libellé par chaque client supposerait qu'il ait
/// chargé le référentiel — vrai sur un formulaire, faux sur une simple fiche de lecture.
///
/// Chaîne vide si aucune commune n'est déclarée (le champ reste facultatif : c'est du
/// déclaratif de dossier KYB, pas une adresse de livraison).
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
