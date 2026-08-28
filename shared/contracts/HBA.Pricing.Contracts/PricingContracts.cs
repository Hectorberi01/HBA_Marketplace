namespace HBA.Pricing.Contracts;

/// <summary>
/// Demande de calcul de prix effectif pour une ligne (offre/variante).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CETTE DEMANDE EST PAR LIGNE, ET UN COUPON EST PAR PANIER. C'EST TOUTE LA
/// DIFFICULTÉ DU FOURNISSEUR QUI L'HONORE.
///
/// `Subtotal` est le sous-total de CETTE ligne (`BaseAmount × Quantity`), pas
/// celui du panier. Une remise « 1 000 F sur la commande » évaluée ligne par ligne
/// serait donc accordée AUTANT DE FOIS qu'il y a de lignes — cinq lignes, cinq
/// mille francs de remise pour un coupon qui en promettait mille.
///
/// D'où `CartSubtotal`, ajouté par le lot D28 : le fournisseur évalue le coupon UNE
/// fois contre le panier entier, puis impute à chaque ligne sa quote-part. Sans ce
/// champ, il n'existe aucune façon correcte d'honorer une remise en montant fixe
/// depuis un contrat par ligne.
///
/// LES DEUX CHAMPS SONT AJOUTÉS EN FIN, AVEC UN DÉFAUT (D32).
///
/// Un appelant déjà écrit continue de compiler et passe `default`. Le fournisseur
/// traite `CartSubtotal = 0` comme « l'appelant ne sait pas » et retombe sur le
/// sous-total de la ligne : correct pour une remise proportionnelle, faux pour une
/// remise fixe — c'est pourquoi le repli est JOURNALISÉ et non silencieux.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="BuyerId">
/// L'acheteur. <c>default</c> = inconnu.
///
/// LE PLAFOND PAR COMPTE SE COMPTE SUR UN `UserId` : sans lui, il est
/// indéterminable, donc inapplicable. Il ne sert pas à l'ÉVALUATION (qui est en
/// lecture pure et ne consulte pas les plafonds) mais il voyage dès maintenant,
/// pour que la retenue au checkout n'ait pas à le redécouvrir.
/// </param>
/// <param name="CartSubtotal">
/// Sous-total du PANIER entier, toutes lignes confondues. <c>0</c> = inconnu.
/// Voir l'encadré ci-dessus.
/// </param>
public sealed record PriceRequest(
    decimal BaseAmount,
    string Currency,
    Guid ProductId,
    Guid CategoryId,
    Guid SellerId,
    int Quantity,
    decimal Subtotal,
    string? Code = null,
    bool IsFirstOrder = false,
    Guid BuyerId = default,
    decimal CartSubtotal = 0m);

/// <summary>
/// Résultat du calcul : prix de base, réductions tracées par financeur, prix
/// final. Indispensable pour un payout vendeur correct et auditable.
/// </summary>
public sealed record PriceBreakdownDto(
    decimal BaseAmount,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalAmount,
    string Currency);

/// <summary>Vue publique d'une promotion.</summary>
/// <param name="PerUserLimit">
/// <summary>Plafond par acheteur. 0 = illimité.</summary>
/// </param>
public sealed record PromotionSummary(
    Guid Id,
    string OwnerType,
    Guid OwnerId,
    string FundedBy,
    string Type,
    decimal Value,
    string ScopeType,
    IReadOnlyList<Guid> Targets,
    string? Code,
    DateTime StartUtc,
    DateTime EndUtc,
    int UsageLimit,
    int UsedCount,
    string Status,
    int PerUserLimit = 0);

/// <summary>
/// Verdict de validation d'un code promo, AVANT de l'attacher au panier.
///
/// Sert à donner une réponse immédiate et lisible à l'acheteur (« code expiré »,
/// « déjà utilisé »), plutôt que de le laisser attacher un code mort au panier et
/// découvrir au checkout que la remise ne s'est jamais appliquée.
///
/// Ce verdict est INDICATIF, pas contractuel : entre cette validation et la
/// confirmation de la commande, le dernier exemplaire du coupon peut partir. La
/// consommation réelle — et la seule qui fasse autorité — a lieu dans
/// RedeemPromotionOnOrderConfirmedHandler, sous verrou.
/// </summary>
public sealed record CouponValidation(bool IsValid, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static CouponValidation Valid() => new(true);

    public static CouponValidation Invalid(string code, string message) => new(false, code, message);
}
