using HBA.Pricing.Contracts;
using HBA.Promotions.Contracts;
using Microsoft.Extensions.Logging;

// DÉPLACÉ DEPUIS `HBA.Commerce.Infrastructure.Public` LE 29 AOÛT 2026.
namespace HBA.Pricing.Promotion;

/// <summary>
/// LE FOURNISSEUR DE TARIFICATION, BRANCHÉ SUR promotion-service (ISSUE-033, D28).
/// </summary>
public sealed class PromotionPricingModuleApi : IPricingModuleApi
{
    /// <summary>L'univers de ce service de panier.</summary>
    private const string Univers = "MARKETPLACE";

    /// <summary>
    /// Sous-total de sonde pour `ValidateCouponAsync` quand l'appelant ne connaît
    /// pas le panier.
    /// </summary>
    private const long SousTotalDeSonde = 1_000_000_000L;

    private readonly IPromotionModuleApi _promotions;
    private readonly ILogger<PromotionPricingModuleApi> _logger;

    public PromotionPricingModuleApi(
        IPromotionModuleApi promotions, ILogger<PromotionPricingModuleApi> logger)
    {
        _promotions = promotions;
        _logger = logger;
    }

    public async Task<PriceBreakdownDto> CalculatePriceAsync(
        PriceRequest request, CancellationToken cancellationToken = default)
    {
        var prixDeBase = SansRemise(request);

        // AUCUN CODE = AUCUNE REMISE, ET C'EST UNE LIMITE À CONNAÎTRE.
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return prixDeBase;
        }

        var sousTotalLigne = EnUnitesEntieres(request.Subtotal);

        // `CartSubtotal` à zéro veut dire « l'appelant ne sait pas ».
        var sousTotalPanier = EnUnitesEntieres(request.CartSubtotal);

        if (sousTotalPanier <= 0)
        {
            sousTotalPanier = sousTotalLigne;

            _logger.LogWarning(
                "Tarification : sous-total de panier absent pour la ligne {ProductId}. "
                + "Le coupon est evalue sur la seule ligne — une remise en montant fixe serait "
                + "accordee autant de fois qu'il y a de lignes. Verifier l'appelant de CalculatePriceAsync.",
                request.ProductId);
        }

        if (sousTotalLigne <= 0 || sousTotalPanier <= 0)
        {
            return prixDeBase;
        }

        var evaluation = await EvaluerAsync(
            request.Code!, request.BuyerId, sousTotalPanier, request.Currency, cancellationToken);

        // TROIS CONDITIONS À PLAT PLUTÔT QU'UN MOTIF NÉGATIF QUI CAPTURE.
        if (evaluation is null || !evaluation.Valid || evaluation.Discount <= 0)
        {
            // UN COUPON REFUSÉ N'EST PAS JOURNALISÉ ICI.
            return prixDeBase;
        }

        // ── La quote-part de cette ligne dans la remise du panier ─────────────
        var remiseLigne = (long)Math.Floor(
            (decimal)evaluation.Discount * sousTotalLigne / sousTotalPanier);

        if (remiseLigne <= 0)
        {
            return prixDeBase;
        }

        var partVendeur = PartVendeur(request, evaluation, remiseLigne);
        var partPlateforme = remiseLigne - partVendeur;

        // ── Du montant de LIGNE au montant UNITAIRE ───────────────────────────
        var quantite = request.Quantity > 0 ? request.Quantity : 1;

        var remiseVendeurUnitaire = (decimal)partVendeur / quantite;
        var remisePlateformeUnitaire = (decimal)partPlateforme / quantite;

        // Le prix final ne peut pas devenir négatif : le domaine plafonne déjà la
        // remise au sous-total, mais un `CartSubtotal` incohérent avec les
        // `Subtotal` de ligne — panier modifié entre deux appels — le pourrait.
        var prixFinal = Math.Max(
            0m, request.BaseAmount - remiseVendeurUnitaire - remisePlateformeUnitaire);

        return new PriceBreakdownDto(
            BaseAmount: request.BaseAmount,
            SellerDiscount: remiseVendeurUnitaire,
            PlatformDiscount: remisePlateformeUnitaire,
            FinalAmount: prixFinal,
            Currency: request.Currency);
    }

    public async Task<CouponValidation> ValidateCouponAsync(
        string code, Guid buyerId, decimal cartSubtotal = 0m,
        CancellationToken cancellationToken = default)
    {
        var sousTotal = EnUnitesEntieres(cartSubtotal);

        if (sousTotal <= 0)
        {
            sousTotal = SousTotalDeSonde;
        }

        var evaluation = await EvaluerAsync(code, buyerId, sousTotal, "XOF", cancellationToken);

        // SERVICE INJOIGNABLE : ON REFUSE D'ATTACHER, ON N'ACCEPTE PAS.
        if (evaluation is null)
        {
            return CouponValidation.Invalid(
                "promotions.unavailable",
                "Le service de promotion est momentanément indisponible. Réessayez dans un instant.");
        }

        return evaluation.Valid
            ? CouponValidation.Valid()
            : CouponValidation.Invalid(
                evaluation.Reason ?? "promotions.coupon.not_applicable",
                evaluation.Message);
    }

    /// <summary>
    /// Interroge promotion-service, ou rend <c> null</c> si le service n'a pas
    /// répondu.
    /// </summary>
    private async Task<PromotionEvaluationResult?> EvaluerAsync(
        string code, Guid buyerId, long subtotal, string currency, CancellationToken cancellationToken)
    {
        try
        {
            return await _promotions.EvaluateAsync(
                code,
                new PromotionEvaluationContext(
                    Univers,
                    subtotal,

                    // FRAIS DE LIVRAISON À ZÉRO, ET UNE CAMPAGNE EN PÂTIT.
                    0,
                    string.IsNullOrWhiteSpace(currency) ? "XOF" : currency,
                    buyerId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Tarification : promotion-service injoignable. Le panier est valorise SANS REMISE "
                + "et la vente se poursuit. Aucun budget n'a ete engage — l'evaluation est en lecture pure.");

            return null;
        }
    }

    /// <summary>La part de la remise de ligne imputable au VENDEUR de cette ligne.</summary>
    private long PartVendeur(PriceRequest request, PromotionEvaluationResult verdict, long remiseLigne)
    {
        if (verdict.SellerFundedDiscount <= 0)
        {
            return 0;
        }

        if (verdict.OwnerSellerId is not { } financeur || financeur != request.SellerId)
        {
            _logger.LogWarning(
                "Tarification : campagne {PromotionId} financee par le vendeur {Financeur}, "
                + "appliquee a une ligne du vendeur {Vendeur}. La part vendeur ({Part}) est imputee a la "
                + "PLATEFORME pour ne facturer personne a tort. `Promotion` n'a pas de ciblage par vendeur.",
                verdict.PromotionId, verdict.OwnerSellerId, request.SellerId, verdict.SellerFundedDiscount);

            return 0;
        }

        // Même règle d'arrondi que `Promotion.SplitDiscount` : le plancher pour le
        // vendeur, le reste pour la plateforme.
        return (long)Math.Floor(
            (decimal)remiseLigne * verdict.SellerFundedDiscount / verdict.Discount);
    }

    /// <summary>
    /// Le prix de base, sans aucune remise — la réponse à « aucun coupon », «
    /// coupon refusé » et « promotion-service injoignable ».
    /// </summary>
    private static PriceBreakdownDto SansRemise(PriceRequest request)
        => new(
            BaseAmount: request.BaseAmount,
            SellerDiscount: 0m,
            PlatformDiscount: 0m,
            FinalAmount: request.BaseAmount,
            Currency: request.Currency);

    /// <summary>`decimal` → unités monétaires entières (§2).</summary>
    private static long EnUnitesEntieres(decimal montant)
        => montant <= 0m ? 0L : (long)Math.Round(montant, MidpointRounding.AwayFromZero);
}
